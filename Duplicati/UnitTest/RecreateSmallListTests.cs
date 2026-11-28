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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A recreate lists the single block of every small file and metadata in a temporary table, which
/// is kept in memory. With a row for every rowid and two unique indexes, the table held 42 MB for
/// 100,000 small files, and a restore without a local database starts with that recreate (#7538).
/// </summary>
public class RecreateSmallListTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task TheSmallFileListHasNoExtraIndexAndSkipsAnEqualBlockHashAsync()
    {
        await using var parent = await LocalDatabase.CreateLocalDatabaseAsync(DBFILE, "Recreate", true, null, CancellationToken.None);
        await using var db = await LocalRecreateDatabase.CreateAsync(parent, new Options(TestOptions), null, CancellationToken.None);

        await db.AddSmallBlocksetLinkAsync("same", "same", 10, CancellationToken.None);
        await db.AddSmallBlocksetLinkAsync("file", "block", 20, CancellationToken.None);
        await db.AddSmallBlocksetLinkAsync("file", "block", 20, CancellationToken.None);

        await using var cmd = db.Connection.CreateCommand(db.Transaction.Transaction);
        cmd.CommandText = @"SELECT ""name"" FROM ""temp"".""sqlite_master"" WHERE ""type"" = 'table' AND ""name"" LIKE 'TempSmalllist_%'";
        var table = (string?)await cmd.ExecuteScalarAsync();
        Assert.That(table, Is.Not.Null, "The small file list was not found");

        var indexes = new List<string>();
        cmd.CommandText = $@"SELECT ""name"", ""origin"" FROM pragma_index_list('{table}', 'temp')";
        await using (var rd = await cmd.ExecuteReaderAsync())
            while (await rd.ReadAsync())
                if (rd.GetString(1) != "pk")
                    indexes.Add(rd.GetString(0));
        Assert.That(indexes, Is.Empty, "The small file list has indexes besides its key");

        var rows = new List<(string, string?, long)>();
        cmd.CommandText = $@"SELECT ""FileHash"", ""BlockHash"", ""BlockSize"" FROM ""{table}"" ORDER BY ""BlockSize""";
        await using (var rd = await cmd.ExecuteReaderAsync())
            while (await rd.ReadAsync())
                rows.Add((rd.GetString(0), rd.IsDBNull(1) ? null : rd.GetString(1), rd.GetInt64(2)));

        Assert.That(rows, Is.EqualTo(new List<(string, string?, long)> { ("same", null, 10), ("file", "block", 20) }),
            "A block hash equal to the file hash is stored again, or a repeated link is stored twice");
    }
}
