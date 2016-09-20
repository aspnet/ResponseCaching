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

        public async Task<object> GetAsync(string key)
        {
            try
            {
                var entry = CacheEntrySerializer.Deserialize(await _cache.GetAsync(key));

                var cachedResponse = entry as CachedResponse;
                if (cachedResponse != null)
                {
                    // TODO: parallelize
                    for (int i = 0; i < cachedResponse.Body.Shards.Capacity; i++)
                    {
                        var cachedResponseBodyShard = CacheEntrySerializer.DeserializeCachedResponseBodyShard(
                            await _cache.GetAsync(cachedResponse.BodyKeyPrefix + i));
                        cachedResponse.Body.Shards.Add(cachedResponseBodyShard.Shard);
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

        public async Task SetAsync(string key, object entry, TimeSpan validFor)
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
                    CacheEntrySerializer.Serialize(entry),
                    new DistributedCacheEntryOptions()
                    {
                        AbsoluteExpirationRelativeToNow = validFor
                    });

                if (cachedResponse != null)
                {
                    // TODO: parallelize
                    for (int i = 0; i < cachedResponse.Body.Shards.Count - 1; i++)
                    {
                        await _cache.SetAsync(
                            cachedResponse.BodyKeyPrefix + i,
                            CacheEntrySerializer.SerializeCachedResponseBodyShard(new CachedResponseBodyShard()
                            {
                                BodyKeyPrefix = cachedResponse.BodyKeyPrefix,
                                Shard = cachedResponse.Body.Shards[i]
                            }),
                            new DistributedCacheEntryOptions()
                            {
                                AbsoluteExpirationRelativeToNow = validFor
                            });
                    }

                    var partialShardLength = (int)(cachedResponse.Body.Length % cachedResponse.Body.BufferShardSize);
                    var partialShard = new byte[partialShardLength];
                    Array.Copy(cachedResponse.Body.Shards[cachedResponse.Body.Shards.Count - 1], partialShard, partialShardLength);

                    await _cache.SetAsync(
                        cachedResponse.BodyKeyPrefix + (cachedResponse.Body.Shards.Count - 1),
                        CacheEntrySerializer.SerializeCachedResponseBodyShard(new CachedResponseBodyShard()
                        {
                            BodyKeyPrefix = cachedResponse.BodyKeyPrefix,
                            Shard = partialShard
                        }),
                        new DistributedCacheEntryOptions()
                        {
                            AbsoluteExpirationRelativeToNow = validFor
                        });
                }
            }
            catch { }
        }
    }
}