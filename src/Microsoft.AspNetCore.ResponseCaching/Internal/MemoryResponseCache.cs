﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    public class MemoryResponseCache : IResponseCache
    {
        private readonly IMemoryCache _cache;

        public MemoryResponseCache(IMemoryCache cache)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            _cache = cache;
        }

        public IResponseCacheEntry Get(string key)
        {
            var entry = _cache.Get(key);

            var memoryCachedResponse = entry as MemoryCachedResponse;
            if (memoryCachedResponse != null)
            {
                return new CachedResponse
                {
                    Created = memoryCachedResponse.Created,
                    StatusCode = memoryCachedResponse.StatusCode,
                    Headers = memoryCachedResponse.Headers,
                    Body = new SegmentReadStream(memoryCachedResponse.BodySegments, memoryCachedResponse.BodyLength)
                };
            }
            else
            {
                return entry as IResponseCacheEntry;
            }
        }

        public Task<IResponseCacheEntry> GetAsync(string key)
        {
            return Task.FromResult(Get(key));
        }

        public void Set(string key, IResponseCacheEntry entry, TimeSpan validFor)
        {
            var cachedResponse = entry as CachedResponse;
            if (cachedResponse != null)
            {
                var segmentStream = new SegmentWriteStream(StreamUtilities.BodySegmentSize);
                cachedResponse.Body.CopyTo(segmentStream);

                _cache.Set(
                    key,
                    new MemoryCachedResponse
                    {
                        Created = cachedResponse.Created,
                        StatusCode = cachedResponse.StatusCode,
                        Headers = cachedResponse.Headers,
                        BodySegments = segmentStream.GetSegments(),
                        BodyLength = segmentStream.Length
                    },
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = validFor
                    });
            }
            else
            {
                _cache.Set(
                    key,
                    entry,
                    new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = validFor
                    });
            }
        }

        public Task SetAsync(string key, IResponseCacheEntry entry, TimeSpan validFor)
        {
            Set(key, entry, validFor);
            return Task.CompletedTask;
        }
    }
}