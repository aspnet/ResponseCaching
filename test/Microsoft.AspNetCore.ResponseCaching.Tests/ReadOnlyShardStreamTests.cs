// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.ResponseCaching.Internal;
using Xunit;

namespace Microsoft.AspNetCore.ResponseCaching.Tests
{
    public class ReadOnlyShardStreamTests
    {
        public class TestStreamInitInfo
        {
            internal List<byte[]> Shards { get; set; }
            internal int ShardSize { get; set; }
            internal long Length { get; set; }
        }

        public static TheoryData<TestStreamInitInfo> TestStreams
        {
            get
            {
                return new TheoryData<TestStreamInitInfo>
                {
                    // Partial Shard
                    new TestStreamInitInfo()
                    {
                        Shards = new List<byte[]>(new[]
                        {
                            new byte[] { 0, 1, 2, 3, 4 },
                            new byte[] { 5, 6, 7, 8, 9 },
                            new byte[] { 10, 11, 12 },
                        }),
                        ShardSize = 5,
                        Length = 13
                    },
                    // Full Shards
                    new TestStreamInitInfo()
                    {
                        Shards = new List<byte[]>(new[]
                        {
                            new byte[] { 0, 1, 2, 3, 4 },
                            new byte[] { 5, 6, 7, 8, 9 },
                            new byte[] { 10, 11, 12, 13, 14 },
                        }),
                        ShardSize = 5,
                        Length = 15
                    }
                };
            }
        }

        [Fact]
        public void ReadOnlyShardStream_NullShards_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ReadOnlyShardStream(null, 0));
        }

        [Fact]
        public void Position_ResetToZero_Succeeds()
        {
            var stream = new ReadOnlyShardStream(new List<byte[]>(), 0);

            // This should not throw
            stream.Position = 0;
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(100)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void Position_SetToNonZero_Throws(long position)
        {
            var stream = new ReadOnlyShardStream(new List<byte[]>(new[] { new byte[100] }), 100);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = position);
        }

        [Fact]
        public void WriteOperations_Throws()
        {
            var stream = new ReadOnlyShardStream(new List<byte[]>(), 0);


            Assert.Throws<NotSupportedException>(() => stream.Flush());
            Assert.Throws<NotSupportedException>(() => stream.Write(new byte[1], 0, 0));
        }

        [Fact]
        public void SetLength_Throws()
        {
            var stream = new ReadOnlyShardStream(new List<byte[]>(), 0);

            Assert.Throws<NotSupportedException>(() => stream.SetLength(0));
        }

        [Theory]
        [InlineData(SeekOrigin.Current)]
        [InlineData(SeekOrigin.End)]
        public void Seek_NotBegin_Throws(SeekOrigin origin)
        {
            var stream = new ReadOnlyShardStream(new List<byte[]>(), 0);

            Assert.Throws<ArgumentException>(() => stream.Seek(0, origin));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(-1)]
        [InlineData(100)]
        [InlineData(long.MaxValue)]
        [InlineData(long.MinValue)]
        public void Seek_NotZero_Throws(long offset)
        {
            var stream = new ReadOnlyShardStream(new List<byte[]>(), 0);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(offset, SeekOrigin.Begin));
        }

        [Theory]
        [MemberData(nameof(TestStreams))]
        public void ReadByte_CanReadAllBytes(TestStreamInitInfo info)
        {
            var stream = new ReadOnlyShardStream(info.Shards, info.Length);

            for (var i = 0; i < stream.Length; i++)
            {
                Assert.Equal(i, stream.Position);
                Assert.Equal(i, stream.ReadByte());
            }
            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(-1, stream.ReadByte());
            Assert.Equal(stream.Length, stream.Position);
        }

        [Theory]
        [MemberData(nameof(TestStreams))]
        public void Read_CountLessThanShardSize_CanReadAllBytes(TestStreamInitInfo info)
        {
            var stream = new ReadOnlyShardStream(info.Shards, info.Length);
            var count = info.ShardSize - 1;

            for (var i = 0; i < stream.Length; i+=count)
            {
                var output = new byte[count];
                var expectedOutput = new byte[count];
                var expectedBytesRead = Math.Min(count, stream.Length - i);
                for (var j = 0; j < expectedBytesRead; j++)
                {
                    expectedOutput[j] = (byte)(i + j);
                }
                Assert.Equal(i, stream.Position);
                Assert.Equal(expectedBytesRead, stream.Read(output, 0, count));
                Assert.True(expectedOutput.SequenceEqual(output));
            }
            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(0, stream.Read(new byte[count], 0, count));
            Assert.Equal(stream.Length, stream.Position);
        }

        [Theory]
        [MemberData(nameof(TestStreams))]
        public void Read_CountEqualShardSize_CanReadAllBytes(TestStreamInitInfo info)
        {
            var stream = new ReadOnlyShardStream(info.Shards, info.Length);
            var count = info.ShardSize;

            for (var i = 0; i < stream.Length; i += count)
            {
                var output = new byte[count];
                var expectedOutput = new byte[count];
                var expectedBytesRead = Math.Min(count, stream.Length - i);
                for (var j = 0; j < expectedBytesRead; j++)
                {
                    expectedOutput[j] = (byte)(i + j);
                }
                Assert.Equal(i, stream.Position);
                Assert.Equal(expectedBytesRead, stream.Read(output, 0, count));
                Assert.True(expectedOutput.SequenceEqual(output));
            }
            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(0, stream.Read(new byte[count], 0, count));
            Assert.Equal(stream.Length, stream.Position);
        }

        [Theory]
        [MemberData(nameof(TestStreams))]
        public void Read_CountGreaterThanShardSize_CanReadAllBytes(TestStreamInitInfo info)
        {
            var stream = new ReadOnlyShardStream(info.Shards, info.Length);
            var count = info.ShardSize + 1;

            for (var i = 0; i < stream.Length; i += count)
            {
                var output = new byte[count];
                var expectedOutput = new byte[count];
                var expectedBytesRead = Math.Min(count, stream.Length - i);
                for (var j = 0; j < expectedBytesRead; j++)
                {
                    expectedOutput[j] = (byte)(i + j);
                }
                Assert.Equal(i, stream.Position);
                Assert.Equal(expectedBytesRead, stream.Read(output, 0, count));
                Assert.True(expectedOutput.SequenceEqual(output));
            }
            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(0, stream.Read(new byte[count], 0, count));
            Assert.Equal(stream.Length, stream.Position);
        }

        [Theory]
        [MemberData(nameof(TestStreams))]
        public void CopyToAsync_CopiesAllBytes(TestStreamInitInfo info)
        {
            var stream = new ReadOnlyShardStream(info.Shards, info.Length);
            var writeStream = new WriteOnlyShardStream(info.ShardSize);

            stream.CopyTo(writeStream);

            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(stream.Length, writeStream.Length);
            var writeShards = writeStream.Shards;
            for (var i = 0; i < info.Shards.Count; i++)
            {
                Assert.True(writeShards[i].SequenceEqual(info.Shards[i]));
            }
        }

        [Theory]
        [MemberData(nameof(TestStreams))]
        public void CopyToAsync_CopiesFromCurrentPosition(TestStreamInitInfo info)
        {
            var skippedBytes = info.ShardSize;
            var writeStream = new WriteOnlyShardStream((int)info.Length);
            var stream = new ReadOnlyShardStream(info.Shards, info.Length);
            stream.Read(new byte[skippedBytes], 0, skippedBytes);

            stream.CopyTo(writeStream);

            Assert.Equal(stream.Length, stream.Position);
            Assert.Equal(stream.Length - skippedBytes, writeStream.Length);
            var writeShards = writeStream.Shards;

            for (var i = skippedBytes; i < info.Length; i++)
            {
                Assert.Equal(info.Shards[i / info.ShardSize][i % info.ShardSize], writeShards[0][i - skippedBytes]);
            }
        }
    }
}
