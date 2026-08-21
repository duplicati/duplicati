using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
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
    private class MockPartitionTable(PartitionTableType tableType, IRawDisk? rawDisk = null) : IPartitionTable
    {
        public IRawDisk? RawDisk => rawDisk;
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
    /// Tests that ParsePartition throws UserInformationException for partitions that
    /// do not exist on the target disk when the backup contains no partition
    /// information (partitioninfo.json) to create them from.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_NonExistentPartition_ThrowsUserInformationException()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        // Test with partition number that doesn't exist; without partition info in
        // the backup, the partition cannot be created on the target disk
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePartitionMethod!.Invoke(provider, ["part_GPT_99"]),
            "Should throw for non-existent partition.");
        Assert.IsInstanceOf<UserInformationException>(ex!.InnerException, "Inner exception should be UserInformationException.");
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
        var geometryPath = $"{s_gptRawDisk!.DevicePath}{Path.DirectorySeparatorChar}{GeometryMetadata.FileName}";
        var result = parsePathMethod!.Invoke(provider, [geometryPath]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.Geometry, tuple.Type, "Type should be 'Geometry'.");
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

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.Disk, tuple.Type, "Type should be 'Disk'.");
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

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.Partition, tuple.Type, "Type should be 'Partition'.");
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

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.File, tuple.Type, "Type should be 'File'.");
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

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.File, tuple.Type, "Type should be 'File'.");
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

            var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
            Assert.AreEqual(RestorePathType.File, tuple.Type, "Type should be 'File'.");
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

    /// <summary>
    /// Tests that ParsePath fails for partition-level content when the target is a
    /// whole disk, since there is no partition to write the content into.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_PartitionContentOnDiskTarget_Throws()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Filesystem block without a partition segment indicates a partition backup
        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePathMethod!.Invoke(provider, ["fs_NTFS/0000AB"]),
            "Should throw for filesystem content without a partition segment.");
        Assert.IsInstanceOf<UserInformationException>(ex!.InnerException, "Inner exception should be UserInformationException.");

        // A partition info file without a partition segment likewise
        ex = Assert.Throws<TargetInvocationException>(() =>
            parsePathMethod!.Invoke(provider, [PartitionInfoMetadata.FileName]),
            "Should throw for a partition info file without a partition segment.");
        Assert.IsInstanceOf<UserInformationException>(ex!.InnerException, "Inner exception should be UserInformationException.");
    }

    /// <summary>
    /// Creates a RestoreProvider that targets a single partition within a disk
    /// (subpath mode), with only the given target partition registered.
    /// The target partition's table is backed by the GPT test disk so that
    /// block size validation in filesystem creation has a sector size.
    /// </summary>
    private static RestoreProvider CreateRestoreProviderForPartitionTargetTests(string subpath, IPartition targetPartition)
    {
        var provider = new RestoreProvider();

        var partitionsField = typeof(RestoreProvider).GetField("_partitions", BindingFlags.NonPublic | BindingFlags.Instance);
        var filesystemsField = typeof(RestoreProvider).GetField("_filesystems", BindingFlags.NonPublic | BindingFlags.Instance);
        var targetDiskField = typeof(RestoreProvider).GetField("_targetDisk", BindingFlags.NonPublic | BindingFlags.Instance);
        var subpathField = typeof(RestoreProvider).GetField("_subpath", BindingFlags.NonPublic | BindingFlags.Instance);

        partitionsField?.SetValue(provider, new List<IPartition> { targetPartition });
        filesystemsField?.SetValue(provider, new List<IFilesystem>());
        targetDiskField?.SetValue(provider, s_gptRawDisk);
        subpathField?.SetValue(provider, subpath);

        return provider;
    }

    /// <summary>
    /// Creates a mock target partition (number 1 on a GPT table) for partition-target tests.
    /// </summary>
    private static MockPartition CreateTargetPartition(long size = 20971520)
        => new(new MockPartitionTable(PartitionTableType.GPT, s_gptRawDisk), 1, PartitionType.Primary, 1048576, size, "Target Partition", FileSystemType.NTFS);

    /// <summary>
    /// Gets the internal partition info dictionary from a RestoreProvider.
    /// </summary>
    private static ConcurrentDictionary<int, PartitionInfoMetadata> GetPartitionInfos(RestoreProvider provider)
    {
        var field = typeof(RestoreProvider).GetField("_partitionInfos", BindingFlags.NonPublic | BindingFlags.Instance);
        return (ConcurrentDictionary<int, PartitionInfoMetadata>)field!.GetValue(provider)!;
    }

    /// <summary>
    /// Creates partition info metadata describing a source partition.
    /// The block size is a multiple of common sector sizes (512 and 4096).
    /// </summary>
    private static PartitionInfoMetadata CreateTestPartitionInfo(int partitionNumber, long partitionSize, int blockSize, FileSystemType fsType)
        => new()
        {
            Partition = new PartitionGeometry
            {
                Number = partitionNumber,
                Type = PartitionType.Primary,
                StartOffset = 1048576,
                Size = partitionSize,
                Name = "Source Partition",
                FilesystemType = fsType,
                VolumeGuid = Guid.NewGuid(),
                TableType = PartitionTableType.GPT
            },
            Filesystem = new FilesystemGeometry
            {
                PartitionNumber = partitionNumber,
                Type = fsType,
                PartitionStartOffset = 1048576,
                BlockSize = blockSize
            }
        };

    /// <summary>
    /// Tests that ParsePath identifies a partition info file directly inside a
    /// partition folder as a partition info item.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_PartitionInfoFile_ReturnsPartitionInfoType()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        var result = parsePathMethod!.Invoke(provider, [$"part_GPT_1/{PartitionInfoMetadata.FileName}"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.PartitionInfo, tuple.Type, "Type should be 'PartitionInfo'.");
        Assert.IsNull(tuple.Partition, "Partition should be null for partition info.");
        Assert.IsNull(tuple.Filesystem, "Filesystem should be null for partition info.");
    }

    /// <summary>
    /// Tests that ParsePath treats a file named partitioninfo.json inside a
    /// filesystem as a regular file, not as partition info metadata.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_PartitionInfoInsideFilesystem_ReturnsFileType()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        var result = parsePathMethod!.Invoke(provider, [$"part_GPT_1/fs_FAT32/{PartitionInfoMetadata.FileName}"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.File, tuple.Type, "A partitioninfo.json file inside a filesystem should be treated as a regular file.");
        Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
        Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Partition number should be 1.");
        Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
        Assert.AreEqual(FileSystemType.FAT32, tuple.Filesystem!.Type, "Filesystem type should be FAT32.");
    }

    /// <summary>
    /// Tests that ParsePartition maps a differing source partition number to the
    /// resolved target partition when restoring into a partition (subpath mode).
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_DifferentSourceNumberWithSubpath_MapsToTargetPartition()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        // The restore path refers to source partition 2, but the target is partition 1
        var result = parsePartitionMethod!.Invoke(provider, ["part_GPT_2"]);
        Assert.IsNotNull(result, "Should return a partition.");

        var partition = (IPartition)result!;
        Assert.AreEqual(1, partition.PartitionNumber, "Should map to the target partition number.");
        Assert.AreSame(targetPartition, partition, "Should return the resolved target partition.");
    }

    /// <summary>
    /// Tests that ParsePartition still throws for a non-existent partition when not
    /// restoring into a partition (no subpath), even if only one partition is registered.
    /// Without partition information in the backup, the partition cannot be created
    /// on the target disk.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePartition_DifferentSourceNumberWithoutSubpath_ThrowsUserInformationException()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests(string.Empty, targetPartition);
        var parsePartitionMethod = typeof(RestoreProvider).GetMethod("ParsePartition", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePartitionMethod, "ParsePartition method should exist.");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePartitionMethod!.Invoke(provider, ["part_GPT_2"]),
            "Should throw for a non-existent partition when not in subpath mode.");
        Assert.IsInstanceOf<UserInformationException>(ex!.InnerException, "Inner exception should be UserInformationException.");
    }

    /// <summary>
    /// Tests that ParseFilesystem creates a filesystem handler from the partition info
    /// captured for the target partition, even when the restore path refers to a
    /// different source partition number.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParseFilesystem_PartitionInfoForTargetPartition_CreatesFilesystem()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);
        var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

        // Partition info describing source partition 2 was captured for target partition 1
        GetPartitionInfos(provider)[1] = CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS);

        var result = parseFilesystemMethod!.Invoke(provider, [targetPartition, "fs_NTFS"]);
        Assert.IsNotNull(result, "Should return a filesystem.");

        var filesystem = (IFilesystem)result!;
        Assert.AreEqual(FileSystemType.NTFS, filesystem.Type, "Filesystem type should be NTFS.");
        Assert.AreEqual(1, filesystem.Partition.PartitionNumber, "Filesystem should reference the target partition.");
    }

    /// <summary>
    /// Tests that ParseFilesystem throws a UserInformationException when restoring
    /// into a partition without any captured partition info.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParseFilesystem_MissingPartitionInfo_ThrowsUserInformationException()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);
        var parseFilesystemMethod = typeof(RestoreProvider).GetMethod("ParseFilesystem", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parseFilesystemMethod, "ParseFilesystem method should exist.");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parseFilesystemMethod!.Invoke(provider, [targetPartition, "fs_NTFS"]),
            "Should throw when no partition info has been captured.");
        Assert.IsInstanceOf<UserInformationException>(ex!.InnerException, "Inner exception should be UserInformationException.");
    }

    /// <summary>
    /// Tests the end-to-end path parsing for restoring a backup of a source
    /// partition into a target partition with a different partition number.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_DifferentSourceNumberWithSubpath_ReturnsTargetPartition()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Partition info describing source partition 2 was captured for target partition 1
        GetPartitionInfos(provider)[1] = CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS);

        var result = parsePathMethod!.Invoke(provider, ["part_GPT_2/fs_NTFS/data/file.txt"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.File, tuple.Type, "Type should be 'File'.");
        Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
        Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Should resolve to the target partition number.");
        Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
        Assert.AreEqual(FileSystemType.NTFS, tuple.Filesystem!.Type, "Filesystem type should be NTFS.");
    }

    /// <summary>
    /// Tests that writing a partition info file during a partition-target restore
    /// captures the metadata for the target partition, regardless of the partition
    /// number in the path.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_OpenWrite_PartitionInfo_CapturedForTargetPartition()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);

        var info = CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS);
        var bytes = System.Text.Encoding.UTF8.GetBytes(info.ToJson());

        await using (var stream = await provider.OpenWrite($"part_GPT_2/{PartitionInfoMetadata.FileName}", CancellationToken.None))
        {
            await stream.WriteAsync(bytes);
        }

        var infos = GetPartitionInfos(provider);
        Assert.IsTrue(infos.TryGetValue(1, out var captured), "Partition info should be captured under the target partition number.");
        Assert.AreEqual(4096, captured!.Filesystem!.BlockSize, "Block size should be preserved.");
        Assert.AreEqual(FileSystemType.NTFS, captured.Filesystem.Type, "Filesystem type should be preserved.");
    }

    /// <summary>
    /// Tests that ParsePath fails when a geometry file is part of the restore set
    /// and the target is a partition, since that indicates a full-disk backup.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_GeometryFileOnPartitionTarget_Throws()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        var ex = Assert.Throws<TargetInvocationException>(() =>
            parsePathMethod!.Invoke(provider, [$"part_GPT_1/{GeometryMetadata.FileName}"]),
            "Should throw for a geometry file when the target is a partition.");
        Assert.IsInstanceOf<UserInformationException>(ex!.InnerException, "Inner exception should be UserInformationException.");
    }

    /// <summary>
    /// Tests that capturing partition info for a second, different source partition
    /// fails, as that indicates a multi-partition (disk) backup being restored into
    /// a single partition.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_OpenWrite_ConflictingPartitionInfos_Throws()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);

        var first = System.Text.Encoding.UTF8.GetBytes(CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS).ToJson());
        var second = System.Text.Encoding.UTF8.GetBytes(CreateTestPartitionInfo(3, 10485760, 4096, FileSystemType.NTFS).ToJson());

        await using (var stream = await provider.OpenWrite($"part_GPT_1/{PartitionInfoMetadata.FileName}", CancellationToken.None))
            await stream.WriteAsync(first);

        var stream2 = await provider.OpenWrite($"part_GPT_1/{PartitionInfoMetadata.FileName}", CancellationToken.None);
        await stream2.WriteAsync(second);
        Assert.Throws<UserInformationException>(() => stream2.Dispose(),
            "Capturing info for a different source partition should throw.");
    }

    /// <summary>
    /// Tests that reading a partition info file that was not captured (whole-disk
    /// restore) returns an empty stream instead of throwing.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_OpenRead_PartitionInfoNotCaptured_ReturnsEmptyStream()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();

        await using var stream = await provider.OpenRead($"part_GPT_1/{PartitionInfoMetadata.FileName}", CancellationToken.None);

        Assert.AreEqual(0, stream.Length, "Whole-disk restores should return an empty stream for uncaptured partition info.");
    }

    /// <summary>
    /// Tests that reading a captured partition info file returns its JSON content.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_OpenRead_CapturedPartitionInfo_ReturnsJson()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);

        GetPartitionInfos(provider)[1] = CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS);

        await using var stream = await provider.OpenRead($"part_GPT_2/{PartitionInfoMetadata.FileName}", CancellationToken.None);
        Assert.Greater(stream.Length, 0, "Captured partition info should be readable.");

        using var reader = new StreamReader(stream);
        var parsed = PartitionInfoMetadata.FromJson(await reader.ReadToEndAsync());

        Assert.IsNotNull(parsed, "Read content should be valid partition info JSON.");
        Assert.AreEqual(4096, parsed!.Filesystem!.BlockSize, "Block size should be preserved.");
    }

    /// <summary>
    /// Tests that GetFileLength returns zero for a partition info file that was
    /// not captured (whole-disk restore) instead of throwing.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_GetFileLength_PartitionInfoNotCaptured_ReturnsZero()
    {
        using var provider = CreateRestoreProviderForPathParsingTests();

        var length = await provider.GetFileLength($"part_GPT_1/{PartitionInfoMetadata.FileName}", CancellationToken.None);

        Assert.AreEqual(0L, length, "Whole-disk restores should report zero length for uncaptured partition info.");
    }

    /// <summary>
    /// Tests that a file named geometry.json inside a filesystem is treated as a
    /// regular file when the restore target is a partition; only a geometry file
    /// at the disk or partition level indicates a full-disk backup.
    /// </summary>
    [Test]
    public void Test_RestoreProvider_ParsePath_GeometryFileInsideFilesystemOnPartitionTarget_ReturnsFileType()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);
        var parsePathMethod = typeof(RestoreProvider).GetMethod("ParsePath", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.IsNotNull(parsePathMethod, "ParsePath method should exist.");

        // Partition info describing source partition 2 was captured for target partition 1
        GetPartitionInfos(provider)[1] = CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS);

        var result = parsePathMethod!.Invoke(provider, [$"part_GPT_2/fs_NTFS/backups/{GeometryMetadata.FileName}"]);
        Assert.IsNotNull(result, "Should return a tuple.");

        var tuple = ((RestorePathType Type, IPartition? Partition, IFilesystem? Filesystem))result!;
        Assert.AreEqual(RestorePathType.File, tuple.Type, "A geometry.json file inside a filesystem should be treated as a regular file.");
        Assert.IsNotNull(tuple.Partition, "Partition should not be null.");
        Assert.AreEqual(1, tuple.Partition!.PartitionNumber, "Should resolve to the target partition number.");
        Assert.IsNotNull(tuple.Filesystem, "Filesystem should not be null.");
    }

    /// <summary>
    /// Tests that a partition info file without a source partition number is not
    /// captured, since multi-partition conflicts cannot be detected without it.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_OpenWrite_IncompletePartitionInfo_NotCaptured()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);

        var incomplete = new PartitionInfoMetadata
        {
            Filesystem = new FilesystemGeometry
            {
                PartitionNumber = 2,
                Type = FileSystemType.NTFS,
                PartitionStartOffset = 1048576,
                BlockSize = 4096
            }
        };
        var bytes = System.Text.Encoding.UTF8.GetBytes(incomplete.ToJson());

        await using (var stream = await provider.OpenWrite($"part_GPT_2/{PartitionInfoMetadata.FileName}", CancellationToken.None))
            await stream.WriteAsync(bytes);

        Assert.IsEmpty(GetPartitionInfos(provider), "Partition info without a source partition number should not be captured.");
    }

    /// <summary>
    /// Tests that a partition info file without a source partition number does not
    /// overwrite a previously captured, complete partition info.
    /// </summary>
    [Test]
    public async Task Test_RestoreProvider_OpenWrite_IncompletePartitionInfo_DoesNotOverwriteCaptured()
    {
        var targetPartition = CreateTargetPartition();
        using var provider = CreateRestoreProviderForPartitionTargetTests("part_GPT_1", targetPartition);

        var complete = System.Text.Encoding.UTF8.GetBytes(CreateTestPartitionInfo(2, 10485760, 4096, FileSystemType.NTFS).ToJson());
        var incomplete = System.Text.Encoding.UTF8.GetBytes(new PartitionInfoMetadata
        {
            Filesystem = new FilesystemGeometry
            {
                PartitionNumber = 2,
                Type = FileSystemType.NTFS,
                PartitionStartOffset = 1048576,
                BlockSize = 8192
            }
        }.ToJson());

        await using (var stream = await provider.OpenWrite($"part_GPT_2/{PartitionInfoMetadata.FileName}", CancellationToken.None))
            await stream.WriteAsync(complete);

        await using (var stream = await provider.OpenWrite($"part_GPT_2/{PartitionInfoMetadata.FileName}", CancellationToken.None))
            await stream.WriteAsync(incomplete);

        var infos = GetPartitionInfos(provider);
        Assert.IsTrue(infos.TryGetValue(1, out var captured), "Partition info should be captured under the target partition number.");
        Assert.AreEqual(2, captured!.Partition!.Number, "The captured partition info should be unchanged.");
        Assert.AreEqual(4096, captured.Filesystem!.BlockSize, "The captured block size should be unchanged.");
    }

}