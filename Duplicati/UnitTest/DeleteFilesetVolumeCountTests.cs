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
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database.Local;
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
    private static readonly DateTime DroppedFilesetTime = DateTime.UnixEpoch.AddSeconds(1000);
    /// <summary>The timestamp of the fileset that the tests keep</summary>
    private static readonly DateTime KeptFilesetTime = DateTime.UnixEpoch.AddSeconds(2000);

    /// <summary>
    /// Builds a database with two filesets, the dropped one's dlist volume in the given state,
    /// and optionally a third dlist volume that no fileset uses.
    /// </summary>
    private static async Task<TempFile> CreateDatabaseAsync(RemoteVolumeState droppedVolumeState, RemoteVolumeState? extraVolumeState)
    {
        var dbfile = new TempFile();
        try
        {
            await using var db = await LocalDeleteDatabase.CreateAsync(dbfile, "Test", null, CancellationToken.None);

            var dropped = await db.RegisterRemoteVolumeAsync("dropped.dlist.zip", RemoteVolumeType.Files, 10, droppedVolumeState, CancellationToken.None);
            await db.CreateFilesetAsync(dropped, DroppedFilesetTime, CancellationToken.None);

            var kept = await db.RegisterRemoteVolumeAsync("kept.dlist.zip", RemoteVolumeType.Files, 20, RemoteVolumeState.Verified, CancellationToken.None);
            await db.CreateFilesetAsync(kept, KeptFilesetTime, CancellationToken.None);

            if (extraVolumeState != null)
                await db.RegisterRemoteVolumeAsync("extra.dlist.zip", RemoteVolumeType.Files, -1, extraVolumeState.Value, CancellationToken.None);

            await db.Transaction.CommitAsync(CancellationToken.None);

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
        await foreach (var v in db.DropFilesetsFromTableAsync([DroppedFilesetTime], CancellationToken.None))
            removed.Add(v.Key);
        return removed.OrderBy(x => x, StringComparer.Ordinal).ToArray();
    }

    [Test]
    [Category("Database")]
    [TestCase(RemoteVolumeState.Verified)]
    [TestCase(RemoteVolumeState.Uploaded)]
    // A fileset whose dlist file is already being deleted still has to be deletable
    [TestCase(RemoteVolumeState.Deleting)]
    public async Task DroppingAFilesetMarksItsVolumeAsync(RemoteVolumeState droppedVolumeState)
    {
        using var dbfile = await CreateDatabaseAsync(droppedVolumeState, null);
        Assert.That(await DropAsync(dbfile), Is.EqualTo(new[] { "dropped.dlist.zip" }));
    }

    [Test]
    [Category("Database")]
    [TestCase(RemoteVolumeState.Verified)]
    // The dropped fileset's own volume is counted even when it is already being deleted,
    // while the entry without a fileset is not, so the two must be told apart
    [TestCase(RemoteVolumeState.Deleting)]
    public async Task AnEntryLeftByARenameDoesNotCountAsync(RemoteVolumeState droppedVolumeState)
    {
        // A failed upload is retried under a new name, and the old name stays registered as
        // being deleted, with no fileset. It is removed along with the dropped volume.
        using var dbfile = await CreateDatabaseAsync(droppedVolumeState, RemoteVolumeState.Deleting);
        Assert.That(await DropAsync(dbfile), Is.EqualTo(new[] { "dropped.dlist.zip", "extra.dlist.zip" }));
    }

    [Test]
    [Category("Database")]
    [TestCase(RemoteVolumeState.Verified)]
    [TestCase(RemoteVolumeState.Uploaded)]
    [TestCase(RemoteVolumeState.Temporary)]
    public async Task ADlistVolumeWithoutAFilesetStopsTheDeleteAsync(RemoteVolumeState extraVolumeState)
    {
        // A dlist volume that is not being deleted and has no fileset means the database does
        // not match what the delete expects, and it must not go on
        using var dbfile = await CreateDatabaseAsync(RemoteVolumeState.Verified, extraVolumeState);
        var ex = Assert.CatchAsync<Exception>(async () => await DropAsync(dbfile));
        Assert.That(ex!.Message, Does.Contain("Unexpected number of remote volumes marked as deleted"));
    }
}
