// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Microsoft.AspNetCore.ResponseCaching
{
    public class CopyOnlyDistributedCacheStream : Stream
    {
        private readonly IDistributedCache _cache;
        private readonly string _shardKeyPrefix;
        private readonly long _length;
        private readonly long _shardCount;

        public CopyOnlyDistributedCacheStream(IDistributedCache cache, string shardKeyPrefix, long length, long shardCount)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            _cache = cache;
            _shardKeyPrefix = shardKeyPrefix;
            _length = length;
            _shardCount = shardCount;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get { return 0; }
            set { }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => 0;

        public override long Seek(long offset, SeekOrigin origin) => 0;

        public override void SetLength(long value) { }

        public override void Write(byte[] buffer, int offset, int count) { }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            for (int i = 0; i < _shardCount; i++)
            {
                var shard = await _cache.GetAsync(_shardKeyPrefix + i);
                await destination.WriteAsync(shard, 0, shard.Length);
            }
        }
    }
}
