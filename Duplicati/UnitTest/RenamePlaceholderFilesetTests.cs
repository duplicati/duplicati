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
/// A dlist upload that fails is retried under a new name, and the old name stays registered
/// until the destination is verified again. That entry must not show up as a backup version.
/// </summary>
public class RenamePlaceholderFilesetTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new { no_encryption = true, number_of_retries = 2, retry_delay = "0s", no_auto_compact = true });

    private static string Describe(IEnumerable<Library.Interface.IListResultFileset> filesets)
        => string.Join(" | ", filesets.OrderBy(x => x.Version).Select(x => $"v{x.Version} {x.Time:O} full={x.IsFullBackup} files={x.FileCount}"));

    /// <summary>
    /// Two backups, a.txt and then also b.txt, more than two seconds apart: purge rewrites a
    /// version under the first free dlist name after its own timestamp, at least a second
    /// later, and refuses when that reaches the next version.
    /// </summary>
    private async Task SetUpTwoVersionsAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        Thread.Sleep(3100);
        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "b");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    /// <summary>
    /// Runs the operation with the first dlist upload failing, then checks the versions
    /// </summary>
    private async Task RunWithOneFailedDlistUploadAsync(Func<Library.Main.Controller, Task> operation)
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new DeterministicErrorBackend());
        var failed = false;
        DeterministicErrorBackend.ErrorGenerator = (action, remotename) =>
        {
            if (!failed && action == DeterministicErrorBackend.BackendAction.PutBefore && remotename.Contains(".dlist.", StringComparison.Ordinal))
                return failed = true;
            return false;
        };
        try
        {
            using var c = new Library.Main.Controller(new DeterministicErrorBackend().ProtocolKey + "://" + TARGETFOLDER, Options, null);
            await operation(c);
        }
        finally
        {
            DeterministicErrorBackend.ErrorGenerator = null;
        }
        Assert.That(failed, Is.True, "No dlist upload failed");

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
        {
            var filesets = (await c.ListAsync()).Filesets.ToArray();
            Assert.That(filesets.Length, Is.EqualTo(2), $"versions: {Describe(filesets)}");
            Assert.That(filesets.Select(x => x.Time), Has.None.LessThan(new DateTime(2000, 1, 1)), $"versions: {Describe(filesets)}");
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task RepairRetryLeavesNoExtraVersionAsync()
    {
        await SetUpTwoVersionsAsync();
        File.Delete(Directory.GetFiles(TARGETFOLDER, "*.dlist.*").OrderBy(x => x, StringComparer.Ordinal).Last());
        await RunWithOneFailedDlistUploadAsync(async c => TestUtils.AssertResults(await c.RepairAsync()));
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeRetryLeavesNoExtraVersionAsync()
    {
        // The purge deletes the rewritten version's old fileset while the entry the retry
        // left behind is still registered
        await SetUpTwoVersionsAsync();
        await RunWithOneFailedDlistUploadAsync(async c => TestUtils.AssertResults(await c.PurgeFilesAsync(new Library.Utility.FilterExpression(Path.Combine(DATAFOLDER, "a.txt")))));
    }
}
