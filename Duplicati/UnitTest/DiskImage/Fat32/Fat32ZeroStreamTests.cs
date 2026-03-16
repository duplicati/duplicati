// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Filesystem;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage
{
    /// <summary>
    /// Unit tests for the <see cref="Fat32Filesystem.Fat32ZeroStream"/> class.
    /// </summary>
    [TestFixture]
    [Category("DiskImageUnit")]
    public class Fat32ZeroStreamTests
    {
        [Test]
        public void Test_Fat32ZeroStream_Read_ReturnsZeros()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            var buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            Assert.AreEqual(1024, bytesRead, "Should read requested number of bytes");

            // Verify all bytes are zero
            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.AreEqual(0, buffer[i], $"Byte at position {i} should be zero");
            }
        }

        [Test]
        public async Task Test_Fat32ZeroStream_ReadAsync_ReturnsZeros()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            var buffer = new byte[1024];
            int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

            Assert.AreEqual(1024, bytesRead, "Should read requested number of bytes");

            // Verify all bytes are zero
            for (int i = 0; i < buffer.Length; i++)
            {
                Assert.AreEqual(0, buffer[i], $"Byte at position {i} should be zero");
            }
        }

        [Test]
        public void Test_Fat32ZeroStream_Length_MatchesBlockSize()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.AreEqual(blockSize, stream.Length, "Stream length should match block size");
        }

        [Test]
        public void Test_Fat32ZeroStream_Seek_Works()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            // Seek to beginning
            var position = stream.Seek(0, SeekOrigin.Begin);
            Assert.AreEqual(0, position, "Should be at position 0");

            // Seek to middle
            position = stream.Seek(blockSize / 2, SeekOrigin.Begin);
            Assert.AreEqual(blockSize / 2, position, "Should be at middle position");

            // Seek relative to current
            position = stream.Seek(1024, SeekOrigin.Current);
            Assert.AreEqual(blockSize / 2 + 1024, position, "Should be at relative position");

            // Seek from end
            position = stream.Seek(-1024, SeekOrigin.End);
            Assert.AreEqual(blockSize - 1024, position, "Should be at position from end");
        }

        [Test]
        public void Test_Fat32ZeroStream_Write_ThrowsNotSupported()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            var buffer = new byte[1024];
            Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length));
        }

        [Test]
        public void Test_Fat32ZeroStream_MultipleInstances_ShareBuffer()
        {
            const int blockSize = 1024 * 1024; // 1MB

            // Create two streams with the same block size
            using var stream1 = new Fat32Filesystem.Fat32ZeroStream(blockSize);
            using var stream2 = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            // Both should read zeros
            var buffer1 = new byte[1024];
            var buffer2 = new byte[1024];

            stream1.Read(buffer1, 0, buffer1.Length);
            stream2.Read(buffer2, 0, buffer2.Length);

            // Verify both have zeros
            for (int i = 0; i < buffer1.Length; i++)
            {
                Assert.AreEqual(0, buffer1[i], $"Buffer1 byte at position {i} should be zero");
                Assert.AreEqual(0, buffer2[i], $"Buffer2 byte at position {i} should be zero");
            }
        }

        [Test]
        public void Test_Fat32ZeroStream_ReadPartial_ReturnsCorrectCount()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            // Seek to near the end
            stream.Seek(blockSize - 100, SeekOrigin.Begin);

            var buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            Assert.AreEqual(100, bytesRead, "Should only read remaining bytes");
        }

        [Test]
        public void Test_Fat32ZeroStream_ReadPastEnd_ReturnsZero()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            // Seek to end
            stream.Seek(0, SeekOrigin.End);

            var buffer = new byte[1024];
            int bytesRead = stream.Read(buffer, 0, buffer.Length);

            Assert.AreEqual(0, bytesRead, "Should return 0 when reading past end");
        }

        [Test]
        public void Test_Fat32ZeroStream_CanRead_CanWrite_CanSeek()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.IsTrue(stream.CanRead, "CanRead should be true");
            Assert.IsFalse(stream.CanWrite, "CanWrite should be false");
            Assert.IsTrue(stream.CanSeek, "CanSeek should be true");
        }

        [Test]
        public void Test_Fat32ZeroStream_InvalidBlockSize_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Fat32Filesystem.Fat32ZeroStream(0));
            Assert.Throws<ArgumentException>(() => new Fat32Filesystem.Fat32ZeroStream(-1));
        }

        [Test]
        public void Test_Fat32ZeroStream_SetLength_ThrowsNotSupported()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.Throws<NotSupportedException>(() => stream.SetLength(1024));
        }

        [Test]
        public void Test_Fat32ZeroStream_Flush_DoesNotThrow()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.DoesNotThrow(() => stream.Flush());
        }

        [Test]
        public void Test_Fat32ZeroStream_Position_Property()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.AreEqual(0, stream.Position, "Initial position should be 0");

            stream.Position = 1024;
            Assert.AreEqual(1024, stream.Position, "Position should be settable");
        }

        [Test]
        public void Test_Fat32ZeroStream_Position_InvalidValue_Throws()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1);
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = blockSize + 1);
        }

        [Test]
        public void Test_Fat32ZeroStream_Seek_InvalidOrigin_Throws()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)999));
        }

        [Test]
        public void Test_Fat32ZeroStream_Seek_OutOfRange_Throws()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin));
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(blockSize + 1, SeekOrigin.Begin));
        }

        [Test]
        public void Test_Fat32ZeroStream_Read_NullBuffer_Throws()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

#pragma warning disable CS8600, CS8625
            Assert.Throws<ArgumentNullException>(() => stream.Read(null, 0, 1024));
#pragma warning restore CS8600, CS8625
        }

        [Test]
        public void Test_Fat32ZeroStream_Read_InvalidOffset_Throws()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            var buffer = new byte[1024];
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(buffer, -1, 1024));
            Assert.Throws<ArgumentException>(() => stream.Read(buffer, buffer.Length + 1, 1024));
        }

        [Test]
        public void Test_Fat32ZeroStream_Read_InvalidCount_Throws()
        {
            const int blockSize = 1024 * 1024; // 1MB
            using var stream = new Fat32Filesystem.Fat32ZeroStream(blockSize);

            var buffer = new byte[1024];
            Assert.Throws<ArgumentOutOfRangeException>(() => stream.Read(buffer, 0, -1));
            Assert.Throws<ArgumentException>(() => stream.Read(buffer, 512, 1024)); // Exceeds buffer length
        }
    }
}
