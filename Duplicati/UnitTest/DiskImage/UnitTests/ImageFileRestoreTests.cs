using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
using Duplicati.Proprietary.DiskImage.Partition;
using Docker.DotNet;
using Microsoft.Win32.SafeHandles;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

/// <summary>
/// Tests for restoring disk images to raw image files. These tests need no
/// privileges, as they never touch a physical disk.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class ImageFileRestoreTests
{
    private const long MiB = 1024 * 1024;
    private const int BlockSize = (int)MiB;

    /// <summary>
    /// The folder below which each test gets its own folder; it is mounted into
    /// the qemu-img container.
    /// </summary>
    private string _root = null!;
    private string _folder = null!;

    /// <summary>
    /// How qemu-img is run: from the PATH, in a container, or not at all.
    /// </summary>
    private bool _qemuResolved;
    private bool _localQemu;
    private IContainer? _qemuContainer;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), $"duplicati-imagefile-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDownAsync()
    {
        if (_qemuContainer != null)
            await _qemuContainer.DisposeAsync();

        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [SetUp]
    public void SetUp()
    {
        _folder = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    /// <summary>
    /// Creates a geometry describing a 64 MiB GPT disk with two partitions that use
    /// raw block-level (unknown filesystem) backups.
    /// </summary>
    private static GeometryMetadata CreateGptGeometry()
        => new()
        {
            Disk = new DiskGeometry
            {
                DevicePath = "/dev/source",
                SectorSize = 512,
                Size = 64 * MiB,
                Sectors = 64 * MiB / 512,
                TableType = PartitionTableType.GPT
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.GPT,
                SectorSize = 512,
                HasProtectiveMbr = true,
                HeaderSize = 92
            },
            Partitions =
            [
                new PartitionGeometry { Number = 1, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 1 * MiB, Size = 20 * MiB, Name = "first", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT },
                new PartitionGeometry { Number = 2, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 21 * MiB, Size = 30 * MiB, Name = "second", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT }
            ],
            Filesystems =
            [
                new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = 1, PartitionStartOffset = 1 * MiB, BlockSize = BlockSize },
                new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = 2, PartitionStartOffset = 21 * MiB, BlockSize = BlockSize }
            ]
        };

    /// <summary>
    /// Gets the test content of a block. Every third block is all zero, like
    /// unused space on a disk.
    /// </summary>
    private static byte[] GetBlock(int partition, int block)
    {
        var data = new byte[BlockSize];
        if (block % 3 == 2)
            return data;

        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(partition * 31 + block * 7 + i);
        return data;
    }

    private static Dictionary<string, string?> Options(bool overwrite)
    {
        var options = new Dictionary<string, string?>();
        if (overwrite)
            options["overwrite"] = "true";
        return options;
    }

    private static async Task WriteAsync(IRestoreDestinationProvider provider, string path, byte[] data, bool readWrite)
    {
        await using var stream = readWrite
            ? await provider.OpenReadWrite(path, CancellationToken.None)
            : await provider.OpenWrite(path, CancellationToken.None);
        await stream.WriteAsync(data);
    }

    private static string BlockPath(string target, int partition, int block)
        => Path.Combine(target, $"part_GPT_{partition}", "fs_Unknown", ((long)block * BlockSize).ToString("X16"));

    /// <summary>
    /// Restores the geometry and all blocks of a disk (normally <see cref="CreateGptGeometry"/>) to the image.
    /// </summary>
    private static async Task RestoreFullDiskAsync(string imagePath, Dictionary<string, string?> options, GeometryMetadata geometry)
    {
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", options);
        await provider.Initialize(CancellationToken.None);

        var target = provider.TargetDestination;
        await WriteAsync(provider, Path.Combine(target, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(geometry.ToJson()), readWrite: true);

        foreach (var partition in geometry.Partitions!)
            for (var block = 0; block < partition.Size / BlockSize; block++)
                await WriteAsync(provider, BlockPath(target, partition.Number, block), GetBlock(partition.Number, block), readWrite: false);

        await provider.Finalize(null, true, CancellationToken.None);
    }

    private static async Task<List<IPartition>> ReadPartitionsAsync(IRawDisk disk)
    {
        using var table = await PartitionTableFactory.CreateAsync(disk, CancellationToken.None);
        Assert.IsNotNull(table, "The image should have a partition table.");
        Assert.AreEqual(PartitionTableType.GPT, table!.TableType, "The image should have a GPT partition table.");

        var partitions = new List<IPartition>();
        await foreach (var p in table.EnumeratePartitions(CancellationToken.None))
            partitions.Add(p);
        return partitions;
    }

    private static async Task<ImageFile> OpenImageAsync(string path)
    {
        var disk = new ImageFile(path, path, createNew: false, sparse: true, format: null);
        Assert.IsNull(await disk.InitializeAsync(false, CancellationToken.None), "The image should open.");
        return disk;
    }

    private static async Task<byte[]> ReadAsync(IRawDisk disk, long offset, int length)
    {
        var buffer = new byte[length];
        Assert.AreEqual(length, await disk.ReadBytesAsync(offset, buffer, CancellationToken.None), "The full range should be read.");
        return buffer;
    }

    /// <summary>
    /// Gets the number of bytes allocated on disk for a file, or null on Windows.
    /// </summary>
    private static long? GetAllocatedBytes(string path)
    {
        if (OperatingSystem.IsWindows())
            return null;

        var psi = new ProcessStartInfo("du", ["-k", path]) { RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return long.Parse(output.Split('\t', ' ')[0]) * 1024;
    }

    [Test]
    public void Test_SplitImageAndSubpath()
    {
        Assert.AreEqual(("/images/disk.img", ""), SourceProvider.SplitImageAndSubpath("/images/disk.img"));
        Assert.AreEqual(("/images/disk.img", ""), SourceProvider.SplitImageAndSubpath("/images/disk.img/"));
        Assert.AreEqual(("/images/disk.img", "part_GPT_2"), SourceProvider.SplitImageAndSubpath("/images/disk.img/part_GPT_2"));
        Assert.AreEqual(("/images/disk.img", "part_MBR_1/fs_NTFS"), SourceProvider.SplitImageAndSubpath("/images/disk.img/part_MBR_1/fs_NTFS"));
        Assert.AreEqual(("/images/part.img", ""), SourceProvider.SplitImageAndSubpath("/images/part.img"));

        // Folders that start with "part_" but do not name a partition belong to the image path
        Assert.AreEqual(("/data/part_backups/server.img", ""), SourceProvider.SplitImageAndSubpath("/data/part_backups/server.img"));
        Assert.AreEqual(("/data/part_backups/server.img", "part_GPT_1"), SourceProvider.SplitImageAndSubpath("/data/part_backups/server.img/part_GPT_1"));

        // An existing image file ends the image path, whatever the folder names
        var folder = Path.Combine(_folder, "part_GPT_9");
        Directory.CreateDirectory(folder);
        var image = Path.Combine(folder, "disk.img");
        File.WriteAllBytes(image, new byte[512]);
        Assert.AreEqual((image, "part_GPT_1"), SourceProvider.SplitImageAndSubpath(image + "/part_GPT_1"));

        if (OperatingSystem.IsWindows())
            Assert.AreEqual(("C:\\images\\disk.img", "part_GPT_1"), SourceProvider.SplitImageAndSubpath("C:\\images\\disk.img\\part_GPT_1"));
    }

    [Test]
    public async Task Test_ImageFile_ApplyGeometry_ReadWrite()
    {
        var path = Path.Combine(_folder, "disk.img");
        using (var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Raw))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            Assert.AreEqual(0, disk.Size, "A new image is empty until the geometry is known.");
            Assert.AreEqual(ImageFile.DefaultSectorSize, disk.SectorSize);

            disk.ApplyGeometry(8 * MiB, 4096);
            Assert.AreEqual(8 * MiB, disk.Size);
            Assert.AreEqual(4096, disk.SectorSize);
            Assert.AreEqual(8 * MiB / 4096, disk.Sectors);
            Assert.AreEqual(8 * MiB, new FileInfo(path).Length, "The file should have the size of the disk.");

            var data = Enumerable.Range(0, 8192).Select(i => (byte)(i % 251 + 1)).ToArray();
            await disk.WriteBytesAsync(3 * MiB, data, CancellationToken.None);
            CollectionAssert.AreEqual(data, await ReadAsync(disk, 3 * MiB, data.Length));

            // Unaligned writes are allowed on image files
            await disk.WriteBytesAsync(5 * MiB + 3, new byte[] { 9, 9, 9 }, CancellationToken.None);
            CollectionAssert.AreEqual(new byte[] { 0, 9, 9, 9, 0 }, await ReadAsync(disk, 5 * MiB + 2, 5));

            Assert.ThrowsAsync<InvalidOperationException>(() => disk.WriteBytesAsync(8 * MiB - 2, new byte[4], CancellationToken.None));
            Assert.ThrowsAsync<InvalidOperationException>(() => disk.ReadBytesAsync(8 * MiB - 2, new byte[4], CancellationToken.None));

            // The sector size cannot change once data has been written
            var changed = Assert.Throws<UserInformationException>(() => disk.ApplyGeometry(8 * MiB, 512));
            Assert.AreEqual("DiskImageFileSectorSizeChanged", changed!.HelpID);
        }
    }

    [Test]
    public async Task Test_ImageFile_ZeroWrites_KeepHolesAndOverwriteData()
    {
        var path = Path.Combine(_folder, "disk.img");
        using var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Raw);
        Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
        disk.ApplyGeometry(64 * MiB, 512);

        // Writing zeros to a hole must not allocate it
        for (var i = 0; i < 64; i++)
            await disk.WriteBytesAsync(i * MiB, new byte[MiB], CancellationToken.None);
        disk.Commit();

        var allocated = GetAllocatedBytes(path);
        if (allocated != null)
            Assert.Less(allocated.Value, 4 * MiB, "Zero writes to a sparse image should not allocate space.");

        // Writing zeros over data must replace the data
        var data = Enumerable.Repeat((byte)0xAB, 4096).ToArray();
        await disk.WriteBytesAsync(MiB, data, CancellationToken.None);
        await disk.WriteBytesAsync(MiB, new byte[4096], CancellationToken.None);
        CollectionAssert.AreEqual(new byte[4096], await ReadAsync(disk, MiB, 4096), "Zeros written over data should be stored.");
    }

    [Test]
    public async Task Test_ImageFile_NotSparse_WritesZeros()
    {
        var path = Path.Combine(_folder, "disk.img");
        using var disk = new ImageFile(path, path, createNew: true, sparse: false, format: ImageFormat.Raw);
        Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
        disk.ApplyGeometry(4 * MiB, 512);

        await disk.WriteBytesAsync(0, new byte[MiB], CancellationToken.None);
        Assert.AreEqual(4 * MiB, new FileInfo(path).Length);
    }

    [Test]
    public async Task Test_ImageFileRestore_FullDisk()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());

        Assert.IsTrue(File.Exists(imagePath), "The image should be written.");
        Assert.IsFalse(File.Exists(RestoreProvider.GetPartialImagePath(imagePath)), "The partial file should be renamed.");
        Assert.AreEqual(64 * MiB, new FileInfo(imagePath).Length, "The image should have the size of the source disk.");

        // APFS fills holes between written extents when the file is synced, so the
        // allocation is only checked on Linux, where the filesystems keep them
        var allocated = OperatingSystem.IsLinux() ? GetAllocatedBytes(imagePath) : null;
        if (allocated != null)
            Assert.Less(allocated.Value, 40 * MiB, "The all-zero blocks should not be allocated.");

        await VerifyFullDiskAsync(imagePath);
    }

    /// <summary>
    /// Verifies that an image holds the disk restored by <see cref="RestoreFullDiskAsync"/>.
    /// </summary>
    private static async Task VerifyFullDiskAsync(string imagePath)
    {
        var geometry = CreateGptGeometry();
        using var disk = await OpenImageAsync(imagePath);
        Assert.AreEqual(64 * MiB, disk.Size, "The disk should have the size of the source disk.");

        var partitions = await ReadPartitionsAsync(disk);
        Assert.AreEqual(2, partitions.Count, "Both partitions should be in the partition table.");

        foreach (var expected in geometry.Partitions!)
        {
            var actual = partitions.Single(p => p.PartitionNumber == expected.Number);
            Assert.AreEqual(expected.StartOffset, actual.StartOffset, $"Partition {expected.Number} offset");
            Assert.AreEqual(expected.Size, actual.Size, $"Partition {expected.Number} size");

            for (var block = 0; block < expected.Size / BlockSize; block++)
                CollectionAssert.AreEqual(GetBlock(expected.Number, block), await ReadAsync(disk, expected.StartOffset + (long)block * BlockSize, BlockSize), $"Partition {expected.Number} block {block}");
        }

        // The secondary GPT header is in the last sector
        var lastSector = await ReadAsync(disk, 64 * MiB - 512, 512);
        Assert.AreEqual("EFI PART", Encoding.ASCII.GetString(lastSector, 0, 8), "The secondary GPT header should be written.");
    }

    [Test]
    public async Task Test_ImageFileRestore_ExistingImage_RequiresOverwrite()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        await File.WriteAllTextAsync(imagePath, "existing");

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
            Assert.AreEqual("DiskImageFileExists", ex!.HelpID);
            ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Test(CancellationToken.None));
            Assert.AreEqual("DiskImageFileExists", ex!.HelpID);
        }

        Assert.AreEqual("existing", await File.ReadAllTextAsync(imagePath), "The existing image should not be touched.");

        await RestoreFullDiskAsync(imagePath, Options(overwrite: true), CreateGptGeometry());
        Assert.AreEqual(64 * MiB, new FileInfo(imagePath).Length, "The existing image should be replaced.");
    }

    [Test]
    public async Task Test_ImageFileRestore_ImageInUse_Aborts()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        await File.WriteAllTextAsync(imagePath, "existing");

        await using var inUse = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: true));
        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileInUse", ex!.HelpID);
    }

    [Test]
    public async Task Test_ImageFileRestore_ImageOpenedDuringRestore_KeepsPartial()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        var geometry = CreateGptGeometry();

        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: true));
        await provider.Initialize(CancellationToken.None);
        await WriteAsync(provider, Path.Combine(provider.TargetDestination, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(geometry.ToJson()), readWrite: true);
        await WriteAsync(provider, BlockPath(provider.TargetDestination, 1, 0), GetBlock(1, 0), readWrite: false);

        // Another program creates and opens the image before the restore completes
        await File.WriteAllTextAsync(imagePath, "other");
        await using (var inUse = new FileStream(imagePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Finalize(null, true, CancellationToken.None));
            Assert.AreEqual("DiskImageFileInUse", ex!.HelpID);
        }

        Assert.AreEqual("other", await File.ReadAllTextAsync(imagePath), "The image in use should not be replaced.");
        Assert.IsTrue(File.Exists(RestoreProvider.GetPartialImagePath(imagePath)), "The restored data should be kept in the partial file.");
    }

    [Test]
    public async Task Test_ImageFileRestore_FailedRestore_LeavesNoImage()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            await WriteAsync(provider, Path.Combine(provider.TargetDestination, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(CreateGptGeometry().ToJson()), readWrite: true);
            // The restore fails before Finalize
        }

        Assert.IsFalse(File.Exists(imagePath), "An incomplete restore should not produce the image.");
        Assert.IsTrue(File.Exists(RestoreProvider.GetPartialImagePath(imagePath)), "The incomplete image should be kept in the partial file.");
    }

    [Test]
    public async Task Test_ImageFileRestore_MissingFolder_Fails()
    {
        var imagePath = Path.Combine(_folder, "missing", "disk.img");
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false));
        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileFolderMissing", ex!.HelpID);
        ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Test(CancellationToken.None));
        Assert.AreEqual("DiskImageFileFolderMissing", ex!.HelpID);
        await Task.CompletedTask;
    }

    [Test]
    public async Task Test_ImageFileRestore_Test_DoesNotCreateImage()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false));
        await provider.Test(CancellationToken.None);

        Assert.IsEmpty(Directory.GetFileSystemEntries(_folder), "Testing the target should leave the folder unchanged.");
    }

    [Test]
    public async Task Test_ImageFileRestore_PartitionBackup_CreatesDisk()
    {
        var imagePath = Path.Combine(_folder, "partition.img");
        var source = new PartitionGeometry { Number = 3, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 5 * MiB, Size = 8 * MiB, Name = "data", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT };
        var info = new PartitionInfoMetadata
        {
            Partition = source,
            Filesystem = new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = 3, PartitionStartOffset = source.StartOffset, BlockSize = BlockSize }
        };

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            await WriteAsync(provider, Path.Combine(target, "part_GPT_3", PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes(info.ToJson()), readWrite: false);
            for (var block = 0; block < source.Size / BlockSize; block++)
                await WriteAsync(provider, BlockPath(target, 3, block), GetBlock(3, block), readWrite: false);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        using var disk = await OpenImageAsync(imagePath);
        var partitions = await ReadPartitionsAsync(disk);
        Assert.AreEqual(1, partitions.Count, "The image should hold the restored partition.");
        var partition = partitions[0];
        Assert.AreEqual(source.StartOffset, partition.StartOffset, "The partition should keep its source offset.");
        Assert.AreEqual(source.Size, partition.Size);
        Assert.Greater(disk.Size, source.StartOffset + source.Size, "The image should have room for the secondary GPT.");

        for (var block = 0; block < source.Size / BlockSize; block++)
            CollectionAssert.AreEqual(GetBlock(3, block), await ReadAsync(disk, partition.StartOffset + (long)block * BlockSize, BlockSize), $"Block {block}");
    }

    [Test]
    public async Task Test_ImageFileRestore_IntoPartitionOfExistingImage()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());

        // Restore a backup of a single partition into partition 2 of the image
        var info = new PartitionInfoMetadata
        {
            Partition = new PartitionGeometry { Number = 7, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 90 * MiB, Size = 4 * MiB, TableType = PartitionTableType.GPT },
            Filesystem = new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = 7, PartitionStartOffset = 90 * MiB, BlockSize = BlockSize }
        };

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            await WriteAsync(provider, Path.Combine(target, "part_GPT_7", PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes(info.ToJson()), readWrite: false);
            for (var block = 0; block < 4; block++)
                await WriteAsync(provider, BlockPath(target, 7, block), GetBlock(7, block), readWrite: false);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        Assert.IsFalse(File.Exists(RestoreProvider.GetPartialImagePath(imagePath)), "A partition restore writes the image in place.");

        var geometry = CreateGptGeometry();
        using var disk = await OpenImageAsync(imagePath);
        var second = geometry.Partitions![1];
        for (var block = 0; block < second.Size / BlockSize; block++)
        {
            var expected = block < 4 ? GetBlock(7, block) : GetBlock(2, block);
            CollectionAssert.AreEqual(expected, await ReadAsync(disk, second.StartOffset + (long)block * BlockSize, BlockSize), $"Partition 2 block {block}");
        }

        // The first partition is untouched
        CollectionAssert.AreEqual(GetBlock(1, 0), await ReadAsync(disk, geometry.Partitions[0].StartOffset, BlockSize));
    }

    [Test]
    public async Task Test_ImageFileRestore_IntoPartition_RequiresExistingImage()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_1", Options(overwrite: false));
        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileNotFound", ex!.HelpID);
        await Task.CompletedTask;
    }

    /// <summary>
    /// The container image qemu-img runs in when it is not installed locally.
    /// </summary>
    private const string QemuImage = "alpine:3.20";

    /// <summary>
    /// The path the test root folder is mounted at in the qemu-img container.
    /// </summary>
    private const string QemuMountPath = "/work";

    /// <summary>
    /// Runs qemu-img, which validates VHD and VHDX files independently of the
    /// code that wrote them. A local qemu-img is used when it is installed;
    /// otherwise qemu-img runs in a container, with the test folder mounted.
    /// The paths in the arguments must be inside the test folder.
    /// </summary>
    /// <returns>The exit code and output, or null when neither qemu-img nor Docker is available.</returns>
    private Task<(long ExitCode, string Output)?> RunQemuImgAsync(params string[] arguments)
        => RunQemuToolAsync("qemu-img", arguments);

    /// <summary>
    /// Runs a qemu tool (qemu-img or qemu-io), locally when qemu-img is
    /// installed, otherwise in a container with the test folder mounted. The
    /// paths in the arguments must be inside the test folder.
    /// </summary>
    /// <param name="tool">The tool to run.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The exit code and output, or null when neither qemu-img nor Docker is available.</returns>
    private async Task<(long ExitCode, string Output)?> RunQemuToolAsync(string tool, string[] arguments)
    {
        if (!_qemuResolved)
        {
            _qemuResolved = true;
            _localQemu = RunLocalQemuTool("qemu-img", ["--version"]) != null;
            if (!_localQemu)
                _qemuContainer = await StartQemuContainerAsync();
        }

        if (_localQemu)
            return RunLocalQemuTool(tool, arguments);

        if (_qemuContainer == null)
            return null;

        string ToContainerPath(string path)
            => QemuMountPath + "/" + Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');

        // The output is written to a file in the mounted folder and read from
        // there, as the exec output stream occasionally carries stray bytes
        var outputPath = Path.Combine(_root, $"qemu-{Guid.NewGuid():N}.out");
        var mapped = arguments.Select(a => a.StartsWith(_root, StringComparison.Ordinal) ? ToContainerPath(a) : a);
        var result = await _qemuContainer.ExecAsync(["sh", "-c", $"{tool} \"$@\" > {ToContainerPath(outputPath)} 2>&1", "sh", .. mapped]);
        try
        {
            return (result.ExitCode, await File.ReadAllTextAsync(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    /// <summary>
    /// Runs a locally installed qemu tool.
    /// </summary>
    /// <param name="tool">The tool to run.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The exit code and output, or null when the tool is not installed.</returns>
    private static (long ExitCode, string Output)? RunLocalQemuTool(string tool, string[] arguments)
    {
        try
        {
            var psi = new ProcessStartInfo(tool, arguments) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Starts a container with qemu-img installed and the test root folder mounted.
    /// </summary>
    /// <returns>The container, or null when Docker is not available.</returns>
    private async Task<IContainer?> StartQemuContainerAsync()
    {
        try
        {
            using (var client = new DockerClientConfiguration().CreateClient())
                await client.System.PingAsync();
        }
        catch (Exception ex)
        {
            await TestContext.Progress.WriteLineAsync($"Neither qemu-img nor Docker is available, so the image files are not checked with qemu-img: {ex.Message}");
            return null;
        }

        var container = new ContainerBuilder()
            .WithImage(QemuImage)
            .WithImagePullPolicy(PullPolicy.Missing)
            .WithBindMount(_root, QemuMountPath)
            .WithEntrypoint("sleep", "infinity")
            .Build();
        await container.StartAsync();

        var install = await container.ExecAsync(["apk", "add", "--no-cache", "qemu-img"]);
        Assert.AreEqual(0, install.ExitCode, $"Installing qemu-img in the container failed: {install.Stderr}");
        return container;
    }

    /// <summary>
    /// Checks a VHD or VHDX file with qemu-img, which reads the format
    /// independently of the code that wrote it: VHDX files must pass
    /// <c>qemu-img check</c> (qemu cannot check VHD files), qemu must report the
    /// same disk size, and the disk content qemu reads must equal the content
    /// <see cref="ImageFile"/> reads. Skipped when neither qemu-img nor Docker
    /// is available.
    /// </summary>
    /// <param name="path">The VHD or VHDX file.</param>
    private async Task AssertQemuReadsSameAsync(string path)
    {
        long size;
        string qemuFormat;
        var rawPath = path + ".qemu-compare.img";
        using (var disk = await OpenImageAsync(path))
        {
            size = disk.Size;
            qemuFormat = disk.Format == ImageFormat.Vhd ? "vpc" : "vhdx";

            // Dump the disk as this code reads it
            await using var raw = new FileStream(rawPath, FileMode.Create, FileAccess.Write);
            for (long offset = 0; offset < size; offset += MiB)
                await raw.WriteAsync(await ReadAsync(disk, offset, (int)Math.Min(MiB, size - offset)));
        }

        try
        {
            if (qemuFormat == "vhdx")
            {
                var check = await RunQemuImgAsync("check", "-f", qemuFormat, path);
                if (check == null)
                    return;
                Assert.AreEqual(0, check.Value.ExitCode, $"qemu-img check: {check.Value.Output}");
            }

            var info = await RunQemuImgAsync("info", "-f", qemuFormat, path);
            if (info == null)
                return;
            Assert.AreEqual(0, info.Value.ExitCode, $"qemu-img info: {info.Value.Output}");
            var virtualSize = System.Text.RegularExpressions.Regex.Match(info.Value.Output, @"virtual size: .*\((\d+) bytes\)");
            Assert.IsTrue(virtualSize.Success, $"qemu-img info should report the disk size: {info.Value.Output}");
            Assert.AreEqual(size, long.Parse(virtualSize.Groups[1].Value), "qemu-img should report the disk size.");

            var compare = await RunQemuImgAsync("compare", "-f", qemuFormat, "-F", "raw", path, rawPath);
            Assert.AreEqual(0, compare!.Value.ExitCode, $"qemu-img compare: {compare.Value.Output}");
        }
        finally
        {
            File.Delete(rawPath);
        }
    }

    [Test]
    public void Test_Crc32C_KnownValue()
    {
        Assert.AreEqual(0xE3069283u, Crc32C.Compute("123456789"u8), "CRC-32C check value");
        Assert.AreEqual(0x8A9136AAu, Crc32C.Compute(new byte[32]), "CRC-32C of 32 zero bytes");
    }

    [Test]
    public void Test_ImageFormatFromExtension()
    {
        Assert.AreEqual(ImageFormat.Vhd, RestoreProvider.GetImageFormatFromExtension("/images/disk.vhd"));
        Assert.AreEqual(ImageFormat.Vhdx, RestoreProvider.GetImageFormatFromExtension("/images/disk.VHDX"));
        Assert.AreEqual(ImageFormat.Raw, RestoreProvider.GetImageFormatFromExtension("/images/disk.img"));
        Assert.AreEqual(ImageFormat.Raw, RestoreProvider.GetImageFormatFromExtension("/images/disk"));
    }

    [TestCase("Vhd")]
    [TestCase("Vhdx")]
    public async Task Test_VirtualDisk_WriteCommitReopen(string formatName)
    {
        var format = Enum.Parse<ImageFormat>(formatName);
        var path = Path.Combine(_folder, "disk." + format.ToString().ToLowerInvariant());
        var data = Enumerable.Range(0, (int)MiB).Select(i => (byte)(i % 249 + 1)).ToArray();

        using (var disk = new ImageFile(path, path, createNew: true, sparse: true, format: format))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            disk.ApplyGeometry(64 * MiB, 512);

            // Spans the boundary between two 2 MiB blocks
            await disk.WriteBytesAsync(MiB + MiB / 2, data, CancellationToken.None);
            // Zeros are not stored
            await disk.WriteBytesAsync(32 * MiB, new byte[4 * MiB], CancellationToken.None);
            // The last sector of the disk
            await disk.WriteBytesAsync(64 * MiB - 512, Enumerable.Repeat((byte)0x5A, 512).ToArray(), CancellationToken.None);
            disk.Commit();
        }

        Assert.Less(new FileInfo(path).Length, 16 * MiB, "Only the written blocks should be stored.");

        using (var disk = new ImageFile(path, path, createNew: false, sparse: true, format: null))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            Assert.AreEqual(format, disk.Format, "The format should be detected from the content.");
            Assert.AreEqual(64 * MiB, disk.Size);
            Assert.AreEqual(512, disk.SectorSize);

            CollectionAssert.AreEqual(data, await ReadAsync(disk, MiB + MiB / 2, data.Length));
            CollectionAssert.AreEqual(new byte[MiB], await ReadAsync(disk, 32 * MiB, (int)MiB), "Unallocated blocks read as zeros.");
            CollectionAssert.AreEqual(Enumerable.Repeat((byte)0x5A, 512).ToArray(), await ReadAsync(disk, 64 * MiB - 512, 512));

            // Write into an existing block and a new block of the existing file
            await disk.WriteBytesAsync(MiB + 4096, new byte[] { 1, 2, 3, 4 }, CancellationToken.None);
            await disk.WriteBytesAsync(40 * MiB, data, CancellationToken.None);
            disk.Commit();
        }

        using (var disk = await OpenImageAsync(path))
        {
            Assert.AreEqual(format, disk.Format);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, await ReadAsync(disk, MiB + 4096, 4));
            CollectionAssert.AreEqual(new byte[4], await ReadAsync(disk, MiB + 4100, 4), "The rest of the existing block is unchanged.");
            CollectionAssert.AreEqual(data, await ReadAsync(disk, MiB + MiB / 2, data.Length), "The rest of the existing block is unchanged.");
            CollectionAssert.AreEqual(data, await ReadAsync(disk, 40 * MiB, data.Length));
            CollectionAssert.AreEqual(Enumerable.Repeat((byte)0x5A, 512).ToArray(), await ReadAsync(disk, 64 * MiB - 512, 512));
        }

        await AssertQemuReadsSameAsync(path);
    }

    [Test]
    public async Task Test_Vhdx_4KSectors()
    {
        var path = Path.Combine(_folder, "disk.vhdx");
        using (var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Vhdx))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            disk.ApplyGeometry(64 * MiB, 4096);
            await disk.WriteBytesAsync(8192, Enumerable.Repeat((byte)7, 4096).ToArray(), CancellationToken.None);
            disk.Commit();
        }

        using var reopened = await OpenImageAsync(path);
        Assert.AreEqual(4096, reopened.SectorSize);
        Assert.AreEqual(64 * MiB, reopened.Size);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)7, 4096).ToArray(), await ReadAsync(reopened, 8192, 4096));
    }

    [Test]
    public async Task Test_Vhd_RejectsUnsupportedGeometry()
    {
        var path = Path.Combine(_folder, "disk.vhd");
        using var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Vhd);
        Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));

        var ex = Assert.Throws<UserInformationException>(() => disk.ApplyGeometry(64 * MiB, 4096));
        Assert.AreEqual("DiskImageFileSectorSizeNotSupported", ex!.HelpID);

        ex = Assert.Throws<UserInformationException>(() => disk.ApplyGeometry(4096L * 1024 * MiB, 512));
        Assert.AreEqual("DiskImageFileTooLargeForFormat", ex!.HelpID);
    }

    [TestCase("disk.vhd", "Vhd")]
    [TestCase("disk.vhdx", "Vhdx")]
    public async Task Test_ImageFileRestore_FullDisk_VirtualDisk(string fileName, string formatName)
    {
        var format = Enum.Parse<ImageFormat>(formatName);
        var imagePath = Path.Combine(_folder, fileName);
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());

        Assert.IsFalse(File.Exists(RestoreProvider.GetPartialImagePath(imagePath)), "The partial file should be renamed.");
        // The test data leaves no 2 MiB block all zero inside the partitions, but
        // the unpartitioned space at the end of the disk is not stored
        Assert.Less(new FileInfo(imagePath).Length, 60 * MiB, "The unused end of the disk should not be stored.");

        using (var disk = await OpenImageAsync(imagePath))
            Assert.AreEqual(format, disk.Format);
        await VerifyFullDiskAsync(imagePath);

        await AssertQemuReadsSameAsync(imagePath);
    }

    [Test]
    public async Task Test_ImageFileRestore_FormatOption_OverridesExtension()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        var options = Options(overwrite: false);
        options["diskimage-restore-image-format"] = "vhdx";
        await RestoreFullDiskAsync(imagePath, options, CreateGptGeometry());

        using (var disk = await OpenImageAsync(imagePath))
            Assert.AreEqual(ImageFormat.Vhdx, disk.Format);
        await VerifyFullDiskAsync(imagePath);

        options["diskimage-restore-image-format"] = "1";
        var numeric = Assert.Throws<UserInformationException>(() => new ImageFileRestoreProvider($"diskimage-file://{imagePath}", options));
        Assert.AreEqual("DiskImageFileUnknownFormat", numeric!.HelpID);

        options["diskimage-restore-image-format"] = "raw,vhd";
        var list = Assert.Throws<UserInformationException>(() => new ImageFileRestoreProvider($"diskimage-file://{imagePath}", options));
        Assert.AreEqual("DiskImageFileUnknownFormat", list!.HelpID);

        options["diskimage-restore-image-format"] = "qcow2";
        var ex = Assert.Throws<UserInformationException>(() => new ImageFileRestoreProvider($"diskimage-file://{imagePath}", options));
        Assert.AreEqual("DiskImageFileUnknownFormat", ex!.HelpID);
    }

    [TestCase("partition.vhd")]
    [TestCase("partition.vhdx")]
    public async Task Test_ImageFileRestore_PartitionBackups_CreateVirtualDisk(string fileName)
    {
        var imagePath = Path.Combine(_folder, fileName);
        var sources = new[]
        {
            new PartitionGeometry { Number = 3, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 5 * MiB, Size = 8 * MiB, Name = "data", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT },
            new PartitionGeometry { Number = 4, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 40 * MiB, Size = 16 * MiB, Name = "more", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT }
        };

        // The second partition grows the disk after blocks of the first one are
        // stored, so the block table has to move
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            foreach (var source in sources)
            {
                var info = new PartitionInfoMetadata
                {
                    Partition = source,
                    Filesystem = new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = source.Number, PartitionStartOffset = source.StartOffset, BlockSize = BlockSize }
                };
                await WriteAsync(provider, Path.Combine(target, $"part_GPT_{source.Number}", PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes(info.ToJson()), readWrite: false);
            }

            foreach (var source in sources)
                for (var block = 0; block < source.Size / BlockSize; block++)
                    await WriteAsync(provider, BlockPath(target, source.Number, block), GetBlock(source.Number, block), readWrite: false);

            await provider.Finalize(null, true, CancellationToken.None);
        }

        using var disk = await OpenImageAsync(imagePath);
        var partitions = await ReadPartitionsAsync(disk);
        Assert.AreEqual(2, partitions.Count, "The image should hold both restored partitions.");
        foreach (var source in sources)
        {
            var partition = partitions.Single(p => p.StartOffset == source.StartOffset);
            Assert.AreEqual(source.Size, partition.Size);
            for (var block = 0; block < source.Size / BlockSize; block++)
                CollectionAssert.AreEqual(GetBlock(source.Number, block), await ReadAsync(disk, partition.StartOffset + (long)block * BlockSize, BlockSize), $"Partition {source.Number} block {block}");
        }

        await AssertQemuReadsSameAsync(imagePath);
    }

    [TestCase("disk.vhd")]
    [TestCase("disk.vhdx")]
    public async Task Test_ImageFileRestore_IntoPartitionOfExistingVirtualDisk(string fileName)
    {
        var imagePath = Path.Combine(_folder, fileName);
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());

        var info = new PartitionInfoMetadata
        {
            Partition = new PartitionGeometry { Number = 7, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 90 * MiB, Size = 4 * MiB, TableType = PartitionTableType.GPT },
            Filesystem = new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = 7, PartitionStartOffset = 90 * MiB, BlockSize = BlockSize }
        };

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            await WriteAsync(provider, Path.Combine(target, "part_GPT_7", PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes(info.ToJson()), readWrite: false);
            for (var block = 0; block < 4; block++)
                await WriteAsync(provider, BlockPath(target, 7, block), GetBlock(7, block), readWrite: false);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        var geometry = CreateGptGeometry();
        using (var disk = await OpenImageAsync(imagePath))
        {
            var second = geometry.Partitions![1];
            for (var block = 0; block < second.Size / BlockSize; block++)
            {
                var expected = block < 4 ? GetBlock(7, block) : GetBlock(2, block);
                CollectionAssert.AreEqual(expected, await ReadAsync(disk, second.StartOffset + (long)block * BlockSize, BlockSize), $"Partition 2 block {block}");
            }
            CollectionAssert.AreEqual(GetBlock(1, 0), await ReadAsync(disk, geometry.Partitions[0].StartOffset, BlockSize));
        }

        await AssertQemuReadsSameAsync(imagePath);

        // Restoring into an existing image keeps its format
        var options = Options(overwrite: false);
        options["diskimage-restore-image-format"] = "raw";
        using var mismatched = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", options);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => mismatched.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileFormatMismatch", ex!.HelpID);
    }

    [TestCase("disk.img")]
    [TestCase("disk.vhdx")]
    public async Task Test_ImageFileRestore_FolderNamesLikePartitionsAndFilesystems(string fileName)
    {
        var folder = Path.Combine(_folder, "part_backups", "fs_images");
        Directory.CreateDirectory(folder);
        var imagePath = Path.Combine(folder, fileName);

        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());
        await VerifyFullDiskAsync(imagePath);
    }

    [Test]
    public async Task Test_ImageFileRestore_TruncatedSectorCount_WritesBackupGptAtEnd()
    {
        // Backups made before the sector count was 64-bit store a truncated
        // count for large disks; the backup GPT must still go to the last sector
        var geometry = CreateGptGeometry();
        geometry.Disk!.Sectors = -1;

        var imagePath = Path.Combine(_folder, "disk.vhdx");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), geometry);
        await VerifyFullDiskAsync(imagePath);
    }

    /// <summary>
    /// Creates a variant of <see cref="CreateGptGeometry"/> with 4096-byte sectors.
    /// </summary>
    private static GeometryMetadata Create4KGptGeometry()
    {
        var geometry = CreateGptGeometry();
        geometry.Disk!.SectorSize = 4096;
        geometry.Disk.Sectors = geometry.Disk.Size / 4096;
        geometry.PartitionTable!.SectorSize = 4096;
        return geometry;
    }

    [Test]
    public async Task Test_ImageFileRestore_GeometryNotSupportedByFormat_FailsTheGeometryFile()
    {
        // A VHD cannot hold a disk with 4096-byte sectors; the error must reach
        // the restore when the geometry file is written, not crash the process
        var imagePath = Path.Combine(_folder, "disk.vhd");
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false));
        await provider.Initialize(CancellationToken.None);

        var ex = Assert.ThrowsAsync<UserInformationException>(() => WriteAsync(provider, Path.Combine(provider.TargetDestination, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(Create4KGptGeometry().ToJson()), readWrite: true));
        Assert.AreEqual("DiskImageFileSectorSizeNotSupported", ex!.HelpID);

        provider.Dispose();
        Assert.IsEmpty(Directory.GetFileSystemEntries(_folder), "The empty partial file should be removed.");
    }

    [TestCase("disk.img")]
    [TestCase("disk.vhdx")]
    public async Task Test_ImageFileRestore_Incomplete_KeepsExistingImage(string fileName)
    {
        var imagePath = Path.Combine(_folder, fileName);
        await File.WriteAllTextAsync(imagePath, "existing");

        var geometry = CreateGptGeometry();
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: true)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            await WriteAsync(provider, Path.Combine(target, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(geometry.ToJson()), readWrite: true);
            await WriteAsync(provider, BlockPath(target, 1, 0), GetBlock(1, 0), readWrite: false);

            // The restore was stopped, or some files failed
            await provider.Finalize(null, false, CancellationToken.None);
        }

        Assert.AreEqual("existing", await File.ReadAllTextAsync(imagePath), "An incomplete restore must not replace the existing image.");

        // The partial file is a valid image with the data that was restored
        var partialPath = RestoreProvider.GetPartialImagePath(imagePath);
        using var disk = await OpenImageAsync(partialPath);
        Assert.AreEqual(64 * MiB, disk.Size);
        CollectionAssert.AreEqual(GetBlock(1, 0), await ReadAsync(disk, 1 * MiB, BlockSize));
    }

    [Test]
    public async Task Test_ImageFileRestore_IntoPartitionOfRawImageWith4KSectors()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), Create4KGptGeometry());

        using (var disk = await OpenImageAsync(imagePath))
            Assert.AreEqual(4096, disk.SectorSize, "The sector size should be detected from the GPT header.");

        var info = new PartitionInfoMetadata
        {
            Partition = new PartitionGeometry { Number = 7, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 90 * MiB, Size = 4 * MiB, TableType = PartitionTableType.GPT },
            Filesystem = new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = 7, PartitionStartOffset = 90 * MiB, BlockSize = BlockSize }
        };

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            await WriteAsync(provider, Path.Combine(target, "part_GPT_7", PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes(info.ToJson()), readWrite: false);
            await WriteAsync(provider, BlockPath(target, 7, 0), GetBlock(7, 0), readWrite: false);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        using var reopened = await OpenImageAsync(imagePath);
        CollectionAssert.AreEqual(GetBlock(7, 0), await ReadAsync(reopened, 21 * MiB, BlockSize));
    }

    [Test]
    public async Task Test_Vhd_ExistingBlock_SectorsWithoutBitmapBitReadAsZero()
    {
        var path = Path.Combine(_folder, "disk.vhd");
        using (var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Vhd))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            disk.ApplyGeometry(8 * MiB, 512);
            await disk.WriteBytesAsync(0, Enumerable.Repeat((byte)0xAB, 4096).ToArray(), CancellationToken.None);
            disk.Commit();
        }

        // Clear the bitmap bit of the second sector of block 0, as a writer that
        // left stale data in a sector it does not consider present would
        await using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var buffer = new byte[8];
            file.Position = 512 + 16;
            file.ReadExactly(buffer);
            var tableOffset = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(buffer);
            file.Position = tableOffset;
            file.ReadExactly(buffer, 0, 4);
            var bitmapOffset = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(buffer) * 512;
            file.Position = bitmapOffset;
            file.WriteByte(0b1011_1111);
        }

        using var reopened = await OpenImageAsync(path);
        var data = await ReadAsync(reopened, 0, 4096);
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xAB, 512).ToArray(), data[..512]);
        CollectionAssert.AreEqual(new byte[512], data[512..1024], "A sector without its bitmap bit reads as zeros.");
        CollectionAssert.AreEqual(Enumerable.Repeat((byte)0xAB, 3072).ToArray(), data[1024..]);
    }

    [Test]
    public async Task Test_ImageFileRestore_DryRun_LeavesFilesUntouched()
    {
        var imagePath = Path.Combine(_folder, "disk.vhdx");
        var partialPath = RestoreProvider.GetPartialImagePath(imagePath);
        await File.WriteAllTextAsync(partialPath, "earlier");

        var options = Options(overwrite: false);
        options["dry-run"] = "true";
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", options))
        {
            await provider.Initialize(CancellationToken.None);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        Assert.IsFalse(File.Exists(imagePath), "A dry run must not create the image.");
        Assert.AreEqual("earlier", await File.ReadAllTextAsync(partialPath), "A dry run must not touch an earlier partial file.");
    }

    [Test]
    public async Task Test_ImageFileRestore_DryRun_IntoPartition_LeavesImageUntouched()
    {
        var imagePath = Path.Combine(_folder, "disk.vhd");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());
        var before = await File.ReadAllBytesAsync(imagePath);

        var options = Options(overwrite: false);
        options["dry-run"] = "true";
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", options))
        {
            await provider.Initialize(CancellationToken.None);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(imagePath), "A dry run must not change the image.");
    }

    [Test]
    public void Test_ImageFileRestore_LegacyRestore_IsRejected()
    {
        var options = Options(overwrite: false);
        options["restore-legacy"] = "true";
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{Path.Combine(_folder, "disk.img")}", options);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileLegacyRestore", ex!.HelpID);
    }

    [Test]
    public async Task Test_ImageFileRestore_PartialInUseByAnotherRestore_IsNotRemoved()
    {
        var imagePath = Path.Combine(_folder, "disk.img");
        var partialPath = RestoreProvider.GetPartialImagePath(imagePath);
        await File.WriteAllTextAsync(partialPath, "other restore");

        // Another restore of the same image holds its partial file open
        await using (var other = new FileStream(partialPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false));
            Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
            provider.Dispose();
        }

        Assert.AreEqual("other restore", await File.ReadAllTextAsync(partialPath), "A partial file this restore did not open must not be removed.");
    }

    [TestCase("Vhd")]
    [TestCase("Vhdx")]
    public async Task Test_VirtualDisk_GrowthFailure_StillCommitsValidImage(string formatName)
    {
        // The file system cannot hold the file once it grows, e.g. FAT32 at 4 GiB
        var format = Enum.Parse<ImageFormat>(formatName);
        var path = Path.Combine(_folder, "disk." + formatName.ToLowerInvariant());
        var data = Enumerable.Repeat((byte)0x3C, (int)MiB).ToArray();

        using (var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess))
        {
            var layout = ImageLayout.Create(format, sparse: true);
            layout.Resize(handle, 64 * MiB, 512);
            layout.MaxFileLength = 8 * MiB;

            await layout.WriteAsync(handle, 0, data, CancellationToken.None);
            var ex = Assert.ThrowsAsync<UserInformationException>(async () =>
            {
                for (var i = 1; i < 64; i++)
                    await layout.WriteAsync(handle, i * MiB, data, CancellationToken.None);
            });
            Assert.AreEqual("DiskImageFileTooLargeForFilesystem", ex!.HelpID);

            layout.MaxFileLength = long.MaxValue;
            layout.Commit(handle);
        }

        using var reopened = await OpenImageAsync(path);
        Assert.AreEqual(format, reopened.Format);
        Assert.AreEqual(64 * MiB, reopened.Size);
        CollectionAssert.AreEqual(data, await ReadAsync(reopened, 0, (int)MiB), "The data written before the failure is kept.");
        await AssertQemuReadsSameAsync(path);
    }

    [Test]
    public async Task Test_ImageFileRestore_FolderNamedLikeAPartition()
    {
        var folder = Path.Combine(_folder, "part_GPT_1");
        Directory.CreateDirectory(folder);
        var imagePath = Path.Combine(folder, "new.vhdx");

        Assert.AreEqual((imagePath, ""), SourceProvider.SplitImageAndSubpath(imagePath));
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());
        await VerifyFullDiskAsync(imagePath);
    }

    [Test]
    public void Test_ImageFileRestore_RestoreAllFiles_IsRejected()
    {
        var options = Options(overwrite: false);
        options["restore-all-files"] = "True";
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{Path.Combine(_folder, "disk.img")}", options);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileRestoreAllFiles", ex!.HelpID);
    }

    [Test]
    public async Task Test_ImageFileRestore_IncompleteWithoutDisk_ReportsNoExtraError()
    {
        // The disk description itself failed to restore: the restore already
        // reports that, so Finalize must not fail with a second error
        var imagePath = Path.Combine(_folder, "disk.img");
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            await WriteAsync(provider, Path.Combine(provider.TargetDestination, "part_GPT_1", "partitioninfo.json"), Encoding.UTF8.GetBytes("{}"), readWrite: false);
            await provider.Finalize(null, false, CancellationToken.None);
        }

        Assert.IsEmpty(Directory.GetFileSystemEntries(_folder), "No image and no empty partial file should be left.");
    }

    [TestCase("no")]
    [TestCase("")]
    [TestCase("False")]
    public async Task Test_ImageFileRestore_RestoreAllFilesOff_IsAccepted(string value)
    {
        // Parsed as the restore does: anything but True or Unique means a single version
        var options = Options(overwrite: false);
        options["restore-all-files"] = value;
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{Path.Combine(_folder, "disk.img")}", options);
        await provider.Initialize(CancellationToken.None);
    }

    [Test]
    public void Test_ImageFileRestore_DevicePath_IsRejected()
    {
        var path = OperatingSystem.IsWindows() ? @"\\.\PhysicalDrive9" : "/dev/null";
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{path}", Options(overwrite: true));
        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileIsDevice", ex!.HelpID);
    }

    [Test]
    public async Task Test_ImageFileRestore_SymlinkImagePath_IsRejected()
    {
        var target = Path.Combine(_folder, "real.img");
        await File.WriteAllTextAsync(target, "existing");
        var link = Path.Combine(_folder, "link.img");
        try
        {
            File.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }

        using var provider = new ImageFileRestoreProvider($"diskimage-file://{link}", Options(overwrite: true));
        var error = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileIsLink", error!.HelpID);
        Assert.AreEqual("existing", await File.ReadAllTextAsync(target));
    }

    [TestCase("Vhd")]
    [TestCase("Vhdx")]
    public async Task Test_VirtualDisk_GrowingPastTheTable_FailsEarlyAndStaysValid(string formatName)
    {
        // A disk that grows after its first block is placed needs its block table
        // moved to the end. When the file cannot grow, that fails when the disk
        // grows, and the smaller disk can still be committed as a valid image
        var format = Enum.Parse<ImageFormat>(formatName);
        var path = Path.Combine(_folder, "disk." + formatName.ToLowerInvariant());
        var data = Enumerable.Repeat((byte)0x5D, (int)MiB).ToArray();
        const long LargeSize = 1024L * 1024 * MiB;

        using (var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess))
        {
            var layout = ImageLayout.Create(format, sparse: true);
            layout.Resize(handle, 8 * MiB, 512);
            await layout.WriteAsync(handle, 0, data, CancellationToken.None);

            // Room for the headers, but not for a block table that covers 1 TiB
            layout.MaxFileLength = RandomAccess.GetLength(handle) + MiB;
            var ex = Assert.Throws<UserInformationException>(() => layout.Resize(handle, LargeSize, 512));
            Assert.AreEqual("DiskImageFileTooLargeForFilesystem", ex!.HelpID);
            Assert.AreEqual(8 * MiB, layout.Size, "A failed resize leaves the disk as it was.");
            layout.Commit(handle);
        }

        using (var reopened = await OpenImageAsync(path))
        {
            Assert.AreEqual(8 * MiB, reopened.Size);
            CollectionAssert.AreEqual(data, await ReadAsync(reopened, 0, (int)MiB));
        }
        await AssertQemuReadsSameAsync(path);

        // Without the limit, the table moves and the large disk works
        File.Delete(path);
        using (var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess))
        {
            var layout = ImageLayout.Create(format, sparse: true);
            layout.Resize(handle, 8 * MiB, 512);
            await layout.WriteAsync(handle, 0, data, CancellationToken.None);
            layout.Resize(handle, LargeSize, 512);
            await layout.WriteAsync(handle, 1000L * 1024 * MiB, data, CancellationToken.None);
            layout.Commit(handle);
        }

        using (var reopened = await OpenImageAsync(path))
        {
            Assert.AreEqual(LargeSize, reopened.Size);
            CollectionAssert.AreEqual(data, await ReadAsync(reopened, 0, (int)MiB));
            CollectionAssert.AreEqual(data, await ReadAsync(reopened, 1000L * 1024 * MiB, (int)MiB));
        }

        var check = await RunQemuImgAsync(format == ImageFormat.Vhdx ? "check" : "info", "-f", format == ImageFormat.Vhd ? "vpc" : "vhdx", path);
        if (check != null)
            Assert.AreEqual(0, check.Value.ExitCode, check.Value.Output);
    }

    [Test]
    public async Task Test_ImageFileRestore_DryRun_IntoReadOnlyImage()
    {
        var imagePath = Path.Combine(_folder, "disk.vhdx");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());
        var attributes = File.GetAttributes(imagePath);
        File.SetAttributes(imagePath, attributes | FileAttributes.ReadOnly);
        try
        {
            var options = Options(overwrite: false);
            options["dry-run"] = "true";
            using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", options);
            await provider.Initialize(CancellationToken.None);
            await provider.Finalize(null, true, CancellationToken.None);
        }
        finally
        {
            File.SetAttributes(imagePath, attributes);
        }
    }

    [Test]
    public async Task Test_ImageFileRestore_SymlinkAtPartialPath_IsRejected()
    {
        var target = Path.Combine(_folder, "important.dat");
        await File.WriteAllTextAsync(target, "important");
        var imagePath = Path.Combine(_folder, "disk.img");
        try
        {
            File.CreateSymbolicLink(RestoreProvider.GetPartialImagePath(imagePath), target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be created here: {ex.Message}");
        }

        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false));
        var error = Assert.ThrowsAsync<UserInformationException>(() => provider.Initialize(CancellationToken.None));
        Assert.AreEqual("DiskImageFileIsLink", error!.HelpID);
        Assert.AreEqual("important", await File.ReadAllTextAsync(target), "The link target must not be overwritten.");
    }

    [Test]
    public async Task Test_ImageFileRestore_HardLinkAtPartialPath_KeepsLinkedContent()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Uses ln to create the hard link.");

        var target = Path.Combine(_folder, "important.dat");
        await File.WriteAllTextAsync(target, "important");
        var imagePath = Path.Combine(_folder, "disk.img");
        using (var ln = Process.Start(new ProcessStartInfo("ln", [target, RestoreProvider.GetPartialImagePath(imagePath)]) { UseShellExecute = false })!)
            ln.WaitForExit();

        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());

        Assert.AreEqual("important", await File.ReadAllTextAsync(target), "A file hard-linked to the partial path must keep its content.");
        await VerifyFullDiskAsync(imagePath);
    }

    [Test]
    public void Test_ImageFileRestore_InvalidPath_IsReported()
    {
        var ex = Assert.Throws<UserInformationException>(() => new ImageFileRestoreProvider("diskimage-file:///images/a\0b.img", Options(overwrite: false)));
        Assert.AreEqual("DiskImageFileInvalidPath", ex!.HelpID);
    }

    [Test]
    public async Task Test_ImageFileRestore_SizedImageWithoutPendingItems_IsKept()
    {
        // The geometry sized the image, but nothing else reached the provider
        // (e.g. the partition content failed); the image must not be deleted
        var imagePath = Path.Combine(_folder, "disk.vhdx");
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            await WriteAsync(provider, Path.Combine(provider.TargetDestination, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(CreateGptGeometry().ToJson()), readWrite: true);

            // Simulate the reconstruction having produced no pending disk write
            var pending = typeof(RestoreProvider).GetField("_pendingWrites", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var inner = (RestoreProvider)typeof(ImageFileRestoreProvider).GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(provider)!;
            ((System.Collections.IDictionary)pending.GetValue(inner)!).Clear();

            await provider.Finalize(null, false, CancellationToken.None);
        }

        using var disk = await OpenImageAsync(RestoreProvider.GetPartialImagePath(imagePath));
        Assert.AreEqual(64 * MiB, disk.Size, "The incomplete image is kept as a valid partial image.");
    }

    [Test]
    public async Task Test_ImageFileRestore_MetadataWriteFailure_KeepsImageAsPartial()
    {
        // Restoring a partition larger than its target fails in WriteMetadata,
        // which the restore only logs; the image must not be presented as complete
        var imagePath = Path.Combine(_folder, "disk.img");
        var geometry = CreateGptGeometry();
        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            await WriteAsync(provider, Path.Combine(target, GeometryMetadata.FileName), Encoding.UTF8.GetBytes(geometry.ToJson()), readWrite: true);

            var metadata = new Dictionary<string, string?>
            {
                ["diskimage:Type"] = "partition",
                ["partition:Number"] = "1",
                ["partition:Size"] = (100 * MiB).ToString()
            };
            Assert.ThrowsAsync<UserInformationException>(() => provider.WriteMetadata(Path.Combine(target, "part_GPT_1"), metadata, false, false, true, CancellationToken.None));

            await provider.Finalize(null, true, CancellationToken.None);
        }

        Assert.IsFalse(File.Exists(imagePath), "An image with a failed metadata check must not be renamed into place.");
        Assert.IsTrue(File.Exists(RestoreProvider.GetPartialImagePath(imagePath)));
    }

    [Test]
    public async Task Test_ImageFileRestore_CompleteWithoutDisk_IsAnError()
    {
        // Only partition information was selected: no disk description and no
        // partition content, so no image can be written
        var imagePath = Path.Combine(_folder, "disk.img");
        using var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false));
        await provider.Initialize(CancellationToken.None);
        await WriteAsync(provider, Path.Combine(provider.TargetDestination, "part_GPT_1", PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes("{}"), readWrite: false);

        var ex = Assert.ThrowsAsync<UserInformationException>(() => provider.Finalize(null, true, CancellationToken.None));
        Assert.AreEqual("DiskImageFileNoDisk", ex!.HelpID);
        Assert.IsEmpty(Directory.GetFileSystemEntries(_folder), "No image and no empty partial file should be left.");
    }

    [Test]
    public async Task Test_ImageFileRestore_IntoPartition_NothingWritten_LeavesImageUntouched()
    {
        var imagePath = Path.Combine(_folder, "disk.vhd");
        await RestoreFullDiskAsync(imagePath, Options(overwrite: false), CreateGptGeometry());
        var before = await File.ReadAllBytesAsync(imagePath);
        var modified = File.GetLastWriteTimeUtc(imagePath);

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}/part_GPT_2", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            await provider.Finalize(null, true, CancellationToken.None);
        }

        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(imagePath));
        Assert.AreEqual(modified, File.GetLastWriteTimeUtc(imagePath), "An image that was not changed must not be rewritten.");
    }

    [TestCase("part_GPT_1", true, "GPT", 1)]
    [TestCase("PART_mbr_12", true, "MBR", 12)]
    [TestCase("part_GPT", false, "GPT", 0)]
    [TestCase("part_GPT_1_2", false, "GPT", 0)]
    [TestCase("part_1_1", false, "GPT", 0)]
    [TestCase("part_GPT,MBR_1", false, "GPT", 0)]
    [TestCase("part_INVALID_1", false, "GPT", 0)]
    [TestCase("part_GPT_ABC", false, "GPT", 0)]
    [TestCase("part_GPT_-1", false, "GPT", 0)]
    [TestCase("part_GPT_ 1", false, "GPT", 0)]
    [TestCase("disk_GPT_1", false, "GPT", 0)]
    public void Test_PartitionSegment_TryParse(string segment, bool valid, string tableTypeName, int number)
    {
        var tableType = Enum.Parse<PartitionTableType>(tableTypeName);
        Assert.AreEqual(valid, PartitionSegment.TryParse(segment, out var parsedType, out var parsedNumber), segment);
        if (valid)
        {
            Assert.AreEqual(tableType, parsedType);
            Assert.AreEqual(number, parsedNumber);
            Assert.IsTrue(PartitionSegment.TryParse(PartitionSegment.Format(parsedType, parsedNumber), out _, out _), "A formatted segment parses.");
        }
    }

    [Test]
    public async Task Test_ImageFileRestore_PartitionBackups_PlacedAsSized()
    {
        // The image is sized with the same placement rule the partitions are
        // allocated with: a free source offset is kept, even an unaligned one,
        // and a taken or reserved one moves to the first aligned gap
        var imagePath = Path.Combine(_folder, "partitions.img");
        const long Sector34 = 34 * 512;
        var sources = new[]
        {
            (Source: new PartitionGeometry { Number = 1, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = Sector34, Size = 4 * MiB, Name = "first", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT }, Expected: Sector34),
            (Source: new PartitionGeometry { Number = 2, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 2 * MiB, Size = 3 * MiB, Name = "overlaps", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT }, Expected: 5 * MiB),
            (Source: new PartitionGeometry { Number = 3, Type = PartitionType.Primary, FilesystemType = FileSystemType.Unknown, StartOffset = 0, Size = 1 * MiB, Name = "reserved", VolumeGuid = Guid.NewGuid(), TableType = PartitionTableType.GPT }, Expected: 8 * MiB),
        };

        using (var provider = new ImageFileRestoreProvider($"diskimage-file://{imagePath}", Options(overwrite: false)))
        {
            await provider.Initialize(CancellationToken.None);
            var target = provider.TargetDestination;
            foreach (var (source, _) in sources)
            {
                var info = new PartitionInfoMetadata
                {
                    Partition = source,
                    Filesystem = new FilesystemGeometry { Type = FileSystemType.Unknown, PartitionNumber = source.Number, PartitionStartOffset = source.StartOffset, BlockSize = BlockSize }
                };
                await WriteAsync(provider, Path.Combine(target, PartitionSegment.Format(PartitionTableType.GPT, source.Number), PartitionInfoMetadata.FileName), Encoding.UTF8.GetBytes(info.ToJson()), readWrite: false);
            }

            foreach (var (source, _) in sources)
                for (var block = 0; block < source.Size / BlockSize; block++)
                    await WriteAsync(provider, BlockPath(target, source.Number, block), GetBlock(source.Number, block), readWrite: false);

            await provider.Finalize(null, true, CancellationToken.None);
        }

        using var disk = await OpenImageAsync(imagePath);
        var partitions = await ReadPartitionsAsync(disk);
        Assert.AreEqual(sources.Length, partitions.Count);
        foreach (var (source, expected) in sources)
        {
            var partition = partitions.Single(p => p.Size == source.Size);
            Assert.AreEqual(expected, partition.StartOffset, $"Partition {source.Number} placement");
            for (var block = 0; block < source.Size / BlockSize; block++)
                CollectionAssert.AreEqual(GetBlock(source.Number, block), await ReadAsync(disk, partition.StartOffset + (long)block * BlockSize, BlockSize), $"Partition {source.Number} block {block}");
        }

        var (_, reservedEnd) = PartitionCreator.GetNewGptReservedSpace(512);
        Assert.AreEqual(Alignment.AlignUp(9 * MiB + reservedEnd, MiB), disk.Size, "The image is just large enough for the partitions and the GPT.");
    }

    /// <summary>
    /// Creates the layout of a new VHD or VHDX file with the given disk size.
    /// </summary>
    private static ImageLayout CreateLayout(SafeFileHandle handle, ImageFormat format, long size, int sectorSize)
    {
        var layout = ImageLayout.Create(format, sparse: true);
        layout.Resize(handle, size, sectorSize);
        return layout;
    }

    /// <summary>
    /// Gets a test pattern of the given length filled with a byte that is never zero.
    /// </summary>
    private static byte[] Pattern(int index, int length)
        => Enumerable.Repeat((byte)(1 + index % 250), length).ToArray();

    /// <summary>
    /// Checks with qemu-io that qemu reads the given byte in a range of a VHD or
    /// VHDX file. Skipped when neither qemu-img nor Docker is available.
    /// </summary>
    private async Task AssertQemuReadsPatternAsync(string path, ImageFormat format, long offset, long length, byte value)
    {
        var result = await RunQemuToolAsync("qemu-io", ["-r", "-f", format == ImageFormat.Vhd ? "vpc" : "vhdx", "-c", $"read -P 0x{value:x2} {offset} {length}", path]);
        if (result == null)
            return;
        Assert.AreEqual(0, result.Value.ExitCode, $"qemu-io should read 0x{value:x2} at {offset}: {result.Value.Output}");
    }

    [TestCase("Vhd", false)]
    [TestCase("Vhd", true)]
    [TestCase("Vhdx", false)]
    [TestCase("Vhdx", true)]
    public async Task Test_VirtualDisk_ConcurrentWritesToSameNewBlocks(string formatName, bool existingFile)
    {
        // Writers that hit the same unallocated block at once must share one
        // allocation; a block allocated twice loses the writes to the other copy
        var format = Enum.Parse<ImageFormat>(formatName);
        var path = Path.Combine(_folder, "disk." + formatName.ToLowerInvariant());
        const long Size = 64 * MiB;
        const long VirtualBlock = 2 * MiB;
        const int SliceSize = 64 * 1024;
        const int Blocks = 24;

        if (existingFile)
        {
            using var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess);
            var layout = CreateLayout(handle, format, Size, 512);
            await layout.WriteAsync(handle, Size - MiB, Pattern(255, (int)MiB), CancellationToken.None);
            layout.Commit(handle);
        }

        var slices = Enumerable.Range(0, Blocks * (int)(VirtualBlock / SliceSize))
            .Select(i => (Index: i, Offset: (long)i * SliceSize))
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();

        using (var handle = File.OpenHandle(path, existingFile ? FileMode.Open : FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess))
        {
            var layout = existingFile ? ImageLayout.Open(handle, path, sparse: true) : CreateLayout(handle, format, Size, 512);
            Assert.AreEqual(format, layout.Format);

            await Parallel.ForEachAsync(slices, new ParallelOptions { MaxDegreeOfParallelism = 16 },
                async (slice, ct) => await layout.WriteAsync(handle, slice.Offset, Pattern(slice.Index, SliceSize), ct));
            layout.Commit(handle);
        }

        Assert.Less(new FileInfo(path).Length, (Blocks + 1) * VirtualBlock + 8 * MiB, "Each block should be stored once.");

        using (var disk = await OpenImageAsync(path))
        {
            foreach (var slice in slices)
                CollectionAssert.AreEqual(Pattern(slice.Index, SliceSize), await ReadAsync(disk, slice.Offset, SliceSize), $"Slice {slice.Index}");
            if (existingFile)
                CollectionAssert.AreEqual(Pattern(255, (int)MiB), await ReadAsync(disk, Size - MiB, (int)MiB), "The data of the existing file is kept.");
        }

        await AssertQemuReadsSameAsync(path);
    }

    [TestCase("Vhd")]
    [TestCase("Vhdx")]
    public async Task Test_VirtualDisk_ResizeWhileWriting(string formatName)
    {
        // A partition backup restored to a new image grows the disk while the
        // blocks of other partitions are written; the block table may move
        var format = Enum.Parse<ImageFormat>(formatName);
        var path = Path.Combine(_folder, "disk." + formatName.ToLowerInvariant());
        const long InitialSize = 32 * MiB;
        const int SliceSize = 64 * 1024;
        var finalSize = format == ImageFormat.Vhd ? 1024L * 1024 * MiB : 2048L * 1024 * MiB;

        var sizes = new List<long>();
        for (var size = InitialSize * 4; size < finalSize; size *= 4)
            sizes.Add(size);
        sizes.Add(finalSize);

        var slices = Enumerable.Range(0, (int)(InitialSize / SliceSize))
            .Select(i => (Index: i, Offset: (long)i * SliceSize))
            .OrderBy(_ => Random.Shared.Next())
            .ToArray();
        var lastOffset = finalSize - 2 * MiB;

        using (var handle = File.OpenHandle(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None, FileOptions.RandomAccess))
        {
            var layout = CreateLayout(handle, format, InitialSize, 512);
            // Places the block table, sized for the initial disk
            await layout.WriteAsync(handle, slices[0].Offset, Pattern(slices[0].Index, SliceSize), CancellationToken.None);

            var writers = Parallel.ForEachAsync(slices.Skip(1), new ParallelOptions { MaxDegreeOfParallelism = 8 },
                async (slice, ct) => await layout.WriteAsync(handle, slice.Offset, Pattern(slice.Index, SliceSize), ct));
            var resizer = Task.Run(async () =>
            {
                foreach (var size in sizes)
                {
                    layout.Resize(handle, size, 512);
                    await Task.Yield();
                }
            });
            await Task.WhenAll(writers, resizer);

            await layout.WriteAsync(handle, lastOffset, Pattern(251, (int)MiB), CancellationToken.None);
            layout.Commit(handle);
        }

        using (var disk = await OpenImageAsync(path))
        {
            Assert.AreEqual(finalSize, disk.Size);
            foreach (var slice in slices)
                CollectionAssert.AreEqual(Pattern(slice.Index, SliceSize), await ReadAsync(disk, slice.Offset, SliceSize), $"Slice {slice.Index}");
            CollectionAssert.AreEqual(Pattern(251, (int)MiB), await ReadAsync(disk, lastOffset, (int)MiB));
            CollectionAssert.AreEqual(new byte[MiB], await ReadAsync(disk, InitialSize, (int)MiB), "Blocks that were not written read as zeros.");
        }

        if (format == ImageFormat.Vhdx)
        {
            var check = await RunQemuImgAsync("check", "-f", "vhdx", path);
            if (check != null)
                Assert.AreEqual(0, check.Value.ExitCode, check.Value.Output);
        }

        foreach (var slice in slices.Take(32))
            await AssertQemuReadsPatternAsync(path, format, slice.Offset, SliceSize, Pattern(slice.Index, 1)[0]);
        await AssertQemuReadsPatternAsync(path, format, lastOffset, MiB, Pattern(251, 1)[0]);
        await AssertQemuReadsPatternAsync(path, format, InitialSize, MiB, 0);
    }

    [Test]
    public async Task Test_Vhdx_Existing4KSectors_SpanningSeveralBitmapChunks()
    {
        // With 4096-byte sectors and 2 MiB blocks, a sector bitmap chunk covers
        // 16384 blocks (32 GiB), and its BAT entry follows the chunk's blocks
        var path = Path.Combine(_folder, "disk.vhdx");
        const long Size = 80L * 1024 * MiB;
        const int Length = 64 * 1024;
        const long ChunkStart = 32L * 1024 * MiB;
        var ranges = new List<(long Offset, int Index)>
        {
            (0, 1),                       // chunk 0, first block
            (ChunkStart + 4096, 2),       // chunk 1, first block
        };

        using (var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Vhdx))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            disk.ApplyGeometry(Size, 4096);
            foreach (var (offset, index) in ranges)
                await disk.WriteBytesAsync(offset, Pattern(index, Length), CancellationToken.None);
            disk.Commit();
        }

        var added = new List<(long Offset, int Index)>
        {
            (ChunkStart + MiB, 3),        // chunk 1, the existing first block
            (ChunkStart - MiB, 4),        // chunk 0, last block (new)
            (ChunkStart + 2 * MiB, 5),    // chunk 1, second block (new)
            (40000L * 2 * MiB + 8192, 6), // chunk 2 (new)
            (Size - Length, 7),           // the last sectors of the disk (new)
        };

        using (var disk = new ImageFile(path, path, createNew: false, sparse: true, format: null))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            Assert.AreEqual(4096, disk.SectorSize);
            foreach (var (offset, index) in added)
                await disk.WriteBytesAsync(offset, Pattern(index, Length), CancellationToken.None);
            disk.Commit();
        }

        ranges.AddRange(added);
        using (var disk = await OpenImageAsync(path))
        {
            Assert.AreEqual(Size, disk.Size);
            foreach (var (offset, index) in ranges)
                CollectionAssert.AreEqual(Pattern(index, Length), await ReadAsync(disk, offset, Length), $"Range {index}");
            CollectionAssert.AreEqual(new byte[Length], await ReadAsync(disk, ChunkStart + 4096 + Length, Length), "The rest of the existing block is unchanged.");
        }

        // qemu only opens VHDX files with 512-byte logical sectors, so the block
        // table is checked here: each chunk of 16384 payload entries is followed
        // by a sector bitmap entry, which stays unused in a disk without parent
        const long ChunkRatio = 16384;
        var bat = ReadVhdxBat(path);
        // Payload entries plus one sector bitmap entry per full chunk; the rest of
        // the region is padding
        const long Entries = 40960 + 40960 / ChunkRatio;
        Assert.GreaterOrEqual(bat.Length, Entries);
        Assert.IsTrue(bat.Skip((int)Entries).All(e => e == 0), "The padding after the entries is unused.");
        for (var chunk = 1; chunk <= 40960 / ChunkRatio; chunk++)
            Assert.AreEqual(0UL, bat[chunk * (ChunkRatio + 1) - 1], $"The sector bitmap entry of chunk {chunk - 1} is unused.");

        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        foreach (var (offset, index) in ranges)
        {
            var block = offset / (2 * MiB);
            var entry = bat[block + block / ChunkRatio];
            Assert.AreEqual(6UL, entry & 7, $"Block {block} is fully present.");
            var buffer = new byte[Length];
            file.Position = (long)(entry >> 20) * MiB + offset % (2 * MiB);
            file.ReadExactly(buffer);
            CollectionAssert.AreEqual(Pattern(index, Length), buffer, $"The block table locates range {index}.");
        }

        var allocated = bat.Where((e, i) => (i + 1) % (ChunkRatio + 1) != 0 && (e & 7) != 0).Count();
        Assert.AreEqual(ranges.Select(r => r.Offset / (2 * MiB)).Distinct().Count(), allocated, "Only the written blocks are allocated.");
    }

    /// <summary>
    /// Reads the block allocation table of a VHDX file, found through its region table.
    /// </summary>
    private static ulong[] ReadVhdxBat(string path)
    {
        var batGuid = new Guid("2DC27766-F623-4200-9D64-115E9BFD4A08");
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        var regions = new byte[64 * 1024];
        file.Position = 192 * 1024;
        file.ReadExactly(regions);
        Assert.AreEqual("regi", Encoding.ASCII.GetString(regions, 0, 4));

        var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(regions.AsSpan(8));
        for (var i = 0; i < count; i++)
        {
            var entry = regions.AsSpan(16 + i * 32, 32);
            if (new Guid(entry[..16]) != batGuid)
                continue;

            var table = new byte[System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(entry[24..])];
            file.Position = (long)System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(entry[16..]);
            file.ReadExactly(table);
            return Enumerable.Range(0, table.Length / 8)
                .Select(j => System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(table.AsSpan(j * 8)))
                .ToArray();
        }

        Assert.Fail("The VHDX file has no block table region.");
        return [];
    }

    [TestCase(512 * 1024)]
    [TestCase(8 * 1024 * 1024)]
    public async Task Test_Vhd_ExistingWithOtherBlockSize(int blockSize)
    {
        // VHD files from other tools may use block sizes other than 2 MiB. Make
        // one by changing the block size of an empty file written here
        var path = Path.Combine(_folder, "disk.vhd");
        const long Size = 16 * MiB;
        using (var disk = new ImageFile(path, path, createNew: true, sparse: true, format: ImageFormat.Vhd))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            disk.ApplyGeometry(Size, 512);
            disk.Commit();
        }

        await using (var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite))
        {
            var header = new byte[1024];
            file.Position = 512;
            file.ReadExactly(header);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(28), (uint)(Size / blockSize));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(32), (uint)blockSize);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(36), VhdImageLayout.Checksum(header, 36));
            file.Position = 512;
            file.Write(header);
        }

        var first = (Offset: (long)blockSize - 32 * 1024, Data: Pattern(1, 64 * 1024));   // across a block boundary
        var second = (Offset: Size - 4096, Data: Pattern(2, 4096));                          // the last block
        var third = (Offset: (long)blockSize - 40 * 1024, Data: Pattern(3, 4096));           // into the first, now existing, block

        using (var disk = new ImageFile(path, path, createNew: false, sparse: true, format: null))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            await disk.WriteBytesAsync(first.Offset, first.Data, CancellationToken.None);
            await disk.WriteBytesAsync(second.Offset, second.Data, CancellationToken.None);
            disk.Commit();
        }

        using (var disk = new ImageFile(path, path, createNew: false, sparse: true, format: null))
        {
            Assert.IsNull(await disk.InitializeAsync(true, CancellationToken.None));
            await disk.WriteBytesAsync(third.Offset, third.Data, CancellationToken.None);
            disk.Commit();
        }

        using (var disk = await OpenImageAsync(path))
        {
            Assert.AreEqual(Size, disk.Size);
            CollectionAssert.AreEqual(first.Data, await ReadAsync(disk, first.Offset, first.Data.Length));
            CollectionAssert.AreEqual(second.Data, await ReadAsync(disk, second.Offset, second.Data.Length));
            CollectionAssert.AreEqual(third.Data, await ReadAsync(disk, third.Offset, third.Data.Length));
            CollectionAssert.AreEqual(new byte[4096], await ReadAsync(disk, third.Offset - 4096, 4096), "The rest of the block reads as zeros.");
            CollectionAssert.AreEqual(new byte[4096], await ReadAsync(disk, Size - 8192, 4096), "The rest of the last block reads as zeros.");
        }

        await AssertQemuReadsSameAsync(path);
    }
}
