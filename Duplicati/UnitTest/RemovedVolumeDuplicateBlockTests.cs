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
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A compact that is stopped before its new block volume is uploaded leaves the volume, and the
/// blocks it recorded as copied into it, in the database. Cleaning up the volume must take those
/// records with it: volume ids are reused, so the next volume would otherwise claim blocks it
/// does not hold. A test then rewrites its index file with them, and after a database recreate a
/// restore reads the blocks from the wrong volume.
/// </summary>
public class RemovedVolumeDuplicateBlockTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        blocksize = "10KB",
        dblock_size = "100KB",
        no_auto_compact = true
    });

    private void WriteFile(string name, int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
    }

    private async Task BackupAsync()
    {
        using var c = new Controller("file://" + TARGETFOLDER, Options, null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    private async Task<long> ScalarAsync(string sql)
    {
        using var db = new SqliteConnection($"Data Source={DBFILE};Pooling=False");
        await db.OpenAsync();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToInt64(await cmd.ExecuteScalarAsync());
    }

    /// <summary>
    /// Leaves the database as a compact stopped before its upload does: a new block volume in the
    /// uploading state, which is not at the destination, recorded as holding copies of some blocks
    /// </summary>
    /// <returns>The id of the volume</returns>
    private async Task<long> LeaveStoppedCompactAsync()
    {
        string name;
        using (var vol = new BlockVolumeWriter(new Options(Options!)))
            name = vol.RemoteFilename;

        long id;
        await using (var db = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "Test", true, null, CancellationToken.None))
        {
            id = await db.RegisterRemoteVolumeAsync(name, RemoteVolumeType.Blocks, RemoteVolumeState.Uploading, CancellationToken.None);
            await db.TerminatedWithActiveUploadsAsync(CancellationToken.None, true);
            await db.Transaction.CommitAsync(CancellationToken.None);
        }

        using var conn = new SqliteConnection($"Data Source={DBFILE};Pooling=False");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"INSERT INTO ""DuplicateBlock"" (""BlockID"", ""VolumeID"") SELECT ""ID"", @VolumeID FROM ""Block"" WHERE ""VolumeID"" != @VolumeID ORDER BY ""ID"" LIMIT 5";
        cmd.Parameters.AddWithValue("@VolumeID", id);
        Assert.That(await cmd.ExecuteNonQueryAsync(), Is.EqualTo(5));
        return id;
    }

    [Test]
    [Category("Targeted")]
    public async Task CleaningUpAVolumeRemovesItsDuplicateBlocksAsync()
    {
        // Versions that leave space to compact
        WriteFile("a.bin", 300 * 1024, 1);
        for (var i = 0; i < 3; i++)
        {
            WriteFile("b.bin", 200 * 1024, 10 + i);
            await BackupAsync();
        }
        using (var c = new Controller("file://" + TARGETFOLDER, Options.Expand(new { keep_versions = 1 }), null))
            TestUtils.AssertResults(await c.DeleteAsync());

        var stopped = await LeaveStoppedCompactAsync();

        // Repair cleans up after the stopped upload, and the block volume the compact then writes
        // first is registered under the freed id
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());
        using (var c = new Controller("file://" + TARGETFOLDER, Options.Expand(new { threshold = 1 }), null))
            TestUtils.AssertResults(await c.CompactAsync());

        Assert.That(await ScalarAsync($@"SELECT COUNT(*) FROM ""Remotevolume"" WHERE ""ID"" = {stopped} AND ""Type"" = 'Blocks' AND ""State"" != 'Uploading'"), Is.EqualTo(1), "The compact did not reuse the id of the removed volume");
        Assert.That(await ScalarAsync($@"SELECT COUNT(*) FROM ""DuplicateBlock"" WHERE ""VolumeID"" = {stopped} AND ""BlockID"" IN (SELECT ""ID"" FROM ""Block"" WHERE ""VolumeID"" != {stopped})"), Is.EqualTo(0),
            "The compacted volume took over the copies recorded for the removed volume");

        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));

        // A recreate reads the index files, so a restore shows whether they point at the right volumes
        File.Delete(DBFILE);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.RepairAsync());

        var restore = Path.Combine(RESTOREFOLDER, "restored");
        using (var c = new Controller("file://" + TARGETFOLDER, Options.Expand(new { restore_path = restore }), null))
            TestUtils.AssertResults(await c.RestoreAsync(["*"]));
        Assert.That(File.ReadAllBytes(Path.Combine(restore, "a.bin")), Is.EqualTo(File.ReadAllBytes(Path.Combine(DATAFOLDER, "a.bin"))));
        Assert.That(File.ReadAllBytes(Path.Combine(restore, "b.bin")), Is.EqualTo(File.ReadAllBytes(Path.Combine(DATAFOLDER, "b.bin"))));
    }
}
