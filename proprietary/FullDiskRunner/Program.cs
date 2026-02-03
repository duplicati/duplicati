using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.Partition;

// TODO this project is just a test project to test the disk image library. Remove later, or elevate to a proper tool.

/*
using var disk = new Duplicati.Proprietary.DiskImage.Raw.Windows(@"\\.\PhysicalDrive0");
var initialized = await disk.InitializeAsync(CancellationToken.None);
if (!initialized)
{
    Console.WriteLine("Failed to initialize disk.");
    return;
}
using var partition = await disk.ReadBytesAsync(0, 4096, CancellationToken.None);
byte[] buffer = new byte[4096];
await partition.ReadExactlyAsync(buffer, CancellationToken.None);

await File.WriteAllBytesAsync("partition0.bin", buffer);
*/

var bytes = await File.ReadAllBytesAsync("partition1.bin");

// Use the factory for auto-detection of partition table type
var table = await PartitionTableFactory.CreateAsync(bytes, 512, CancellationToken.None);
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
