using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

    [Test]
    public async Task Test_PartitionWriteStream_WriteAndDispose_DataFlushedToDisk()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 10 * sectorSize;
        var partitionSize = 4 * sectorSize;

        // Write data using PartitionWriteStream
        var writeData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
            await stream.WriteAsync(writeData, CancellationToken.None);

        // Read back and verify data was flushed to disk
        using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, writeData.Length, CancellationToken.None);
        var readBuffer = new byte[writeData.Length];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

        Assert.AreEqual(writeData.Length, bytesRead, "Should have read the written data.");
        Assert.AreEqual(writeData, readBuffer, "Data should match what was written.");
    }

    [Test]
    public void Test_PartitionWriteStream_SeekBegin_ValidPosition()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 4 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Test Seek with SeekOrigin.Begin
        var newPosition = stream.Seek(100, SeekOrigin.Begin);
        Assert.AreEqual(100, newPosition, "Position should be 100 after seeking from Begin.");
        Assert.AreEqual(100, stream.Position, "Stream.Position should match returned position.");
    }

    [Test]
    public void Test_PartitionWriteStream_SeekCurrent_ValidPosition()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 4 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Write some data first to move position
        stream.Write([0x01, 0x02, 0x03], 0, 3);
        Assert.AreEqual(3, stream.Position, "Position should be 3 after writing 3 bytes.");

        // Test Seek with SeekOrigin.Current
        var newPosition = stream.Seek(50, SeekOrigin.Current);
        Assert.AreEqual(53, newPosition, "Position should be 53 after seeking 50 from current position 3.");
        Assert.AreEqual(53, stream.Position, "Stream.Position should match returned position.");
    }

    [Test]
    public void Test_PartitionWriteStream_SeekEnd_ValidPosition()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 4 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Write some data first to establish length
        stream.Write([0x01, 0x02, 0x03, 0x04, 0x05], 0, 5);
        Assert.AreEqual(5, stream.Length, "Length should be 5 after writing 5 bytes.");

        // Test Seek with SeekOrigin.End
        var newPosition = stream.Seek(-2, SeekOrigin.End);
        Assert.AreEqual(3, newPosition, "Position should be 3 after seeking -2 from end of length 5.");
        Assert.AreEqual(3, stream.Position, "Stream.Position should match returned position.");
    }

    [Test]
    public void Test_PartitionWriteStream_WriteBeyondSize_ThrowsIOException()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 10 * sectorSize;
        var partitionSize = 2 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize);

        // Try to write more than partition size
        var largeBuffer = new byte[partitionSize + 100];
        new Random().NextBytes(largeBuffer);

        var ex = Assert.Throws<IOException>(() =>
        {
            stream.Write(largeBuffer, 0, largeBuffer.Length);
        }, "Writing beyond partition size should throw IOException.");

        StringAssert.Contains("partition size", ex!.Message, "Exception message should mention partition size.");
    }

    [Test]
    public void Test_PartitionWriteStream_SeekBeyondSize_ThrowsIOException()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 2 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Try to seek beyond partition size
        var ex = Assert.Throws<IOException>(() =>
        {
            stream.Seek(partitionSize + 100, SeekOrigin.Begin);
        }, "Seeking beyond partition size should throw IOException.");

        StringAssert.Contains("partition size", ex!.Message, "Exception message should mention partition size.");
    }

    [Test]
    public void Test_PartitionWriteStream_SeekNegativePosition_ThrowsIOException()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 2 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Try to seek to negative position
        Assert.Throws<IOException>(() =>
        {
            stream.Seek(-100, SeekOrigin.Begin);
        }, "Seeking to negative position should throw IOException.");
    }

    [Test]
    public async Task Test_PartitionWriteStream_WriteAtUnalignedPosition_DataMatches()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 15 * sectorSize;
        var partitionSize = 4 * sectorSize;

        // Write data at various unaligned positions
        using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
        {
            // Write at position 0
            stream.Write([0xAA, 0xBB], 0, 2);

            // Seek to unaligned position
            stream.Seek(7, SeekOrigin.Begin);
            stream.Write([0xCC, 0xDD, 0xEE], 0, 3);

            // Seek to another unaligned position
            stream.Seek(20, SeekOrigin.Begin);
            stream.Write([0x11, 0x22, 0x33, 0x44], 0, 4);
        }

        // Read back and verify
        using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 25, CancellationToken.None);
        var readBuffer = new byte[25];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

        Assert.AreEqual(25, bytesRead, "Should read exactly 25 bytes.");

        // Verify data at position 0
        Assert.AreEqual(0xAA, readBuffer[0], "Byte at position 0 should be 0xAA.");
        Assert.AreEqual(0xBB, readBuffer[1], "Byte at position 1 should be 0xBB.");

        // Verify data at position 7
        Assert.AreEqual(0xCC, readBuffer[7], "Byte at position 7 should be 0xCC.");
        Assert.AreEqual(0xDD, readBuffer[8], "Byte at position 8 should be 0xDD.");
        Assert.AreEqual(0xEE, readBuffer[9], "Byte at position 9 should be 0xEE.");

        // Verify data at position 20
        Assert.AreEqual(0x11, readBuffer[20], "Byte at position 20 should be 0x11.");
        Assert.AreEqual(0x22, readBuffer[21], "Byte at position 21 should be 0x22.");
        Assert.AreEqual(0x33, readBuffer[22], "Byte at position 22 should be 0x33.");
        Assert.AreEqual(0x44, readBuffer[23], "Byte at position 23 should be 0x44.");
    }

    [Test]
    public async Task Test_PartitionWriteStream_WriteMultipleTimes_DataMatches()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 20 * sectorSize;
        var partitionSize = 3 * sectorSize;

        // Write data in multiple operations
        using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
        {
            // First write
            stream.Write([0x01, 0x02, 0x03], 0, 3);

            // Second write (should append)
            stream.Write([0x04, 0x05], 0, 2);

            // Third write
            stream.Write([0x06, 0x07, 0x08, 0x09], 0, 4);
        }

        // Read back and verify all data was written
        using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 9, CancellationToken.None);
        var readBuffer = new byte[9];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

        Assert.AreEqual(9, bytesRead, "Should read exactly 9 bytes.");

        var expected = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09 };
        Assert.AreEqual(expected, readBuffer, "All written data should match.");
    }

    [Test]
    public void Test_PartitionWriteStream_SetLengthBeyondMaxSize_ThrowsIOException()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 2 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Try to set length beyond partition size
        var ex = Assert.Throws<IOException>(() =>
        {
            stream.SetLength(partitionSize + 100);
        }, "Setting length beyond partition size should throw IOException.");

        StringAssert.Contains("partition size", ex!.Message, "Exception message should mention partition size.");
    }

    [Test]
    public async Task Test_PartitionWriteStream_SetLengthWithinMaxSize_SetsLength()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 10 * sectorSize;
        var partitionSize = 2 * sectorSize;

        using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
        {
            // Set length to a specific value within bounds
            stream.SetLength(100);
            Assert.AreEqual(100, stream.Length, "Length should be set to 100.");

            // Write some data at position 0
            stream.Write([0xAA, 0xBB, 0xCC], 0, 3);
            // Length should be max(SetLength(100), written position) = 100
            Assert.AreEqual(100, stream.Length, "Length should remain 100 (SetLength value).");
        }

        // Verify the 3 bytes were written at position 0 and length 100 bytes are flushed
        using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 100, CancellationToken.None);
        var readBuffer = new byte[100];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

        Assert.AreEqual(100, bytesRead, "Should read exactly 100 bytes.");

        Assert.AreEqual(0xAA, readBuffer[0], "Byte at position 0 should be 0xAA.");
        Assert.AreEqual(0xBB, readBuffer[1], "Byte at position 1 should be 0xBB.");
        Assert.AreEqual(0xCC, readBuffer[2], "Byte at position 2 should be 0xCC.");
    }

    [Test]
    public void Test_PartitionWriteStream_CanRead_ReturnsFalse()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

        Assert.IsFalse(stream.CanRead, "CanRead should be false.");
    }

    [Test]
    public void Test_PartitionWriteStream_CanWrite_ReturnsTrue()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

        Assert.IsTrue(stream.CanWrite, "CanWrite should be true.");
    }

    [Test]
    public void Test_PartitionWriteStream_CanSeek_ReturnsTrue()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

        Assert.IsTrue(stream.CanSeek, "CanSeek should be true.");
    }

    [Test]
    public void Test_PartitionWriteStream_Read_ThrowsNotSupported()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

        Assert.Throws<NotSupportedException>(() =>
        {
            var _ = stream.Read(new byte[10], 0, 10);
        }, "Read should throw NotSupportedException.");
    }

    [Test]
    public async Task Test_PartitionWriteStream_WriteEmptyData_NoFlush()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 10 * sectorSize;
        var partitionSize = sectorSize;

        // Read initial data at this offset (may not be zero due to previous tests)
        using var initialReadStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 10, CancellationToken.None);
        var initialBuffer = new byte[10];
        var initialBytesRead = await initialReadStream.ReadAsync(initialBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(10, initialBytesRead, "Should read 10 bytes.");

        // Create stream but write nothing
        using (var stream = new PartitionWriteStream(_writableRawDisk, partitionOffset, partitionSize))
            // Don't write anything
            Assert.AreEqual(0, stream.Length, "Length should be 0 when nothing is written.");

        // Read back and verify no data was flushed (should be unchanged)
        using var readStream = await _writableRawDisk.ReadBytesAsync(partitionOffset, 10, CancellationToken.None);
        var readBuffer = new byte[10];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);

        Assert.AreEqual(10, bytesRead, "Should read 10 bytes.");

        // All bytes should be unchanged from before the stream was created
        for (int i = 0; i < readBuffer.Length; i++)
        {
            Assert.AreEqual(initialBuffer[i], readBuffer[i], $"Byte at position {i} should be unchanged (no data written).");
        }
    }

}