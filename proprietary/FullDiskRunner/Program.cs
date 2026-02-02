using Duplicati.Proprietary.DiskImage;

// TODO this project is just a test project to test the disk image library. Remove later, or elevate to a proper tool.

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