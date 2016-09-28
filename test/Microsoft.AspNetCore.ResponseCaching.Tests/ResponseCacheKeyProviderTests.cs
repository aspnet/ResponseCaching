// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Xunit;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class ResponseCacheKeyProviderTests
    {
        private static readonly char KeyDelimiter = '\x1e';

        [Fact]
        public void ResponseCacheKeyProvider_CreateBaseKey_IncludesOnlyNormalizedMethodAndPath()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.Method = "head";
            context.HttpContext.Request.Path = "/path/subpath";
            context.HttpContext.Request.Scheme = "https";
            context.HttpContext.Request.Host = new HostString("example.com", 80);
            context.HttpContext.Request.PathBase = "/pathBase";
            context.HttpContext.Request.QueryString = new QueryString("?query.Key=a&query.Value=b");

            Assert.Equal($"HEAD{KeyDelimiter}/PATH/SUBPATH", cacheKeyProvider.CreateBaseKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateBaseKey_CaseInsensitivePath_NormalizesPath()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider(new ResponseCacheOptions()
            {
                UseCaseSensitivePaths = false
            });
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.Method = "GET";
            context.HttpContext.Request.Path = "/Path";

            Assert.Equal($"GET{KeyDelimiter}/PATH", cacheKeyProvider.CreateBaseKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateBaseKey_CaseSensitivePath_PreservesPathCase()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider(new ResponseCacheOptions()
            {
                UseCaseSensitivePaths = true
            });
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.Method = "GET";
            context.HttpContext.Request.Path = "/Path";

            Assert.Equal($"GET{KeyDelimiter}/Path", cacheKeyProvider.CreateBaseKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_Throws_IfVaryByRulesIsNull()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();

            Assert.Throws<InvalidOperationException>(() => cacheKeyProvider.CreateVaryByKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_ReturnsCachedVaryByGuid_IfVaryByRulesIsEmpty()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.CachedVaryByRules = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString
            };

            Assert.Equal($"{context.CachedVaryByRules.VaryByKeyPrefix}", cacheKeyProvider.CreateVaryByKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_IncludesListedHeadersOnly()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.Headers["HeaderA"] = "ValueA";
            context.HttpContext.Request.Headers["HeaderB"] = "ValueB";
            context.CachedVaryByRules = new CachedVaryByRules()
            {
                Headers = new string[] { "HeaderA", "HeaderC" }
            };

            Assert.Equal($"{context.CachedVaryByRules.VaryByKeyPrefix}{KeyDelimiter}H{KeyDelimiter}HeaderA=ValueA{KeyDelimiter}HeaderC=",
                cacheKeyProvider.CreateVaryByKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_IncludesListedQueryKeysOnly()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.QueryString = new QueryString("?QueryA=ValueA&QueryB=ValueB");
            context.CachedVaryByRules = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                QueryKeys = new string[] { "QueryA", "QueryC" }
            };

            Assert.Equal($"{context.CachedVaryByRules.VaryByKeyPrefix}{KeyDelimiter}Q{KeyDelimiter}QueryA=ValueA{KeyDelimiter}QueryC=",
                cacheKeyProvider.CreateVaryByKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_IncludesQueryKeys_QueryKeyCaseInsensitive_UseQueryKeyCasing()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.QueryString = new QueryString("?queryA=ValueA&queryB=ValueB");
            context.CachedVaryByRules = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                QueryKeys = new string[] { "QueryA",  "QueryC" }
            };

            Assert.Equal($"{context.CachedVaryByRules.VaryByKeyPrefix}{KeyDelimiter}Q{KeyDelimiter}QueryA=ValueA{KeyDelimiter}QueryC=",
                cacheKeyProvider.CreateVaryByKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_IncludesAllQueryKeysGivenAsterisk()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.QueryString = new QueryString("?QueryA=ValueA&QueryB=ValueB");
            context.CachedVaryByRules = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                QueryKeys = new string[] { "*" }
            };

            // To support case insensitivity, all query keys are converted to upper case.
            // Explicit query keys uses the casing specified in the setting.
            Assert.Equal($"{context.CachedVaryByRules.VaryByKeyPrefix}{KeyDelimiter}Q{KeyDelimiter}QUERYA=ValueA{KeyDelimiter}QUERYB=ValueB",
                cacheKeyProvider.CreateVaryByKey(context));
        }

        [Fact]
        public void ResponseCacheKeyProvider_CreateVaryByKey_IncludesListedHeadersAndQueryKeys()
        {
            var cacheKeyProvider = TestUtils.CreateTestKeyProvider();
            var context = TestUtils.CreateTestContext();
            context.HttpContext.Request.Headers["HeaderA"] = "ValueA";
            context.HttpContext.Request.Headers["HeaderB"] = "ValueB";
            context.HttpContext.Request.QueryString = new QueryString("?QueryA=ValueA&QueryB=ValueB");
            context.CachedVaryByRules = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                Headers = new string[] { "HeaderA", "HeaderC" },
                QueryKeys = new string[] { "QueryA", "QueryC" }
            };

            Assert.Equal($"{context.CachedVaryByRules.VaryByKeyPrefix}{KeyDelimiter}H{KeyDelimiter}HeaderA=ValueA{KeyDelimiter}HeaderC={KeyDelimiter}Q{KeyDelimiter}QueryA=ValueA{KeyDelimiter}QueryC=",
                cacheKeyProvider.CreateVaryByKey(context));
        }
    }
}
