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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Volumes;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A purge replaces the filelist of a fileset under a new name, which is a time between the
/// filesets next to it. When the filesets are seconds apart, the later seconds run out, and
/// purge-broken-files used to stop halfway, after the blocks of the missing volumes were
/// already dropped from the database. That left a database every operation refused with
/// "Found inconsistency", including the purge-broken-files run meant to fix it.
/// </summary>
public class PurgeFilesetTimestampTests : BasicSetupHelper
{
    private static readonly DateTime Start = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);

    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        blocksize = "10KB",
        dblock_size = "100KB",
        no_auto_compact = true
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

    /// <summary>
    /// Moves the filesets, oldest first, to the given times, renaming their filelists to match
    /// </summary>
    private async Task MoveFilesetsAsync(params DateTime[] times)
    {
        var options = new Options(Options!);
        using var db = new SqliteConnection($"Data Source={DBFILE};Pooling=False");
        await db.OpenAsync();

        var filesets = new List<(long ID, long VolumeID, string Name)>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = @"SELECT ""Fileset"".""ID"", ""Fileset"".""VolumeID"", ""Remotevolume"".""Name"" FROM ""Fileset"" JOIN ""Remotevolume"" ON ""Remotevolume"".""ID"" = ""Fileset"".""VolumeID"" ORDER BY ""Fileset"".""Timestamp""";
            using var rd = await cmd.ExecuteReaderAsync();
            while (await rd.ReadAsync())
                filesets.Add((rd.GetInt64(0), rd.GetInt64(1), rd.GetString(2)));
        }

        Assert.That(filesets, Has.Count.EqualTo(times.Length));
        for (var i = 0; i < times.Length; i++)
        {
            var name = VolumeBase.GenerateFilename(RemoteVolumeType.Files, options, null, times[i]);
            using var cmd = db.CreateCommand();
            cmd.CommandText = @"UPDATE ""Fileset"" SET ""Timestamp"" = @Timestamp WHERE ""ID"" = @ID; UPDATE ""Remotevolume"" SET ""Name"" = @Name WHERE ""ID"" = @VolumeID";
            cmd.Parameters.AddWithValue("@Timestamp", new DateTimeOffset(times[i]).ToUnixTimeSeconds());
            cmd.Parameters.AddWithValue("@ID", filesets[i].ID);
            cmd.Parameters.AddWithValue("@Name", name);
            cmd.Parameters.AddWithValue("@VolumeID", filesets[i].VolumeID);
            await cmd.ExecuteNonQueryAsync();
            File.Move(Path.Combine(TARGETFOLDER, filesets[i].Name), Path.Combine(TARGETFOLDER, name));
        }
    }

    private async Task<DateTime[]> FilesetTimesAsync()
    {
        using var c = new Controller("file://" + TARGETFOLDER, Options, null);
        var result = await c.ListAsync();
        return result.Filesets.OrderByDescending(x => x.Version).Select(x => x.Time.ToUniversalTime()).ToArray();
    }

    /// <summary>
    /// Makes the given number of versions, each holding the same large file spread over several
    /// block volumes, and a small file of its own
    /// </summary>
    private async Task BackupVersionsAsync(int count)
    {
        WriteFile("large.bin", 600 * 1024, 1);
        for (var i = 0; i < count; i++)
        {
            WriteFile("small.bin", 1000 + i, 100 + i);
            await BackupAsync();
        }
    }

    /// <summary>
    /// Moves a block volume that only holds blocks of the large file out of the destination, so
    /// every version has a broken file but none is fully broken
    /// </summary>
    /// <returns>The path the volume had in the destination</returns>
    private string MoveAwayLargeFileVolume()
    {
        // Which blocks share a volume depends on the order the files are read in, so the volume is
        // chosen from the database: one that holds only data blocks of the large file. A volume with
        // metadata blocks as well would make purge-broken-files replace that metadata, with a warning.
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

        var volume = Path.Combine(TARGETFOLDER, name);
        File.Move(volume, Path.Combine(Path.GetDirectoryName(DBFILE)!, name));
        return volume;
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeMovesAFilesetBackWhenTheNextSecondIsTakenAsync()
    {
        WriteFile("keep.bin", 1000, 1);
        await BackupAsync();
        WriteFile("only-in-middle.bin", 1000, 2);
        await BackupAsync();
        File.Delete(Path.Combine(DATAFOLDER, "only-in-middle.bin"));
        WriteFile("keep.bin", 1001, 3);
        await BackupAsync();
        await MoveFilesetsAsync(Start, Start.AddSeconds(10), Start.AddSeconds(11));

        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.PurgeFilesAsync(new Library.Utility.FilterExpression("*only-in-middle.bin")));

        Assert.That(await FilesetTimesAsync(), Is.EqualTo(new[] { Start, Start.AddSeconds(9), Start.AddSeconds(11) }));
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeBrokenFilesHandlesFilesetsOneSecondApartAsync()
    {
        await BackupVersionsAsync(2);
        await MoveFilesetsAsync(Start, Start.AddSeconds(1));
        MoveAwayLargeFileVolume();

        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.PurgeBrokenFilesAsync(null));

        var times = await FilesetTimesAsync();
        Assert.That(times, Has.Length.EqualTo(2));
        Assert.That(times[0], Is.LessThan(times[1]));

        WriteFile("small.bin", 2000, 200);
        await BackupAsync();
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
    }

    [Test]
    [Category("Targeted")]
    public async Task PurgeBrokenFilesChangesNothingWhenAFilesetCannotGetATimestampAsync()
    {
        await BackupVersionsAsync(3);
        await MoveFilesetsAsync(Start, Start.AddSeconds(1), Start.AddSeconds(2));
        var volume = MoveAwayLargeFileVolume();

        Exception? ex;
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            ex = Assert.CatchAsync<Exception>(async () => await c.PurgeBrokenFilesAsync(null));

        // With the volume back, the backup is complete again, and so must the database be
        File.Move(Path.Combine(Path.GetDirectoryName(DBFILE)!, Path.GetFileName(volume)), volume);
        WriteFile("small.bin", 2000, 200);
        await BackupAsync();
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.TestAsync(long.MaxValue));
        Assert.That(await FilesetTimesAsync(), Has.Length.EqualTo(4));

        Assert.That(ex, Is.InstanceOf<UserInformationException>(), ex?.ToString());
        Assert.That(((UserInformationException)ex!).HelpID, Is.EqualTo("NoUnusedFilesetTimestamp"));
    }

    private static Task<DateTime?> FindAsync(DateTime? previous, DateTime? next, params int[] takenOffsets)
    {
        var options = new Options(new Dictionary<string, string?>());
        var taken = takenOffsets.Select(x => VolumeBase.GenerateFilename(RemoteVolumeType.Files, options, null, Start.AddSeconds(x))).ToHashSet();
        return FilesetVolumeWriter.FindUnusedReplacementTimeAsync((name, _) => Task.FromResult(taken.Contains(name)), options, Start, previous, next, CancellationToken.None);
    }

    [Test]
    [Category("Targeted")]
    public async Task ReplacementTimeIsTheFirstFreeSecondAfterTheOriginalAsync()
        => Assert.That(await FindAsync(Start.AddSeconds(-10), Start.AddSeconds(10), 0, 1, 2), Is.EqualTo(Start.AddSeconds(3)));

    [Test]
    [Category("Targeted")]
    public async Task ReplacementTimeIsBeforeTheOriginalWhenTheNextFilesetIsReachedAsync()
        => Assert.That(await FindAsync(Start.AddSeconds(-10), Start.AddSeconds(2), 0, 1, -1), Is.EqualTo(Start.AddSeconds(-2)));

    [Test]
    [Category("Targeted")]
    public async Task ReplacementTimeIsNullWhenNoSecondBetweenTheNeighboursIsFreeAsync()
        => Assert.That(await FindAsync(Start.AddSeconds(-2), Start.AddSeconds(2), 0, 1, -1), Is.Null);

    [Test]
    [Category("Targeted")]
    public async Task ReplacementTimeIsNullBetweenFilesetsOneSecondApartAsync()
        => Assert.That(await FindAsync(Start.AddSeconds(-1), Start.AddSeconds(1), 0), Is.Null);
}
