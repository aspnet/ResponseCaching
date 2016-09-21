// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class CacheEntrySerializerTests
    {
        [Fact]
        public void Serialize_NullObject_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => CacheEntrySerializer.Serialize(null));
        }

        private class UnknownResponseCacheEntry : IResponseCacheEntry
        {
        }

        [Fact]
        public void Serialize_UnknownObject_Throws()
        {
            Assert.Throws<NotSupportedException>(() => CacheEntrySerializer.Serialize(new UnknownResponseCacheEntry()));
        }

        [Fact]
        public void Deserialize_NullObject_ReturnsNull()
        {
            Assert.Null(CacheEntrySerializer.Deserialize(null));
        }

        [Fact]
        public void RoundTrip_CachedResponseWithBody_Succeeds()
        {
            var headers = new HeaderDictionary();
            headers["keyA"] = "valueA";
            headers["keyB"] = "valueB";
            var body = Encoding.ASCII.GetBytes("Hello world");
            var cachedResponse = new CachedResponse()
            {
                Created = DateTimeOffset.UtcNow,
                StatusCode = StatusCodes.Status200OK,
                BodyKeyPrefix = FastGuid.NewGuid().IdString,
                Body = new ResponseCacheStream(new List<byte[]>(new []{ body }), body.Length, body.Length),
                Headers = headers
            };

            AssertCachedResponseEqual(cachedResponse, (CachedResponse)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedResponse)));
        }

        [Fact]
        public void RoundTrip_CachedResponseWithMultivalueHeaders_Succeeds()
        {
            var headers = new HeaderDictionary();
            headers["keyA"] = new StringValues(new[] { "ValueA", "ValueB" });
            var body = Encoding.ASCII.GetBytes("Hello world");
            var cachedResponse = new CachedResponse()
            {
                BodyKeyPrefix = FastGuid.NewGuid().IdString,
                Created = DateTimeOffset.UtcNow,
                StatusCode = StatusCodes.Status200OK,
                Body = new ResponseCacheStream(new List<byte[]>(new[] { body }), body.Length, body.Length),
                Headers = headers
            };

            AssertCachedResponseEqual(cachedResponse, (CachedResponse)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedResponse)));
        }

        [Fact]
        public void RoundTrip_CachedResponseWithEmptyHeaders_Succeeds()
        {
            var headers = new HeaderDictionary();
            headers["keyA"] = StringValues.Empty;
            var body = Encoding.ASCII.GetBytes("Hello world");
            var cachedResponse = new CachedResponse()
            {
                BodyKeyPrefix = FastGuid.NewGuid().IdString,
                Created = DateTimeOffset.UtcNow,
                StatusCode = StatusCodes.Status200OK,
                Body = new ResponseCacheStream(new List<byte[]>(new[] { body }), body.Length, body.Length),
                Headers = headers
            };

            AssertCachedResponseEqual(cachedResponse, (CachedResponse)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedResponse)));
        }

        [Fact]
        public void RoundTrip_CachedVaryByRule_EmptyRules_Succeeds()
        {
            var cachedVaryByRule = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString
            };

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedVaryByRule)));
        }

        [Fact]
        public void RoundTrip_CachedVaryByRule_HeadersOnly_Succeeds()
        {
            var headers = new[] { "headerA", "headerB" };
            var cachedVaryByRule = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                Headers = headers
            };

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedVaryByRule)));
        }

        [Fact]
        public void RoundTrip_CachedVaryByRule_QueryKeysOnly_Succeeds()
        {
            var queryKeys = new[] { "queryA", "queryB" };
            var cachedVaryByRule = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                QueryKeys = queryKeys
            };

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedVaryByRule)));
        }

        [Fact]
        public void RoundTrip_CachedVaryByRule_HeadersAndQueryKeys_Succeeds()
        {
            var headers = new[] { "headerA", "headerB" };
            var queryKeys = new[] { "queryA", "queryB" };
            var cachedVaryByRule = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                Headers = headers,
                QueryKeys = queryKeys
            };

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)CacheEntrySerializer.Deserialize(CacheEntrySerializer.Serialize(cachedVaryByRule)));
        }

        [Fact]
        public void Deserialize_InvalidEntries_ReturnsNull()
        {
            var headers = new[] { "headerA", "headerB" };
            var cachedVaryByRule = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString,
                Headers = headers
            };
            var serializedEntry = CacheEntrySerializer.Serialize(cachedVaryByRule);
            Array.Reverse(serializedEntry);

            Assert.Null(CacheEntrySerializer.Deserialize(serializedEntry));
        }

        private static void AssertCachedResponseEqual(CachedResponse expected, CachedResponse actual)
        {
            Assert.NotNull(actual);
            Assert.NotNull(expected);
            Assert.Equal(expected.Created, actual.Created);
            Assert.Equal(expected.StatusCode, actual.StatusCode);
            Assert.Equal(expected.Headers.Count, actual.Headers.Count);
            foreach (var expectedHeader in expected.Headers)
            {
                Assert.Equal(expectedHeader.Value, actual.Headers[expectedHeader.Key]);
            }
            Assert.Equal(expected.BodyKeyPrefix, actual.BodyKeyPrefix);
            Assert.Equal(expected.Body.Length, actual.Body.Length);
            // Comparing capacity here since shards are retrieved separately
            Assert.Equal(expected.Body.FinalizedShards.Capacity, actual.Body.FinalizedShards.Capacity);
        }

        private static void AssertCachedVaryByRuleEqual(CachedVaryByRules expected, CachedVaryByRules actual)
        {
            Assert.NotNull(actual);
            Assert.NotNull(expected);
            Assert.Equal(expected.VaryByKeyPrefix, actual.VaryByKeyPrefix);
            Assert.Equal(expected.Headers, actual.Headers);
            Assert.Equal(expected.QueryKeys, actual.QueryKeys);
        }
    }
}
