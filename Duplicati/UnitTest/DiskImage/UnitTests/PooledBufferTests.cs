using System;
using System.IO;
using Duplicati.Proprietary.DiskImage.General;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

    /// <summary>
    /// Tests that PooledBuffer rents a buffer from the ArrayPool and returns it on dispose.
    /// </summary>
    [Test]
    public void Test_PooledBuffer_RentAndReturn_BufferRecycled()
    {
        // Rent a buffer using PooledBuffer
        using var pooledBuffer = new PooledBuffer(1024);
        byte[] bufferArray = pooledBuffer.Array;
        var bufferLength = pooledBuffer.Length;

        // Verify the buffer was rented
        Assert.IsNotNull(bufferArray, "Buffer array should not be null.");
        Assert.AreEqual(1024, bufferLength, "Buffer length should match requested size.");
        Assert.GreaterOrEqual(bufferArray.Length, 1024, "Underlying array should be at least the requested size.");

        // Write some data to verify we have a valid buffer
        bufferArray[0] = 0xAB;
        bufferArray[1023] = 0xCD;

        // Verify Memory and Span properties work correctly
        var memory = pooledBuffer.Memory;
        var span = pooledBuffer.Span;

        Assert.AreEqual(1024, memory.Length, "Memory length should match requested size.");
        Assert.AreEqual(1024, span.Length, "Span length should match requested size.");
        Assert.AreEqual(0xAB, memory.Span[0], "Memory should reflect buffer changes.");
        Assert.AreEqual(0xCD, span[1023], "Span should reflect buffer changes.");
    }

    /// <summary>
    /// Tests that PooledBuffer handles different sizes correctly.
    /// </summary>
    [Test]
    public void Test_PooledBuffer_DifferentSizes_WorksCorrectly()
    {
        // Test small buffer
        using (var small = new PooledBuffer(16))
        {
            Assert.AreEqual(16, small.Length, "Small buffer length should be correct.");
            Assert.GreaterOrEqual(small.Array.Length, 16, "Small buffer array should be at least requested size.");
        }

        // Test large buffer (above LOH threshold of ~85KB)
        using (var large = new PooledBuffer(128 * 1024))
        {
            Assert.AreEqual(128 * 1024, large.Length, "Large buffer length should be correct.");
            Assert.GreaterOrEqual(large.Array.Length, 128 * 1024, "Large buffer array should be at least requested size.");
        }

        // Test zero-size buffer
        using (var empty = new PooledBuffer(0))
        {
            Assert.AreEqual(0, empty.Length, "Empty buffer length should be 0.");
            Assert.IsNotNull(empty.Array, "Empty buffer array should still be rented.");
        }
    }

    /// <summary>
    /// Tests that PooledMemoryStream reads correctly and returns buffer on dispose.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_Read_ReturnsCorrectData()
    {
        // Rent a buffer and populate it with test data
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(1024))
        {
            buffer = pooledBuffer.Array;
            for (int i = 0; i < 1024; i++)
                buffer[i] = (byte)(i & 0xFF);
        }

        // Create a PooledMemoryStream with the buffer
        using var stream = new PooledMemoryStream(buffer, 1024);

        // Verify stream properties
        Assert.IsTrue(stream.CanRead, "Stream should be readable.");
        Assert.IsTrue(stream.CanSeek, "Stream should be seekable.");
        Assert.IsFalse(stream.CanWrite, "Stream should not be writable.");
        Assert.AreEqual(1024, stream.Length, "Stream length should be correct.");
        Assert.AreEqual(0, stream.Position, "Initial position should be 0.");

        // Read data from the stream
        var readBuffer = new byte[256];
        var bytesRead = stream.Read(readBuffer, 0, 256);

        Assert.AreEqual(256, bytesRead, "Should have read 256 bytes.");
        Assert.AreEqual(256, stream.Position, "Position should be updated.");

        // Verify the data
        for (int i = 0; i < 256; i++)
            Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
    }

    /// <summary>
    /// Tests that PooledMemoryStream seek operations work correctly.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_Seek_BeginCurrentEnd_WorksCorrectly()
    {
        // Rent a buffer and populate it
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(512))
        {
            buffer = pooledBuffer.Array;
            for (int i = 0; i < 512; i++)
                buffer[i] = (byte)(i & 0xFF);
        }

        using (var stream = new PooledMemoryStream(buffer, 512))
        {
            // Test SeekOrigin.Begin
            var pos = stream.Seek(100, SeekOrigin.Begin);
            Assert.AreEqual(100, pos, "Seek from Begin should return correct position.");
            Assert.AreEqual(100, stream.Position, "Position should be 100.");

            // Read a byte to verify position
            var readBuffer = new byte[1];
            var bytesRead = stream.Read(readBuffer, 0, 1);
            Assert.AreEqual(1, bytesRead, "Should read 1 byte.");
            Assert.AreEqual(100, readBuffer[0], "Should read byte at position 100.");

            // Test SeekOrigin.Current
            pos = stream.Seek(50, SeekOrigin.Current);
            Assert.AreEqual(151, pos, "Seek from Current should return correct position.");
            Assert.AreEqual(151, stream.Position, "Position should be 151.");

            bytesRead = stream.Read(readBuffer, 0, 1);
            Assert.AreEqual(1, bytesRead, "Should read 1 byte.");
            Assert.AreEqual(151, readBuffer[0], "Should read byte at position 151.");

            // Test SeekOrigin.End
            pos = stream.Seek(-10, SeekOrigin.End);
            Assert.AreEqual(502, pos, "Seek from End should return correct position.");
            Assert.AreEqual(502, stream.Position, "Position should be 502.");

            bytesRead = stream.Read(readBuffer, 0, 1);
            Assert.AreEqual(1, bytesRead, "Should read 1 byte.");
            // 502 & 0xFF = 246 (byte overflow)
            Assert.AreEqual(246, readBuffer[0], "Should read byte at position 502 (502 & 0xFF = 246).");
        }
    }

    /// <summary>
    /// Tests that seeking beyond stream bounds throws ArgumentOutOfRangeException.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_SeekOutOfBounds_ThrowsArgumentOutOfRange()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(100))
            buffer = pooledBuffer.Array;

        using var stream = new PooledMemoryStream(buffer, 100);

        // Seek before beginning
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin), "Seek before beginning should throw.");

        // Seek past end
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(101, SeekOrigin.Begin), "Seek past end should throw.");

        // Seek before beginning using Current
        stream.Seek(50, SeekOrigin.Begin);
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-51, SeekOrigin.Current), "Seek before beginning from current should throw.");

        // Seek past end using End
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(1, SeekOrigin.End), "Seek past end from end should throw.");
    }

    /// <summary>
    /// Tests that reading past the end of PooledMemoryStream returns 0 bytes.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_ReadPastEnd_ReturnsZero()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(100))
        {
            buffer = pooledBuffer.Array;
            // Fill with test data
            for (int i = 0; i < 100; i++)
                buffer[i] = (byte)(i & 0xFF);
        }

        using var stream = new PooledMemoryStream(buffer, 100);

        // Seek to near end
        stream.Seek(95, SeekOrigin.Begin);

        // Read 10 bytes when only 5 remain
        var readBuffer = new byte[10];
        var bytesRead = stream.Read(readBuffer, 0, 10);

        Assert.AreEqual(5, bytesRead, "Should only read remaining 5 bytes.");
        Assert.AreEqual(100, stream.Position, "Position should be at end.");

        // Read again - should return 0
        bytesRead = stream.Read(readBuffer, 0, 10);
        Assert.AreEqual(0, bytesRead, "Should return 0 when at end of stream.");
    }

    /// <summary>
    /// Tests that PooledMemoryStream returns buffer to pool on dispose.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_Dispose_ReturnsBufferToPool()
    {
        // This test verifies that the stream can be disposed without errors
        // and that the buffer is returned to the pool (which we can't directly verify,
        // but we can ensure disposal works correctly)

        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(256))
            buffer = pooledBuffer.Array;

        var stream = new PooledMemoryStream(buffer, 256);

        // Use the stream
        var readBuffer = new byte[64];
        var bytesRead = stream.Read(readBuffer, 0, 64);
        Assert.AreEqual(64, bytesRead, "Should read 64 bytes.");

        // Dispose
        stream.Dispose();

        // After dispose, attempting to read should throw ObjectDisposedException
        Assert.Throws<ObjectDisposedException>(() => { var _ = stream.Read(readBuffer, 0, 64); }, "Reading after dispose should throw.");

        // Note: Seek doesn't throw ObjectDisposedException in this implementation
        // because it doesn't access the buffer directly (only _position and _length)

        // CanRead, CanSeek should return false after dispose
        Assert.IsFalse(stream.CanRead, "CanRead should be false after dispose.");
        Assert.IsFalse(stream.CanSeek, "CanSeek should be false after dispose.");
    }

    /// <summary>
    /// Tests that PooledMemoryStream write and set length operations throw NotSupportedException.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_WriteAndSetLength_ThrowsNotSupported()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(100))
            buffer = pooledBuffer.Array;

        using var stream = new PooledMemoryStream(buffer, 100);

        // Write should throw
        Assert.Throws<NotSupportedException>(() => stream.Write(new byte[10], 0, 10), "Write should throw NotSupportedException.");

        // SetLength should throw
        Assert.Throws<NotSupportedException>(() => stream.SetLength(50), "SetLength should throw NotSupportedException.");
    }

    /// <summary>
    /// Tests that PooledMemoryStream handles empty buffers correctly.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_EmptyBuffer_WorksCorrectly()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(0))
            buffer = pooledBuffer.Array;

        using var stream = new PooledMemoryStream(buffer, 0);

        Assert.AreEqual(0, stream.Length, "Length should be 0.");
        Assert.AreEqual(0, stream.Position, "Position should be 0.");

        // Read should return 0
        var readBuffer = new byte[10];
        var bytesRead = stream.Read(readBuffer, 0, 10);
        Assert.AreEqual(0, bytesRead, "Read from empty stream should return 0.");

        // Seek to 0 should work
        var pos = stream.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(0, pos, "Seek to 0 should work.");
    }

    /// <summary>
    /// Tests that PooledBuffer can be used multiple times in succession.
    /// </summary>
    [Test]
    public void Test_PooledBuffer_MultipleInstances_WorkIndependently()
    {
        // Create multiple PooledBuffers
        using var buffer1 = new PooledBuffer(64);
        using var buffer2 = new PooledBuffer(128);
        using var buffer3 = new PooledBuffer(256);

        // Write different data to each
        buffer1.Array[0] = 0x11;
        buffer2.Array[0] = 0x22;
        buffer3.Array[0] = 0x33;

        // Verify each buffer has its own data
        Assert.AreEqual(0x11, buffer1.Array[0], "Buffer1 should have its own data.");
        Assert.AreEqual(0x22, buffer2.Array[0], "Buffer2 should have its own data.");
        Assert.AreEqual(0x33, buffer3.Array[0], "Buffer3 should have its own data.");

        // Verify lengths
        Assert.AreEqual(64, buffer1.Length, "Buffer1 length should be correct.");
        Assert.AreEqual(128, buffer2.Length, "Buffer2 length should be correct.");
        Assert.AreEqual(256, buffer3.Length, "Buffer3 length should be correct.");
    }

}