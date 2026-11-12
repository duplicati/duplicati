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
/// The synthetic dlist of an interrupted backup holds the files of the backup before it,
/// plus the files the interrupted backup got done.
/// </summary>
public class SyntheticFilelistNewFilesTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new { no_encryption = true });

    private async Task<string> DescribeVersionsAsync()
    {
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null);
        var result = await c.ListAsync();
        var versions = new List<string>();
        foreach (var f in result.Filesets.OrderBy(x => x.Version))
            versions.Add($"v{f.Version} full={f.IsFullBackup} [{string.Join(",", await ListFilesAsync((int)f.Version))}]");
        return string.Join(" | ", versions);
    }

    private async Task<string[]> ListFilesAsync(int version)
    {
        using var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options.Expand(new { version = version.ToString() }), null);
        var result = await c.ListAsync(["*"], null);
        return (result.Files ?? [])
            .Select(x => Path.GetFileName(x.Path))
            .Where(x => x.Length > 0)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// A complete backup of a.txt, then b.txt is added and a backup is interrupted after its
    /// data is uploaded (the dlist upload fails), then the next backup uploads the synthetic dlist.
    /// </summary>
    /// <param name="keepNewFile">False to delete b.txt before the next backup, so only the synthetic version can hold it.</param>
    [Test]
    [Category("Targeted")]
    [TestCase(true)]
    [TestCase(false)]
    public async Task SyntheticFilelistHasTheFilesAddedByTheInterruptedBackupAsync(bool keepNewFile)
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        Thread.Sleep(1100);

        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "b");
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var target = new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename)
            => action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist.", StringComparison.Ordinal);
        try
        {
            using var c = new Library.Main.Controller(target, new Dictionary<string, string>(Options) { ["number-of-retries"] = "0" }, null);
            Assert.ThrowsAsync<DeterministicErrorBackend.DeterministicErrorBackendException>(async () => await c.BackupAsync([DATAFOLDER]));
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }
        Thread.Sleep(1100);

        if (!keepNewFile)
            File.Delete(Path.Combine(DATAFOLDER, "b.txt"));
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // v0 is the new backup, v1 the synthetic version, v2 the first backup
        var report = $"versions: {await DescribeVersionsAsync()}";
        Assert.That(await ListFilesAsync(1), Is.EqualTo(new[] { "a.txt", "b.txt" }), report);

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options.Expand(new { version = "1", restore_path = RESTOREFOLDER }), null))
            TestUtils.AssertResults(await c.RestoreAsync(["*"]));
        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "b.txt")), Is.EqualTo("b"), report);

        File.Delete(DBFILE);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());
        report += $"{Environment.NewLine}recreated: {await DescribeVersionsAsync()}";
        Assert.That(await ListFilesAsync(1), Is.EqualTo(new[] { "a.txt", "b.txt" }), report);
    }
}
