// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.AspNetCore.ResponseCaching
{
    public class CopyOnlyMemoryStream : Stream
    {
        private readonly List<byte[]> _shards;
        private readonly long _length;

        public CopyOnlyMemoryStream(List<byte[]> shards, long length)
        {
            if (shards == null)
            {
                throw new ArgumentNullException(nameof(shards));
            }

            _shards = shards;
            _length = length;
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
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            foreach (var shard in _shards)
            {
                // No buffereing here, so ignore bufferSize
                await destination.WriteAsync(shard, 0, shard.Length, cancellationToken);
            }
        }
    }
}
