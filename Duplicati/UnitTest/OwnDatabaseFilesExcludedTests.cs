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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A backup whose source holds its own local database must leave out the files SQLite keeps
/// next to it while the database is open. The database uses write-ahead logging, so those are
/// the "-wal" and "-shm" files; reading them while the backup writes to the database is the
/// problem the "-journal" exclusion was added for (issue #5603), and they are locked (#1757).
/// </summary>
public class OwnDatabaseFilesExcludedTests : BasicSetupHelper
{
    [Test]
    [Category("Utility")]
    public async Task ABackupLeavesOutTheWriteAheadLogFilesOfItsOwnDatabase()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "some data");
        var dbpath = Path.Combine(DATAFOLDER, "own-database.sqlite");
        var options = new Dictionary<string, string>(TestOptions) { ["dbpath"] = dbpath };

        IBackupResults backupResults;
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            backupResults = await c.BackupAsync(new[] { DATAFOLDER });

        var ownFiles = new[] { dbpath + "-wal", dbpath + "-shm" };
        var warnings = backupResults.Warnings.Where(w => ownFiles.Any(f => w.Contains(Path.GetFileName(f), StringComparison.OrdinalIgnoreCase))).ToList();
        Assert.That(warnings, Is.Empty, "The backup should not warn about the files of its own database");

        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(options) { ["version"] = "0" }, null))
        {
            var list = await c.ListAsync("*");
            var stored = list.Files.Select(f => f.Path).Where(p => ownFiles.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            Assert.That(stored, Is.Empty, "The files of the backup's own database should not be stored");
        }
    }
}
