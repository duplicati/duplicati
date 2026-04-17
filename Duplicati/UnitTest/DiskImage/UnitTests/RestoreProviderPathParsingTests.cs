using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage;
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
    /// Creates a RestoreProvider with mock partitions and filesystems for testing path parsing.
    /// Uses reflection to set up the internal state.
    /// </summary>
    private static RestoreProvider CreateRestoreProviderForPathParsingTests(IRawDisk rawDisk)
    {
        // Create a RestoreProvider using the default constructor (for metadata loading)
        var provider = new RestoreProvider();

        // Create mock partition tables
        var gptTable = new MockPartitionTable(PartitionTableType.GPT);
        var mbrTable = new MockPartitionTable(PartitionTableType.MBR);

        // Create mock partitions
        var partitions = new List<IPartition>
        {
            new MockPartition(gptTable, 1, PartitionType.Primary, 1048576, 20971520, "EFI System", FileSystemType.FAT32),
            new MockPartition(gptTable, 2, PartitionType.Primary, 22020096, 41943040, "Windows OS", FileSystemType.NTFS),
            new MockPartition(mbrTable, 1, PartitionType.Primary, 1048576, 10485760, "MBR Partition 1", FileSystemType.FAT32),
            new MockPartition(mbrTable, 2, PartitionType.Primary, 11534336, 20971520, "MBR Partition 2", FileSystemType.NTFS)
        };

        // Create mock filesystems
        var filesystems = new List<IFilesystem>
        {
            new MockFilesystem(partitions[0], FileSystemType.FAT32),
            new MockFilesystem(partitions[1], FileSystemType.NTFS),
            new MockFilesystem(partitions[2], FileSystemType.FAT32),
            new MockFilesystem(partitions[3], FileSystemType.NTFS)
        };

        // Use reflection to set the private fields
        var partitionsField = typeof(RestoreProvider).GetField("_partitions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var filesystemsField = typeof(RestoreProvider).GetField("_filesystems", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var targetDiskField = typeof(RestoreProvider).GetField("_targetDisk", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        partitionsField?.SetValue(provider, partitions);
        filesystemsField?.SetValue(provider, filesystems);
        targetDiskField?.SetValue(provider, rawDisk);

        return provider;
    }

    /// <summary>
    /// Creates a RestoreProvider with mock partitions and filesystems for testing path parsing.
    /// Uses reflection to set up the internal state. Uses the GPT test disk for the device path.
    /// </summary>
    private static RestoreProvider CreateRestoreProviderForPathParsingTests()
    {
        return CreateRestoreProviderForPathParsingTests(s_gptRawDisk!);
    }

    /// <summary>
    /// Mock implementation of IPartitionTable for testing.
    /// </summary>
    private class MockPartitionTable(PartitionTableType tableType) : IPartitionTable
    {
        public IRawDisk? RawDisk => null;
        public PartitionTableType TableType { get; } = tableType;

        public IAsyncEnumerable<IPartition> EnumeratePartitions(CancellationToken cancellationToken)
        {
            return AsyncEnumerable.Empty<IPartition>();
        }

        public Task<IPartition?> GetPartitionAsync(int partitionNumber, CancellationToken cancellationToken)
        {
            return Task.FromResult<IPartition?>(null);
        }

        public Task<Stream> GetProtectiveMbrAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> GetPartitionTableDataAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Mock implementation of IPartition for testing.
    /// </summary>
    private class MockPartition(IPartitionTable table, int number, PartitionType type, long startOffset, long size, string? name, FileSystemType fsType) : IPartition
    {
        public IPartitionTable PartitionTable { get; } = table;
        public int PartitionNumber { get; } = number;
        public PartitionType Type { get; } = type;
        public long StartOffset { get; } = startOffset;
        public long Size { get; } = size;
        public string? Name { get; } = name;
        public FileSystemType FilesystemType { get; } = fsType;
        public Guid? VolumeGuid { get; } = Guid.NewGuid();

        public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Mock implementation of IFilesystem for testing.
    /// </summary>
    private class MockFilesystem(IPartition partition, FileSystemType type) : IFilesystem
    {
        public IPartition Partition { get; } = partition;
        public FileSystemType Type { get; } = type;

        public Task<object?> GetFilesystemMetadataAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<IFile> ListFilesAsync(CancellationToken cancellationToken)
        {
            return AsyncEnumerable.Empty<IFile>();
        }

        public IAsyncEnumerable<IFile> ListFilesAsync(IFile directory, CancellationToken cancellationToken)
        {
            return AsyncEnumerable.Empty<IFile>();
        }

        public Task<Stream> OpenReadStreamAsync(IFile file, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> OpenWriteStreamAsync(IFile file, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> OpenReadWriteStreamAsync(IFile file, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<long> GetFileLengthAsync(IFile file, CancellationToken cancellationToken)
        {
            return Task.FromResult(0L);
        }

        public Task<Stream> OpenReadStreamAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> OpenWriteStreamAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<Stream> OpenReadWriteStreamAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult<Stream>(new MemoryStream());
        }

        public Task<long> GetFileLengthAsync(string path, CancellationToken cancellationToken)
        {
            return Task.FromResult(0L);
        }

        public void Dispose() { }
    }

    /// <summary>
    /// Tests that ParsePartition correctly parses a valid GPT partition segment.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_ValidGPTPartition_ReturnsCorrectPartition()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        // Test parsing "part_GPT_1"
        var result = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
        Assert.IsNotNull(result, "Should return a partition.");

        var partition = (IPartition)result!;
        Assert.AreEqual(1, partition.PartitionNumber, "Partition number should be 1.");
        Assert.AreEqual(PartitionTableType.GPT, partition.PartitionTable.TableType, "Partition table type should be GPT.");
        Assert.AreEqual("EFI System", partition.Name, "Partition name should match.");
    }

    /// <summary>
    /// Tests that ParsePartition correctly parses a valid MBR partition segment.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_ValidMBRPartition_ReturnsCorrectPartition()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        // Test parsing "part_MBR_2"
        var result = parsePartitionMethod!.Invoke(provider, ["part_MBR_2"]);
        Assert.IsNotNull(result, "Should return a partition.");

        var partition = (IPartition)result!;
        Assert.AreEqual(2, partition.PartitionNumber, "Partition number should be 2.");
        Assert.AreEqual(PartitionTableType.MBR, partition.PartitionTable.TableType, "Partition table type should be MBR.");
        Assert.AreEqual("MBR Partition 2", partition.Name, "Partition name should match.");
    }

    /// <summary>
    /// Tests that ParsePartition throws InvalidOperationException for malformed partition segments.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_MalformedSegment_ThrowsInvalidOperationException()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        // Test with too few parts
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePartitionMethod!.Invoke(provider, ["part_GPT"]),
            "Should throw for malformed segment.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");

        // Test with invalid partition table type
        ex = Assert.Throws<TargetInvocationException>(() =>
            parsePartitionMethod!.Invoke(provider, ["part_INVALID_1"]),
            "Should throw for invalid table type.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");

        // Test with non-numeric partition number
        ex = Assert.Throws<TargetInvocationException>(() =>
            parsePartitionMethod!.Invoke(provider, ["part_GPT_ABC"]),
            "Should throw for non-numeric partition number.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
    }

    /// <summary>
    /// Tests that ParsePartition throws InvalidOperationException for non-existent partitions.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_NonExistentPartition_ThrowsInvalidOperationException()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        // Test with partition number that doesn't exist
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePartitionMethod!.Invoke(provider, ["part_GPT_99"]),
            "Should throw for non-existent partition.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
    }

    /// <summary>
    /// Tests that ParseFilesystem correctly parses a valid NTFS filesystem segment.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParseFilesystem_ValidNTFS_ReturnsCorrectFilesystem()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
        Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

        // Get a partition first
        var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_2"]);
        Assert.IsNotNull(partitionResult, "Should return a partition.");
        var partition = (IPartition)partitionResult!;

        // Test parsing "fs_NTFS"
        var result = parseFilesystemMethod!.Invoke(provider, [partition, "fs_NTFS"]);
        Assert.IsNotNull(result, "Should return a filesystem.");

        var filesystem = (IFilesystem)result!;
        Assert.AreEqual(FileSystemType.NTFS, filesystem.Type, "Filesystem type should be NTFS.");
        Assert.AreEqual(partition.PartitionNumber, filesystem.Partition.PartitionNumber, "Partition number should match.");
    }

    /// <summary>
    /// Tests that ParseFilesystem correctly parses a valid FAT32 filesystem segment.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParseFilesystem_ValidFAT32_ReturnsCorrectFilesystem()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
        Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

        // Get a partition first
        var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
        Assert.IsNotNull(partitionResult, "Should return a partition.");
        var partition = (IPartition)partitionResult!;

        // Test parsing "fs_FAT32"
        var result = parseFilesystemMethod!.Invoke(provider, [partition, "fs_FAT32"]);
        Assert.IsNotNull(result, "Should return a filesystem.");

        var filesystem = (IFilesystem)result!;
        Assert.AreEqual(FileSystemType.FAT32, filesystem.Type, "Filesystem type should be FAT32.");
    }

    /// <summary>
    /// Tests that ParseFilesystem throws InvalidOperationException for malformed filesystem segments.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParseFilesystem_MalformedSegment_ThrowsInvalidOperationException()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
        Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

        // Get a partition first
        var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
        Assert.IsNotNull(partitionResult, "Should return a partition.");
        var partition = (IPartition)partitionResult!;

        // Test with too few parts
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parseFilesystemMethod!.Invoke(provider, [partition, "fs"]),
            "Should throw for malformed segment.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");

        // Test with invalid filesystem type
        ex = Assert.Throws<TargetInvocationException>(() =>
            parseFilesystemMethod!.Invoke(provider, [partition, "fs_INVALID"]),
            "Should throw for invalid filesystem type.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
    }

    /// <summary>
    /// Tests that ParseFilesystem throws InvalidOperationException for non-existent filesystems.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParseFilesystem_NonExistentFilesystem_ThrowsInvalidOperationException()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");
        Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

        // Get a partition that doesn't have an Ext4 filesystem
        var partitionResult = parsePartitionMethod!.Invoke(provider, ["part_GPT_1"]);
        Assert.IsNotNull(partitionResult, "Should return a partition.");
        var partition = (IPartition)partitionResult!;

        // Test with filesystem type that doesn't exist for this partition
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parseFilesystemMethod!.Invoke(provider, [partition, "fs_Ext4"]),
            "Should throw for non-existent filesystem.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
    }

    /// <summary>
    /// Tests that ParsePath correctly identifies a geometry file path.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_GeometryFile_ReturnsGeometryType()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Test with geometry file at the root of the device
        // Use the actual device path from the GPT test disk
        var geometryPath = $"{s_gptRawDisk!.DevicePath}{Path.DirectorySeparatorChar}geometry.json";
        var result = parsePathMethod!.Invoke(provider, [geometryPath]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual("geometry", tuple.Type, "Type should be 'geometry'.");
        Assert.IsNull(tuple.Partition, "Partition should be null for geometry.");
        Assert.IsNull(tuple.Filesystem, "Filesystem should be null for geometry.");
    }

    /// <summary>
    /// Tests that ParsePath correctly identifies a disk-level path.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_DiskPath_ReturnsDiskType()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Test with root path
        var result = parsePathMethod!.Invoke(provider, ["/"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual("disk", tuple.Type, "Type should be 'disk'.");
        Assert.IsNull(tuple.Partition, "Partition should be null for disk.");
        Assert.IsNull(tuple.Filesystem, "Filesystem should be null for disk.");
    }

    /// <summary>
    /// Tests that ParsePath correctly identifies a partition path.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_PartitionPath_ReturnsPartitionType()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Test with partition path (no filesystem segment)
        var result = parsePathMethod!.Invoke(provider, ["part_GPT_1"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual("partition", tuple.Type, "Type should be 'partition'.");
        Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
        Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Partition number should be 1.");
        Assert.IsNull(tuple.Filesystem, "Filesystem should be null for partition-only path.");
    }

    /// <summary>
    /// Tests that ParsePath correctly identifies a file path with partition and filesystem.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_FilePath_ReturnsFileType()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Test with full file path
        var result = parsePathMethod!.Invoke(provider, ["part_GPT_1/fs_FAT32/test/file.txt"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual("file", tuple.Type, "Type should be 'file'.");
        Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
        Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
        Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Partition number should be 1.");
        Assert.AreEqual(FileSystemType.FAT32, tuple.Filesystem!.Type, "Filesystem type should be FAT32.");
    }

    /// <summary>
    /// Tests that ParsePath handles MBR partition paths correctly.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_MBRPartitionPath_ReturnsCorrectPartition()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Test with MBR partition path
        var result = parsePathMethod!.Invoke(provider, ["part_MBR_2/fs_NTFS/data/file.dat"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual("file", tuple.Type, "Type should be 'file'.");
        Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
        Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
        Assert.AreEqual(2, tuple.Partition!.PartitionNumber, "Partition number should be 2.");
        Assert.AreEqual(PartitionTableType.MBR, tuple.Partition.PartitionTable.TableType, "Table type should be MBR.");
        Assert.AreEqual(FileSystemType.NTFS, tuple.Filesystem!.Type, "Filesystem type should be NTFS.");
    }

    /// <summary>
    /// Tests that ParsePath handles paths with different separators correctly on Windows.
    /// On Windows, both forward slash and backslash are valid path separators.
    /// On Linux/macOS, backslash is a valid filename character, not a separator.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_DifferentSeparators_NormalizesCorrectly()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        if (OperatingSystem.IsWindows())
        {
            // On Windows, backslash is a path separator
            var result = parsePathMethod!.Invoke(provider, ["part_GPT_1\\fs_FAT32\\test\\file.txt"]);
            Assert.IsNotNull(result, "Should return a tuple.");

            var tuple = ((string Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual("file", tuple.Type, "Type should be 'file'.");
            Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
            Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
        }
        else
        {
            // On Linux/macOS, backslash is a filename character, not a separator
            // The entire string becomes one segment, which fails partition parsing
            var ex = Assert.Throws<TargetInvocationException>(() =>
                parsePathMethod!.Invoke(provider, ["part_GPT_1\\fs_FAT32\\test\\file.txt"]),
                "Should throw for backslash path on non-Windows platforms.");
            Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
        }
    }

    /// <summary>
    /// Tests that ParsePath throws appropriate exceptions for invalid paths.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_InvalidPartitionSegment_ThrowsException()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Test with invalid partition segment format - this should still parse as file type
        // but will fail when trying to parse the partition
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePathMethod!.Invoke(provider, ["part_INVALID/fs_NTFS/file.txt"]),
            "Should throw for invalid partition segment.");
        Assert.IsInstanceOf<InvalidOperationException>(ex!.InnerException, "Inner exception should be InvalidOperationException.");
    }

}