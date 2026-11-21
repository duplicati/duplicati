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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A backup with a remote synchronization records the synchronization as a "Rsync N" row in the Operation table,
    /// after the backup operation. The result of the backup belongs to the backup operation, not to that row.
    /// </summary>
    public class RemoteSyncResultLogTests : BasicSetupHelper
    {
        [Test]
        [Category("RemoteSync")]
        public async Task TheResultOfABackupWithASyncBelongsToTheBackup_Async()
        {
            var syncTarget = Path.Combine(BASEFOLDER, "sync-target");
            Directory.CreateDirectory(syncTarget);
            var syncUrl = "file://" + syncTarget.Replace('\\', '/');

            var options = new Dictionary<string, string>(TestOptions)
            {
                ["no-encryption"] = "true",
                ["remote-sync-json-config"] = $@"{{""destinations"": [{{""url"": ""{syncUrl}""}}]}}"
            };
            File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "content");
            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]).ConfigureAwait(false));

            Assert.That(Directory.GetFiles(syncTarget), Is.Not.Empty, "The synchronization should have run");

            using var connection = await SQLiteLoader.LoadConnectionAsync(DBFILE).ConfigureAwait(false);
            using var command = connection.CreateCommand();

            command.CommandText = @"SELECT COUNT(*) FROM ""Operation"" WHERE ""Description"" = 'Rsync 0'";
            Assert.That(Convert.ToInt64(await command.ExecuteScalarAsync().ConfigureAwait(false)), Is.EqualTo(1), "The synchronization should be recorded");

            // The result is the last log entry of the run
            command.CommandText = @"
                SELECT ""O"".""Description""
                FROM ""LogData"" ""L""
                JOIN ""Operation"" ""O"" ON ""O"".""ID"" = ""L"".""OperationID""
                ORDER BY ""L"".""ID"" DESC
                LIMIT 1";
            Assert.That(await command.ExecuteScalarAsync().ConfigureAwait(false), Is.EqualTo("Backup"), "The result of the backup should belong to the backup operation");
        }
    }
}
