using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

public partial class DiskImageUnitTests : BasicSetupHelper
{

    [Test]
    public async Task Test_GPT_GetProtectiveMbrAsync_ReturnsValidMbr()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;

        // Read enough sectors to parse GPT
        var sectorsToRead = 34;
        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
        var diskBytes = new byte[sectorSize * sectorsToRead];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        // Parse GPT
        var gpt = new GPT(s_gptRawDisk);
        var parsed = await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);
        Assert.IsTrue(parsed, "GPT should parse successfully.");

        // Get protective MBR
        using var mbrStream = await gpt.GetProtectiveMbrAsync(CancellationToken.None);
        Assert.IsNotNull(mbrStream, "Protective MBR stream should not be null.");
        Assert.Greater(mbrStream.Length, 0, "Protective MBR stream should have data.");

        // Read MBR bytes and verify boot signature
        var mbrBytes = new byte[mbrStream.Length];
        var bytesRead = await mbrStream.ReadAsync(mbrBytes.AsMemory(), CancellationToken.None);
        Assert.Greater(bytesRead, 0, "Should have read MBR bytes.");

        // Verify MBR boot signature (0x55AA at offset 510-511)
        if (bytesRead >= 512)
        {
            Assert.AreEqual(0x55, mbrBytes[510], "MBR boot signature first byte should be 0x55.");
            Assert.AreEqual(0xAA, mbrBytes[511], "MBR boot signature second byte should be 0xAA.");
        }

        gpt.Dispose();
    }

    [Test]
    public async Task Test_GPT_GetPartitionTableDataAsync_ReturnsValidData()
    {
        var sectorSize = s_gptRawDisk!.SectorSize;

        // Read enough sectors to parse GPT
        var sectorsToRead = 34;
        using var stream = await s_gptRawDisk.ReadSectorsAsync(0, sectorsToRead, CancellationToken.None);
        var diskBytes = new byte[sectorSize * sectorsToRead];
        await stream.ReadAtLeastAsync(diskBytes, diskBytes.Length, cancellationToken: CancellationToken.None);

        // Parse GPT with raw disk reference
        var gpt = new GPT(s_gptRawDisk);
        var parsed = await gpt.ParseAsync(diskBytes, sectorSize, CancellationToken.None);
        Assert.IsTrue(parsed, "GPT should parse successfully.");

        // Get partition table data
        using var ptStream = await gpt.GetPartitionTableDataAsync(CancellationToken.None);
        Assert.IsNotNull(ptStream, "Partition table data stream should not be null.");
        Assert.Greater(ptStream.Length, 0, "Partition table data should have content.");

        // Read the data and verify it contains the GPT signature
        var ptBytes = new byte[ptStream.Length];
        var bytesRead = await ptStream.ReadAsync(ptBytes.AsMemory(), CancellationToken.None);
        Assert.Greater(bytesRead, sectorSize, "Should have read more than one sector of partition table data.");

        // Verify GPT signature "EFI PART" at sector 1 (offset 512)
        var expectedSignature = "EFI PART"u8.ToArray();
        for (int i = 0; i < 8; i++)
            Assert.AreEqual(expectedSignature[i], ptBytes[sectorSize + i], $"GPT signature byte {i} should match.");

        gpt.Dispose();
    }

    [Test]
    public async Task Test_UnknownFilesystem_GetFilesystemMetadataAsync_ReturnsCorrectMetadata()
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

        var blockSize = 1024 * 1024;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        var metadata = await fs.GetFilesystemMetadataAsync(CancellationToken.None);
        Assert.IsNotNull(metadata, "Metadata should not be null.");
        Assert.IsInstanceOf<UnkownFilesystemMetadata>(metadata, "Metadata should be UnkownFilesystemMetadata.");

        var fsMetadata = (UnkownFilesystemMetadata)metadata!;
        Assert.AreEqual(blockSize, fsMetadata.BlockSize, "BlockSize should match the configured value.");
    }

    [Test]
    public async Task Test_UnknownFilesystem_GetFileLengthAsync_ReturnsCorrectSize()
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

        var length = await fs.GetFileLengthAsync(firstFile!, CancellationToken.None);
        Assert.AreEqual(firstFile!.Size, length, "GetFileLengthAsync should return the file's size.");
        Assert.AreEqual(1024 * 1024, length, "First file should be 1MB (full block).");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_SetLength_ThrowsNotSupported()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None);
        Assert.Throws<NotSupportedException>(() => writeStream.SetLength(100),
            "SetLength should throw NotSupportedException on UnknownFilesystemStream.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_WriteOnReadOnly_ThrowsNotSupported()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);
        Assert.IsFalse(readStream.CanWrite, "Read stream should not be writable.");
        Assert.Throws<NotSupportedException>(() => readStream.Write(new byte[10], 0, 10),
            "Write on read-only stream should throw NotSupportedException.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_ReadOnWriteOnly_ThrowsNotSupported()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None);
        Assert.IsFalse(writeStream.CanRead, "Write stream should not be readable.");
        Assert.Throws<NotSupportedException>(() => { var _ = writeStream.Read(new byte[10], 0, 10); },
            "Read on write-only stream should throw NotSupportedException.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_DisposeAsync_FlushesDirtyData()
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

        var blockSize = sectorSize * 2;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write data using DisposeAsync (await using)
        var writeData = new byte[blockSize];
        new Random(99).NextBytes(writeData);

        await using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
        {
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);
        }

        // Read back and verify data was flushed via DisposeAsync
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            var readData = new byte[blockSize];
            var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
            Assert.AreEqual(blockSize, bytesRead, "Should have read the full block.");
            Assert.AreEqual(writeData, readData, "Data should match after DisposeAsync flush.");
        }
    }

    [Test]
    public void Test_UnknownFilesystem_DoubleDispose_DoesNotThrow()
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

        var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);
        fs.Dispose();

        // Second dispose should not throw
        Assert.DoesNotThrow(() => fs.Dispose(), "Double dispose should not throw.");
    }

    [Test]
    public void Test_UnknownFilesystem_Type_ReturnsUnknown()
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
        Assert.AreEqual(FileSystemType.Unknown, fs.Type, "Type should be Unknown.");
        Assert.AreEqual(partition, fs.Partition, "Partition should match the one passed to constructor.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_PositionSetter_ValidatesBounds()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

        // Valid position
        readStream.Position = 100;
        Assert.AreEqual(100, readStream.Position, "Position should be 100.");

        // Position at end (equal to size) should be valid
        readStream.Position = blockSize;
        Assert.AreEqual(blockSize, readStream.Position, "Position at end should be valid.");

        // Negative position should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => readStream.Position = -1,
            "Negative position should throw ArgumentOutOfRangeException.");

        // Position beyond size should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => readStream.Position = blockSize + 1,
            "Position beyond size should throw ArgumentOutOfRangeException.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_Flush_DoesNotThrow()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None);
        Assert.DoesNotThrow(() => writeStream.Flush(), "Flush should be a no-op and not throw.");
    }

    [Test]
    public void Test_PartitionWriteStream_Flush_DoesNotThrow()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

        Assert.DoesNotThrow(() => stream.Flush(), "Flush should be a no-op and not throw.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_WriteBeyondSize_ThrowsArgumentException()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None);

        // Try to write more data than the stream size
        var largeData = new byte[blockSize + 100];
        Assert.Throws<ArgumentException>(() => writeStream.Write(largeData, 0, largeData.Length),
            "Writing beyond stream size should throw ArgumentException.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_SyncRead_ReturnsCorrectData()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write test data
        var writeData = new byte[blockSize];
        for (int i = 0; i < blockSize; i++)
            writeData[i] = (byte)(i & 0xFF);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Read using synchronous Read method
        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);
        var readBuffer = new byte[256];
        var bytesRead = readStream.Read(readBuffer, 0, 256);

        Assert.AreEqual(256, bytesRead, "Sync Read should return 256 bytes.");
        for (int i = 0; i < 256; i++)
            Assert.AreEqual((byte)(i & 0xFF), readBuffer[i], $"Byte at position {i} should match.");
    }

    [Test]
    public async Task Test_UnknownFilesystemStream_SyncWrite_DataMatches()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write using synchronous Write method
        var writeData = new byte[100];
        new Random(42).NextBytes(writeData);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            writeStream.Write(writeData, 0, writeData.Length);

        // Read back and verify
        using var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);
        var readBuffer = new byte[100];
        var bytesRead = readStream.Read(readBuffer, 0, 100);

        Assert.AreEqual(100, bytesRead, "Should read 100 bytes.");
        Assert.AreEqual(writeData, readBuffer, "Sync-written data should match on read-back.");
    }

    [Test]
    public void Test_PooledMemoryStream_Flush_DoesNotThrow()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(100))
            buffer = pooledBuffer.Array;

        using var stream = new PooledMemoryStream(buffer, 100);
        Assert.DoesNotThrow(() => stream.Flush(), "Flush should be a no-op and not throw.");
    }

    [Test]
    public void Test_PooledBuffer_SpanAndMemory_ReturnCorrectSize()
    {
        using var buffer = new PooledBuffer(512);

        Assert.AreEqual(512, buffer.Span.Length, "Span length should match requested size.");
        Assert.AreEqual(512, buffer.Memory.Length, "Memory length should match requested size.");

        // Write via Span, read via Memory
        buffer.Span[0] = 0xAA;
        buffer.Span[511] = 0xBB;

        Assert.AreEqual(0xAA, buffer.Memory.Span[0], "Memory should reflect Span writes.");
        Assert.AreEqual(0xBB, buffer.Memory.Span[511], "Memory should reflect Span writes.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable stores the correct table type and raw disk reference.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_Constructor_SetsProperties()
    {
        var metadata = new GeometryMetadata
        {
            Disk = new DiskGeometry { SectorSize = 512, Size = 50 * MiB },
            PartitionTable = new PartitionTableGeometry { Type = PartitionTableType.GPT }
        };

        using var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.GPT);

        Assert.AreEqual(PartitionTableType.GPT, table.TableType, "TableType should be GPT.");
        Assert.AreEqual(_writableRawDisk, table.RawDisk, "RawDisk should match the provided disk.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable.EnumeratePartitions throws NotSupportedException.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_EnumeratePartitions_ThrowsNotSupported()
    {
        var metadata = new GeometryMetadata();
        using var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.GPT);

        Assert.Throws<NotSupportedException>(() =>
        {
            // Force enumeration to trigger the throw
            var enumerator = table.EnumeratePartitions(CancellationToken.None).GetAsyncEnumerator();
            enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult();
        }, "EnumeratePartitions should throw NotSupportedException.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable.GetPartitionAsync throws NotSupportedException.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_GetPartitionAsync_ThrowsNotSupported()
    {
        var metadata = new GeometryMetadata();
        using var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.GPT);

        Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await table.GetPartitionAsync(1, CancellationToken.None);
        }, "GetPartitionAsync should throw NotSupportedException.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable.GetProtectiveMbrAsync throws NotSupportedException for GPT.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_GetProtectiveMbrAsync_GPT_ThrowsNotSupported()
    {
        var metadata = new GeometryMetadata();
        using var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.GPT);

        Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await table.GetProtectiveMbrAsync(CancellationToken.None);
        }, "GetProtectiveMbrAsync should throw NotSupportedException for GPT.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable.GetProtectiveMbrAsync throws NotSupportedException for MBR.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_GetProtectiveMbrAsync_MBR_ThrowsNotSupported()
    {
        var metadata = new GeometryMetadata();
        using var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.MBR);

        var ex = Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await table.GetProtectiveMbrAsync(CancellationToken.None);
        }, "GetProtectiveMbrAsync should throw NotSupportedException for MBR.");

        StringAssert.Contains("MBR", ex!.Message, "Exception message should mention MBR.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable.GetPartitionTableDataAsync throws NotSupportedException.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_GetPartitionTableDataAsync_ThrowsNotSupported()
    {
        var metadata = new GeometryMetadata();
        using var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.GPT);

        Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await table.GetPartitionTableDataAsync(CancellationToken.None);
        }, "GetPartitionTableDataAsync should throw NotSupportedException.");
    }

    /// <summary>
    /// Tests that ReconstructedPartitionTable.Dispose can be called multiple times without error.
    /// </summary>
    [Test]
    public void Test_ReconstructedPartitionTable_DoubleDispose_DoesNotThrow()
    {
        var metadata = new GeometryMetadata();
        var table = new ReconstructedPartitionTable(_writableRawDisk, metadata, PartitionTableType.GPT);
        table.Dispose();
        Assert.DoesNotThrow(() => table.Dispose(), "Double dispose should not throw.");
    }

    /// <summary>
    /// Tests that BasePartition.OpenReadAsync returns a readable stream.
    /// </summary>
    [Test]
    public async Task Test_BasePartition_OpenReadAsync_ReturnsReadableStream()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 10L * sectorSize;
        var partitionSize = 4L * sectorSize;

        // Write known data to the partition area
        var writeData = new byte[sectorSize];
        for (int i = 0; i < sectorSize; i++)
            writeData[i] = (byte)(i & 0xFF);
        await _writableRawDisk.WriteBytesAsync(partitionOffset, writeData, CancellationToken.None);

        var partition = new BasePartition
        {
            PartitionNumber = 1,
            Type = PartitionType.Primary,
            PartitionTable = new TestPartitionTable { RawDisk = _writableRawDisk },
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var stream = await partition.OpenReadAsync(CancellationToken.None);
        Assert.IsNotNull(stream, "OpenReadAsync should return a stream.");
        Assert.IsTrue(stream.CanRead, "Stream should be readable.");

        var readBuffer = new byte[sectorSize];
        var bytesRead = await stream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(sectorSize, bytesRead, "Should read one sector.");
        Assert.AreEqual(writeData, readBuffer, "Data should match what was written.");

        partition.Dispose();
    }

    /// <summary>
    /// Tests that BasePartition.OpenWriteAsync returns a writable PartitionWriteStream.
    /// </summary>
    [Test]
    public async Task Test_BasePartition_OpenWriteAsync_ReturnsWritableStream()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionOffset = 10L * sectorSize;
        var partitionSize = 4L * sectorSize;

        var partition = new BasePartition
        {
            PartitionNumber = 1,
            Type = PartitionType.Primary,
            PartitionTable = new TestPartitionTable { RawDisk = _writableRawDisk },
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        using var stream = await partition.OpenWriteAsync(CancellationToken.None);
        Assert.IsNotNull(stream, "OpenWriteAsync should return a stream.");
        Assert.IsTrue(stream.CanWrite, "Stream should be writable.");
        Assert.IsInstanceOf<PartitionWriteStream>(stream, "Stream should be a PartitionWriteStream.");

        partition.Dispose();
    }

    /// <summary>
    /// Tests that UnknownPartitionTable.GetProtectiveMbrAsync throws NotSupportedException.
    /// </summary>
    [Test]
    public void Test_UnknownPartitionTable_GetProtectiveMbrAsync_ThrowsNotSupported()
    {
        using var table = new UnknownPartitionTable(null);

        Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await table.GetProtectiveMbrAsync(CancellationToken.None);
        }, "GetProtectiveMbrAsync should throw NotSupportedException.");
    }

    /// <summary>
    /// Tests that UnknownPartitionTable.GetPartitionTableDataAsync returns empty data.
    /// </summary>
    [Test]
    public async Task Test_UnknownPartitionTable_GetPartitionTableDataAsync_ReturnsEmptyStream()
    {
        using var table = new UnknownPartitionTable(null);

        using var stream = await table.GetPartitionTableDataAsync(CancellationToken.None);
        Assert.IsNotNull(stream, "Stream should not be null.");
        Assert.AreEqual(0, stream.Length, "Stream should be empty.");
    }

    /// <summary>
    /// Tests that UnknownPartitionTable.Dispose can be called multiple times.
    /// </summary>
    [Test]
    public void Test_UnknownPartitionTable_DoubleDispose_DoesNotThrow()
    {
        var table = new UnknownPartitionTable(null);
        table.Dispose();
        Assert.DoesNotThrow(() => table.Dispose(), "Double dispose should not throw.");
    }

    /// <summary>
    /// Tests that UnknownPartitionTable.GetPartitionAsync returns null for invalid partition numbers.
    /// </summary>
    [Test]
    public async Task Test_UnknownPartitionTable_GetPartitionAsync_InvalidNumber_ReturnsNull()
    {
        using var table = new UnknownPartitionTable(null);

        var result = await table.GetPartitionAsync(2, CancellationToken.None);
        Assert.IsNull(result, "Should return null for partition number != 1.");

        var result0 = await table.GetPartitionAsync(0, CancellationToken.None);
        Assert.IsNull(result0, "Should return null for partition number 0.");
    }

    /// <summary>
    /// Tests that UnknownPartitionTable.GetPartitionAsync returns null when no raw disk is set.
    /// </summary>
    [Test]
    public async Task Test_UnknownPartitionTable_GetPartitionAsync_NullDisk_ReturnsNull()
    {
        using var table = new UnknownPartitionTable(null);

        var result = await table.GetPartitionAsync(1, CancellationToken.None);
        Assert.IsNull(result, "Should return null when raw disk is null.");
    }

    /// <summary>
    /// Tests that UnknownPartitionTable.GetPartitionAsync returns a valid partition when disk is set.
    /// </summary>
    [Test]
    public async Task Test_UnknownPartitionTable_GetPartitionAsync_ValidDisk_ReturnsPartition()
    {
        using var table = new UnknownPartitionTable(_writableRawDisk);

        var result = await table.GetPartitionAsync(1, CancellationToken.None);
        Assert.IsNotNull(result, "Should return a partition when disk is available.");
        Assert.AreEqual(1, result!.PartitionNumber, "Partition number should be 1.");
        Assert.AreEqual(_writableRawDisk.Size, result.Size, "Partition size should equal disk size.");
        Assert.AreEqual(0, result.StartOffset, "Partition start offset should be 0.");

        result.Dispose();
    }

    /// <summary>
    /// Tests that PooledMemoryStream.Position setter validates bounds correctly.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_PositionSetter_ValidatesBounds()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(100))
            buffer = pooledBuffer.Array;

        using var stream = new PooledMemoryStream(buffer, 100);

        // Valid position
        stream.Position = 50;
        Assert.AreEqual(50, stream.Position, "Position should be 50.");

        // Position at end
        stream.Position = 100;
        Assert.AreEqual(100, stream.Position, "Position at end should be valid.");

        // Position at start
        stream.Position = 0;
        Assert.AreEqual(0, stream.Position, "Position at start should be valid.");

        // Negative position should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1,
            "Negative position should throw.");

        // Position beyond length should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = 101,
            "Position beyond length should throw.");
    }

    /// <summary>
    /// Tests that PooledMemoryStream.Seek with invalid origin throws ArgumentOutOfRangeException.
    /// </summary>
    [Test]
    public void Test_PooledMemoryStream_Seek_InvalidOrigin_ThrowsArgumentOutOfRange()
    {
        byte[] buffer;
        using (var pooledBuffer = new PooledBuffer(100))
            buffer = pooledBuffer.Array;

        using var stream = new PooledMemoryStream(buffer, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99),
            "Invalid SeekOrigin should throw ArgumentOutOfRangeException.");
    }

    /// <summary>
    /// Tests that PartitionWriteStream.Position setter validates bounds correctly.
    /// </summary>
    [Test]
    public void Test_PartitionWriteStream_PositionSetter_ValidatesBounds()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 2 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Valid position
        stream.Position = 100;
        Assert.AreEqual(100, stream.Position, "Position should be 100.");

        // Position at max size
        stream.Position = partitionSize;
        Assert.AreEqual(partitionSize, stream.Position, "Position at max size should be valid.");

        // Negative position should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = -1,
            "Negative position should throw.");

        // Position beyond max size should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Position = partitionSize + 1,
            "Position beyond max size should throw.");
    }

    /// <summary>
    /// Tests that PartitionWriteStream.Seek with invalid origin throws ArgumentOutOfRangeException.
    /// </summary>
    [Test]
    public void Test_PartitionWriteStream_Seek_InvalidOrigin_ThrowsArgumentOutOfRange()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, sectorSize);

        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(0, (SeekOrigin)99),
            "Invalid SeekOrigin should throw ArgumentOutOfRangeException.");
    }

    /// <summary>
    /// Tests that PartitionWriteStream.SetLength adjusts position when it exceeds new length.
    /// </summary>
    [Test]
    public void Test_PartitionWriteStream_SetLength_AdjustsPosition()
    {
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 4 * sectorSize;

        using var stream = new PartitionWriteStream(_writableRawDisk, 10 * sectorSize, partitionSize);

        // Write some data to move position
        stream.Write(new byte[200], 0, 200);
        Assert.AreEqual(200, stream.Position, "Position should be 200.");

        // Set length to less than current position
        stream.SetLength(100);
        Assert.AreEqual(100, stream.Length, "Length should be 100.");
        Assert.AreEqual(100, stream.Position, "Position should be adjusted to new length.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem path-based OpenReadStreamAsync works correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_OpenReadStreamAsync_ByPath_ReturnsStream()
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

        var blockSize = 1024 * 1024;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        // Write data first using IFile-based API
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        var writeData = new byte[100];
        new Random(42).NextBytes(writeData);
        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);

        // Read using path-based API
        var path = $"root/part_Unknown_1/fs_Unknown/{((UnknownFilesystemFile)firstFile!).Address:X}";
        using var readStream = await fs.OpenReadStreamAsync(path, CancellationToken.None);
        Assert.IsNotNull(readStream, "Path-based OpenReadStreamAsync should return a stream.");

        var readBuffer = new byte[100];
        var bytesRead = await readStream.ReadAsync(readBuffer.AsMemory(), CancellationToken.None);
        Assert.AreEqual(100, bytesRead, "Should read 100 bytes.");
        Assert.AreEqual(writeData, readBuffer, "Data should match.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem path-based OpenWriteStreamAsync works correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_OpenWriteStreamAsync_ByPath_ReturnsStream()
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

        // Write using path-based API
        var path = $"root/part_Unknown_1/fs_Unknown/0";
        using var writeStream = await fs.OpenWriteStreamAsync(path, CancellationToken.None);
        Assert.IsNotNull(writeStream, "Path-based OpenWriteStreamAsync should return a stream.");
        Assert.IsTrue(writeStream.CanWrite, "Stream should be writable.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem path-based OpenReadWriteStreamAsync works correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_OpenReadWriteStreamAsync_ByPath_ReturnsStream()
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

        // Open read-write using path-based API
        var path = $"root/part_Unknown_1/fs_Unknown/0";
        using var stream = await fs.OpenReadWriteStreamAsync(path, CancellationToken.None);
        Assert.IsNotNull(stream, "Path-based OpenReadWriteStreamAsync should return a stream.");
        Assert.IsTrue(stream.CanRead, "Stream should be readable.");
        Assert.IsTrue(stream.CanWrite, "Stream should be writable.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem path-based GetFileLengthAsync returns correct size.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_GetFileLengthAsync_ByPath_ReturnsCorrectSize()
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

        var blockSize = 1024 * 1024;
        using var fs = new UnknownFilesystem(partition, blockSize: blockSize);

        // Get file length using path-based API
        var path = $"root/part_Unknown_1/fs_Unknown/0";
        var length = await fs.GetFileLengthAsync(path, CancellationToken.None);
        Assert.AreEqual(blockSize, length, "File length should equal block size for first block.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem.GetFileLengthAsync throws for non-UnknownFilesystemFile (including directories).
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_GetFileLengthAsync_NonUnknownFile_ThrowsArgumentException()
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

        // A directory file that is not UnknownFilesystemFile should throw "does not belong"
        var dirFile = new MockDirectoryFile();

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await fs.GetFileLengthAsync(dirFile, CancellationToken.None);
        }, "GetFileLengthAsync should throw for non-UnknownFilesystemFile.");

        StringAssert.Contains("does not belong", ex!.Message, "Exception message should mention file doesn't belong.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem.GetFileLengthAsync throws for non-UnknownFilesystemFile.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_GetFileLengthAsync_WrongFileType_ThrowsArgumentException()
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

        // Create a mock file that is not UnknownFilesystemFile
        var wrongFile = new MockNonUnknownFile();

        var ex = Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await fs.GetFileLengthAsync(wrongFile, CancellationToken.None);
        }, "GetFileLengthAsync should throw for non-UnknownFilesystemFile.");

        StringAssert.Contains("does not belong", ex!.Message, "Exception message should mention file doesn't belong.");
    }

    /// <summary>
    /// Tests that UnknownFilesystemStream.Seek with invalid origin throws ArgumentException.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_Seek_InvalidOrigin_ThrowsArgumentException()
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

        using var fs = new UnknownFilesystem(partition, blockSize: 4096);

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var stream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

        Assert.Throws<ArgumentException>(() => stream.Seek(0, (SeekOrigin)99),
            "Invalid SeekOrigin should throw ArgumentException.");
    }

    /// <summary>
    /// Tests that UnknownFilesystemStream.Seek beyond bounds throws ArgumentOutOfRangeException.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystemStream_Seek_BeyondBounds_ThrowsArgumentOutOfRange()
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

        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        using var stream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None);

        // Seek before beginning
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(-1, SeekOrigin.Begin),
            "Seek before beginning should throw.");

        // Seek past end
        Assert.Throws<ArgumentOutOfRangeException>(() => stream.Seek(blockSize + 1, SeekOrigin.Begin),
            "Seek past end should throw.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem.ListFilesAsync with a non-UnknownFilesystemFile directory throws ArgumentException.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_ListFilesAsync_WithNonUnknownFile_ThrowsArgumentException()
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

        // ListFilesAsync with a non-UnknownFilesystemFile should throw
        var dirFile = new MockDirectoryFile();
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var file in fs.ListFilesAsync(dirFile, CancellationToken.None))
            { }
        }, "ListFilesAsync should throw for non-UnknownFilesystemFile.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem.ListFilesAsync with an UnknownFilesystemFile (non-directory) throws ArgumentException.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_ListFilesAsync_WithNonDirectory_ThrowsArgumentException()
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

        // UnknownFilesystemFile.IsDirectory is always false, so this should throw "not a directory"
        var nonDirFile = new UnknownFilesystemFile { Address = 0, Size = 1024 * 1024 };
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await foreach (var file in fs.ListFilesAsync(nonDirFile, CancellationToken.None))
            { }
        }, "ListFilesAsync should throw for non-directory UnknownFilesystemFile.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem.ParsePathToAddress throws for invalid path format.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_OpenReadStreamAsync_InvalidPath_ThrowsArgumentException()
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

        // Path with too few segments should throw
        Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await fs.OpenReadStreamAsync("invalid", CancellationToken.None);
        }, "Invalid path should throw ArgumentException.");
    }

    /// <summary>
    /// Mock directory file for testing.
    /// </summary>
    private class MockDirectoryFile : IFile
    {
        public string? Path => "/dir";
        public long? Address => null;
        public long Size => 0;
        public bool IsDirectory => true;
        public DateTime? LastModified => null;
    }

    /// <summary>
    /// Mock non-UnknownFilesystemFile for testing.
    /// </summary>
    private class MockNonUnknownFile : IFile
    {
        public string? Path => "/file.txt";
        public long? Address => 0;
        public long Size => 100;
        public bool IsDirectory => false;
        public DateTime? LastModified => null;
    }

}