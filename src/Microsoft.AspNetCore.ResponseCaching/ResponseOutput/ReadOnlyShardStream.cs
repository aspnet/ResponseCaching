// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal class ReadOnlyShardStream : Stream
    {
        private readonly List<byte[]> _shards;
        private readonly long _length;
        private readonly int _shardSize;
        private int _shardPosition;
        private int _shardOffset;

        internal ReadOnlyShardStream(List<byte[]> shards, int shardSize, long length)
        {
            if (shards == null)
            {
                throw new ArgumentNullException(nameof(shards));
            }

            _shards = shards;
            _shardSize = shardSize;
            _length = length;
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => _length;

        public override long Position
        {
            get
            {
                return _shardPosition * _shardSize + _shardOffset;
            }
            set
            {
                if (value < 0 || value > Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"The Position must be within the length of the Stream: {Length}");
                }

                _shardOffset = (int)(value % _shardSize);
                _shardPosition = (int)(value / _shardSize);
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException("The stream does not support writing.");
        }

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

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin)
            {
                Position = offset;
            }
            else if (origin == SeekOrigin.End)
            {
                Position = Length + offset;
            }
            else // if (origin == SeekOrigin.Current)
            {
                Position = Position + offset;
            }

            return Position;
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException("The stream does not support writing.");
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

            for (; _shardPosition < _shards.Count; _shardPosition++, _shardOffset = 0)
            {
                await destination.WriteAsync(_shards[_shardPosition], _shardOffset, _shards[_shardPosition].Length - _shardOffset, cancellationToken);
            }
        }
    }
}
