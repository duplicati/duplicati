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
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Microsoft.Data.Sqlite;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A backup made on Windows and restored on Linux or macOS to its original location, without
/// --restore-path, is meant to be restored below a root folder named after the drive letter, so
/// C:\Data\file.txt goes to /C/Data/file.txt. The paths were instead restored relative to the
/// working folder, below a folder named "C:".
/// </summary>
public class RestoreWindowsPathsToOriginalLocationTests : BasicSetupHelper
{
    private sealed class LogSink : ILogDestination
    {
        public List<LogEntry> Entries { get; } = [];

        public void WriteMessage(LogEntry entry) => Entries.Add(entry);
    }

    /// <summary>
    /// Backs up the data folder, then rewrites the stored paths in the local database as if the
    /// backup was made on Windows, from C:\ followed by the data folder's path.
    /// </summary>
    private async Task BackupAsWindowsPathsAsync()
    {
        Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "sub"));
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sub", "file.txt"), "data");

        using (var c = new Controller("file://" + this.TARGETFOLDER, this.TestOptions, null))
        {
            var r = await c.BackupAsync([this.DATAFOLDER]);
            Assert.That(r.Errors, Is.Empty, "the backup should not fail");
        }

        using var db = new SqliteConnection($"Data Source={this.DBFILE};Pooling=False");
        await db.OpenAsync();
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"UPDATE ""PathPrefix"" SET ""Prefix"" = 'C:' || REPLACE(""Prefix"", '/', char(92))";
        await cmd.ExecuteNonQueryAsync();
    }

    [Test]
    [Category("RestoreHandler")]
    [TestCase(true)]
    [TestCase(false)]
    public async Task RestoresBelowTheDriveLetterFromTheRoot(bool legacy)
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Windows paths are mapped to a drive letter folder only when restoring on Linux or macOS");

        await BackupAsWindowsPathsAsync();

        // A dry run reports the folders it would create, without writing to the root. The
        // legacy restore checks the restored files even in a dry run, and there are none.
        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["dry-run"] = "true",
            ["restore-legacy"] = legacy ? "true" : "false",
            ["skip-restore-verification"] = "true",
        };

        var sink = new LogSink();
        using (Log.StartIsolatingScope(true))
        using (Log.StartScope(sink, LogMessageType.DryRun))
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.RestoreAsync(null);
            Assert.That(r.Errors, Is.Empty, "the restore should not fail");
        }

        var folders = sink.Entries
            .Where(e => e.Id == "WouldCreateFolder")
            .Select(e => e.FormattedMessage)
            .ToList();

        var expected = "/C" + Path.Combine(this.DATAFOLDER, "sub");
        Assert.That(folders, Is.Not.Empty, "the dry run should report the folders it would create");
        Assert.That(folders, Has.None.Contains("C:"), $"got: {string.Join(" | ", folders)}");
        Assert.That(folders.Select(f => Path.GetFullPath(f.Substring(f.IndexOf(':') + 1).Trim()).TrimEnd('/')), Has.Member(expected), $"got: {string.Join(" | ", folders)}");
    }
}
