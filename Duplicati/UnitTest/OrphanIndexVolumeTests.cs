// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// An index file left at the destination for a block volume that is gone (#4507, #4586).
/// Recreate registers the missing block volume as temporary and drops it again when no
/// file uses its blocks, but the index file stayed: repair keeps an unused index file
/// above 2 KiB, and every later recreate warned about the volume again.
/// </summary>
[TestFixture]
public class OrphanIndexVolumeTests : BasicSetupHelper
{
    private string[] TargetFiles()
        => Directory.GetFiles(TARGETFOLDER).Select(Path.GetFileName).Select(x => x!).ToArray();

    private void WriteRandomFile(string name, int seed, int size)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
    }

    /// <summary>
    /// Makes two backups and removes the block volume and file list of the second,
    /// leaving its index file. Returns the name of that index file.
    /// </summary>
    private async Task<string> LeaveOrphanIndexFileAsync(Dictionary<string, string> options)
    {
        WriteRandomFile("a", 1, 64 * 1024);
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        var first = TargetFiles();

        // Large enough that the index file is above the size repair deletes as empty
        WriteRandomFile("b", 2, 1024 * 1024);
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        var added = TargetFiles().Except(first).ToArray();

        foreach (var name in added.Where(x => x.Contains(".dblock.") || x.Contains(".dlist.")))
            File.Delete(Path.Combine(TARGETFOLDER, name));

        File.Delete(Path.Combine(DATAFOLDER, "b"));
        File.Delete(options["dbpath"]);
        var orphan = added.Single(x => x.Contains(".dindex."));
        Assert.Greater(new FileInfo(Path.Combine(TARGETFOLDER, orphan)).Length, 2048);
        return orphan;
    }

    private async Task<string[]> RecreateWarningsAsync(Dictionary<string, string> options)
    {
        File.Delete(options["dbpath"]);
        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var results = await c.RepairAsync();
        Assert.AreEqual(0, results.Errors.Count(), string.Join(Environment.NewLine, results.Errors));
        return results.Warnings.ToArray();
    }

    [Test]
    [Category("RepairHandler")]
    [TestCase("backup")]
    [TestCase("repair")]
    public async Task AnIndexFileForAMissingBlockVolumeIsRemovedAfterRecreate(string operation)
    {
        var options = new Dictionary<string, string>(TestOptions);
        var orphan = await LeaveOrphanIndexFileAsync(options);

        var warnings = await RecreateWarningsAsync(options);
        Assert.IsTrue(warnings.Any(x => x.Contains("MissingFileDetected") && x.Contains(orphan)),
            "Recreate did not report the index file: " + string.Join(Environment.NewLine, warnings));

        // The next operation that cleans up the destination deletes it
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            if (operation == "backup")
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            else
                TestUtils.AssertResults(await c.RepairAsync());
        }

        CollectionAssert.DoesNotContain(TargetFiles(), orphan);

        warnings = await RecreateWarningsAsync(options);
        CollectionAssert.IsEmpty(warnings);
    }

    [Test]
    [Category("RepairHandler")]
    public async Task ADryRunKeepsTheIndexFile()
    {
        var options = new Dictionary<string, string>(TestOptions);
        var orphan = await LeaveOrphanIndexFileAsync(options);
        await RecreateWarningsAsync(options);

        var dryrun = new Dictionary<string, string>(options) { ["dry-run"] = "true" };
        using (var c = new Controller("file://" + TARGETFOLDER, dryrun, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        CollectionAssert.Contains(TargetFiles(), orphan);
    }

    /// <summary>
    /// Index files of block volumes that are still there keep their state
    /// </summary>
    [Test]
    [Category("RepairHandler")]
    public async Task OtherIndexFilesAreKept()
    {
        var options = new Dictionary<string, string>(TestOptions);
        var orphan = await LeaveOrphanIndexFileAsync(options);
        var kept = TargetFiles().Where(x => x.Contains(".dindex.") && x != orphan).ToArray();
        Assert.IsNotEmpty(kept);

        await RecreateWarningsAsync(options);
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        CollectionAssert.IsSubsetOf(kept, TargetFiles());
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
    }
}
