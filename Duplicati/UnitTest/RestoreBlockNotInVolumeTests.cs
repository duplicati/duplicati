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
using Duplicati.Library.Main.Volumes;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// When the database places a block in a volume that does not hold it, reading the block must
/// say which block is missing, rather than stop with a <see cref="NullReferenceException"/>.
/// </summary>
public class RestoreBlockNotInVolumeTests : BasicSetupHelper
{
    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        blocksize = "10KB",
        dblock_size = "100KB"
    });

    /// <summary>
    /// Backs up a file over several block volumes, and moves one of its blocks, in the database,
    /// to another block volume that does not hold it
    /// </summary>
    /// <returns>The hash of the moved block</returns>
    private async Task<string> BackupAndMisplaceABlockAsync()
    {
        var data = new byte[400 * 1024];
        new Random(1).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a.bin"), data);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        using var db = new SqliteConnection($"Data Source={DBFILE};Pooling=False");
        await db.OpenAsync();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"
            SELECT ""Block"".""Hash"", ""Other"".""ID""
            FROM ""Block""
            JOIN ""Remotevolume"" ""Other"" ON ""Other"".""Type"" = 'Blocks' AND ""Other"".""ID"" != ""Block"".""VolumeID""
            WHERE ""Block"".""Size"" = 10240
            ORDER BY ""Block"".""ID""
            LIMIT 1";
        string hash;
        long other;
        using (var rd = await cmd.ExecuteReaderAsync())
        {
            Assert.That(await rd.ReadAsync(), Is.True, "Expected the file to span several block volumes");
            hash = rd.GetString(0);
            other = rd.GetInt64(1);
        }

        cmd.CommandText = @"UPDATE ""Block"" SET ""VolumeID"" = @VolumeID WHERE ""Hash"" = @Hash";
        cmd.Parameters.AddWithValue("@VolumeID", other);
        cmd.Parameters.AddWithValue("@Hash", hash);
        Assert.That(await cmd.ExecuteNonQueryAsync(), Is.EqualTo(1));
        return hash;
    }

    [Test]
    [Category("Targeted")]
    public async Task RestoreNamesTheBlockThatIsNotInItsVolumeAsync()
    {
        var hash = await BackupAndMisplaceABlockAsync();

        using var c = new Controller("file://" + TARGETFOLDER, Options.Expand(new { restore_path = RESTOREFOLDER }), null);
        var ex = Assert.CatchAsync<Exception>(async () => await c.RestoreAsync(["*"]));

        Assert.That(ex, Is.Not.InstanceOf<NullReferenceException>(), ex?.ToString());
        Assert.That(ex!.ToString(), Does.Contain(hash), ex.ToString());
    }

    [Test]
    [Category("Targeted")]
    public async Task ReadingABlockThatIsNotInTheVolumeNamesTheBlockAsync()
    {
        var data = new byte[50 * 1024];
        new Random(2).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "b.bin"), data);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var volume = Directory.GetFiles(TARGETFOLDER, "*.dblock.zip").Single();
        const string hash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
        using var reader = new BlockVolumeReader("zip", volume, new Options(Options!));

        var ex = Assert.Throws<InvalidDataException>(() => reader.ReadBlock(hash, new byte[10240]));
        Assert.That(ex!.Message, Does.Contain(hash));
    }
}
