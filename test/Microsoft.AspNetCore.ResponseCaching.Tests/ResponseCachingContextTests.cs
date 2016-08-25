﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class ResponseCachingContextTests
    {
        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        public void RequestIsCacheable_CacheableMethods_Allowed(string method)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = method;
            var context = CreateTestContext(httpContext);

            Assert.True(context.RequestIsCacheable());
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("OPTIONS")]
        [InlineData("PUT")]
        [InlineData("DELETE")]
        [InlineData("TRACE")]
        [InlineData("CONNECT")]
        [InlineData("")]
        [InlineData(null)]
        public void RequestIsCacheable_UncacheableMethods_NotAllowed(string method)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = method;
            var context = CreateTestContext(httpContext);

            Assert.False(context.RequestIsCacheable());
        }

        [Fact]
        public void RequestIsCacheable_AuthorizationHeaders_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.Headers[HeaderNames.Authorization] = "Basic plaintextUN:plaintextPW";
            var context = CreateTestContext(httpContext);

            Assert.False(context.RequestIsCacheable());
        }

        [Fact]
        public void RequestIsCacheable_NoCache_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                NoCache = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.RequestIsCacheable());
        }

        [Fact]
        public void RequestIsCacheable_NoStore_Allowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                NoStore = true
            };
            var context = CreateTestContext(httpContext);

            Assert.True(context.RequestIsCacheable());
        }

        [Fact]
        public void RequestIsCacheable_LegacyDirectives_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.Headers[HeaderNames.Pragma] = "no-cache";
            var context = CreateTestContext(httpContext);

            Assert.False(context.RequestIsCacheable());
        }

        [Fact]
        public void RequestIsCacheable_LegacyDirectives_OverridenByCacheControl()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.Headers[HeaderNames.Pragma] = "no-cache";
            httpContext.Request.Headers[HeaderNames.CacheControl] = "max-age=10";
            var context = CreateTestContext(httpContext);

            Assert.True(context.RequestIsCacheable());
        }

        private class AllowUnrecognizedHTTPMethodRequests : IResponseCachingCacheabilityValidator
        {
            public OverrideResult RequestIsCacheableOverride(HttpContext httpContext) =>
                httpContext.Request.Method == "UNRECOGNIZED" ? OverrideResult.Cache : OverrideResult.DoNotCache;

            public OverrideResult ResponseIsCacheableOverride(HttpContext httpContext) => OverrideResult.UseDefaultLogic;
        }

        [Fact]
        public void RequestIsCacheableOverride_OverridesDefaultBehavior_ToAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "UNRECOGNIZED";
            var responseCachingContext = CreateTestContext(httpContext, new AllowUnrecognizedHTTPMethodRequests());

            Assert.True(responseCachingContext.RequestIsCacheable());
        }

        private class DisallowGetHTTPMethodRequests : IResponseCachingCacheabilityValidator
        {
            public OverrideResult RequestIsCacheableOverride(HttpContext httpContext) =>
                httpContext.Request.Method == "GET" ? OverrideResult.DoNotCache : OverrideResult.Cache;

            public OverrideResult ResponseIsCacheableOverride(HttpContext httpContext) => OverrideResult.UseDefaultLogic;
        }

        [Fact]
        public void RequestIsCacheableOverride_OverridesDefaultBehavior_ToNotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            var responseCachingContext = CreateTestContext(httpContext, new DisallowGetHTTPMethodRequests());

            Assert.False(responseCachingContext.RequestIsCacheable());
        }

        [Fact]
        public void RequestIsCacheableOverride_IgnoreFallsBackToDefaultBehavior()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            var responseCachingContext = CreateTestContext(httpContext, new NoopCacheabilityValidator());

            Assert.True(responseCachingContext.RequestIsCacheable());

            httpContext.Request.Method = "UNRECOGNIZED";

            Assert.False(responseCachingContext.RequestIsCacheable());
        }

        [Fact]
        public void CreateCacheKey_Includes_UppercaseMethodAndPath()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "head";
            httpContext.Request.Path = "/path/subpath";
            httpContext.Request.Scheme = "https";
            httpContext.Request.Host = new HostString("example.com", 80);
            httpContext.Request.PathBase = "/pathBase";
            httpContext.Request.QueryString = new QueryString("?query.Key=a&query.Value=b");
            var context = CreateTestContext(httpContext);

            Assert.Equal("HEAD;/PATH/SUBPATH", context.CreateCacheKey());
        }

        [Fact]
        public void CreateCacheKey_Includes_ListedVaryByHeadersOnly()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/";
            httpContext.Request.Headers["HeaderA"] = "ValueA";
            httpContext.Request.Headers["HeaderB"] = "ValueB";
            var context = CreateTestContext(httpContext);

            Assert.Equal("GET;/;HeaderA=ValueA;HeaderC=null", context.CreateCacheKey(new CachedVaryBy()
            {
                Headers = new string[] { "HeaderA", "HeaderC" }
            }));
        }

        private class CustomizeKeySuffixProvider : IResponseCachingCacheKeySuffixProvider
        {
            public string CreateCustomKeySuffix(HttpContext httpContext) => "CustomizedKey";
        }

        [Fact]
        public void CreateCacheKey_OptionalCacheKey_AppendedToDefaultKey()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Method = "GET";
            httpContext.Request.Path = "/";
            httpContext.Request.Headers["HeaderA"] = "ValueA";
            httpContext.Request.Headers["HeaderB"] = "ValueB";
            var responseCachingContext = CreateTestContext(httpContext, new CustomizeKeySuffixProvider());

            Assert.Equal("GET;/;HeaderA=ValueA;HeaderC=null;CustomizedKey", responseCachingContext.CreateCacheKey(new CachedVaryBy()
            {
                Headers = new string[] { "HeaderA", "HeaderC" }
            }));
        }

        [Fact]
        public void ResponseIsCacheable_NoPublic_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheable_Public_Allowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var context = CreateTestContext(httpContext);

            Assert.True(context.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheable_NoCache_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true,
                NoCache = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheable_RequestNoStore_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                NoStore = true
            };
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheable_ResponseNoStore_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true,
                NoStore = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheable_VaryByStar_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            httpContext.Response.Headers[HeaderNames.Vary] = "*";
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheable_Private_NotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true,
                Private = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        [Theory]
        [InlineData(StatusCodes.Status200OK)]
        public void ResponseIsCacheable_SuccessStatusCodes_Allowed(int statusCode)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var context = CreateTestContext(httpContext);

            Assert.True(context.ResponseIsCacheable());
        }

        [Theory]
        [InlineData(StatusCodes.Status201Created)]
        [InlineData(StatusCodes.Status202Accepted)]
        [InlineData(StatusCodes.Status203NonAuthoritative)]
        [InlineData(StatusCodes.Status204NoContent)]
        [InlineData(StatusCodes.Status205ResetContent)]
        [InlineData(StatusCodes.Status206PartialContent)]
        [InlineData(StatusCodes.Status207MultiStatus)]
        [InlineData(StatusCodes.Status300MultipleChoices)]
        [InlineData(StatusCodes.Status301MovedPermanently)]
        [InlineData(StatusCodes.Status302Found)]
        [InlineData(StatusCodes.Status303SeeOther)]
        [InlineData(StatusCodes.Status304NotModified)]
        [InlineData(StatusCodes.Status305UseProxy)]
        [InlineData(StatusCodes.Status306SwitchProxy)]
        [InlineData(StatusCodes.Status307TemporaryRedirect)]
        [InlineData(StatusCodes.Status308PermanentRedirect)]
        [InlineData(StatusCodes.Status400BadRequest)]
        [InlineData(StatusCodes.Status401Unauthorized)]
        [InlineData(StatusCodes.Status402PaymentRequired)]
        [InlineData(StatusCodes.Status403Forbidden)]
        [InlineData(StatusCodes.Status404NotFound)]
        [InlineData(StatusCodes.Status405MethodNotAllowed)]
        [InlineData(StatusCodes.Status406NotAcceptable)]
        [InlineData(StatusCodes.Status407ProxyAuthenticationRequired)]
        [InlineData(StatusCodes.Status408RequestTimeout)]
        [InlineData(StatusCodes.Status409Conflict)]
        [InlineData(StatusCodes.Status410Gone)]
        [InlineData(StatusCodes.Status411LengthRequired)]
        [InlineData(StatusCodes.Status412PreconditionFailed)]
        [InlineData(StatusCodes.Status413RequestEntityTooLarge)]
        [InlineData(StatusCodes.Status414RequestUriTooLong)]
        [InlineData(StatusCodes.Status415UnsupportedMediaType)]
        [InlineData(StatusCodes.Status416RequestedRangeNotSatisfiable)]
        [InlineData(StatusCodes.Status417ExpectationFailed)]
        [InlineData(StatusCodes.Status418ImATeapot)]
        [InlineData(StatusCodes.Status419AuthenticationTimeout)]
        [InlineData(StatusCodes.Status422UnprocessableEntity)]
        [InlineData(StatusCodes.Status423Locked)]
        [InlineData(StatusCodes.Status424FailedDependency)]
        [InlineData(StatusCodes.Status451UnavailableForLegalReasons)]
        [InlineData(StatusCodes.Status500InternalServerError)]
        [InlineData(StatusCodes.Status501NotImplemented)]
        [InlineData(StatusCodes.Status502BadGateway)]
        [InlineData(StatusCodes.Status503ServiceUnavailable)]
        [InlineData(StatusCodes.Status504GatewayTimeout)]
        [InlineData(StatusCodes.Status505HttpVersionNotsupported)]
        [InlineData(StatusCodes.Status506VariantAlsoNegotiates)]
        [InlineData(StatusCodes.Status507InsufficientStorage)]
        public void ResponseIsCacheable_NonSuccessStatusCodes_NotAllowed(int statusCode)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.StatusCode = statusCode;
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.ResponseIsCacheable());
        }

        private class Allow500Response : IResponseCachingCacheabilityValidator
        {
            public OverrideResult RequestIsCacheableOverride(HttpContext httpContext) => OverrideResult.UseDefaultLogic;

            public OverrideResult ResponseIsCacheableOverride(HttpContext httpContext) =>
                httpContext.Response.StatusCode == StatusCodes.Status500InternalServerError ? OverrideResult.Cache : OverrideResult.DoNotCache;
        }

        [Fact]
        public void ResponseIsCacheableOverride_OverridesDefaultBehavior_ToAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var responseCachingContext = CreateTestContext(httpContext, new Allow500Response());

            Assert.True(responseCachingContext.ResponseIsCacheable());
        }

        private class Disallow200Response : IResponseCachingCacheabilityValidator
        {
            public OverrideResult RequestIsCacheableOverride(HttpContext httpContext) => OverrideResult.UseDefaultLogic;

            public OverrideResult ResponseIsCacheableOverride(HttpContext httpContext) =>
                httpContext.Response.StatusCode == StatusCodes.Status200OK ? OverrideResult.DoNotCache : OverrideResult.Cache;
        }

        [Fact]
        public void ResponseIsCacheableOverride_OverridesDefaultBehavior_ToNotAllowed()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var responseCachingContext = CreateTestContext(httpContext, new Disallow200Response());

            Assert.False(responseCachingContext.ResponseIsCacheable());
        }

        [Fact]
        public void ResponseIsCacheableOverride_IgnoreFallsBackToDefaultBehavior()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                Public = true
            };
            var responseCachingContext = CreateTestContext(httpContext, new NoopCacheabilityValidator());

            Assert.True(responseCachingContext.ResponseIsCacheable());

            httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

            Assert.False(responseCachingContext.ResponseIsCacheable());
        }

        [Fact]
        public void EntryIsFresh_NoExpiryRequirements_IsFresh()
        {
            var httpContext = new DefaultHttpContext();
            var context = CreateTestContext(httpContext);

            Assert.True(context.EntryIsFresh(new ResponseHeaders(new HeaderDictionary()), TimeSpan.MaxValue, verifyAgainstRequest: false));
        }

        [Fact]
        public void EntryIsFresh_PastExpiry_IsNotFresh()
        {
            var httpContext = new DefaultHttpContext();
            var utcNow = DateTimeOffset.UtcNow;
            httpContext.Response.GetTypedHeaders().Expires = utcNow;
            var context = CreateTestContext(httpContext);
            context._responseTime = utcNow;

            Assert.False(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.MaxValue, verifyAgainstRequest: false));
        }

        [Fact]
        public void EntryIsFresh_MaxAgeOverridesExpiry_ToFresh()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var httpContext = new DefaultHttpContext();

            var responseHeaders = httpContext.Response.GetTypedHeaders();
            responseHeaders.Expires = utcNow;
            responseHeaders.CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10)
            };

            var context = CreateTestContext(httpContext);
            context._responseTime = utcNow;

            Assert.True(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(10), verifyAgainstRequest: false));
        }

        [Fact]
        public void EntryIsFresh_MaxAgeOverridesExpiry_ToNotFresh()
        {
            var utcNow = DateTimeOffset.UtcNow;
            var httpContext = new DefaultHttpContext();

            var responseHeaders = httpContext.Response.GetTypedHeaders();
            responseHeaders.Expires = utcNow;
            responseHeaders.CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10)
            };

            var context = CreateTestContext(httpContext);
            context._responseTime = utcNow;

            Assert.False(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(11), verifyAgainstRequest: false));
        }

        [Fact]
        public void EntryIsFresh_SharedMaxAgeOverridesMaxAge_ToFresh()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10),
                SharedMaxAge = TimeSpan.FromSeconds(15)
            };
            var context = CreateTestContext(httpContext);

            Assert.True(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(11), verifyAgainstRequest: false));
        }

        [Fact]
        public void EntryIsFresh_SharedMaxAgeOverridesMaxAge_ToNotFresh()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10),
                SharedMaxAge = TimeSpan.FromSeconds(5)
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(6), verifyAgainstRequest: false));
        }

        [Fact]
        public void EntryIsFresh_MinFreshReducesFreshness_ToNotFresh()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MinFresh = TimeSpan.FromSeconds(3)
            };
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10),
                SharedMaxAge = TimeSpan.FromSeconds(5)
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(3), verifyAgainstRequest: true));
        }

        [Fact]
        public void EntryIsFresh_RequestMaxAgeRestrictAge_ToNotFresh()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(5)
            };
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10),
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(6), verifyAgainstRequest: true));
        }

        [Fact]
        public void EntryIsFresh_MaxStaleOverridesFreshness_ToFresh()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(5),
                MaxStale = true, // This value must be set to true in order to specify MaxStaleLimit
                MaxStaleLimit = TimeSpan.FromSeconds(10)
            };
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(5),
            };
            var context = CreateTestContext(httpContext);

            Assert.True(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(6), verifyAgainstRequest: true));
        }

        [Fact]
        public void EntryIsFresh_MustRevalidateOverridesRequestMaxStale_ToNotFresh()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(5),
                MaxStale = true, // This value must be set to true in order to specify MaxStaleLimit
                MaxStaleLimit = TimeSpan.FromSeconds(10)
            };
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(5),
                MustRevalidate = true
            };
            var context = CreateTestContext(httpContext);

            Assert.False(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(6), verifyAgainstRequest: true));
        }

        [Fact]
        public void EntryIsFresh_IgnoresRequestVerificationWhenSpecified()
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MinFresh = TimeSpan.FromSeconds(1),
                MaxAge = TimeSpan.FromSeconds(3)
            };
            httpContext.Response.GetTypedHeaders().CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromSeconds(10),
                SharedMaxAge = TimeSpan.FromSeconds(5)
            };
            var context = CreateTestContext(httpContext);

            Assert.True(context.EntryIsFresh(httpContext.Response.GetTypedHeaders(), TimeSpan.FromSeconds(3), verifyAgainstRequest: false));
        }

        private static ResponseCachingContext CreateTestContext(HttpContext httpContext)
        {
            return CreateTestContext(
                httpContext,
                new NoopCacheKeySuffixProvider(),
                new NoopCacheabilityValidator());
        }

        private static ResponseCachingContext CreateTestContext(HttpContext httpContext, IResponseCachingCacheKeySuffixProvider cacheKeySuffixProvider)
        {
            return CreateTestContext(
                httpContext,
                cacheKeySuffixProvider,
                new NoopCacheabilityValidator());
        }

        private static ResponseCachingContext CreateTestContext(HttpContext httpContext, IResponseCachingCacheabilityValidator cacheabilityValidator)
        {
            return CreateTestContext(
                httpContext,
                new NoopCacheKeySuffixProvider(),
                cacheabilityValidator);
        }

        private static ResponseCachingContext CreateTestContext(
            HttpContext httpContext,
            IResponseCachingCacheKeySuffixProvider cacheKeySuffixProvider,
            IResponseCachingCacheabilityValidator cacheabilityValidator)
        {
            return new ResponseCachingContext(
                httpContext,
                new TestResponseCache(),
                new SystemClock(),
                null,
                cacheabilityValidator,
                cacheKeySuffixProvider);
        }

        private class TestResponseCache : IResponseCache
        {
            public object Get(string key)
            {
                return null;
            }

            public void Remove(string key)
            {
            }

            public void Set(string key, object entry, TimeSpan validFor)
            {
            }
        }

        private class TestHttpSendFileFeature : IHttpSendFileFeature
        {
            public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellation)
            {
                return Task.FromResult(0);
            }
        }
    }
}
