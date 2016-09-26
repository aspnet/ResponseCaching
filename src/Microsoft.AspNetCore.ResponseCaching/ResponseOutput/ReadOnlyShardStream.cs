﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        private int _shardIndex;
        private int _shardOffset;
        private long _position;

        internal ReadOnlyShardStream(List<byte[]> shards, long length)
        {
            if (shards == null)
            {
                throw new ArgumentNullException(nameof(shards));
            }

            _shards = shards;
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
                return _position;
            }
            set
            {
                if (value != 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, $"{nameof(Position)} can only be set to 0.");
                }

                _position = 0;
                _shardOffset = 0;
                _shardIndex = 0;
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
                _position += shardBytesRead;
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
            _position++;

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
            if (origin != SeekOrigin.Begin)
            {
                throw new ArgumentException(nameof(origin), $"{nameof(Seek)} can only be set to {nameof(SeekOrigin.Begin)}.");
            }
            if (offset != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), offset, $"{nameof(Seek)} can only be set to 0.");
            }

            Position = 0;
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
                _position+= _shards[_shardIndex].Length - _shardOffset;
                await destination.WriteAsync(_shards[_shardIndex], _shardOffset, _shards[_shardIndex].Length - _shardOffset, cancellationToken);
            }
        }
    }
}
