// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal class DistributedCacheOutput : Stream
    {
        private readonly IDistributedCache _cache;
        private readonly string _shardKeyPrefix;
        private readonly long _bodyLength;
        private readonly long _shardCount;

        // Note: Only CopyToAsync is supported on this stream.
        internal DistributedCacheOutput(IDistributedCache cache, string shardKeyPrefix, long shardCount, long bodyLength)
        {
            if (cache == null)
            {
                throw new ArgumentNullException(nameof(cache));
            }

            _cache = cache;
            _shardKeyPrefix = shardKeyPrefix;
            _shardCount = shardCount;
            _bodyLength = bodyLength;
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _bodyLength;

        public override long Position
        {
            get
            {
                throw new NotSupportedException("The stream does not support seeking.");
            }
            set
            {
                throw new NotSupportedException("The stream does not support seeking.");
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException("The stream does not support writing.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The stream does not support reading.");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException("The stream does not support seeking.");
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("The stream does not support seeking or writing.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The stream does not support writing.");
        }

        public override async Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (!destination.CanWrite)
            {
                throw new NotSupportedException("The destination stream does not support writing.");
            }
            // TODO: buffer size?

            for (var i = 0; i < _shardCount; i++)
            {
                var shard = await _cache.GetAsync(_shardKeyPrefix + i);
                await destination.WriteAsync(shard, 0, shard.Length);
            }
        }
    }
}
