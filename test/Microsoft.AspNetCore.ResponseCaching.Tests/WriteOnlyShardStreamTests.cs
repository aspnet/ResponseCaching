// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Xunit;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class WriteOnlyShardStreamTests
    {
        private static byte[] WriteData = new byte[]
        {
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14
        };

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void WriteOnlyShardStream_InvalidShardSize_Throws(int shardSize)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new WriteOnlyShardStream(shardSize));
        }

        [Fact]
        public void ReadAndSeekOperations_Throws()
        {
            var stream = new WriteOnlyShardStream(1);

            Assert.Throws<NotSupportedException>(() => stream.Read(new byte[1], 0, 0));
            Assert.Throws<NotSupportedException>(() => stream.Position = 0);
            Assert.Throws<NotSupportedException>(() => stream.Seek(0, SeekOrigin.Begin));
        }

        [Fact]
        public void Shards_ExtractionDisablesWriting()
        {
            var stream = new WriteOnlyShardStream(1);

            Assert.True(stream.CanWrite);
            Assert.Equal(0, stream.Shards.Count);
            Assert.False(stream.CanWrite);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void WriteByte_CanWriteAllBytes(int shardSize)
        {
            var stream = new WriteOnlyShardStream(shardSize);

            foreach (var datum in WriteData)
            {
                stream.WriteByte(datum);
            }
            var shards = stream.Shards;

            Assert.Equal(WriteData.Length, stream.Length);
            Assert.Equal((WriteData.Length + shardSize - 1)/ shardSize, shards.Count);

            for (var i = 0; i < WriteData.Length; i += shardSize)
            {
                var expectedShardSize = Math.Min(shardSize, WriteData.Length - i);
                var expectedShard = new byte[expectedShardSize];
                for (int j = 0; j < expectedShardSize; j++)
                {
                    expectedShard[j] = (byte)(i + j);
                }
                var shard = shards[i / shardSize];

                Assert.Equal(expectedShardSize, shard.Length);
                Assert.True(expectedShard.SequenceEqual(shard));
            }
        }

        [Theory]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        public void Write_CanWriteAllBytes(int writeSize)
        {
            var shardSize = 5;
            var stream = new WriteOnlyShardStream(shardSize);


            for (var i = 0; i < WriteData.Length; i += writeSize)
            {
                stream.Write(WriteData, i, Math.Min(writeSize, WriteData.Length - i));
            }
            var shards = stream.Shards;

            Assert.Equal(WriteData.Length, stream.Length);
            Assert.Equal((WriteData.Length + shardSize - 1) / shardSize, shards.Count);

            for (var i = 0; i < WriteData.Length; i += shardSize)
            {
                var expectedShardSize = Math.Min(shardSize, WriteData.Length - i);
                var expectedShard = new byte[expectedShardSize];
                for (int j = 0; j < expectedShardSize; j++)
                {
                    expectedShard[j] = (byte)(i + j);
                }
                var shard = shards[i / shardSize];

                Assert.Equal(expectedShardSize, shard.Length);
                Assert.True(expectedShard.SequenceEqual(shard));
            }
        }
    }
}
