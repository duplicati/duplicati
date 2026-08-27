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
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A dlist file that is missing from the destination is replaced under a new name,
/// because the old name is still registered. The fileset behind it must keep its
/// original time regardless: that time is what orders the backup versions, so moving
/// it can silently renumber the versions a user restores from.
/// </summary>
public class RepairFilesetTimestampTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new { no_encryption = true });

    private string[] DlistFiles
        => Directory.GetFiles(TARGETFOLDER, "*.dlist.*", SearchOption.TopDirectoryOnly)
            .Select(x => Path.GetFileName(x))
            .OrderBy(x => x)
            .ToArray();

    /// <summary>
    /// The filesets as (version, time, full backup), ordered by version.
    /// </summary>
    private async Task<(long Version, DateTime Time, string IsFull)[]> GetFilesetsAsync()
    {
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        var result = await c.ListAsync();
        return result.Filesets
            .OrderBy(x => x.Version)
            .Select(x => ((long)x.Version, x.Time, x.IsFullBackup.ToString()))
            .ToArray();
    }

    private async Task RunBackupAsync(string filename)
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, filename), filename);
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    private async Task RunRepairAsync()
    {
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        TestUtils.AssertResults(await c.RepairAsync());
    }

    private static string Describe((long Version, DateTime Time, string IsFull)[] filesets)
        => string.Join(" | ", filesets.Select(x => $"v{x.Version} {x.Time:O} full={x.IsFull}"));

    [Test]
    [Category("Targeted")]
    public async Task RepairKeepsTheFilesetTimeWhenAllDlistFilesAreMissingAsync()
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(1100);
        await RunBackupAsync("b.txt");

        var before = await GetFilesetsAsync();
        var namesBefore = DlistFiles;
        Assert.That(namesBefore.Length, Is.EqualTo(2));

        foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dlist.*"))
            File.Delete(f);

        await RunRepairAsync();

        var after = await GetFilesetsAsync();

        Assert.That(after.Select(x => x.Time).ToArray(), Is.EqualTo(before.Select(x => x.Time).ToArray()),
            $"Repair moved the filesets in time.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(after)}");
        Assert.That(after.Select(x => x.IsFull).ToArray(), Is.EqualTo(before.Select(x => x.IsFull).ToArray()),
            "Each version must still describe the same backup");

        // The replacement files do get a new name, because the old one is still
        // registered. That is expected; only the fileset time has to stay put.
        Assert.That(DlistFiles.Length, Is.EqualTo(2));
        Assert.That(DlistFiles, Is.Not.EqualTo(namesBefore));
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairKeepsTheFilesetTimeAcrossADatabaseRecreateAsync()
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(1100);
        await RunBackupAsync("b.txt");

        var before = await GetFilesetsAsync();

        foreach (var f in Directory.GetFiles(TARGETFOLDER, "*.dlist.*"))
            File.Delete(f);

        await RunRepairAsync();

        var afterRepair = await GetFilesetsAsync();
        Assert.That(afterRepair.Select(x => x.Time).ToArray(), Is.EqualTo(before.Select(x => x.Time).ToArray()),
            $"Repair moved the filesets in time.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(afterRepair)}");

        // Rebuilding the local database from the destination has to give the same times
        // back. Keeping them only in the database would hold just until the next recreate,
        // because a recreate takes the time from the file name.
        File.Delete(DBFILE);
        await RunRepairAsync();

        var afterRecreate = await GetFilesetsAsync();
        Assert.That(afterRecreate.Select(x => x.Time).ToArray(), Is.EqualTo(before.Select(x => x.Time).ToArray()),
            $"Recreating the database lost the fileset times.{Environment.NewLine}before:   {Describe(before)}{Environment.NewLine}recreate: {Describe(afterRecreate)}");
        Assert.That(afterRecreate.Select(x => x.IsFull).ToArray(), Is.EqualTo(before.Select(x => x.IsFull).ToArray()),
            "Each version must still describe the same backup after a recreate");
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairKeepsTheFilesetTimeWhenOneDlistFileIsMissingAsync()
    {
        await RunBackupAsync("a.txt");
        Thread.Sleep(1100);
        await RunBackupAsync("b.txt");

        var before = await GetFilesetsAsync();

        // Delete the older of the two, which is the one that can be pushed past the newer.
        var oldest = Directory.GetFiles(TARGETFOLDER, "*.dlist.*").OrderBy(x => x).First();
        File.Delete(oldest);

        await RunRepairAsync();

        var after = await GetFilesetsAsync();

        Assert.That(after.Select(x => x.Time).ToArray(), Is.EqualTo(before.Select(x => x.Time).ToArray()),
            $"Repair moved the fileset in time.{Environment.NewLine}before: {Describe(before)}{Environment.NewLine}after:  {Describe(after)}");
        Assert.That(after.Select(x => x.IsFull).ToArray(), Is.EqualTo(before.Select(x => x.IsFull).ToArray()),
            "Each version must still describe the same backup");
    }
}
