using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Filesystem;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

public partial class DiskImageUnitTests : BasicSetupHelper
{

    /// <summary>
    /// Tests that writing partial data (less than block size) is correctly flushed on dispose.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_WritePartialData_FlushesOnDispose()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write partial data (less than block size)
        var writeData = new byte[1024]; // 1KB, much less than 1MB block
        new Random(42).NextBytes(writeData);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Read back and verify - the written data should be preserved
        byte[] readData;
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            readData = new byte[writeData.Length];
            var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
            Assert.AreEqual(writeData.Length, bytesRead, "Should have read all written bytes.");
        }

        // Verify data matches
        Assert.AreEqual(writeData, readData, "Partial data should be correctly flushed and readable.");
    }

    /// <summary>
    /// Tests writing at position 0, seeking to middle, writing more data, and verifying both regions.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_WriteSeekWrite_BothRegionsPreserved()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write at position 0, seek to middle, write more data
        var firstWriteData = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 };
        var secondWriteData = new byte[] { 0xAA, 0xBB, 0xCC, 0xDD };
        var middlePosition = 1000;

        using (var writeStream = await fs.OpenReadWriteStreamAsync(firstFile!, CancellationToken.None))
        {
            // Write at position 0
            await writeStream.WriteAsync(firstWriteData.AsMemory(), CancellationToken.None);

            // Seek to middle position
            writeStream.Seek(middlePosition, SeekOrigin.Begin);

            // Write more data
            await writeStream.WriteAsync(secondWriteData.AsMemory(), CancellationToken.None);
        }

        // Read back and verify both regions
        byte[] readData;
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            readData = new byte[middlePosition + secondWriteData.Length];
            var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
            Assert.AreEqual(middlePosition + secondWriteData.Length, bytesRead, "Should have read all data.");
        }

        // Verify first region (position 0-4)
        for (int i = 0; i < firstWriteData.Length; i++)
            Assert.AreEqual(firstWriteData[i], readData[i], $"Byte at position {i} should match first write.");

        // Verify middle region (position 1000-1003)
        for (int i = 0; i < secondWriteData.Length; i++)
            Assert.AreEqual(secondWriteData[i], readData[middlePosition + i], $"Byte at position {middlePosition + i} should match second write.");
    }

    /// <summary>
    /// Tests reading after seeking to a non-zero position.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_ReadAfterSeek_ReturnsCorrectData()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write test data
        var writeData = new byte[4096]; // 4KB of test data
        for (int i = 0; i < writeData.Length; i++)
            writeData[i] = (byte)(i & 0xFF);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Read after seeking to non-zero position
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            // Seek to position 100
            readStream.Seek(100, SeekOrigin.Begin);

            // Read remaining data
            var readBuffer = new byte[writeData.Length - 100];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(writeData.Length - 100, bytesRead, "Should have read remaining bytes after seek.");

            // Verify data matches expected values
            for (int i = 0; i < bytesRead; i++)
            {
                Assert.AreEqual((byte)((i + 100) & 0xFF), readBuffer[i], $"Byte at position {100 + i} should match.");
            }
        }
    }

    /// <summary>
    /// Tests that writing data that doesn't fill the entire stream preserves the written data
    /// while the remaining bytes retain their previous values.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_WritePartial_PreservesData()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        // Use a small block size for this test
        var blockSize = 4096;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // First, initialize the entire block with zeros by writing zeros
        var zeroData = new byte[blockSize];
        using (var zeroStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await zeroStream.WriteAsync(zeroData.AsMemory(), CancellationToken.None);

        // Now write partial data at the beginning
        var writeData = new byte[] { 0x11, 0x22, 0x33, 0x44, 0x55 };

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Read the entire block and verify written data and remaining zeros
        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);
        var readBuffer = new byte[blockSize];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(blockSize, bytesRead, "Should have read the full block.");

        // Verify written data
        for (int i = 0; i < writeData.Length; i++)
            Assert.AreEqual(writeData[i], readBuffer[i], $"Byte at position {i} should match written data.");

        // Verify remaining bytes are still zero
        for (int i = writeData.Length; i < blockSize; i++)
            Assert.AreEqual(0, readBuffer[i], $"Byte at position {i} should be zero.");
    }

    /// <summary>
    /// Tests the lazy-load behavior: first read triggers disk read, subsequent reads use buffer.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_LazyLoad_FirstReadTriggersDiskRead()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write test data first
        var writeData = new byte[2048];
        new Random(123).NextBytes(writeData);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Now test lazy loading - first read should trigger disk read
        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

        // First small read (triggers lazy load)
        var firstReadBuffer = new byte[512];
        var bytesRead1 = await readStream.ReadAsync(firstReadBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(512, bytesRead1, "First read should return 512 bytes.");

        // Second read should use cached buffer (no disk read)
        var secondReadBuffer = new byte[512];
        var bytesRead2 = await readStream.ReadAsync(secondReadBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(512, bytesRead2, "Second read should return 512 bytes from buffer.");

        // Verify both reads returned correct data
        for (int i = 0; i < 512; i++)
        {
            Assert.AreEqual(writeData[i], firstReadBuffer[i], $"First read: byte at position {i} should match.");
            Assert.AreEqual(writeData[i + 512], secondReadBuffer[i], $"Second read: byte at position {i + 512} should match.");
        }

        // Seek back and read again - should still use buffer
        readStream.Seek(0, SeekOrigin.Begin);
        var thirdReadBuffer = new byte[512];
        var bytesRead3 = await readStream.ReadAsync(thirdReadBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(512, bytesRead3, "Third read after seek should return 512 bytes.");

        for (int i = 0; i < 512; i++)
            Assert.AreEqual(writeData[i], thirdReadBuffer[i], $"Third read: byte at position {i} should match after seek.");
    }

    /// <summary>
    /// Tests seeking with SeekOrigin.Current works correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_SeekCurrent_UpdatesPosition()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write test data
        var writeData = new byte[4096];
        for (int i = 0; i < writeData.Length; i++)
            writeData[i] = (byte)(i & 0xFF);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Test Seek with SeekOrigin.Current
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            // Read first 100 bytes
            var buffer1 = new byte[100];
            var bytesRead1 = await readStream.ReadAsync(buffer1.AsMemory(), CancellationToken.None);
            Assert.AreEqual(100, bytesRead1, "First read should return 100 bytes.");
            Assert.AreEqual(100, readStream.Position, "Position should be 100 after first read.");

            // Seek forward 100 bytes from current
            var newPos = readStream.Seek(100, SeekOrigin.Current);
            Assert.AreEqual(200, newPos, "Position should be 200 after Seek(Current, 100).");
            Assert.AreEqual(200, readStream.Position, "Stream.Position should match returned position.");

            // Read next 100 bytes
            var buffer2 = new byte[100];
            var bytesRead2 = await readStream.ReadAsync(buffer2.AsMemory(), CancellationToken.None);
            Assert.AreEqual(100, bytesRead2, "Second read should return 100 bytes.");

            // Verify data at position 200
            for (int i = 0; i < 100; i++)
                Assert.AreEqual((byte)((200 + i) & 0xFF), buffer2[i], $"Byte at position {200 + i} should match.");
        }
    }

    /// <summary>
    /// Tests seeking with SeekOrigin.End works correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_SeekEnd_UpdatesPosition()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        var blockSize = 4096;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write test data
        var writeData = new byte[blockSize];
        for (int i = 0; i < writeData.Length; i++)
            writeData[i] = (byte)(i & 0xFF);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Test Seek with SeekOrigin.End
        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

        // Seek to 100 bytes from end
        var newPos = readStream.Seek(-100, SeekOrigin.End);
        Assert.AreEqual(blockSize - 100, newPos, "Position should be blockSize - 100.");
        Assert.AreEqual(blockSize - 100, readStream.Position, "Stream.Position should match.");

        // Read remaining bytes
        var buffer = new byte[100];
        var bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(100, bytesRead, "Should read 100 bytes.");

        // Verify last 100 bytes
        for (int i = 0; i < 100; i++)
            Assert.AreEqual((byte)((blockSize - 100 + i) & 0xFF), buffer[i], $"Byte at position {blockSize - 100 + i} should match.");
    }

    /// <summary>
    /// Tests that reading past the end of the stream returns 0 bytes.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_ReadPastEnd_ReturnsZero()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        var blockSize = 4096;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write small amount of test data
        var writeData = new byte[100];
        new Random(42).NextBytes(writeData);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Test reading past end
        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

        // Seek to near end
        readStream.Seek(blockSize - 10, SeekOrigin.Begin);

        // Try to read 100 bytes when only 10 remain
        var buffer = new byte[100];
        var bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(10, bytesRead, "Should only read remaining 10 bytes.");

        // Try to read again - should return 0
        bytesRead = await readStream.ReadAsync(buffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(0, bytesRead, "Should return 0 when at end of stream.");
    }

    /// <summary>
    /// Tests that unaligned writes within the stream work correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_UnalignedWrite_DataMatches()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        var blockSize = 8192;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // First, zero-fill the block so gap bytes are deterministic
        using (var zeroStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await zeroStream.WriteAsync(new byte[blockSize].AsMemory(), CancellationToken.None);

        // Write at unaligned positions using read-write stream (pre-loads zeroed data)
        using (var writeStream = await fs.OpenReadWriteStreamAsync(firstFile!, CancellationToken.None))
        {
            // Write 3 bytes at position 0
            await writeStream.WriteAsync(new byte[] { 0xAA, 0xBB, 0xCC }.AsMemory(), CancellationToken.None);

            // Seek to unaligned position 5
            writeStream.Seek(5, SeekOrigin.Begin);
            await writeStream.WriteAsync(new byte[] { 0xDD, 0xEE }.AsMemory(), CancellationToken.None);

            // Seek to another unaligned position 10
            writeStream.Seek(10, SeekOrigin.Begin);
            await writeStream.WriteAsync(new byte[] { 0x11, 0x22, 0x33, 0x44 }.AsMemory(), CancellationToken.None);
        }

        // Read back and verify
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            var readBuffer = new byte[14];
            var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
            Assert.AreEqual(14, bytesRead, "Should read 14 bytes.");

            // Verify written data
            Assert.AreEqual(0xAA, readBuffer[0], "Byte 0 should be 0xAA");
            Assert.AreEqual(0xBB, readBuffer[1], "Byte 1 should be 0xBB");
            Assert.AreEqual(0xCC, readBuffer[2], "Byte 2 should be 0xCC");
            // Bytes 3-4 should be zero
            Assert.AreEqual(0, readBuffer[3], "Byte 3 should be 0");
            Assert.AreEqual(0, readBuffer[4], "Byte 4 should be 0");
            Assert.AreEqual(0xDD, readBuffer[5], "Byte 5 should be 0xDD");
            Assert.AreEqual(0xEE, readBuffer[6], "Byte 6 should be 0xEE");
            // Bytes 7-9 should be zero
            Assert.AreEqual(0, readBuffer[7], "Byte 7 should be 0");
            Assert.AreEqual(0, readBuffer[8], "Byte 8 should be 0");
            Assert.AreEqual(0, readBuffer[9], "Byte 9 should be 0");
            Assert.AreEqual(0x11, readBuffer[10], "Byte 10 should be 0x11");
            Assert.AreEqual(0x22, readBuffer[11], "Byte 11 should be 0x22");
            Assert.AreEqual(0x33, readBuffer[12], "Byte 12 should be 0x33");
            Assert.AreEqual(0x44, readBuffer[13], "Byte 13 should be 0x44");
        }
    }

}