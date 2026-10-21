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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.CommandLine.DatabaseTool;
using Duplicati.CommandLine.DatabaseTool.Commands;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// The server database lives in the data folder but is never referenced by
/// dbconfig.json or by its own Backup table. These tests check that verify and
/// cleanup do not report it as orphaned, so cleanup cannot delete it.
/// </summary>
[TestFixture]
[Category("DatabaseTool")]
public class DatabaseToolServerDatabaseTests
{
    /// <summary>
    /// Creates a server database with the current schema, as the server does
    /// on first start, and returns its path.
    /// </summary>
    /// <param name="datafolder">The data folder</param>
    /// <returns>The path to the server database</returns>
    private static async Task<string> CreateServerDatabaseAsync(string datafolder)
    {
        var serverDb = Path.Combine(datafolder, Library.AutoUpdater.DataFolderManager.SERVER_DATABASE_FILENAME);
        await using var con = await SQLiteLoader.LoadConnectionAsync(serverDb);
        DatabaseUpgrader.UpgradeDatabase(con, serverDb, typeof(DatabaseSchemaMarker));
        return serverDb;
    }

    [Test]
    public async Task ServerDatabaseIsNotOrphanedAsync()
    {
        using var tempFolder = new Library.Utility.TempFolder();
        var tempDir = (string)tempFolder;
        var serverDb = await CreateServerDatabaseAsync(tempDir);

        // The same call cleanup uses to pick the files it deletes
        var orphaned = await Verify.GetOrphanedDatabasesAsync(tempDir);

        var orphanedServerDbs = orphaned.Where(x => string.Equals(x.Path, Path.GetFullPath(serverDb), StringComparison.OrdinalIgnoreCase));
        Assert.That(orphanedServerDbs, Is.Empty, "the server database must not be reported as orphaned");
    }

    [Test]
    public async Task VerifyReportsServerDatabaseAsFoundAsync()
    {
        using var tempFolder = new Library.Utility.TempFolder();
        var tempDir = (string)tempFolder;
        var serverDb = await CreateServerDatabaseAsync(tempDir);

        var results = await Verify.AnalyzeDatabasesAsync(tempDir, includeServer: true);
        var entry = results.Single(x => string.Equals(x.Path, Path.GetFullPath(serverDb), StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual(DatabaseType.Server, entry.Type);
        Assert.AreEqual("Found", entry.Status);
    }

    [Test]
    public async Task CleanupForceKeepsServerDatabaseAsync()
    {
        using var tempFolder = new Library.Utility.TempFolder();
        var tempDir = (string)tempFolder;
        var serverDb = await CreateServerDatabaseAsync(tempDir);

        Assert.AreEqual(0, await Program.MainAsync(["cleanup", "--datafolder", tempDir, "--force"]));

        Assert.That(File.Exists(serverDb), Is.True, "cleanup --force must not delete the server database");
    }
}
