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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Deleting filesets marks their dlist volumes for deletion, and refuses to go on when the
/// number of volumes it marks is not the number of filesets it deleted. These pin down what
/// that check counts.
/// </summary>
[TestFixture]
public class DeleteFilesetVolumeCountTests
{
    /// <summary>The timestamp of the fileset that the tests drop</summary>
    private const long DroppedFilesetTime = 1000;

    /// <summary>
    /// Builds a database with two filesets, the dropped one's dlist volume in the given state,
    /// and optionally a third dlist volume that no fileset uses.
    /// </summary>
    private static async Task<TempFile> CreateDatabaseAsync(string droppedVolumeState, string? extraVolumeState)
    {
        var dbfile = new TempFile();
        try
        {
            using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
            DatabaseUpgrader.UpgradeDatabase(db, dbfile, typeof(DatabaseSchemaMarker));
            using var cmd = db.CreateCommand();

            var statements = new List<string>
            {
                @"INSERT INTO ""Operation"" (""ID"", ""Description"", ""Timestamp"") VALUES (1, 'Test', 0)",
                $@"INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""Size"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                   VALUES (1, 1, 'dropped.dlist.zip', 'Files', 10, '{droppedVolumeState}', 0, 0, 0, 0)",
                @"INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""Size"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                   VALUES (2, 1, 'kept.dlist.zip', 'Files', 20, 'Verified', 0, 0, 0, 0)",
                $@"INSERT INTO ""Fileset"" (""ID"", ""OperationID"", ""VolumeID"", ""IsFullBackup"", ""Timestamp"") VALUES (1, 1, 1, 1, {DroppedFilesetTime})",
                @"INSERT INTO ""Fileset"" (""ID"", ""OperationID"", ""VolumeID"", ""IsFullBackup"", ""Timestamp"") VALUES (2, 1, 2, 1, 2000)",
            };
            if (extraVolumeState != null)
                statements.Add($@"INSERT INTO ""Remotevolume"" (""ID"", ""OperationID"", ""Name"", ""Type"", ""Size"", ""State"", ""VerificationCount"", ""DeleteGraceTime"", ""ArchiveTime"", ""LockExpirationTime"")
                   VALUES (3, 1, 'extra.dlist.zip', 'Files', -1, '{extraVolumeState}', 0, 0, 0, 0)");

            foreach (var sql in statements)
            {
                cmd.CommandText = sql;
                await cmd.ExecuteNonQueryAsync();
            }

            return dbfile;
        }
        catch
        {
            dbfile.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Drops the fileset at <see cref="DroppedFilesetTime"/> and returns the volumes up for removal
    /// </summary>
    private static async Task<string[]> DropAsync(string dbfile)
    {
        await using var db = await LocalDeleteDatabase.CreateAsync(dbfile, "Test", null, CancellationToken.None);
        var removed = new List<string>();
        await foreach (var v in db.DropFilesetsFromTableAsync([DateTime.UnixEpoch.AddSeconds(DroppedFilesetTime)], CancellationToken.None))
            removed.Add(v.Key);
        return removed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    [Test]
    [Category("Database")]
    [TestCase("Verified")]
    [TestCase("Uploaded")]
    // A fileset whose dlist file is already being deleted still has to be deletable
    [TestCase("Deleting")]
    public async Task DroppingAFilesetMarksItsVolumeAsync(string droppedVolumeState)
    {
        using var dbfile = await CreateDatabaseAsync(droppedVolumeState, null);
        Assert.That(await DropAsync(dbfile), Is.EqualTo(new[] { "dropped.dlist.zip" }));
    }

    [Test]
    [Category("Database")]
    public async Task AnEntryLeftByARenameDoesNotCountAsync()
    {
        // A failed upload is retried under a new name, and the old name stays registered as
        // being deleted, with no fileset. It is removed along with the dropped volume.
        using var dbfile = await CreateDatabaseAsync("Verified", "Deleting");
        Assert.That(await DropAsync(dbfile), Is.EqualTo(new[] { "dropped.dlist.zip", "extra.dlist.zip" }));
    }

    [Test]
    [Category("Database")]
    [TestCase("Verified")]
    [TestCase("Uploaded")]
    [TestCase("Temporary")]
    public async Task ADlistVolumeWithoutAFilesetStopsTheDeleteAsync(string extraVolumeState)
    {
        // A dlist volume that is not being deleted and has no fileset means the database does
        // not match what the delete expects, and it must not go on
        using var dbfile = await CreateDatabaseAsync("Verified", extraVolumeState);
        var ex = Assert.CatchAsync<Exception>(async () => await DropAsync(dbfile));
        Assert.That(ex!.Message, Does.Contain("Unexpected number of remote volumes marked as deleted"));
    }
}
