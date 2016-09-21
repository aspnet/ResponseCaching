// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    public class DistributedResponseCacheStore : IResponseCacheStore
    {
        private readonly IDistributedCache _cache;

        public DistributedResponseCacheStore(IDistributedCache cache)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            _cache = cache;
        }

        public async Task<IResponseCacheEntry> GetAsync(string key)
        {
            try
            {
                var entry = ResponseCacheEntrySerializer.Deserialize(await _cache.GetAsync(key));

                var cachedResponse = entry as CachedResponse;
                if (cachedResponse != null)
                {
                    // TODO: parallelize
                    var shards = cachedResponse.Body.FinalizedShards;
                    for (int i = 0; i < shards.Capacity; i++)
                    {
                        shards.Add(await _cache.GetAsync(cachedResponse.BodyKeyPrefix + i));
                    }
                }
                return entry;
            }
            catch
            {
                return null;
            }
        }

        public async Task RemoveAsync(string key)
        {
            try
            {
                await _cache.RemoveAsync(key);
            }
            catch { }
        }

        public async Task SetAsync(string key, IResponseCacheEntry entry, TimeSpan validFor)
        {
            try
            {
                var cachedResponse = entry as CachedResponse;
                if (cachedResponse != null)
                {
                    cachedResponse.BodyKeyPrefix = FastGuid.NewGuid().IdString;
                }

                await _cache.SetAsync(
                    key,
                    ResponseCacheEntrySerializer.Serialize(entry),
                    new DistributedCacheEntryOptions()
                    {
                        AbsoluteExpirationRelativeToNow = validFor
                    });

                if (cachedResponse != null)
                {
                    // TODO: parallelize
                    var shards = cachedResponse.Body.FinalizedShards;
                    for (int i = 0; i < shards.Count; i++)
                    {
                        await _cache.SetAsync(
                            cachedResponse.BodyKeyPrefix + i,
                            shards[i],
                            new DistributedCacheEntryOptions()
                            {
                                AbsoluteExpirationRelativeToNow = validFor
                            });
                    }
                }
            }
            catch { }
        }
    }
}