using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage;
using Duplicati.Proprietary.DiskImage.SourceItems;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

#nullable enable

/// <summary>
/// Tests for the partition subpath filtering in <see cref="DiskSourceEntry"/>,
/// used when a backup source points to a single partition on a disk
/// instead of the whole disk.
/// </summary>
public partial class DiskImageUnitTests : BasicSetupHelper
{
    [Test]
    public async Task Test_DiskSourceEntry_NoSubpath_EmitsAllPartitions_Async()
    {
        var provider = new SourceProvider();
        var entry = new DiskSourceEntry(provider, s_gptRawDisk!, string.Empty);

        var entries = await EnumerateDirectChildrenAsync(entry);

        var partitionEntries = GetPartitionEntries(entries);
        Assert.AreEqual(2, partitionEntries.Count, "Whole-disk enumeration should emit both partitions.");
        Assert.IsTrue(entries.Any(x => x.Path.EndsWith("geometry.json")), "geometry.json should be emitted.");
    }

    [Test]
    public async Task Test_DiskSourceEntry_PartitionSubpath_EmitsOnlyMatchingPartition_Async()
    {
        var provider = new SourceProvider();
        var entry = new DiskSourceEntry(provider, s_gptRawDisk!, "part_GPT_1");

        var entries = await EnumerateDirectChildrenAsync(entry);

        var partitionEntries = GetPartitionEntries(entries);
        Assert.AreEqual(1, partitionEntries.Count, "Only the selected partition should be emitted.");
        Assert.IsTrue(partitionEntries[0].Path.EndsWith($"part_GPT_1{Path.DirectorySeparatorChar}"),
            $"The emitted partition should be part_GPT_1, but was: {partitionEntries[0].Path}");
        Assert.IsTrue(entries.Any(x => x.Path.EndsWith("geometry.json")), "geometry.json should still be emitted.");
    }

    [Test]
    public async Task Test_DiskSourceEntry_UnknownPartitionSubpath_EmitsNoPartitions_Async()
    {
        var provider = new SourceProvider();
        var entry = new DiskSourceEntry(provider, s_gptRawDisk!, "part_GPT_99");

        var entries = await EnumerateDirectChildrenAsync(entry);

        var partitionEntries = GetPartitionEntries(entries);
        Assert.AreEqual(0, partitionEntries.Count, "A subpath naming a non-existing partition should emit no partitions.");
        Assert.IsTrue(entries.Any(x => x.Path.EndsWith("geometry.json")), "geometry.json should still be emitted.");
    }

    private static List<ISourceProviderEntry> GetPartitionEntries(List<ISourceProviderEntry> entries)
        => entries.Where(x => x.IsFolder && x.Path.Contains($"{Path.DirectorySeparatorChar}part_")).ToList();

    private static async Task<List<ISourceProviderEntry>> EnumerateDirectChildrenAsync(ISourceProviderEntry entry)
    {
        var entries = new List<ISourceProviderEntry>();
        await foreach (var child in entry.Enumerate(CancellationToken.None))
            entries.Add(child);
        return entries;
    }
}
