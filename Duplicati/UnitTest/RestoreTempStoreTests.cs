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
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The temporary tables of a restore hold a row for every file and block to restore. In memory, as for
/// the other operations, they used gigabytes for a restore of millions of files (#7538), so
/// --restore-temp-tables-on-disk keeps them in a file instead.
/// </summary>
public class RestoreTempStoreTests : BasicSetupHelper
{
    private sealed class LogSink : ILogDestination
    {
        public List<LogEntry> Entries { get; } = [];

        public void WriteMessage(LogEntry entry) => Entries.Add(entry);
    }

    /// <summary>
    /// Reads where the temporary tables of the database are kept: 0 is the default of the build,
    /// 1 is a file and 2 is memory
    /// </summary>
    private static async Task<long> TempStoreAsync(LocalRestoreDatabase db)
    {
        await using var cmd = db.Connection.CreateCommand(db.Transaction.Transaction);
        cmd.CommandText = "PRAGMA temp_store";
        return await cmd.ExecuteScalarInt64Async(CancellationToken.None);
    }

    [Test]
    [Category("Targeted")]
    [TestCase(false, 2)]
    [TestCase(true, 1)]
    public async Task TheOptionChoosesWhereTheTemporaryTablesAreKeptAsync(bool onDisk, long expected)
    {
        using var dbfile = new TempFile();
        await using var db = await LocalRestoreDatabase.CreateAsync(dbfile, null, CancellationToken.None, onDisk);
        Assert.That(await TempStoreAsync(db), Is.EqualTo(expected));
    }

    [Test]
    [Category("Targeted")]
    [TestCase(false)]
    [TestCase(true)]
    public async Task ARestoreUsesTheOptionAsync(bool onDisk)
    {
        var data = new byte[3000];
        new Random(1).NextBytes(data);
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a.bin"), data);
        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        var sink = new LogSink();
        var options = TestOptions.Expand(new { restore_path = RESTOREFOLDER, restore_temp_tables_on_disk = onDisk });
        using (Log.StartIsolatingScope(true))
        using (Log.StartScope(sink, LogMessageType.Verbose))
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync(["*"]));

        Assert.That(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "a.bin")), Is.EqualTo(data), "The file was not restored");
        Assert.That(sink.Entries.Any(x => x.Id == "TempTablesOnDisk"), Is.EqualTo(onDisk), "The restore did not use the option as given");
    }
}
