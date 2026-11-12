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
using System.IO.Compression;
using System.Linq;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using System.Threading.Tasks;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class VacuumAndBugReportTests : BasicSetupHelper
    {
        [Test]
        [Category("Vacuum")]
        public async Task VacuumDatabaseAsync()
        {
            var opts = new Dictionary<string, string>(TestOptions);

            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE test (id INTEGER); INSERT INTO test(id) VALUES (1);";
                await cmd.ExecuteNonQueryAsync();
            }

            using (var c = new Controller("file://" + TARGETFOLDER, opts, null))
            {
                var res = await c.VacuumAsync();
                TestUtils.AssertResults(res);
                Assert.AreEqual(OperationMode.Vacuum, ((dynamic)res).MainOperation);
            }

            // Windows sometimes keeps the file handle open for a while after vacuuming
            if (OperatingSystem.IsWindows())
            {
                for (var i = 0; i < 10; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    try
                    {
                        File.Delete(DBFILE);
                        break;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(1000);
                    }
                }
            }
        }

        [Test]
        [Category("CreateBugReport")]
        public async Task CreateBugReportZipAsync([Values(true, false)] bool vacuum)
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "file.txt"), new byte[] { 1 });

            var opts = new Dictionary<string, string>(TestOptions).Expand(new
            {
                auto_vacuum = vacuum,
            });

            using (var c = new Controller("file://" + TARGETFOLDER, opts, null))
            {
                var backup = await c.BackupAsync(new[] { DATAFOLDER });
                TestUtils.AssertResults(backup);
            }
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            var basepath = Path.Combine(BASEFOLDER, Path.GetRandomFileName());
            var expected = basepath + ".zip";

            using (var c = new Controller("file://" + TARGETFOLDER, opts, null))
            {
                var res = await c.CreateLogDatabaseAsync(basepath);
                TestUtils.AssertResults(res);

                Assert.AreEqual(expected, res.TargetPath);
                Assert.IsTrue(File.Exists(expected));

                using var archive = new ZipArchive(File.OpenRead(expected), ZipArchiveMode.Read);
                var names = archive.Entries.Select(e => e.FullName).ToArray();
                CollectionAssert.Contains(names, "log-database.sqlite");
                CollectionAssert.Contains(names, "system-info.txt");
            }
            File.Delete(expected);
        }

        [Test]
        [Category("CreateBugReport")]
        public async Task BugReportKeepsTheLogsWithoutPathsAsync()
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "file.txt"), new byte[] { 1 });
            // A missing source gives a warning with its path, which has spaces in it
            var missing = Path.Combine(DATAFOLDER, "My Secret Plans", "salary list.txt");

            using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null))
            {
                var backup = await c.BackupAsync(new[] { DATAFOLDER, missing });
                Assert.That(backup.Warnings.Any(w => w.Contains("SourceIsMissing")), Is.True);
            }

            var basepath = Path.Combine(BASEFOLDER, Path.GetRandomFileName());
            using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null))
                TestUtils.AssertResults(await c.CreateLogDatabaseAsync(basepath));

            var dbcopy = Path.Combine(BASEFOLDER, Path.GetRandomFileName());
            using (var archive = ZipFile.OpenRead(basepath + ".zip"))
                archive.GetEntry("log-database.sqlite")!.ExtractToFile(dbcopy);

            var messages = new List<string>();
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbcopy};Pooling=false"))
            {
                await conn.OpenAsync();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"SELECT ""Message"" FROM ""LogData"" WHERE ""Type"" = 'Result'";
                using var rd = await cmd.ExecuteReaderAsync();
                while (await rd.ReadAsync())
                    messages.Add(rd.GetString(0));
            }

            Assert.That(messages, Is.Not.Empty);
            var result = messages.Single(m => m.Contains("SourceIsMissing"));
            var warning = Newtonsoft.Json.Linq.JObject.Parse(result)["Warnings"]!.Values<string>().Single(w => w!.Contains("SourceIsMissing"));
            Assert.That(warning, Does.Contain("-redacted-"));

            // The values are compared as written, as the JSON text escapes the backslashes of a path
            var values = messages
                .SelectMany(m => ((Newtonsoft.Json.Linq.JContainer)Newtonsoft.Json.Linq.JToken.Parse(m)).DescendantsAndSelf().OfType<Newtonsoft.Json.Linq.JValue>())
                .Where(v => v.Type == Newtonsoft.Json.Linq.JTokenType.String)
                .Select(v => (string)v.Value!)
                .ToList();
            foreach (var text in new[] { "Secret", "Plans", "salary", "list.txt", DATAFOLDER, BASEFOLDER })
                Assert.That(values, Has.None.Contains(text), text);

            File.Delete(dbcopy);
            File.Delete(basepath + ".zip");
        }

        [Test]
        [Category("CreateBugReport")]
        [TestCase("The operation Backup has started", "The operation Backup has started")]
        [TestCase("Excluding path due to file locked: C:\\Users\\me\\My Secret Plans\\a.txt", "Excluding path due to file locked: -redacted-")]
        [TestCase("Source is missing: /home/me/My Secret Plans/a.txt", "Source is missing: -redacted-")]
        [TestCase("Error\r\n   at A.B() in /src/My Code/x.cs:line 5\r\n   at C.D()", "Error\r\n   at A.B() in -redacted-\r\n   at C.D()")]
        [TestCase("Put - Started: duplicati-b1.dblock.zip (1.77 KiB)", "Put - Started: duplicati-b1.dblock.zip (1.77 KiB)")]
        [TestCase("Moved Secret Plans\\a.txt", "-redacted-")]
        [TestCase("Failed to read: Secret  Plans\\a.txt, retrying", "Failed to read: -redacted-")]
        [TestCase("Started: ssh://host/My Secret Folder/x ()", "Started: -redacted-")]
        [TestCase("   at A.B() in Secret Plans\\x.cs:line 5", "   -redacted-")]
        public void LogTextIsRedactedForABugReport(string text, string expected)
            => Assert.That(Library.Main.Database.Local.LocalBugReportDatabase.RedactLogText(text), Is.EqualTo(expected));

        [Test]
        [Category("CreateBugReport")]
        public void ALogMessageThatIsNotJsonIsRedactedAsText()
            => Assert.That(Library.Main.Database.Local.LocalBugReportDatabase.RedactLogMessage("{ not json C:\\a b\\c"), Is.EqualTo("{ not json -redacted-"));
    }
}
