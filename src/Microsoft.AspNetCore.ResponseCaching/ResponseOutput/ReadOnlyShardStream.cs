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
        private int _shardIndex;
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
                return _shardIndex * _shardSize + _shardOffset;
            }
            set
            {
                if (value < 0 || value > Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"The Position must be within the length of the Stream: {Length}");
                }

                _shardOffset = (int)(value % _shardSize);
                _shardIndex = (int)(value / _shardSize);
            }
        }

        public override void Flush()
        {
            throw new NotSupportedException("The stream does not support writing.");
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_shardIndex == _shards.Count)
            {
                return 0;
            }

            var bytesRead = 0;
            while (count > 0)
            {
                if (_shardOffset == _shards[_shardIndex].Length)
                {
                    // Move to the next shard
                    _shardIndex++;
                    _shardOffset = 0;

                    if (_shardIndex == _shards.Count)
                    {
                        break;
                    }
                }

                // Read up to the end of the shard
                var shardBytesRead = Math.Min(count, _shards[_shardIndex].Length - _shardOffset);
                Array.Copy(_shards[_shardIndex], _shardOffset, buffer, offset, shardBytesRead);
                bytesRead += shardBytesRead;
                _shardOffset += shardBytesRead;
                offset += shardBytesRead;
                count -= shardBytesRead;
            }

            return bytesRead;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return Task.FromResult(Read(buffer, offset, count));
        }

        public override int ReadByte()
        {
            if (Position == Length)
            {
                return -1;
            }

            if (_shardOffset == _shards[_shardIndex].Length)
            {
                // Move to the next shard
                _shardIndex++;
                _shardOffset = 0;
            }

            var byteRead = _shards[_shardIndex][_shardOffset];
            _shardOffset++;

            return byteRead;
        }

#if NETSTANDARD1_3
        public IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
#else
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
#endif
        {
            var tcs = new TaskCompletionSource<int>(state);

            try
            {
                tcs.TrySetResult(Read(buffer, offset, count));
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }

            if (callback != null)
            {
                // Offload callbacks to avoid stack dives on sync completions.
                var ignored = Task.Run(() =>
                {
                    try
                    {
                        callback(tcs.Task);
                    }
                    catch (Exception)
                    {
                        // Suppress exceptions on background threads.
                    }
                });
            }

            return tcs.Task;
        }

#if NETSTANDARD1_3
        public int EndRead(IAsyncResult asyncResult)
#else
        public override int EndRead(IAsyncResult asyncResult)
#endif
        {
            if (asyncResult == null)
            {
                throw new ArgumentNullException(nameof(asyncResult));
            }
            return ((Task<int>)asyncResult).GetAwaiter().GetResult();
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

            for (; _shardIndex < _shards.Count; _shardIndex++, _shardOffset = 0)
            {
                await destination.WriteAsync(_shards[_shardIndex], _shardOffset, _shards[_shardIndex].Length - _shardOffset, cancellationToken);
            }
        }
    }
}
