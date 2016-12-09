﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Internal;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class ResponseCachingMiddlewareTests
    {
        [Fact]
        public async Task TryServeFromCacheAsync_OnlyIfCached_Serves504()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache, keyProvider: new TestResponseCachingKeyProvider());
            var context = TestUtils.CreateTestContext();
            context.TypedRequestHeaders.CacheControl = new CacheControlHeaderValue()
            {
                OnlyIfCached = true
            };

            Assert.True(await middleware.TryServeFromCacheAsync(context));
            Assert.Equal(StatusCodes.Status504GatewayTimeout, context.HttpContext.Response.StatusCode);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.GatewayTimeoutServed);
        }

        [Fact]
        public async Task TryServeFromCacheAsync_CachedResponseNotFound_Fails()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache, keyProvider: new TestResponseCachingKeyProvider("BaseKey"));
            var context = TestUtils.CreateTestContext();

            Assert.False(await middleware.TryServeFromCacheAsync(context));
            Assert.Equal(1, cache.GetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NoResponseServed);
        }

        [Fact]
        public async Task TryServeFromCacheAsync_CachedResponseFound_Succeeds()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache, keyProvider: new TestResponseCachingKeyProvider("BaseKey"));
            var context = TestUtils.CreateTestContext();

            await cache.SetAsync(
                "BaseKey",
                new CachedResponse()
                {
                    Body = new SegmentReadStream(new List<byte[]>(0), 0)
                },
                TimeSpan.Zero);

            Assert.True(await middleware.TryServeFromCacheAsync(context));
            Assert.Equal(1, cache.GetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.CachedResponseServed);
        }

        [Fact]
        public async Task TryServeFromCacheAsync_VaryByRuleFound_CachedResponseNotFound_Fails()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache, keyProvider: new TestResponseCachingKeyProvider("BaseKey", "VaryKey"));
            var context = TestUtils.CreateTestContext();

            await cache.SetAsync(
                "BaseKey",
                new CachedVaryByRules(),
                TimeSpan.Zero);

            Assert.False(await middleware.TryServeFromCacheAsync(context));
            Assert.Equal(2, cache.GetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NoResponseServed);
        }

        [Fact]
        public async Task TryServeFromCacheAsync_VaryByRuleFound_CachedResponseFound_Succeeds()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache, keyProvider: new TestResponseCachingKeyProvider("BaseKey", new[] { "VaryKey", "VaryKey2" }));
            var context = TestUtils.CreateTestContext();

            await cache.SetAsync(
                "BaseKey",
                new CachedVaryByRules(),
                TimeSpan.Zero);
            await cache.SetAsync(
                "BaseKeyVaryKey2",
                new CachedResponse()
                {
                    Body = new SegmentReadStream(new List<byte[]>(0), 0)
                },
                TimeSpan.Zero);

            Assert.True(await middleware.TryServeFromCacheAsync(context));
            Assert.Equal(3, cache.GetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.CachedResponseServed);
        }

        [Fact]
        public async Task TryServeFromCacheAsync_CachedResponseFound_Serves304IfPossible()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache, keyProvider: new TestResponseCachingKeyProvider("BaseKey"));
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.Headers[HeaderNames.IfNoneMatch] = "*";

            await cache.SetAsync(
                "BaseKey",
                new CachedResponse()
                {
                    Body = new SegmentReadStream(new List<byte[]>(0), 0)
                },
                TimeSpan.Zero);

            Assert.True(await middleware.TryServeFromCacheAsync(context));
            Assert.Equal(1, cache.GetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NotModifiedServed);
        }

        [Fact]
        public void ContentIsNotModified_NotConditionalRequest_False()
        {
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary());

            Assert.False(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public void ContentIsNotModified_IfUnmodifiedSince_FallsbackToDateHeader()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary());

            context.TypedRequestHeaders.IfUnmodifiedSince = utcNow;

            // Verify modifications in the past succeeds
            context.CachedResponseHeaders.Date = utcNow - TimeSpan.FromSeconds(10);
            Assert.True(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Equal(1, sink.Writes.Count);

            // Verify modifications at present succeeds
            context.CachedResponseHeaders.Date = utcNow;
            Assert.True(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Equal(2, sink.Writes.Count);

            // Verify modifications in the future fails
            context.CachedResponseHeaders.Date = utcNow + TimeSpan.FromSeconds(10);
            Assert.False(ResponseCachingMiddleware.ContentIsNotModified(context));

            // Verify logging
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NotModifiedIfUnmodifiedSinceSatisfied,
                LoggedMessage.NotModifiedIfUnmodifiedSinceSatisfied);
        }

        [Fact]
        public void ContentIsNotModified_IfUnmodifiedSince_LastModifiedOverridesDateHeader()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary());

            context.TypedRequestHeaders.IfUnmodifiedSince = utcNow;

            // Verify modifications in the past succeeds
            context.CachedResponseHeaders.Date = utcNow + TimeSpan.FromSeconds(10);
            context.CachedResponseHeaders.LastModified = utcNow - TimeSpan.FromSeconds(10);
            Assert.True(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Equal(1, sink.Writes.Count);

            // Verify modifications at present
            context.CachedResponseHeaders.Date = utcNow + TimeSpan.FromSeconds(10);
            context.CachedResponseHeaders.LastModified = utcNow;
            Assert.True(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Equal(2, sink.Writes.Count);

            // Verify modifications in the future fails
            context.CachedResponseHeaders.Date = utcNow - TimeSpan.FromSeconds(10);
            context.CachedResponseHeaders.LastModified = utcNow + TimeSpan.FromSeconds(10);
            Assert.False(ResponseCachingMiddleware.ContentIsNotModified(context));

            // Verify logging
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NotModifiedIfUnmodifiedSinceSatisfied,
                LoggedMessage.NotModifiedIfUnmodifiedSinceSatisfied);
        }

        [Fact]
        public void ContentIsNotModified_IfNoneMatch_Overrides_IfUnmodifiedSince_ToTrue()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary());

            // This would fail the IfUnmodifiedSince checks
            context.TypedRequestHeaders.IfUnmodifiedSince = utcNow;
            context.CachedResponseHeaders.LastModified = utcNow + TimeSpan.FromSeconds(10);

            context.TypedRequestHeaders.IfNoneMatch = new List<EntityTagHeaderValue>(new[] { EntityTagHeaderValue.Any });
            Assert.True(ResponseCachingMiddleware.ContentIsNotModified(context));
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NotModifiedIfNoneMatchStar);
        }

        [Fact]
        public void ContentIsNotModified_IfNoneMatch_Overrides_IfUnmodifiedSince_ToFalse()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary());

            // This would pass the IfUnmodifiedSince checks
            context.TypedRequestHeaders.IfUnmodifiedSince = utcNow;
            context.CachedResponseHeaders.LastModified = utcNow - TimeSpan.FromSeconds(10);

            context.TypedRequestHeaders.IfNoneMatch = new List<EntityTagHeaderValue>(new[] { new EntityTagHeaderValue("\"E1\"") });
            Assert.False(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public void ContentIsNotModified_IfNoneMatch_AnyWithoutETagInResponse_False()
        {
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary());

            context.TypedRequestHeaders.IfNoneMatch = new List<EntityTagHeaderValue>(new[] { new EntityTagHeaderValue("\"E1\"") });

            Assert.False(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Empty(sink.Writes);
        }

        public static TheoryData<EntityTagHeaderValue, EntityTagHeaderValue> EquivalentWeakETags
        {
            get
            {
                return new TheoryData<EntityTagHeaderValue, EntityTagHeaderValue>
                {
                    { new EntityTagHeaderValue("\"tag\""), new EntityTagHeaderValue("\"tag\"") },
                    { new EntityTagHeaderValue("\"tag\"", true), new EntityTagHeaderValue("\"tag\"") },
                    { new EntityTagHeaderValue("\"tag\""), new EntityTagHeaderValue("\"tag\"", true) },
                    { new EntityTagHeaderValue("\"tag\"", true), new EntityTagHeaderValue("\"tag\"", true) }
                };
            }
        }

        [Theory]
        [MemberData(nameof(EquivalentWeakETags))]
        public void  ContentIsNotModified_IfNoneMatch_ExplicitWithMatch_True(EntityTagHeaderValue responseETag, EntityTagHeaderValue requestETag)
        {
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary())
            {
                ETag = responseETag
            };

            context.TypedRequestHeaders.IfNoneMatch = new List<EntityTagHeaderValue>(new[] { requestETag });

            Assert.True(ResponseCachingMiddleware.ContentIsNotModified(context));
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NotModifiedIfNoneMatchMatched);
        }

        [Fact]
        public void ContentIsNotModified_IfNoneMatch_ExplicitWithoutMatch_False()
        {
            var sink = new TestSink();
            var context = TestUtils.CreateTestContext(sink);
            context.CachedResponseHeaders = new ResponseHeaders(new HeaderDictionary())
            {
                ETag = new EntityTagHeaderValue("\"E2\"")
            };

            context.TypedRequestHeaders.IfNoneMatch = new List<EntityTagHeaderValue>(new[] { new EntityTagHeaderValue("\"E1\"") });

            Assert.False(ResponseCachingMiddleware.ContentIsNotModified(context));
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_DoNotUpdateShouldCacheResponse_IfResponseIsNotCacheable()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, policyProvider: new ResponseCachingPolicyProvider());
            var context = TestUtils.CreateTestContext();

            Assert.False(context.ShouldCacheResponse);

            middleware.ShimResponseStream(context);
            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.False(context.ShouldCacheResponse);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_UpdateShouldCacheResponse_IfResponseIsCacheable()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, policyProvider: new ResponseCachingPolicyProvider());
            var context = TestUtils.CreateTestContext();
            context.TypedResponseHeaders.CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };

            Assert.False(context.ShouldCacheResponse);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.True(context.ShouldCacheResponse);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_DefaultResponseValidity_Is10Seconds()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(TimeSpan.FromSeconds(10), context.CachedResponseValidFor);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_ResponseValidity_UseExpiryIfAvailable()
        {
            var utcNow = DateTimeOffset.MinValue;
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();

            context.ResponseTime = utcNow;
            context.TypedResponseHeaders.Expires = utcNow + TimeSpan.FromSeconds(11);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(TimeSpan.FromSeconds(11), context.CachedResponseValidFor);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_ResponseValidity_UseMaxAgeIfAvailable()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();
            context.TypedResponseHeaders.CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(12)
            };

            context.TypedResponseHeaders.Expires = context.ResponseTime + TimeSpan.FromSeconds(11);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(TimeSpan.FromSeconds(12), context.CachedResponseValidFor);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_ResponseValidity_UseSharedMaxAgeIfAvailable()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();
            context.TypedResponseHeaders.CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(12),
                SharedMaxAge = TimeSpan.FromSeconds(13)
            };

            context.TypedResponseHeaders.Expires = context.ResponseTime + TimeSpan.FromSeconds(11);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(TimeSpan.FromSeconds(13), context.CachedResponseValidFor);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_UpdateCachedVaryByRules_IfNotEquivalentToPrevious()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            context.HttpContext.Response.Headers[HeaderNames.Vary] = new StringValues(new[] { "headerA", "HEADERB", "HEADERc" });
            context.HttpContext.Features.Set<IResponseCachingFeature>(new ResponseCachingFeature()
            {
                VaryByQueryKeys = new StringValues(new[] { "queryB", "QUERYA" })
            });
            var cachedVaryByRules = new CachedVaryByRules()
            {
                Headers = new StringValues(new[] { "HeaderA", "HeaderB" }),
                QueryKeys = new StringValues(new[] { "QueryA", "QueryB" })
            };
            context.CachedVaryByRules = cachedVaryByRules;

            await middleware.TryServeFromCacheAsync(context);
            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(1, cache.SetCount);
            Assert.NotSame(cachedVaryByRules, context.CachedVaryByRules);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NoResponseServed,
                LoggedMessage.VaryByRulesUpdated);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_UpdateCachedVaryByRules_IfEquivalentToPrevious()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            context.HttpContext.Response.Headers[HeaderNames.Vary] = new StringValues(new[] { "headerA", "HEADERB" });
            context.HttpContext.Features.Set<IResponseCachingFeature>(new ResponseCachingFeature()
            {
                VaryByQueryKeys = new StringValues(new[] { "queryB", "QUERYA" })
            });
            var cachedVaryByRules = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                Headers = new StringValues(new[] { "HEADERA", "HEADERB" }),
                QueryKeys = new StringValues(new[] { "QUERYA", "QUERYB" })
            };
            context.CachedVaryByRules = cachedVaryByRules;

            await middleware.TryServeFromCacheAsync(context);
            await middleware.FinalizeCacheHeadersAsync(context);

            // An update to the cache is always made but the entry should be the same
            Assert.Equal(1, cache.SetCount);
            Assert.Same(cachedVaryByRules, context.CachedVaryByRules);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NoResponseServed,
                LoggedMessage.VaryByRulesUpdated);
        }

        public static TheoryData<StringValues> NullOrEmptyVaryRules
        {
            get
            {
                return new TheoryData<StringValues>
                {
                    default(StringValues),
                    StringValues.Empty,
                    new StringValues((string)null),
                    new StringValues(string.Empty),
                    new StringValues((string[])null),
                    new StringValues(new string[0]),
                    new StringValues(new string[] { null }),
                    new StringValues(new string[] { string.Empty })
                };
            }
        }

        [Theory]
        [MemberData(nameof(NullOrEmptyVaryRules))]
        public async Task FinalizeCacheHeaders_UpdateCachedVaryByRules_NullOrEmptyRules(StringValues vary)
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            context.HttpContext.Response.Headers[HeaderNames.Vary] = vary;
            context.HttpContext.Features.Set<IResponseCachingFeature>(new ResponseCachingFeature()
            {
                VaryByQueryKeys = vary
            });

            await middleware.TryServeFromCacheAsync(context);
            await middleware.FinalizeCacheHeadersAsync(context);

            // Vary rules should not be updated
            Assert.Equal(0, cache.SetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NoResponseServed);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_DoNotAddDate_IfSpecified()
        {
            var utcNow = DateTimeOffset.MinValue;
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();
            context.ResponseTime = utcNow;

            Assert.Null(context.TypedResponseHeaders.Date);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(utcNow, context.TypedResponseHeaders.Date);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_AddsDate_IfNoneSpecified()
        {
            var utcNow = DateTimeOffset.MinValue;
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();
            context.TypedResponseHeaders.Date = utcNow;
            context.ResponseTime = utcNow + TimeSpan.FromSeconds(10);

            Assert.Equal(utcNow, context.TypedResponseHeaders.Date);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(utcNow, context.TypedResponseHeaders.Date);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_StoresCachedResponse_InState()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();

            Assert.Null(context.CachedResponse);

            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.NotNull(context.CachedResponse);
            Assert.Empty(sink.Writes);
        }

        [Fact]
        public async Task FinalizeCacheHeaders_SplitsVaryHeaderByCommas()
        {
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink);
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Response.Headers[HeaderNames.Vary] = "HeaderB, heaDera";

            await middleware.TryServeFromCacheAsync(context);
            await middleware.FinalizeCacheHeadersAsync(context);

            Assert.Equal(new StringValues(new[] { "HEADERA", "HEADERB" }), context.CachedVaryByRules.Headers);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.NoResponseServed,
                LoggedMessage.VaryByRulesUpdated);
        }

        [Fact]
        public async Task FinalizeCacheBody_Cache_IfContentLengthMatches()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            middleware.ShimResponseStream(context);
            context.HttpContext.Response.ContentLength = 20;
            await context.HttpContext.Response.WriteAsync(new string('0', 20));

            context.ShouldCacheResponse = true;
            context.CachedResponse = new CachedResponse();
            context.BaseKey = "BaseKey";
            context.CachedResponseValidFor = TimeSpan.FromSeconds(10);

            await middleware.FinalizeCacheBodyAsync(context);

            Assert.Equal(1, cache.SetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.ResponseCached);
        }

        [Fact]
        public async Task FinalizeCacheBody_DoNotCache_IfContentLengthMismatches()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            middleware.ShimResponseStream(context);
            context.HttpContext.Response.ContentLength = 9;
            await context.HttpContext.Response.WriteAsync(new string('0', 10));

            context.ShouldCacheResponse = true;
            context.CachedResponse = new CachedResponse();
            context.BaseKey = "BaseKey";
            context.CachedResponseValidFor = TimeSpan.FromSeconds(10);

            await middleware.FinalizeCacheBodyAsync(context);

            Assert.Equal(0, cache.SetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.ResponseContentLengthMismatchNotCached);
        }

        [Fact]
        public async Task FinalizeCacheBody_Cache_IfContentLengthAbsent()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            middleware.ShimResponseStream(context);
            await context.HttpContext.Response.WriteAsync(new string('0', 10));

            context.ShouldCacheResponse = true;
            context.CachedResponse = new CachedResponse();
            context.BaseKey = "BaseKey";
            context.CachedResponseValidFor = TimeSpan.FromSeconds(10);

            await middleware.FinalizeCacheBodyAsync(context);

            Assert.Equal(1, cache.SetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.ResponseCached);
        }

        [Fact]
        public async Task FinalizeCacheBody_DoNotCache_IfShouldCacheResponseFalse()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            middleware.ShimResponseStream(context);
            await context.HttpContext.Response.WriteAsync(new string('0', 10));

            context.ShouldCacheResponse = false;

            await middleware.FinalizeCacheBodyAsync(context);

            Assert.Equal(0, cache.SetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.ResponseNotCached);
        }

        [Fact]
        public async Task FinalizeCacheBody_DoNotCache_IfBufferingDisabled()
        {
            var cache = new TestResponseCache();
            var sink = new TestSink();
            var middleware = TestUtils.CreateTestMiddleware(testSink: sink, cache: cache);
            var context = TestUtils.CreateTestContext();

            middleware.ShimResponseStream(context);
            await context.HttpContext.Response.WriteAsync(new string('0', 10));

            context.ShouldCacheResponse = true;
            context.ResponseCachingStream.DisableBuffering();

            await middleware.FinalizeCacheBodyAsync(context);

            Assert.Equal(0, cache.SetCount);
            TestUtils.AssertLoggedMessages(
                sink.Writes,
                LoggedMessage.ResponseNotCached);
        }

        [Fact]
        public void AddResponseCachingFeature_SecondInvocation_Throws()
        {
            var httpContext = new DefaultHttpContext();

            // Should not throw
            ResponseCachingMiddleware.AddResponseCachingFeature(httpContext);

            // Should throw
            Assert.ThrowsAny<InvalidOperationException>(() => ResponseCachingMiddleware.AddResponseCachingFeature(httpContext));
        }

        private class FakeResponseFeature : HttpResponseFeature
        {
            public override void OnStarting(Func<object, Task> callback, object state) { }
        }

        [Fact]
        public async Task Invoke_CacheableRequest_AddsResponseCachingFeature()
        {
            var responseCachingFeatureAdded = false;
            var middleware = TestUtils.CreateTestMiddleware(next: httpContext =>
            {
                responseCachingFeatureAdded = httpContext.Features.Get<IResponseCachingFeature>() != null;
                return TaskCache.CompletedTask;
            },
            policyProvider: new ResponseCachingPolicyProvider());

            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Get;
            context.Features.Set<IHttpResponseFeature>(new FakeResponseFeature());
            await middleware.Invoke(context);

            Assert.True(responseCachingFeatureAdded);
        }

        [Fact]
        public async Task Invoke_NonCacheableRequest_AddsResponseCachingFeature()
        {
            var responseCachingFeatureAdded = false;
            var middleware = TestUtils.CreateTestMiddleware(next: httpContext =>
            {
                responseCachingFeatureAdded = httpContext.Features.Get<IResponseCachingFeature>() != null;
                return TaskCache.CompletedTask;
            },
            policyProvider: new ResponseCachingPolicyProvider());

            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            await middleware.Invoke(context);

            Assert.True(responseCachingFeatureAdded);
        }

        [Fact]
        public void GetOrderCasingNormalizedStringValues_NormalizesCasingToUpper()
        {
            var uppercaseStrings = new StringValues(new[] { "STRINGA", "STRINGB" });
            var lowercaseStrings = new StringValues(new[] { "stringA", "stringB" });

            var normalizedStrings = ResponseCachingMiddleware.GetOrderCasingNormalizedStringValues(lowercaseStrings);

            Assert.Equal(uppercaseStrings, normalizedStrings);
        }

        [Fact]
        public void GetOrderCasingNormalizedStringValues_NormalizesOrder()
        {
            var orderedStrings = new StringValues(new[] { "STRINGA", "STRINGB" });
            var reverseOrderStrings = new StringValues(new[] { "STRINGB", "STRINGA" });

            var normalizedStrings = ResponseCachingMiddleware.GetOrderCasingNormalizedStringValues(reverseOrderStrings);

            Assert.Equal(orderedStrings, normalizedStrings);
        }

        [Fact]
        public void GetOrderCasingNormalizedStringValues_PreservesCommas()
        {
            var originalStrings = new StringValues(new[] { "STRINGA, STRINGB" });

            var normalizedStrings = ResponseCachingMiddleware.GetOrderCasingNormalizedStringValues(originalStrings);

            Assert.Equal(originalStrings, normalizedStrings);
        }
    }
}
