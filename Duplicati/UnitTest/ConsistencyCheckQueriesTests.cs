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

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The consistency check compares the recorded length of a blockset against the blocks it
/// holds, and the number of blocklist hashes against the number the block count calls for.
/// Both are read with queries that were rewritten so that SQLite no longer has to build an
/// index of its own to run them, so what is pinned here is that they still find the same
/// damage - including the shapes a rewritten outer join drops most easily.
/// </summary>
public class ConsistencyCheckQueriesTests : BasicSetupHelper
{
    /// <summary>
    /// Backs up a small file and one that spans several blocks, so that both a single-block
    /// blockset and a blockset with blocklist hashes are present.
    /// </summary>
    /// <returns>The options the backup ran with.</returns>
    private async Task<Dictionary<string, string>> BackupAsync()
    {
        var options = new Dictionary<string, string>(this.TestOptions);

        File.WriteAllText(Path.Combine(this.DATAFOLDER, "small.txt"), "some data");

        // Comfortably more than the test blocksize, so the blockset spans blocks and the
        // backup writes a blocklist hash for it
        var large = new byte[512 * 1024];
        new System.Random(42).NextBytes(large);
        File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "large.bin"), large);

        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        return options;
    }

    /// <summary>
    /// Runs the consistency check the way the backup does.
    /// </summary>
    /// <param name="options">The options the backup ran with.</param>
    private async Task VerifyAsync(Dictionary<string, string> options)
    {
        var opts = new Options(options);
        await using var db = await LocalDatabase.CreateLocalDatabaseAsync(this.DBFILE, "verify", true, null, CancellationToken.None);
        await db.VerifyConsistencyAsync(opts.Blocksize, opts.BlockhashSize, true, CancellationToken.None);
    }

    /// <summary>
    /// The blockset of the file that spans several blocks.
    /// </summary>
    private static long MultiBlockBlocksetId(SqliteCommand cmd)
    {
        var id = cmd.ExecuteScalarInt64(@"
            SELECT ""BlocksetID""
            FROM ""BlocksetEntry""
            GROUP BY ""BlocksetID""
            HAVING COUNT(*) > 1
            ORDER BY COUNT(*) DESC
            LIMIT 1
        ", -1);

        Assert.That(id, Is.GreaterThanOrEqualTo(0), "The backup left no blockset with more than one block");
        return id;
    }

    /// <summary>
    /// Green before and after: an untouched backup has nothing to report
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task AnUntouchedBackupIsConsistent()
    {
        var options = await BackupAsync();
        Assert.DoesNotThrowAsync(async () => await VerifyAsync(options));
    }

    /// <summary>
    /// The length that does not match the blocks is what the first query looks for
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task ALengthThatDoesNotMatchTheBlocksIsReported()
    {
        var options = await BackupAsync();

        await using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        await using (var cmd = con.CreateCommand())
        {
            var id = MultiBlockBlocksetId(cmd);
            await cmd
                .SetCommandAndParameters(@"UPDATE ""Blockset"" SET ""Length"" = ""Length"" + 1 WHERE ""ID"" = @Id")
                .SetParameterValue("@Id", id)
                .ExecuteNonQueryAsync();
        }

        Assert.ThrowsAsync<DatabaseInconsistencyException>(async () => await VerifyAsync(options));
    }

    /// <summary>
    /// A blockset with a length but no blocks at all. This is the row that disappears first
    /// when the outer join that produces the sums is written the wrong way round, because
    /// there is nothing on the other side of it to join to.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task ABlocksetWithALengthAndNoBlocksIsReported()
    {
        var options = await BackupAsync();

        await using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        await using (var cmd = con.CreateCommand())
        {
            var id = MultiBlockBlocksetId(cmd);
            await cmd
                .SetCommandAndParameters(@"DELETE FROM ""BlocksetEntry"" WHERE ""BlocksetID"" = @Id")
                .SetParameterValue("@Id", id)
                .ExecuteNonQueryAsync();

            // The blocklist hashes go with them, so that the run fails on the length rather
            // than on the blocklist hash count
            await cmd
                .SetCommandAndParameters(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @Id")
                .SetParameterValue("@Id", id)
                .ExecuteNonQueryAsync();
        }

        Assert.ThrowsAsync<DatabaseInconsistencyException>(async () => await VerifyAsync(options));
    }

    /// <summary>
    /// A blockset that has more blocks than one and no blocklist hash to describe them is
    /// what the second query counts
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task AMissingBlocklistHashIsReported()
    {
        var options = await BackupAsync();

        await using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        await using (var cmd = con.CreateCommand())
        {
            var id = MultiBlockBlocksetId(cmd);

            var before = cmd
                .SetCommandAndParameters(@"SELECT COUNT(*) FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @Id")
                .SetParameterValue("@Id", id)
                .ExecuteScalarInt64(0);
            Assert.That(before, Is.GreaterThan(0), "The backup left no blocklist hash to remove");

            await cmd
                .SetCommandAndParameters(@"DELETE FROM ""BlocklistHash"" WHERE ""BlocksetID"" = @Id")
                .SetParameterValue("@Id", id)
                .ExecuteNonQueryAsync();
        }

        Assert.ThrowsAsync<DatabaseInconsistencyException>(async () => await VerifyAsync(options));
    }

    /// <summary>
    /// Green before and after: a blockset with one block has no blocklist hash, and that is
    /// not damage. Counting it as damage is the mistake a rewrite of the second query makes
    /// when it counts rows of an outer join instead of rows of the table.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task ASingleBlockBlocksetNeedsNoBlocklistHash()
    {
        var options = await BackupAsync();

        await using (var con = await SQLiteLoader.LoadConnectionAsync(options["dbpath"]))
        await using (var cmd = con.CreateCommand())
        {
            var singles = cmd.ExecuteScalarInt64(@"
                SELECT COUNT(*)
                FROM (
                    SELECT ""BlocksetID""
                    FROM ""BlocksetEntry""
                    GROUP BY ""BlocksetID""
                    HAVING COUNT(*) = 1
                )
            ", 0);

            Assert.That(singles, Is.GreaterThan(0), "The backup left no single-block blockset, so nothing is being tested");
        }

        Assert.DoesNotThrowAsync(async () => await VerifyAsync(options));
    }
}
