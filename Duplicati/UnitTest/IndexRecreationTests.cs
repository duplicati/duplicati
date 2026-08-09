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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the index file re-creation performed at the start of a backup by
/// <c>RecreateMissingIndexFiles</c>. Prior to these tests, that code path had no
/// direct coverage at all, which allowed an index file with no blocklists to be
/// uploaded and registered as complete.
/// </summary>
public class IndexRecreationTests : BasicSetupHelper
{
    /// <summary>
    /// Writes source files that are large enough to produce blocklists.
    /// With the default test blocksize of 10kb, a 1MiB file spans 100 blocks,
    /// so each file gets a blockset with a blocklist hash.
    /// </summary>
    private void WriteSourceFiles(int count = 3, int size = 1024 * 1024)
    {
        var rng = new Random(42);
        var data = new byte[size];
        for (var i = 0; i < count; i++)
        {
            rng.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file-{i}.bin"), data);
        }
    }

    private string[] DindexFiles
        => Directory.GetFiles(TARGETFOLDER, "*.dindex.*", SearchOption.TopDirectoryOnly)
            .OrderBy(x => x)
            .ToArray();

    /// <summary>
    /// Removes an index file from the remote store and from the database, leaving
    /// the block file it described in place. This is the state a backup is left in
    /// when an in-flight index upload is lost.
    /// </summary>
    private static async Task LoseIndexFileAsync(string dbfile, string dindexPath)
    {
        var name = Path.GetFileName(dindexPath);

        using (var con = await SQLiteLoader.LoadConnectionAsync(dbfile))
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT ID FROM RemoteVolume WHERE Name = @Name";
            cmd.Parameters.AddWithValue("@Name", name);
            var id = Convert.ToInt64(cmd.ExecuteScalar());
            cmd.Parameters.Clear();

            cmd.CommandText = "DELETE FROM IndexBlockLink WHERE IndexVolumeID = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
            cmd.Parameters.Clear();

            cmd.CommandText = "DELETE FROM RemoteVolume WHERE ID = @Id";
            cmd.Parameters.AddWithValue("@Id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        File.Delete(dindexPath);
    }

    private static HashSet<string> ReadBlocklistHashes(string dindexPath, Library.Main.Options opts)
    {
        using var rd = new IndexVolumeReader(opts.CompressionModule, dindexPath, opts, opts.BlockhashSize);
        return rd.BlockLists.Select(x => x.Hash).ToHashSet();
    }

    private static HashSet<string> ReadBlockHashes(string dindexPath, Library.Main.Options opts)
    {
        using var rd = new IndexVolumeReader(opts.CompressionModule, dindexPath, opts, opts.BlockhashSize);
        return rd.Volumes.SelectMany(x => x.Blocks).Select(x => x.Key).ToHashSet();
    }

    /// <summary>
    /// A lost index file is re-created by the next backup, and the result passes a
    /// full remote verification.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task LostIndexFileIsRecreatedAsync()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        WriteSourceFiles();

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var before = DindexFiles;
        Assert.That(before.Length, Is.GreaterThan(0), "The first backup should produce index files");

        await LoseIndexFileAsync(DBFILE, before[0]);
        Assert.That(DindexFiles.Length, Is.EqualTo(before.Length - 1));

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        Assert.That(DindexFiles.Length, Is.EqualTo(before.Length), "The lost index file should be re-created");

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { full_remote_verification = "True" }), null))
            TestUtils.AssertResults(await c.TestAsync(100));
    }

    /// <summary>
    /// The re-created index file describes the same blocks and the same blocklists as
    /// the index file it replaces. An index that is silently missing its blocklists
    /// still passes as a file, but makes the backup unrestorable without a full
    /// database recreate, so the contents have to be compared, not just the count.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task RecreatedIndexFileHasTheSameContentAsync()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);
        WriteSourceFiles();

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var before = DindexFiles;
        var lost = before[0];
        var expectedBlocks = ReadBlockHashes(lost, opts);
        var expectedBlocklists = ReadBlocklistHashes(lost, opts);

        Assert.That(expectedBlocklists, Is.Not.Empty,
            "The test data must produce blocklists, otherwise this test cannot detect a missing one");

        await LoseIndexFileAsync(DBFILE, lost);

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var recreated = DindexFiles.Except(before).ToArray();
        Assert.That(recreated.Length, Is.EqualTo(1), "Exactly one index file should have been re-created");

        Assert.That(ReadBlockHashes(recreated[0], opts), Is.EquivalentTo(expectedBlocks),
            "The re-created index file should list the same blocks");
        Assert.That(ReadBlocklistHashes(recreated[0], opts), Is.EquivalentTo(expectedBlocklists),
            "The re-created index file should contain the same blocklists");
    }

    /// <summary>
    /// The index file policy is honoured when re-creating: <c>Full</c> writes blocklists,
    /// <c>Lookup</c> writes only the block list, and <c>None</c> does not re-create at all.
    /// </summary>
    [Test]
    [Category("Targeted")]
    [TestCase("Full")]
    [TestCase("Lookup")]
    [TestCase("None")]
    public async Task RecreationHonoursIndexFilePolicyAsync(string policy)
    {
        // The first backup always runs with the default policy, so that there is a
        // complete index file to lose; the policy under test applies to the re-creation.
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);
        WriteSourceFiles();

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var before = DindexFiles;
        await LoseIndexFileAsync(DBFILE, before[0]);

        var recreateopts = testopts.Expand(new { index_file_policy = policy });
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, recreateopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var recreated = DindexFiles.Except(before).ToArray();

        if (policy == "None")
        {
            Assert.That(recreated, Is.Empty, "No index file should be re-created when the policy is None");
            return;
        }

        Assert.That(recreated.Length, Is.EqualTo(1), "Exactly one index file should have been re-created");
        Assert.That(ReadBlockHashes(recreated[0], opts), Is.Not.Empty, "The re-created index should list blocks");

        if (policy == "Full")
            Assert.That(ReadBlocklistHashes(recreated[0], opts), Is.Not.Empty,
                "The Full policy should write blocklists");
        else
            Assert.That(ReadBlocklistHashes(recreated[0], opts), Is.Empty,
                "The Lookup policy should not write blocklists");
    }

    /// <summary>
    /// A dry-run does not upload the re-created index file and does not register it.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task RecreationIsSkippedForDryRunAsync()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        WriteSourceFiles();

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var before = DindexFiles;
        await LoseIndexFileAsync(DBFILE, before[0]);
        var afterLoss = DindexFiles;

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts.Expand(new { dry_run = "True" }), null))
            await c.BackupAsync([DATAFOLDER]);

        Assert.That(DindexFiles, Is.EquivalentTo(afterLoss), "A dry-run should not upload the re-created index file");

        using (var con = await SQLiteLoader.LoadConnectionAsync(DBFILE))
        using (var cmd = con.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM RemoteVolume WHERE Type = 'Index'";
            Assert.That(Convert.ToInt64(cmd.ExecuteScalar()), Is.EqualTo(afterLoss.Length),
                "A dry-run should not register the re-created index file");
        }
    }
}
