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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Issue6205 : BasicSetupHelper
    {
        /// <summary>
        /// https://github.com/duplicati/duplicati/issues/6205
        /// If all files are deleted from the remote destination and the user then runs repair
        /// (which recreates the local database), the recreate fails because the destination is
        /// empty. Previously the partial/empty database that was created was left on disk, which
        /// blocked every subsequent operation — a fresh backup could not run, and even retrying
        /// the repair failed because the recreate refuses to run when the database file exists.
        /// The failed recreate must remove the incomplete database so a fresh backup can proceed.
        /// </summary>
        [Test]
        [Category("Targeted")]
        public async Task RepairOnEmptyDestinationDoesNotLeavePartialDatabase()
        {
            var options = new Dictionary<string, string>(this.TestOptions)
            {
                ["no-encryption"] = "true"
            };

            File.WriteAllText(Path.Combine(this.DATAFOLDER, "a.txt"), "hello");
            File.WriteAllText(Path.Combine(this.DATAFOLDER, "b.txt"), "world");

            // Initial backup: creates the remote volumes and the local database.
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count());
            }
            Assert.IsTrue(File.Exists(options["dbpath"]), "The local database should exist after a backup");

            // Simulate "all files deleted on the remote": empty the destination and drop the
            // local database so the next repair goes through the recreate-from-remote path.
            File.Delete(options["dbpath"]);
            foreach (var f in Directory.GetFiles(this.TARGETFOLDER))
                File.Delete(f);

            // Repair now has to recreate the database from an empty destination, which fails.
            Exception repairFailure = null;
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                try
                {
                    await c.RepairAsync();
                }
                catch (Exception ex)
                {
                    repairFailure = ex;
                }
            }
            Assert.IsNotNull(repairFailure, "Repair against an empty destination is expected to fail");

            // The incomplete recreate database must NOT be left behind (this is the fix).
            Assert.IsFalse(File.Exists(options["dbpath"]),
                "The incomplete recreate database should have been removed after the failed repair");

            // With the leftover database gone, a fresh backup must be able to run again.
            using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
            {
                var backupResults = await c.BackupAsync([this.DATAFOLDER]);
                Assert.AreEqual(0, backupResults.Errors.Count(),
                    "A fresh backup should succeed after the failed repair cleaned up the partial database");
            }
        }
    }
}
