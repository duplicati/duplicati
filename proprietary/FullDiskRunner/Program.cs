using System.Runtime.InteropServices;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.Partition;
using Vanara.InteropServices;

// TODO this project is just a test project to test the disk image library. Remove later, or elevate to a proper tool.

// Check if we're on Windows platform
if (Environment.OSVersion.Platform != PlatformID.Win32NT)
{
    Console.WriteLine("This tool currently only supports Windows platform.");
    return;
}

using var disk = new Duplicati.Proprietary.DiskImage.Disk.Windows(@"\\.\PhysicalDrive2");
var initialized = await disk.InitializeAsync(CancellationToken.None);
if (!initialized)
{
    Console.WriteLine("Failed to initialize disk.");
    return;
}

// Raw dump entire disk
if (true)
{
    using var rawoutstream = System.IO.File.OpenWrite("rawdisk_small_org.bin");
    for (int i = 0; i < disk.Sectors; i += 16)
    {
        var read = await disk.ReadSectorsAsync(i, 16, CancellationToken.None);
        await read.CopyToAsync(rawoutstream);
    }
    Console.WriteLine("Raw disk dump completed.");
}

var table = await PartitionTableFactory.CreateAsync(disk, CancellationToken.None);
if (table == null)
{
    Console.WriteLine("Failed to detect partition table type.");
    return;
}

Console.WriteLine($"Detected partition table type: {table.TableType}");

// Print partition information
await foreach (var partition in table.EnumeratePartitions(CancellationToken.None))
{
    Console.WriteLine($"Partition {partition.PartitionNumber}: {partition.Name}");
    Console.WriteLine($"  Type: {partition.Type}");
    Console.WriteLine($"  Size: {partition.Size} bytes ({(double)partition.Size / (1024 * 1024 * 1024)} GB)");
    Console.WriteLine($"  Offset: {partition.StartOffset} bytes");
}

// If this is a GPT disk, demonstrate getting the protective MBR
if (table.TableType == PartitionTableType.GPT)
{
    using var mbrStream = await table.GetProtectiveMbrAsync(CancellationToken.None);
    Console.WriteLine($"Protective MBR retrieved successfully ({mbrStream.Length} bytes).");
}

var firstPartition = await table.GetPartitionAsync(2, CancellationToken.None);

if (firstPartition == null)
{
    Console.WriteLine("No partitions found on the disk.");
    return;
}

using var fs = new UnknownFilesystem(firstPartition);

var filecount = await fs.ListFilesAsync(CancellationToken.None).CountAsync();

Console.WriteLine($"Listed {filecount} files in the unknown filesystem of partition {firstPartition.PartitionNumber}.");

if (filecount == 0) return;

using var outstream = System.IO.File.OpenWrite("test.bin");

if (false)
    await foreach (var file in fs.ListFilesAsync(CancellationToken.None))
    {
        using var fileStream = await fs.OpenReadStreamAsync(file, CancellationToken.None);

        // Copy to a buffer
        var buffer = new byte[file.Size];
        await fileStream.ReadExactlyAsync(buffer, 0, buffer.Length, CancellationToken.None);
        outstream.Write(buffer, 0, buffer.Length);
    }
