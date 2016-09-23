// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal class BufferedOutput : Stream
    {
        private readonly List<byte[]> _shards;
        private readonly long _length;
        private int _shardPosition;
        private int _shardOffset;

        internal BufferedOutput(List<byte[]> shards, long length)
        {
            if (shards == null)
            {
                throw new ArgumentNullException(nameof(shards));
            }

            _shards = shards;
            _length = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get { throw new NotSupportedException("The stream does not support seeking."); }
            set { throw new NotSupportedException("The stream does not support seeking."); }
        }

        public override void Flush() { }

        // Note: Requires soft copies of cached entries on retrival from cache for concurrent stateful reads.
        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = 0;

            while (count > 0 && _shardPosition < _shards.Count && _shardOffset < _shards[_shardPosition].Length)
            {
                // Read up to the end of the shard
                var bytesToRead = Math.Min(count, _shards[_shardPosition].Length - _shardOffset);
                Array.Copy(_shards[_shardPosition], _shardOffset, buffer, offset, bytesToRead);
                bytesRead += bytesToRead;
                _shardOffset += bytesToRead;
                offset += bytesToRead;
                count -= bytesToRead;

                if (_shardOffset == _shards[_shardPosition].Length)
                {
                    // Move to the next shard
                    _shardPosition++;
                    _shardOffset = 0;
                }
            }

            return bytesRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException("The stream does not support seeking."); }

        public override void SetLength(long value) { throw new NotSupportedException("The stream does not support writing."); }

        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException("The stream does not support writing."); }

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

            foreach (var shard in _shards)
            {
                await destination.WriteAsync(shard, 0, shard.Length, cancellationToken);
            }
        }
    }
}
