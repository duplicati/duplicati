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
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using ClassicAssert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// A file whose data is intact but whose metadata volume is gone is kept by purge-broken-files,
/// with replacement metadata. The replacement is supposed to be empty metadata, which says
/// nothing about the file, rather than metadata that describes something else.
/// </summary>
public class ReplacementMetadataTests : BasicSetupHelper
{
    private Dictionary<string, string> BackupOptions()
        => TestOptions.Expand(new { blocksize = "1kb", no_encryption = true, dblock_size = "20kb" });

    /// <summary>
    /// The first run stores the data of "a". Touching only its timestamp makes the second run
    /// store one thing, a new metadata block, in a volume of its own. Deleting that volume loses
    /// the metadata of the newest version and nothing else.
    /// </summary>
    private async Task<(string Hash, int Size)> LoseOnlyTheMetadataAsync(Dictionary<string, string> testopts)
    {
        var emptymeta = Library.Main.Utility.WrapMetadata(new Dictionary<string, string>(), new Options(testopts));

        var patha = Path.Combine(DATAFOLDER, "a");
        var data = new byte[1024 * 60];
        Random.Shared.NextBytes(data);
        File.WriteAllBytes(patha, data);
        File.SetLastWriteTimeUtc(patha, new DateTime(2001, 1, 1, 1, 1, 1, DateTimeKind.Utc));

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var afterFirst = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly).ToHashSet();

        // A second apart, or both filesets land on the same timestamp and the purge stops for that
        // reason instead of doing what this is about.
        await Task.Delay(1500);
        File.SetLastWriteTimeUtc(patha, new DateTime(2010, 10, 10, 10, 10, 10, DateTimeKind.Utc));

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var fromSecond = Directory.GetFiles(TARGETFOLDER, "*.dblock.*", SearchOption.TopDirectoryOnly)
            .Where(x => !afterFirst.Contains(x)).ToList();

        Assert.That(fromSecond, Has.Count.EqualTo(1),
            "The second run was expected to write one volume, holding only the new metadata block");

        File.Delete(fromSecond[0]);
        return (emptymeta.FileHash, emptymeta.Blob.Length);
    }

    /// <summary>
    /// Runs the recovery the way the messages tell a user to: repair reports the missing volume
    /// and points at purge-broken-files, which is then run.
    /// </summary>
    private async Task RecoverAsync(Dictionary<string, string> testopts)
    {
        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            Assert.ThrowsAsync<Library.Interface.UserInformationException>(() => c.RepairAsync(),
                "Repair is expected to report the missing volume and point at purge-broken-files");

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.PurgeBrokenFilesAsync(null));
    }

    /// <summary>
    /// Reads the metadata the newest version of "a" points at. The older version keeps its own and
    /// is not part of this.
    /// </summary>
    private async Task<(long Length, string FullHash)> ReadNewestMetadataOfAAsync()
    {
        await using var con = await SQLiteLoader.LoadConnectionAsync(DBFILE);
        await using var cmd = con.CreateCommand();
        cmd.SetCommandAndParameters(@"
            SELECT ""Blockset"".""Length"", ""Blockset"".""FullHash""
            FROM ""Fileset""
            JOIN ""FilesetEntry"" ON ""FilesetEntry"".""FilesetID"" = ""Fileset"".""ID""
            JOIN ""File"" ON ""File"".""ID"" = ""FilesetEntry"".""FileID""
            JOIN ""Metadataset"" ON ""File"".""MetadataID"" = ""Metadataset"".""ID""
            JOIN ""Blockset"" ON ""Metadataset"".""BlocksetID"" = ""Blockset"".""ID""
            WHERE ""Fileset"".""Timestamp"" = (SELECT MAX(""Timestamp"") FROM ""Fileset"")
                AND ""File"".""Path"" LIKE '%a'");

        await using var rd = await cmd.ExecuteReaderAsync();
        Assert.That(await rd.ReadAsync(), Is.True, "The file was dropped instead of being kept");
        return (rd.ConvertValueToInt64(0), rd.ConvertValueToString(1) ?? "");
    }

    [Test]
    [Category("Targeted")]
    public async Task TheReplacementIsEmptyMetadataAndNotAnotherBlocksetAsync()
    {
        var testopts = BackupOptions();
        var (emptyHash, emptySize) = await LoseOnlyTheMetadataAsync(testopts);
        await RecoverAsync(testopts);

        var metadata = await ReadNewestMetadataOfAAsync();

        // Both the file's own metadata and the previous version's are 137 bytes here, so the hash
        // is what tells empty metadata apart from a blockset that describes something else.
        ClassicAssert.AreEqual(emptyHash, metadata.FullHash,
            "The file was handed a metadata blockset that belongs to something else");
        ClassicAssert.AreEqual(emptySize, metadata.Length,
            "The replacement metadata is not the empty metadata");
    }

    [Test]
    [Category("Targeted")]
    public async Task TheDataAndTheOlderVersionSurviveAsync()
    {
        var testopts = BackupOptions();
        await LoseOnlyTheMetadataAsync(testopts);
        var expected = await File.ReadAllBytesAsync(Path.Combine(DATAFOLDER, "a"));
        await RecoverAsync(testopts);

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            TestUtils.AssertResults(await c.TestAsync(int.MaxValue));

        using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { restore_path = RESTOREFOLDER }), null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        var restored = Path.Combine(RESTOREFOLDER, "a");
        ClassicAssert.IsTrue(File.Exists(restored), "The file was not restored");
        ClassicAssert.AreEqual(expected, await File.ReadAllBytesAsync(restored), "The restored data does not match");
    }

    [Test]
    [Category("Targeted")]
    public async Task ADryRunWritesNothingToTheDestinationAsync()
    {
        var testopts = BackupOptions();
        await LoseOnlyTheMetadataAsync(testopts);

        using (var c = new Controller("file://" + TARGETFOLDER, testopts, null))
            Assert.ThrowsAsync<Library.Interface.UserInformationException>(() => c.RepairAsync());

        var before = Directory.GetFiles(TARGETFOLDER, "*", SearchOption.TopDirectoryOnly).ToHashSet();

        using (var c = new Controller("file://" + TARGETFOLDER, testopts.Expand(new { dry_run = true }), null))
            await c.PurgeBrokenFilesAsync(null);

        var after = Directory.GetFiles(TARGETFOLDER, "*", SearchOption.TopDirectoryOnly).ToHashSet();
        Assert.That(after, Is.EquivalentTo(before), "A dry run wrote to the destination");
    }
}
