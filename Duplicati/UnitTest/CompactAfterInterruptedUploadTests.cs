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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Volumes;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// An operation that is stopped while it uploads leaves a volume in the uploading state that never
/// reached the destination, and a flag saying so. A backup cleans that up; a compact must as well,
/// rather than refusing to run until a backup or a repair has.
/// </summary>
public class CompactAfterInterruptedUploadTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        blocksize = "10KB",
        dblock_size = "100KB",
        no_auto_compact = true,
        threshold = "1"
    });

    private void WriteFile(string name, int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
    }

    /// <summary>
    /// Makes backups with changing data and deletes all but the last, so there is space to compact
    /// </summary>
    private async Task MakeWastedSpaceAsync()
    {
        for (var i = 0; i < 3; i++)
        {
            WriteFile("data.bin", 300 * 1024, i);
            using var c = new Controller("file://" + TARGETFOLDER, Options, null);
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }

        using (var c = new Controller("file://" + TARGETFOLDER, Options.Expand(new { keep_versions = 1 }), null))
            TestUtils.AssertResults(await c.DeleteAsync());
    }

    /// <summary>
    /// Leaves the database as a stopped upload does: a block volume in the uploading state that is
    /// not at the destination, and, if <paramref name="flagged"/>, the flag that uploads were active
    /// </summary>
    private async Task<string> LeaveUnfinishedUploadAsync(bool flagged)
    {
        string name;
        using (var vol = new BlockVolumeWriter(new Options(Options!)))
            name = vol.RemoteFilename;

        await using var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "Test", true, null, CancellationToken.None);
        await db.RegisterRemoteVolumeAsync(name, RemoteVolumeType.Blocks, RemoteVolumeState.Uploading, CancellationToken.None);
        if (flagged)
            await db.TerminatedWithActiveUploadsAsync(CancellationToken.None, true);
        await db.Transaction.CommitAsync(CancellationToken.None);
        return name;
    }

    private async Task<long> RemoteVolumeIdAsync(string name)
    {
        await using var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "Test", true, null, CancellationToken.None);
        return await db.GetRemoteVolumeIDAsync(name, CancellationToken.None);
    }

    [Test]
    [Category("Targeted")]
    public async Task CompactCleansUpAfterAStoppedUploadAsync()
    {
        await MakeWastedSpaceAsync();
        var name = await LeaveUnfinishedUploadAsync(flagged: true);

        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.CompactAsync());

        Assert.That(await RemoteVolumeIdAsync(name), Is.LessThan(0), "The unfinished volume is still registered");

        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
    }

    [Test]
    [Category("Targeted")]
    public async Task CompactStillRefusesAnUnexpectedUploadingVolumeAsync()
    {
        await MakeWastedSpaceAsync();
        await LeaveUnfinishedUploadAsync(flagged: false);

        using var c = new Controller("file://" + TARGETFOLDER, Options, null);
        var ex = Assert.CatchAsync<RemoteListVerificationException>(async () => await c.CompactAsync());
        Assert.That(ex!.HelpID, Is.EqualTo("UnexpectedUploadingFile"));
    }
}
