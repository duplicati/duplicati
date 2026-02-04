using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.Partition;

// TODO this project is just a test project to test the disk image library. Remove later, or elevate to a proper tool.

// Check if we're on Windows platform
if (Environment.OSVersion.Platform != PlatformID.Win32NT)
{
    Console.WriteLine("This tool currently only supports Windows platform.");
    return;
}

using var disk = new Duplicati.Proprietary.DiskImage.Disk.Windows(@"\\.\PhysicalDrive0");
var initialized = await disk.InitializeAsync(CancellationToken.None);
if (!initialized)
{
    Console.WriteLine("Failed to initialize disk.");
    return;
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
    Console.WriteLine($"  Size: {partition.Size} bytes");
    Console.WriteLine($"  Offset: {partition.StartOffset} bytes");
}

// If this is a GPT disk, demonstrate getting the protective MBR
if (table.TableType == PartitionTableType.GPT)
{
    using var mbrStream = await table.GetProtectiveMbrAsync(CancellationToken.None);
    Console.WriteLine($"Protective MBR retrieved successfully ({mbrStream.Length} bytes).");
}

var firstPartition = await table.GetPartitionAsync(0, CancellationToken.None);

if (firstPartition == null)
{
    Console.WriteLine("No partitions found on the disk.");
    return;
}

using var fs = new UnknownFilesystem(firstPartition);

var filecount = await fs.ListFilesAsync(CancellationToken.None).CountAsync();

Console.WriteLine($"Listed {filecount} files in the unknown filesystem of the first partition.");

if (filecount == 0) return;

var firstFile = await fs.ListFilesAsync(CancellationToken.None).FirstAsync();

using var fileStream = await fs.OpenFileAsync(firstFile, CancellationToken.None);

// Copy to a buffer
var buffer = new byte[firstFile.Size];
await fileStream.ReadExactlyAsync(buffer, 0, buffer.Length, CancellationToken.None);

Console.WriteLine($"Read {buffer.Length} bytes from the first file in the unknown filesystem.");