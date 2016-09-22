// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    // TODO: naming and fill in the rest of the stream implementation?
    internal class CopyOnlyDistributedCacheStream : Stream
    {
        private readonly string _shardKeyPrefix;
        private readonly long _length;
        private long _shardCount;
        private IDistributedCache _cache;

        public CopyOnlyDistributedCacheStream(string shardKeyPrefix, long length)
        {
            _shardKeyPrefix = shardKeyPrefix;
            _length = length;
        }

        internal IDistributedCache Cache
        {
            private get
            {
                return _cache;
            }
            set
            {
                _cache = value;
            }
        }

        internal long ShardCount
        {
            private get
            {
                return _shardCount;
            }
            set
            {
                _shardCount = value;
            }
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
            // TODO: Check validity of parameters

            for (int i = 0; i < ShardCount; i++)
            {
                var shard = await Cache.GetAsync(_shardKeyPrefix + i);
                await destination.WriteAsync(shard, 0, shard.Length);
            }
        }
    }
}
