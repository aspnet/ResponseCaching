// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Internal;

namespace Microsoft.AspNetCore.ResponseCaching.Internal
{
    internal class WriteOnlyShardStream : Stream
    {
        private readonly List<byte[]> _shards = new List<byte[]>();
        private readonly MemoryStream _bufferStream = new MemoryStream();
        private readonly int _shardSize;
        private long _length;
        private bool _shardsFinalized;

        internal WriteOnlyShardStream(int shardSize)
        {
            _shardSize = shardSize;
        }

        // Extracting the buffered shards closes the stream for writing
        internal List<byte[]> Shards
        {
            get
            {
                if (!_shardsFinalized)
                {
                    _shardsFinalized = true;
                    FinalizeShards();
                }
                return _shards;
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => !_shardsFinalized;

        public override long Length => _length;

        public override long Position
        {
            get
            {
                return _length;
            }
            set
            {
                throw new NotSupportedException("The stream does not support seeking.");
            }
        }

        private void FinalizeShards()
        {
            // Append any remaining shards
            if (_bufferStream.Length > 0)
            {
                // Add the last shard
                _shards.Add(_bufferStream.ToArray());
            }

            // Clean up the memory stream
            _bufferStream.SetLength(0);
            _bufferStream.Capacity = 0;
            _bufferStream.Dispose();
        }

        public override void Flush()
        {
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
            throw new NotSupportedException("The stream does not support seeking.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (!CanWrite)
            {
                throw new InvalidOperationException("The stream has been closed for writing.");
            }

            var bytesRemainingInShard = _shardSize - (int)(_length % _shardSize);
            while (count > 0)
            {
                var bytesWritten = Math.Min(count, bytesRemainingInShard);
                _bufferStream.Write(buffer, offset, bytesWritten);
                count -= bytesWritten;
                bytesRemainingInShard -= bytesWritten;
                offset += bytesWritten;
                _length += bytesWritten;

                if (count > 0 && bytesRemainingInShard == 0)
                {
                    _shards.Add(_bufferStream.ToArray());
                    _bufferStream.SetLength(0);
                    bytesRemainingInShard = _shardSize;
                }
            }
        }

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Write(buffer, offset, count);
            return TaskCache.CompletedTask;
        }

        // TODO: this can probably be improved
        public override void WriteByte(byte value)
        {
            Write(new[] { value }, 0, 1);
        }
    }
}
