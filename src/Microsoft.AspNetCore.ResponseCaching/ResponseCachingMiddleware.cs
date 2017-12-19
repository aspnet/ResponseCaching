﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Microsoft.AspNetCore.ResponseCaching
{
    public class ResponseCachingMiddleware
    {
        private static readonly TimeSpan DefaultExpirationTimeSpan = TimeSpan.FromSeconds(10);

        private readonly RequestDelegate _next;
        private readonly ResponseCachingOptions _options;
        private readonly ILogger _logger;
        private readonly IResponseCachingPolicyProvider _policyProvider;
        private readonly IResponseCache _cache;
        private readonly IResponseCachingKeyProvider _keyProvider;
        private readonly DiagnosticSource _diagnosticSource;

        public ResponseCachingMiddleware(
            RequestDelegate next,
            IOptions<ResponseCachingOptions> options,
            ILoggerFactory loggerFactory,
            IResponseCachingPolicyProvider policyProvider,
            IResponseCachingKeyProvider keyProvider,
            DiagnosticSource diagnosticSource)
            : this(
                next,
                options,
                loggerFactory,
                policyProvider,
                new MemoryResponseCache(new MemoryCache(new MemoryCacheOptions
                {
                    SizeLimit = options.Value.SizeLimit
                })), keyProvider,
                diagnosticSource)
        { }

        // for testing
        internal ResponseCachingMiddleware(
            RequestDelegate next,
            IOptions<ResponseCachingOptions> options,
            ILoggerFactory loggerFactory,
            IResponseCachingPolicyProvider policyProvider,
            IResponseCache cache,
            IResponseCachingKeyProvider keyProvider,
            DiagnosticSource diagnosticSource)
        {

            _next = next ?? throw new ArgumentNullException(nameof(next));
            _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
            _logger = (loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory))).CreateLogger<ResponseCachingMiddleware>();
            _policyProvider = policyProvider ?? throw new ArgumentNullException(nameof(policyProvider));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
            _diagnosticSource = diagnosticSource ?? throw new ArgumentNullException(nameof(diagnosticSource));
        }

        public async Task Invoke(HttpContext httpContext)
        {
            var context = new ResponseCachingContext(httpContext, _logger);

            // Should we attempt any caching logic?
            if (_policyProvider.AttemptResponseCaching(context))
            {
                // Can this request be served from cache?
                if (_policyProvider.AllowCacheLookup(context) && await TryServeFromCacheAsync(context))
                {
                    return;
                }

                // Should we store the response to this request?
                if (_policyProvider.AllowCacheStorage(context))
                {
                    // Hook up to listen to the response stream
                    ShimResponseStream(context);

                    try
                    {
                        await _next(httpContext);

                        // If there was no response body, check the response headers now. We can cache things like redirects.
                        await StartResponseAsync(context);

                        // Finalize the cache entry
                        await FinalizeCacheBodyAsync(context);
                    }
                    finally
                    {
                        UnshimResponseStream(context);
                    }

                    return;
                }
            }

            // Response should not be captured but add IResponseCachingFeature which may be required when the response is generated
            AddResponseCachingFeature(httpContext);

            try
            {
                await _next(httpContext);
            }
            finally
            {
                RemoveResponseCachingFeature(httpContext);
            }
        }

        internal async Task<bool> TryServeCachedResponseAsync(ResponseCachingContext context, IResponseCacheEntry cacheEntry)
        {
            var cachedResponse = cacheEntry as CachedResponse;
            if (cachedResponse == null)
            {
                return false;
            }

            context.CachedResponse = cachedResponse;
            context.CachedResponseHeaders = cachedResponse.Headers;
            context.ResponseTime = _options.SystemClock.UtcNow;
            var cachedEntryAge = context.ResponseTime.Value - context.CachedResponse.Created;
            context.CachedEntryAge = cachedEntryAge > TimeSpan.Zero ? cachedEntryAge : TimeSpan.Zero;

            if (_policyProvider.IsCachedEntryFresh(context))
            {
                // Check conditional request rules
                if (ContentIsNotModified(context))
                {
                    _logger.LogNotModifiedServed();
                    context.HttpContext.Response.StatusCode = StatusCodes.Status304NotModified;
                }
                else
                {
                    var response = context.HttpContext.Response;
                    // Copy the cached status code and response headers
                    response.StatusCode = context.CachedResponse.StatusCode;
                    foreach (var header in context.CachedResponse.Headers)
                    {
                        response.Headers[header.Key] = header.Value;
                    }

                    // Note: int64 division truncates result and errors may be up to 1 second. This reduction in
                    // accuracy of age calculation is considered appropriate since it is small compared to clock
                    // skews and the "Age" header is an estimate of the real age of cached content.
                    response.Headers[HeaderNames.Age] = HeaderUtilities.FormatNonNegativeInt64(context.CachedEntryAge.Value.Ticks / TimeSpan.TicksPerSecond);

                    // Copy the cached response body
                    var body = context.CachedResponse.Body;
                    if (body.Length > 0)
                    {
                        try
                        {
                            await body.CopyToAsync(response.Body, StreamUtilities.BodySegmentSize, context.HttpContext.RequestAborted);
                        }
                        catch (OperationCanceledException)
                        {
                            context.HttpContext.Abort();
                        }
                    }
                    _logger.LogCachedResponseServed();
                }
                return true;
            }

            return false;
        }

        internal async Task<bool> TryServeFromCacheAsync(ResponseCachingContext context)
        {
            _diagnosticSource.BeforeTryServeFromCache(context);
            context.BaseKey = _keyProvider.CreateBaseKey(context);
            var cacheEntry = await _cache.GetAsync(context.BaseKey);

            var cachedVaryByRules = cacheEntry as CachedVaryByRules;
            if (cachedVaryByRules != null)
            {
                // Request contains vary rules, recompute key(s) and try again
                context.CachedVaryByRules = cachedVaryByRules;

                foreach (var varyKey in _keyProvider.CreateLookupVaryByKeys(context))
                {
                    if (await TryServeCachedResponseAsync(context, await _cache.GetAsync(varyKey)))
                    {
                        _diagnosticSource.AfterTryServeFromCache(context, true);
                        return true;
                    }
                }
            }
            else
            {
                if (await TryServeCachedResponseAsync(context, cacheEntry))
                {
                    _diagnosticSource.AfterTryServeFromCache(context, true);
                    return true;
                }
            }

            if (HeaderUtilities.ContainsCacheDirective(context.HttpContext.Request.Headers[HeaderNames.CacheControl], CacheControlHeaderValue.OnlyIfCachedString))
            {
                _logger.LogGatewayTimeoutServed();
                context.HttpContext.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                _diagnosticSource.AfterTryServeFromCache(context, true);
                return true;
            }

            _logger.LogNoResponseServed();
            _diagnosticSource.AfterTryServeFromCache(context, false);
            return false;
        }


        /// <summary>
        /// Finalize cache headers.
        /// </summary>
        /// <param name="context"></param>
        /// <returns><c>true</c> if a vary by entry needs to be stored in the cache; otherwise <c>false</c>.</returns>
        private bool OnFinalizeCacheHeaders(ResponseCachingContext context)
        {
            if (_policyProvider.IsResponseCacheable(context))
            {
                var storeVaryByEntry = false;
                context.ShouldCacheResponse = true;

                // Create the cache entry now
                var response = context.HttpContext.Response;
                var varyHeaders = new StringValues(response.Headers.GetCommaSeparatedValues(HeaderNames.Vary));
                var varyQueryKeys = new StringValues(context.HttpContext.Features.Get<IResponseCachingFeature>()?.VaryByQueryKeys);
                context.CachedResponseValidFor = context.ResponseSharedMaxAge ??
                    context.ResponseMaxAge ??
                    (context.ResponseExpires - context.ResponseTime.Value) ??
                    DefaultExpirationTimeSpan;

                // Generate a base key if none exist
                if (string.IsNullOrEmpty(context.BaseKey))
                {
                    context.BaseKey = _keyProvider.CreateBaseKey(context);
                }

                // Check if any vary rules exist
                if (!StringValues.IsNullOrEmpty(varyHeaders) || !StringValues.IsNullOrEmpty(varyQueryKeys))
                {
                    // Normalize order and casing of vary by rules
                    var normalizedVaryHeaders = GetOrderCasingNormalizedStringValues(varyHeaders);
                    var normalizedVaryQueryKeys = GetOrderCasingNormalizedStringValues(varyQueryKeys);

                    // Update vary rules if they are different
                    if (context.CachedVaryByRules == null ||
                        !StringValues.Equals(context.CachedVaryByRules.QueryKeys, normalizedVaryQueryKeys) ||
                        !StringValues.Equals(context.CachedVaryByRules.Headers, normalizedVaryHeaders))
                    {
                        context.CachedVaryByRules = new CachedVaryByRules
                        {
                            VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                            Headers = normalizedVaryHeaders,
                            QueryKeys = normalizedVaryQueryKeys
                        };
                    }

                    // Always overwrite the CachedVaryByRules to update the expiry information
                    _logger.LogVaryByRulesUpdated(normalizedVaryHeaders, normalizedVaryQueryKeys);
                    storeVaryByEntry = true;

                    context.StorageVaryKey = _keyProvider.CreateStorageVaryByKey(context);
                }

                // Ensure date header is set
                if (!context.ResponseDate.HasValue)
                {
                    context.ResponseDate = context.ResponseTime.Value;
                    // Setting the date on the raw response headers.
                    context.HttpContext.Response.Headers[HeaderNames.Date] = HeaderUtilities.FormatDate(context.ResponseDate.Value);
                }

                // Store the response on the state
                context.CachedResponse = new CachedResponse
                {
                    Created = context.ResponseDate.Value,
                    StatusCode = context.HttpContext.Response.StatusCode,
                    Headers = new HeaderDictionary()
                };

                foreach (var header in context.HttpContext.Response.Headers)
                {
                    if (!string.Equals(header.Key, HeaderNames.Age, StringComparison.OrdinalIgnoreCase))
                    {
                        context.CachedResponse.Headers[header.Key] = header.Value;
                    }
                }

                return storeVaryByEntry;
            }

            context.ResponseCachingStream.DisableBuffering();
            return false;
        }

        internal void FinalizeCacheHeaders(ResponseCachingContext context)
        {
            if (OnFinalizeCacheHeaders(context))
            {
                _cache.Set(context.BaseKey, context.CachedVaryByRules, context.CachedResponseValidFor);
            }
        }

        internal Task FinalizeCacheHeadersAsync(ResponseCachingContext context)
        {
            if (OnFinalizeCacheHeaders(context))
            {
                return _cache.SetAsync(context.BaseKey, context.CachedVaryByRules, context.CachedResponseValidFor);
            }
            return Task.CompletedTask;
        }

        internal async Task FinalizeCacheBodyAsync(ResponseCachingContext context)
        {
            if (context.ShouldCacheResponse && context.ResponseCachingStream.BufferingEnabled)
            {
                var contentLength = context.HttpContext.Response.ContentLength;
                var bufferStream = context.ResponseCachingStream.GetBufferStream();
                if (!contentLength.HasValue || contentLength == bufferStream.Length)
                {
                    _diagnosticSource.BeforeCacheResponse(context);

                    var response = context.HttpContext.Response;
                    // Add a content-length if required
                    if (!response.ContentLength.HasValue && StringValues.IsNullOrEmpty(response.Headers[HeaderNames.TransferEncoding]))
                    {
                        context.CachedResponse.Headers[HeaderNames.ContentLength] = HeaderUtilities.FormatNonNegativeInt64(bufferStream.Length);
                    }

                    context.CachedResponse.Body = bufferStream;
                    _logger.LogResponseCached();
                    await _cache.SetAsync(context.StorageVaryKey ?? context.BaseKey, context.CachedResponse, context.CachedResponseValidFor);

                    _diagnosticSource.AfterCacheResponse(context);
                }
                else
                {
                    _logger.LogResponseContentLengthMismatchNotCached();
                }
            }
            else
            {
                _logger.LogResponseNotCached();
            }
        }

        /// <summary>
        /// Mark the response as started and set the response time if no reponse was started yet.
        /// </summary>
        /// <param name="context"></param>
        /// <returns><c>true</c> if the response was not started before this call; otherwise <c>false</c>.</returns>
        private bool OnStartResponse(ResponseCachingContext context)
        {
            if (!context.ResponseStarted)
            {
                context.ResponseStarted = true;
                context.ResponseTime = _options.SystemClock.UtcNow;

                return true;
            }
            return false;
        }

        internal void StartResponse(ResponseCachingContext context)
        {
            if (OnStartResponse(context))
            {
                FinalizeCacheHeaders(context);
            }
        }

        internal Task StartResponseAsync(ResponseCachingContext context)
        {
            if (OnStartResponse(context))
            {
                return FinalizeCacheHeadersAsync(context);
            }
            return Task.CompletedTask;
        }

        internal static void AddResponseCachingFeature(HttpContext context)
        {
            if (context.Features.Get<IResponseCachingFeature>() != null)
            {
                throw new InvalidOperationException($"Another instance of {nameof(ResponseCachingFeature)} already exists. Only one instance of {nameof(ResponseCachingMiddleware)} can be configured for an application.");
            }
            context.Features.Set<IResponseCachingFeature>(new ResponseCachingFeature());
        }

        internal void ShimResponseStream(ResponseCachingContext context)
        {
            // Shim response stream
            context.OriginalResponseStream = context.HttpContext.Response.Body;
            context.ResponseCachingStream = new ResponseCachingStream(
                context.OriginalResponseStream,
                _options.MaximumBodySize,
                StreamUtilities.BodySegmentSize,
                () => StartResponse(context),
                () => StartResponseAsync(context));
            context.HttpContext.Response.Body = context.ResponseCachingStream;

            // Shim IHttpSendFileFeature
            context.OriginalSendFileFeature = context.HttpContext.Features.Get<IHttpSendFileFeature>();
            if (context.OriginalSendFileFeature != null)
            {
                context.HttpContext.Features.Set<IHttpSendFileFeature>(new SendFileFeatureWrapper(context.OriginalSendFileFeature, context.ResponseCachingStream));
            }

            // Add IResponseCachingFeature
            AddResponseCachingFeature(context.HttpContext);
        }

        internal static void RemoveResponseCachingFeature(HttpContext context) =>
            context.Features.Set<IResponseCachingFeature>(null);

        internal static void UnshimResponseStream(ResponseCachingContext context)
        {
            // Unshim response stream
            context.HttpContext.Response.Body = context.OriginalResponseStream;

            // Unshim IHttpSendFileFeature
            context.HttpContext.Features.Set(context.OriginalSendFileFeature);

            // Remove IResponseCachingFeature
            RemoveResponseCachingFeature(context.HttpContext);
        }

        internal static bool ContentIsNotModified(ResponseCachingContext context)
        {
            var cachedResponseHeaders = context.CachedResponseHeaders;
            var ifNoneMatchHeader = context.HttpContext.Request.Headers[HeaderNames.IfNoneMatch];

            if (!StringValues.IsNullOrEmpty(ifNoneMatchHeader))
            {
                if (ifNoneMatchHeader.Count == 1 && StringSegment.Equals(ifNoneMatchHeader[0], EntityTagHeaderValue.Any.Tag, StringComparison.OrdinalIgnoreCase))
                {
                    context.Logger.LogNotModifiedIfNoneMatchStar();
                    return true;
                }

                EntityTagHeaderValue eTag;
                IList<EntityTagHeaderValue> ifNoneMatchEtags;
                if (!StringValues.IsNullOrEmpty(cachedResponseHeaders[HeaderNames.ETag])
                    && EntityTagHeaderValue.TryParse(cachedResponseHeaders[HeaderNames.ETag].ToString(), out eTag)
                    && EntityTagHeaderValue.TryParseList(ifNoneMatchHeader, out ifNoneMatchEtags))
                {
                    for (var i = 0; i < ifNoneMatchEtags.Count; i++)
                    {
                        var requestETag = ifNoneMatchEtags[i];
                        if (eTag.Compare(requestETag, useStrongComparison: false))
                        {
                            context.Logger.LogNotModifiedIfNoneMatchMatched(requestETag);
                            return true;
                        }
                    }
                }
            }
            else
            {
                var ifModifiedSince = context.HttpContext.Request.Headers[HeaderNames.IfModifiedSince];
                if (!StringValues.IsNullOrEmpty(ifModifiedSince))
                {
                    DateTimeOffset modified;
                    if (!HeaderUtilities.TryParseDate(cachedResponseHeaders[HeaderNames.LastModified].ToString(), out modified) &&
                        !HeaderUtilities.TryParseDate(cachedResponseHeaders[HeaderNames.Date].ToString(), out modified))
                    {
                        return false;
                    }

                    DateTimeOffset modifiedSince;
                    if (HeaderUtilities.TryParseDate(ifModifiedSince.ToString(), out modifiedSince) &&
                        modified <= modifiedSince)
                    {
                        context.Logger.LogNotModifiedIfModifiedSinceSatisfied(modified, modifiedSince);
                        return true;
                    }
                }
            }

            return false;
        }

        // Normalize order and casing
        internal static StringValues GetOrderCasingNormalizedStringValues(StringValues stringValues)
        {
            if (stringValues.Count == 1)
            {
                return new StringValues(stringValues.ToString().ToUpperInvariant());
            }
            else
            {
                var originalArray = stringValues.ToArray();
                var newArray = new string[originalArray.Length];

                for (var i = 0; i < originalArray.Length; i++)
                {
                    newArray[i] = originalArray[i].ToUpperInvariant();
                }

                // Since the casing has already been normalized, use Ordinal comparison
                Array.Sort(newArray, StringComparer.Ordinal);

                return new StringValues(newArray);
            }
        }
    }
}
