using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

public partial class DiskImageUnitTests : BasicSetupHelper
{

    /// <summary>
    /// Tests that ListFilesAsync returns the expected block-based files for an UnknownFilesystem.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_ListFilesAsync_ReturnsBlockBasedFiles()
    {
        // Create a partition on the writable disk
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 10 * MiB;
        var partitionOffset = sectorSize * 2048; // Start at sector 2048

        // Create a simple partition entry
        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        // Create UnknownFilesystem with 1MB block size
        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // List files
        var files = new List<IFile>();
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
            files.Add(file);

        // Should have 10 files (10MB partition / 1MB block size)
        Assert.AreEqual(10, files.Count, "Should have 10 block files for a 10MB partition with 1MB blocks.");

        // Verify each file has correct properties
        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            Assert.IsInstanceOf<UnknownFilesystemFile>(file, $"File {i} should be UnknownFilesystemFile.");
            Assert.IsFalse(file.IsDirectory, $"File {i} should not be a directory.");

            var unknownFile = (UnknownFilesystemFile)file;
            Assert.AreEqual(i * 1024L * 1024L, unknownFile.Address, $"File {i} should have correct address.");

            // All files except possibly the last should have full block size
            if (i < files.Count - 1)
                Assert.AreEqual(1024 * 1024, unknownFile.Size, $"File {i} should have full block size.");
        }
    }

    /// <summary>
    /// Tests that write and read round-trip preserves data correctly.
    /// </summary>
    [Test]
    public async Task Test_UnknownFilesystem_WriteRead_RoundTrip_DataMatches()
    {
        // Create a partition on the writable disk
        var sectorSize = _writableRawDisk.SectorSize;
        var partitionSize = 5 * MiB;
        var partitionOffset = sectorSize * 2048;

        var partition = new TestPartition
        {
            StartOffset = partitionOffset,
            Size = partitionSize,
            RawDisk = _writableRawDisk
        };

        // Create UnknownFilesystem with 1MB block size
        using var fs = new UnknownFilesystem(partition, blockSize: 1024 * 1024);

        // Get first file
        IFile? firstFile = null;
        await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
        {
            firstFile = file;
            break;
        }
        Assert.IsNotNull(firstFile, "Should have at least one file.");

        // Write data to the file
        var writeData = new byte[1024 * 1024];
        new Random(42).NextBytes(writeData);

        using (var writeStream = await fs.OpenWriteStreamAsync(firstFile!, CancellationToken.None))
        {
            await writeStream.WriteAsync(writeData.AsMemory(), CancellationToken.None);
        }

        // Read data back
        byte[] readData;
        using (var readStream = await fs.OpenReadStreamAsync(firstFile!, CancellationToken.None))
        {
            readData = new byte[writeData.Length];
            var bytesRead = await readStream.ReadAsync(readData.AsMemory(), CancellationToken.None);
            Assert.AreEqual(writeData.Length, bytesRead, "Should have read all bytes.");
        }

        // Verify data matches
        Assert.AreEqual(writeData, readData, "Read data should match written data.");
    }

    /// <summary>
    /// Tests that BoundsCheck validates ranges correctly.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_BoundsCheck_ValidRange_DoesNotThrow()
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

        // Valid ranges should not throw
        Assert.DoesNotThrow(() => fs.BoundsCheck(0, sectorSize), "BoundsCheck should not throw for valid range at start.");
        Assert.DoesNotThrow(() => fs.BoundsCheck(sectorSize, sectorSize), "BoundsCheck should not throw for valid range.");
        Assert.DoesNotThrow(() => fs.BoundsCheck(partitionSize - sectorSize, sectorSize), "BoundsCheck should not throw for valid range at end.");
    }

    /// <summary>
    /// Tests that BoundsCheck throws for negative start or size.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_BoundsCheck_NegativeValues_ThrowsArgumentOutOfRange()
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

        // Negative start should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(-1, sectorSize), "Negative start should throw.");

        // Negative size should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(0, -1), "Negative size should throw.");
    }

    /// <summary>
    /// Tests that BoundsCheck throws for ranges exceeding partition size.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_BoundsCheck_ExceedsPartitionSize_ThrowsArgumentOutOfRange()
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

        // Range exceeding partition size should throw
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(partitionSize - sectorSize, sectorSize * 2), "Range exceeding partition should throw.");
        Assert.Throws<ArgumentOutOfRangeException>(() => fs.BoundsCheck(partitionSize, sectorSize), "Start at partition end should throw.");
    }

    /// <summary>
    /// Tests that BoundsCheck throws for unaligned start or size.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_BoundsCheck_UnalignedValues_ThrowsArgumentException()
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

        // Unaligned start should throw
        Assert.Throws<ArgumentException>(() => fs.BoundsCheck(1, sectorSize), "Unaligned start should throw.");

        // Unaligned size should throw
        Assert.Throws<ArgumentException>(() => fs.BoundsCheck(0, sectorSize + 1), "Unaligned size should throw.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem constructor throws for unaligned block sizes.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_Constructor_UnalignedBlockSize_ThrowsArgumentException()
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

        // Block size not a multiple of sector size should throw
        var unalignedBlockSize = sectorSize + 1;
        var ex = Assert.Throws<ArgumentException>(() => new UnknownFilesystem(partition, blockSize: unalignedBlockSize),
            "Unaligned block size should throw ArgumentException.");
        Assert.That(ex!.ParamName, Is.EqualTo("blockSize"), "Exception should be for blockSize parameter.");
    }

    /// <summary>
    /// Tests that UnknownFilesystem constructor accepts valid block sizes.
    /// </summary>
    [Test]
    public void Test_UnknownFilesystem_Constructor_ValidBlockSize_CreatesInstance()
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

        // Valid block sizes should create instance
        Assert.DoesNotThrow(() =>
        {
            using var fs1 = new UnknownFilesystem(partition, blockSize: sectorSize);
        }, "Block size equal to sector size should work.");

        Assert.DoesNotThrow(() =>
        {
            using var fs2 = new UnknownFilesystem(partition, blockSize: sectorSize * 4);
        }, "Block size multiple of sector size should work.");

        Assert.DoesNotThrow(() =>
        {
            using var fs3 = new UnknownFilesystem(partition, blockSize: 1024 * 1024);
        }, "1MB block size should work.");
    }

    /// <summary>
    /// Helper class to implement IPartition for testing.
    /// </summary>
    private class TestPartition : IPartition
    {
        public long StartOffset { get; init; }
        public long Size { get; init; }
        public IRawDisk? RawDisk { get; init; }
        public int PartitionNumber => 1;
        public string? Name => "TestPartition";
        public Guid? UniqueId => Guid.Empty;
        public PartitionType Type => PartitionType.Primary;
        public FileSystemType FilesystemType => FileSystemType.Unknown;
        public Guid? VolumeGuid => Guid.Empty;
        public IPartitionTable PartitionTable => new TestPartitionTable { RawDisk = RawDisk };

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
            => Task.FromException<Stream>(new NotImplementedException());

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
            => Task.FromException<Stream>(new NotImplementedException());

        public void Dispose() { }
    }

    /// <summary>
    /// Helper class to implement IPartitionTable for testing.
    /// </summary>
    private class TestPartitionTable : IPartitionTable
    {
        public IRawDisk? RawDisk { get; init; }
        public PartitionTableType TableType => PartitionTableType.Unknown;
        public long Size => 0;

        public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
            => AsyncEnumerable.Empty<IPartition>();

        public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
            => Task.FromResult<IPartition?>(null);

        public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
            => Task.FromException<Stream>(new NotImplementedException());

        public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
            => Task.FromException<Stream>(new NotImplementedException());

        public void Dispose() { }
    }

}