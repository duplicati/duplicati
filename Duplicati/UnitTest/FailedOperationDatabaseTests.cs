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
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// A failed operation writes its results to the database. For the first backup to a new
    /// database there is no earlier operation to write them against, and opening the database
    /// for that failed after the file had been opened, which left the file open until the
    /// process exited.
    /// </summary>
    public class FailedOperationDatabaseTests : BasicSetupHelper
    {
        /// <summary>
        /// Tells whether this process still has the file open.
        /// </summary>
        private static bool IsOpenInThisProcess(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                // SQLite opens the file without sharing it for deletion, so an exclusive open
                // fails while any connection has it
                try
                {
                    using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                    return false;
                }
                catch (IOException)
                {
                    return true;
                }
            }

            if (OperatingSystem.IsLinux())
            {
                var full = Path.GetFullPath(path);
                return Directory.GetFiles("/proc/self/fd").Any(fd =>
                {
                    try { return new FileInfo(fd).LinkTarget == full; }
                    catch (IOException) { return false; }
                    catch (UnauthorizedAccessException) { return false; }
                });
            }

            Assert.Ignore("No way to tell whether the file is open on this platform");
            return false;
        }

        [Test]
        [Category("Targeted")]
        public async Task AFailedFirstBackupDoesNotLeaveTheDatabaseOpen()
        {
            File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), [1, 2, 3]);
            Directory.Delete(TARGETFOLDER, true);
            Assert.IsFalse(File.Exists(DBFILE), "The database has to be new");

            // The destination is missing and not created, so the backup fails before it records
            // anything, and without retries so it fails at once
            var options = new Dictionary<string, string>(TestOptions)
            {
                ["disable-autocreate-folder"] = "true",
                ["number-of-retries"] = "0",
            };

            using (var c = new Controller("file://" + TARGETFOLDER, options, null))
                Assert.ThrowsAsync<FolderMissingException>(async () => await c.BackupAsync([DATAFOLDER]));

            Assert.IsTrue(File.Exists(DBFILE), "The backup did not create the database, so the test proves nothing");
            Assert.IsFalse(IsOpenInThisProcess(DBFILE), "The database is still open after the failed backup");
        }
    }
}
