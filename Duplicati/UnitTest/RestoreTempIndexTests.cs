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
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The temporary tables of a restore are kept in memory and hold a row for every file and block to
/// restore. An index nothing uses only adds to them: the one on the target path held more than a
/// quarter of them, and the one on the ID repeated the rowid (#7538).
/// </summary>
public class RestoreTempIndexTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task TheFileListOfARestoreHasNoIndexOnItsIdOrTargetPathAsync()
    {
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a.bin"), new byte[] { 1, 2, 3 });
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        await using var db = await LocalRestoreDatabase.CreateAsync(DBFILE, null, CancellationToken.None);
        await db.PrepareRestoreFilelistAsync(DateTime.UtcNow, null, null, false, CancellationToken.None);

        // The columns of every index on the temporary file list
        var indexed = new List<string>();
        await using var cmd = db.Connection.CreateCommand(db.Transaction.Transaction);
        cmd.CommandText = @"
            SELECT ""ii"".""name""
            FROM ""temp"".""sqlite_master"" ""m"", pragma_index_info(""m"".""name"", 'temp') ""ii""
            WHERE ""m"".""type"" = 'index' AND ""m"".""tbl_name"" LIKE 'Fileset-%'";
        await using (var rd = await cmd.ExecuteReaderAsync(false, CancellationToken.None))
            while (await rd.ReadAsync())
                indexed.Add(rd.GetString(0));

        Assert.That(indexed, Is.Not.Empty, "The file list has no indexes, so the query did not find it");
        Assert.That(indexed, Does.Not.Contain("TargetPath"), "No query looks up or sorts the file list by its target path");
        Assert.That(indexed, Does.Not.Contain("ID"), "The ID is the rowid of the file list");
    }
}
