// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    public class MemoryResponseCacheStore : IResponseCacheStore
    {
        private readonly IMemoryCache _cache;
        private readonly ResponseCacheOptions _options;

        public MemoryResponseCacheStore(IMemoryCache cache, IOptions<ResponseCacheOptions> options)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _cache = cache;
            _options = options.Value;
        }

        public Task<IResponseCacheEntry> GetAsync(string key)
        {
            var entry = _cache.Get(key);

            if (entry is MemoryCachedResponse)
            {
                var memoryCachedResponse = (MemoryCachedResponse)entry;
                return Task.FromResult<IResponseCacheEntry>(new CachedResponse()
                {
                    Created = memoryCachedResponse.Created,
                    StatusCode = memoryCachedResponse.StatusCode,
                    Headers = memoryCachedResponse.Headers,
                    Body = new ReadOnlyShardStream(memoryCachedResponse.Shards, memoryCachedResponse.BodyLength)
                });
            }
            else
            {
                return Task.FromResult(entry as IResponseCacheEntry);
            }
        }

        public async Task SetAsync(string key, IResponseCacheEntry entry, TimeSpan validFor)
        {
            if (entry is CachedResponse)
            {
                var cachedResponse = (CachedResponse)entry;
                var shardStream = new WriteOnlyShardStream(_options.BodyShardSize);
                await cachedResponse.Body.CopyToAsync(shardStream);

                _cache.Set(
                    key,
                    new MemoryCachedResponse()
                    {
                        Created = cachedResponse.Created,
                        StatusCode = cachedResponse.StatusCode,
                        Headers = cachedResponse.Headers,
                        Shards = shardStream.Shards,
                        BodyLength = shardStream.Length
                    },
                    new MemoryCacheEntryOptions()
                    {
                        AbsoluteExpirationRelativeToNow = validFor
                    });
            }
            else
            {
                _cache.Set(
                    key,
                    entry,
                    new MemoryCacheEntryOptions()
                    {
                        AbsoluteExpirationRelativeToNow = validFor
                    });
            }
        }
    }
}