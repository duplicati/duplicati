// Copyright (C) 2025, The Duplicati Team
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
using Duplicati.Library.Main;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class VacuumAndBugReportTests : BasicSetupHelper
    {
        [Test]
        [Category("Vacuum")]
        public void VacuumDatabase()
        {
            var opts = new Dictionary<string, string>(TestOptions);

            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DBFILE};Pooling=false"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "CREATE TABLE test (id INTEGER); INSERT INTO test(id) VALUES (1);";
                cmd.ExecuteNonQuery();
            }

            using (var c = new Controller("file://" + TARGETFOLDER, opts, null))
            {
                var res = c.Vacuum();
                TestUtils.AssertResults(res);
                Assert.AreEqual(OperationMode.Vacuum, ((dynamic)res).MainOperation);
            }
        }

        [Test]
        [Category("CreateBugReport")]
        public void CreateBugReportZip([Values(true, false)] bool vacuum)
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "file.txt"), new byte[] { 1 });

            var opts = new Dictionary<string, string>(TestOptions).Expand(new
            {
                auto_vacuum = vacuum,
            });

            using (var c = new Controller("file://" + TARGETFOLDER, opts, null))
            {
                var backup = c.Backup(new[] { DATAFOLDER });
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
                var res = c.CreateLogDatabase(basepath);
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
    }
}
