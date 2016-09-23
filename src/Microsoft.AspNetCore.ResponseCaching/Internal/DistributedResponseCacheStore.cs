// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    public class DistributedResponseCacheStore : IResponseCacheStore
    {
        private readonly IDistributedCache _cache;
        private readonly DistributedResponseCacheStoreOptions _options;

        public DistributedResponseCacheStore(IDistributedCache cache, IOptions<DistributedResponseCacheStoreOptions> options)
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
            _options = options.Value; // This store needs its own options
        }

        public async Task<IResponseCacheEntry> GetAsync(string key)
        {
            try
            {
                var entry = ResponseCacheEntrySerializer.Deserialize(await _cache.GetAsync(key));

                if (entry is SerializableCachedResponse)
                {
                    var serializableCachedResponse = (SerializableCachedResponse) entry;
                    serializableCachedResponse.CachedResponse.Body = new DistributedCacheOutput(
                        _cache,
                        serializableCachedResponse.ShardKeyPrefix,
                        serializableCachedResponse.ShardCount,
                        serializableCachedResponse.BodyLength);

                    return serializableCachedResponse.CachedResponse;
                }
                else
                {
                    return entry;
                }
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
                if (entry is CachedResponse)
                {
                    var cachedResponse = (CachedResponse) entry;
                    var serializableCachedResponse = new SerializableCachedResponse()
                    {
                        CachedResponse = cachedResponse,
                        ShardKeyPrefix = FastGuid.NewGuid().IdString,
                        ShardCount = (cachedResponse.Body.Length + _options.DistributedCacheBodyShardSize - 1) / _options.DistributedCacheBodyShardSize,
                        BodyLength = cachedResponse.Body.Length
                    };

                    await _cache.SetAsync(
                        key,
                        ResponseCacheEntrySerializer.Serialize(serializableCachedResponse),
                        new DistributedCacheEntryOptions()
                        {
                            AbsoluteExpirationRelativeToNow = validFor
                        });

                    for (var i = 0; i < serializableCachedResponse.ShardCount; i++)
                    {
                        // TODO: doesn't need a new shard every time?
                        var shard = new byte[_options.DistributedCacheBodyShardSize];
                        var bytesRead = cachedResponse.Body.Read(shard, 0, _options.DistributedCacheBodyShardSize);

                        // The last shard may not be full
                        if (bytesRead != _options.DistributedCacheBodyShardSize)
                        {
                            var partialShard = new byte[bytesRead];
                            Array.Copy(shard, partialShard, bytesRead);
                            shard = partialShard;
                        }

                        await _cache.SetAsync(
                            serializableCachedResponse.ShardKeyPrefix + i,
                            shard,
                            new DistributedCacheEntryOptions()
                            {
                                AbsoluteExpirationRelativeToNow = validFor
                            });
                    }
                }
                else
                {
                    await _cache.SetAsync(
                       key,
                       ResponseCacheEntrySerializer.Serialize(entry),
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