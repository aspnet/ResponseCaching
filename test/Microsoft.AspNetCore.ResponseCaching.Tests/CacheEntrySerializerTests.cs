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
            Assert.Throws<ArgumentNullException>(() => ResponseCacheEntrySerializer.Serialize(null));
        }

        private class UnknownResponseCacheEntry : IResponseCacheEntry
        {
        }

        [Fact]
        public void Serialize_UnknownObject_Throws()
        {
            Assert.Throws<NotSupportedException>(() => ResponseCacheEntrySerializer.Serialize(new UnknownResponseCacheEntry()));
        }

        [Fact]
        public void Deserialize_NullObject_ReturnsNull()
        {
            Assert.Null(ResponseCacheEntrySerializer.Deserialize(null));
        }

        [Fact]
        public void RoundTrip_CachedResponseWithBody_Succeeds()
        {
            var headers = new HeaderDictionary();
            headers["keyA"] = "valueA";
            headers["keyB"] = "valueB";
            var body = Encoding.ASCII.GetBytes("Hello world");
            var serializableCachedResponse = new SerializableCachedResponse()
            {
                CachedResponse = new CachedResponse()
                {
                    Created = DateTimeOffset.UtcNow,
                    StatusCode = StatusCodes.Status200OK,
                    Body = new ReadOnlyMemoryStream(new List<byte[]>(new[] { body }), body.Length),
                    Headers = headers
                },
                ShardKeyPrefix = FastGuid.NewGuid().IdString,
                BodyLength = body.Length,
                ShardCount = 2
            };

            AssertSerializableCachedResponseEqual(serializableCachedResponse, (SerializableCachedResponse)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(serializableCachedResponse)));
        }

        [Fact]
        public void RoundTrip_CachedResponseWithMultivalueHeaders_Succeeds()
        {
            var headers = new HeaderDictionary();
            headers["keyA"] = new StringValues(new[] { "ValueA", "ValueB" });
            var body = Encoding.ASCII.GetBytes("Hello world");
            var cachedResponse = new SerializableCachedResponse()
            {
                CachedResponse = new CachedResponse()
                {
                    Created = DateTimeOffset.UtcNow,
                    StatusCode = StatusCodes.Status200OK,
                    Body = new ReadOnlyMemoryStream(new List<byte[]>(new[] { body }), body.Length),
                    Headers = headers
                },
                ShardKeyPrefix = FastGuid.NewGuid().IdString,
                BodyLength = body.Length,
                ShardCount = 2
            };

            AssertSerializableCachedResponseEqual(cachedResponse, (SerializableCachedResponse)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(cachedResponse)));
        }

        [Fact]
        public void RoundTrip_CachedResponseWithEmptyHeaders_Succeeds()
        {
            var headers = new HeaderDictionary();
            headers["keyA"] = StringValues.Empty;
            var body = Encoding.ASCII.GetBytes("Hello world");
            var cachedResponse = new SerializableCachedResponse()
            {
                CachedResponse = new CachedResponse()
                {
                    Created = DateTimeOffset.UtcNow,
                    StatusCode = StatusCodes.Status200OK,
                    Body = new ReadOnlyMemoryStream(new List<byte[]>(new[] { body }), body.Length),
                    Headers = headers
                },
                ShardKeyPrefix = FastGuid.NewGuid().IdString,
                BodyLength = body.Length,
                ShardCount = 2
            };

            AssertSerializableCachedResponseEqual(cachedResponse, (SerializableCachedResponse)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(cachedResponse)));
        }

        [Fact]
        public void RoundTrip_CachedVaryByRule_EmptyRules_Succeeds()
        {
            var cachedVaryByRule = new CachedVaryByRules()
            {
                VaryByKeyPrefix = FastGuid.NewGuid().IdString
            };

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(cachedVaryByRule)));
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

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(cachedVaryByRule)));
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

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(cachedVaryByRule)));
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

            AssertCachedVaryByRuleEqual(cachedVaryByRule, (CachedVaryByRules)ResponseCacheEntrySerializer.Deserialize(ResponseCacheEntrySerializer.Serialize(cachedVaryByRule)));
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
            var serializedEntry = ResponseCacheEntrySerializer.Serialize(cachedVaryByRule);
            Array.Reverse(serializedEntry);

            Assert.Null(ResponseCacheEntrySerializer.Deserialize(serializedEntry));
        }

        private static void AssertSerializableCachedResponseEqual(SerializableCachedResponse expected, SerializableCachedResponse actual)
        {
            Assert.NotNull(actual);
            Assert.NotNull(expected);
            Assert.Equal(expected.CachedResponse.Created, actual.CachedResponse.Created);
            Assert.Equal(expected.CachedResponse.StatusCode, actual.CachedResponse.StatusCode);
            Assert.Equal(expected.CachedResponse.Headers.Count, actual.CachedResponse.Headers.Count);
            foreach (var expectedHeader in expected.CachedResponse.Headers)
            {
                Assert.Equal(expectedHeader.Value, actual.CachedResponse.Headers[expectedHeader.Key]);
            }
            Assert.Equal(expected.ShardKeyPrefix, actual.ShardKeyPrefix);
            Assert.Equal(expected.ShardCount, actual.ShardCount);
            Assert.Equal(expected.BodyLength, actual.BodyLength);
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
