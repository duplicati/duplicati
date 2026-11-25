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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The temporary tables of a restore are kept in memory and hold a row for every file to restore.
/// The target path of a file is computed from its path, so it is not stored with it (#7538).
/// </summary>
public class RestoreTargetPathTests : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public async Task TheTargetPathsAreComputedAndNotStoredAsync()
    {
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a.bin"), new byte[] { 1, 2, 3 });
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        await using var db = await LocalRestoreDatabase.CreateAsync(DBFILE, null, CancellationToken.None);
        await db.PrepareRestoreFilelistAsync(DateTime.UtcNow, null, null, false, CancellationToken.None);
        var prefix = await db.GetLargestPrefixAsync(CancellationToken.None);
        await db.SetTargetPathsAsync(prefix, Library.Common.IO.Util.AppendDirSeparator(RESTOREFOLDER), CancellationToken.None);

        await using var cmd = db.Connection.CreateCommand(db.Transaction.Transaction);

        // "hidden" is 2 for a virtual generated column, which is computed when read and not stored
        cmd.CommandText = @"
            SELECT ""x"".""hidden""
            FROM ""temp"".""sqlite_master"" ""m"", pragma_table_xinfo(""m"".""name"", 'temp') ""x""
            WHERE ""m"".""type"" = 'table' AND ""m"".""name"" LIKE 'Fileset-%' AND ""x"".""name"" = 'TargetPath'";
        Assert.That(Convert.ToInt64(await cmd.ExecuteScalarAsync()), Is.EqualTo(2), "The target paths are stored with every file");

        cmd.CommandText = @"SELECT ""name"" FROM ""temp"".""sqlite_master"" WHERE ""type"" = 'table' AND ""name"" LIKE 'Fileset-%'";
        var table = (string)(await cmd.ExecuteScalarAsync())!;

        cmd.CommandText = $@"SELECT ""ID"", ""TargetPath"" FROM ""{table}"" WHERE ""Path"" = @Path";
        cmd.Parameters.AddWithValue("@Path", Path.Combine(DATAFOLDER, "a.bin"));
        long id;
        await using (var rd = await cmd.ExecuteReaderAsync())
        {
            Assert.That(await rd.ReadAsync(), Is.True, "The file is not in the restore list");
            id = rd.GetInt64(0);
            Assert.That(rd.GetString(1), Is.EqualTo(Path.Combine(RESTOREFOLDER, "a.bin")));
        }

        // A file that is renamed because its target exists keeps the new name
        var renamed = Path.Combine(RESTOREFOLDER, "a (1).bin");
        await db.UpdateTargetPathAsync(id, renamed, CancellationToken.None);
        cmd.Transaction = db.Transaction.Transaction;
        cmd.Parameters.Clear();
        cmd.CommandText = $@"SELECT ""TargetPath"" FROM ""{table}"" WHERE ""ID"" = {id}";
        Assert.That(await cmd.ExecuteScalarAsync(), Is.EqualTo(renamed));
    }

    // The same results as the UPDATE statements that set the stored target paths before
    [TestCase("/", "", "", false, false, "", "/home/u/a.txt", "/home/u/a.txt", TestName = "Original location, same OS")]
    [TestCase("\\", "", "", true, false, "", "C:\\data\\a.txt", "//C/data/a.txt", TestName = "Original location, Windows paths on Linux")]
    [TestCase("\\", "", "", true, false, "", "\\\\srv\\share\\a.txt", "///srv/share/a.txt", TestName = "Original location, UNC paths on Linux")]
    [TestCase("/", "", "", false, true, "C:/", "/home/u/a\\b.txt", "C:\\home\\u\\a_b.txt", TestName = "Original location, Linux paths on Windows")]
    [TestCase("\\", "", "D:\\r\\", false, true, "", "C:\\data\\a.txt", "D:\\r\\C\\data\\a.txt", TestName = "New folder, no common prefix, drive letters")]
    [TestCase("\\", "", "D:\\r\\", false, true, "", "\\\\srv\\share\\a.txt", "D:\\r\\srv\\share\\a.txt", TestName = "New folder, no common prefix, UNC paths")]
    [TestCase("\\", "C:\\data", "D:\\r\\", false, true, "", "C:\\data\\x\\a.txt", "D:\\r\\x\\a.txt", TestName = "New folder, common prefix, same OS")]
    [TestCase("\\", "C:\\data\\", "/r/", true, false, "", "C:\\data\\x\\a.txt", "/r/x/a.txt", TestName = "New folder, Windows paths on Linux")]
    [TestCase("/", "/home", "D:\\r\\", false, true, "", "/home/x/a\\b.txt", "D:\\r\\x\\a_b.txt", TestName = "New folder, Linux paths on Windows")]
    [TestCase("/", "/home", "/tmp/it's/", true, false, "", "/home/x/a.txt", "/tmp/it's/x/a.txt", TestName = "New folder with a quote in its name")]
    public void TheComputedTargetPathMatchesTheRestoreRules(string dirsep, string prefix, string destination, bool onUnix, bool onWindows, string windowsRoot, string path, string expected)
    {
        // The destination is given the directory separator of the OS that runs the restore
        if (!string.IsNullOrEmpty(destination) && (onUnix && OperatingSystem.IsWindows() || onWindows && !OperatingSystem.IsWindows()))
            Assert.Ignore("The destination is given the directory separator of the OS that runs the test");

        var expression = LocalRestoreDatabase.GetTargetPathExpression(dirsep, prefix, destination, onUnix, onWindows, windowsRoot);

        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"CREATE TABLE ""f"" (""Path"" TEXT NOT NULL)";
        cmd.ExecuteNonQuery();
        cmd.CommandText = @"INSERT INTO ""f"" (""Path"") VALUES (@Path)";
        cmd.Parameters.AddWithValue("@Path", path);
        cmd.ExecuteNonQuery();
        cmd.Parameters.Clear();
        cmd.CommandText = $@"SELECT {expression} FROM ""f""";

        Assert.That(cmd.ExecuteScalar(), Is.EqualTo(expected));
    }
}
