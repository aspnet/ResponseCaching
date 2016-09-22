﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    public class DistributedResponseCacheStore : IResponseCacheStore
    {
        private readonly IDistributedCache _cache;
        private readonly ResponseCacheOptions _options;

        public DistributedResponseCacheStore(IDistributedCache cache, IOptions<ResponseCacheOptions> options)
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

                var cachedResponse = entry as CachedResponse;
                if (cachedResponse != null)
                {
                    // TODO: parallelize
                    var shardCount = (cachedResponse.Body.Length + _options.CachedBodyShardSize - 1) / _options.CachedBodyShardSize;
                    cachedResponse.Body = new CopyOnlyDistributedCacheStream(_cache, cachedResponse.BodyKeyPrefix, cachedResponse.Body.Length, shardCount);
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
                    var shardCount = (cachedResponse.Body.Length + _options.CachedBodyShardSize - 1) / _options.CachedBodyShardSize;

                    for (int i = 0; i < shardCount; i++)
                    {
                        var shard = new byte[_options.CachedBodyShardSize];
                        var bytesRead = cachedResponse.Body.Read(shard, 0, _options.CachedBodyShardSize);

                        // The last shard may not be full
                        if (bytesRead != _options.CachedBodyShardSize)
                        {
                            var partialShard = new byte[bytesRead];
                            Array.Copy(shard, partialShard, bytesRead);
                            shard = partialShard;
                        }

                        await _cache.SetAsync(
                            cachedResponse.BodyKeyPrefix + i,
                            shard,
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