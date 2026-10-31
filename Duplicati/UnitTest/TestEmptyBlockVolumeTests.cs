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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A backup can upload a block volume without blocks, when it only needs the volume to carry
/// blocklist hashes to its index file. The test operation then picked a sample of -1 blocks
/// from it and failed with "Index was out of range".
/// </summary>
public class TestEmptyBlockVolumeTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        blocksize = "10KB",
        dblock_size = "100KB",
        no_auto_compact = true,
        // With one compressor, the blocklist hash of the large file opens a volume of its own
        concurrency_compressors = 1
    });

    private async Task BackupAsync()
    {
        using var c = new Controller("file://" + TARGETFOLDER, Options, null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }

    private void WriteFile(string name, int size, int seed)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, name), data);
    }

    private long QueryCount(string sql)
    {
        using var db = new SqliteConnection($"Data Source={DBFILE};Pooling=False");
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Moves a block volume that only holds data blocks of the large file out of the destination
    /// </summary>
    private void MoveAwayLargeFileVolume()
    {
        string name;
        using (var db = new SqliteConnection($"Data Source={DBFILE};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"
                SELECT ""Remotevolume"".""Name""
                FROM ""Remotevolume""
                WHERE ""Remotevolume"".""Type"" = 'Blocks'
                AND EXISTS (SELECT 1 FROM ""Block"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"")
                AND NOT EXISTS (
                    SELECT 1 FROM ""Block""
                    WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID""
                    AND ""Block"".""ID"" NOT IN (
                        SELECT ""BlocksetEntry"".""BlockID""
                        FROM ""BlocksetEntry""
                        JOIN ""File"" ON ""File"".""BlocksetID"" = ""BlocksetEntry"".""BlocksetID""
                        WHERE ""File"".""Path"" = @Path))
                ORDER BY ""Remotevolume"".""ID""
                LIMIT 1";
            cmd.Parameters.AddWithValue("@Path", Path.Combine(DATAFOLDER, "large.bin"));
            name = cmd.ExecuteScalar() as string ?? throw new AssertionException("No block volume holds only data blocks of the large file");
        }

        File.Move(Path.Combine(TARGETFOLDER, name), Path.Combine(Path.GetDirectoryName(DBFILE)!, name));
    }

    [Test]
    [Category("Targeted")]
    public async Task ABlockVolumeWithoutBlocksPassesTheTestAsync()
    {
        WriteFile("large.bin", 600 * 1024, 1);
        WriteFile("small.bin", 1000, 2);
        await BackupAsync();

        // Purging the broken large file leaves its blocklist block in another volume, but no
        // version refers to it, so the next backup sends its blocklist hash in a volume of its own
        MoveAwayLargeFileVolume();
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.PurgeBrokenFilesAsync(null));

        WriteFile("small.bin", 2000, 3);
        await BackupAsync();

        Assert.That(QueryCount(@"SELECT COUNT(*) FROM ""Remotevolume"" WHERE ""Type"" = 'Blocks' AND ""State"" IN ('Uploaded', 'Verified') AND NOT EXISTS (SELECT 1 FROM ""Block"" WHERE ""Block"".""VolumeID"" = ""Remotevolume"".""ID"")"),
            Is.EqualTo(1), "The backup did not upload a block volume without blocks");

        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
    }
}
