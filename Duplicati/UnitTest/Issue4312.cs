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
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace Duplicati.UnitTest;

public class Issue4312 : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public void ChangeTimestampShouldCreateExtraBackup()
    {
        var testopts = TestOptions;
        // Make sure we detect changes from metadata
        testopts["upload-unchanged-backups"] = "false";
        testopts["blocksize"] = "50kb";

        var data = new byte[1024 * 1024 * 10];
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            var r = c.Backup(new string[] { DATAFOLDER });
            Assert.AreEqual(0, r.Errors.Count());
            Assert.AreEqual(0, r.Warnings.Count());
            Assert.AreEqual(1, r.AddedFiles);
            var pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
            if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
                throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));

            System.Threading.Thread.Sleep(3000);

            r = c.Backup(new string[] { DATAFOLDER });
            Assert.AreEqual(0, r.Errors.Count());
            Assert.AreEqual(0, r.Warnings.Count());
            Assert.AreEqual(0, r.AddedFiles);
            Assert.AreEqual(0, r.ModifiedFiles);
            pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
            if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
                throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));

            System.Threading.Thread.Sleep(3000);

            // Force a change in the file metadata            
            File.SetLastWriteTimeUtc(Path.Combine(DATAFOLDER, "a"), DateTime.Now);

            r = c.Backup(new string[] { DATAFOLDER });
            Assert.AreEqual(0, r.Errors.Count());
            Assert.AreEqual(0, r.Warnings.Count());
            Assert.AreEqual(0, r.AddedFiles);
            Assert.AreEqual(1, r.ModifiedFiles);
            pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
            if (pr.KnownFileSize == 0 || pr.KnownFileCount != 6 || pr.BackupListCount != 2)
                throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));
        }
    }

    [Test]
    [Category("Targeted")]
    public void BackupFromRecreatedDatabaseShouldUpdateMetadata()
    {
        var testopts = TestOptions;
        // Make sure we detect changes from metadata
        testopts["upload-unchanged-backups"] = "false";
        testopts["blocksize"] = "50kb";

        var data = new byte[1024 * 1024 * 10];
        File.WriteAllBytes(Path.Combine(DATAFOLDER, "a"), data);
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            var r = c.Backup(new string[] { DATAFOLDER });
            Assert.AreEqual(0, r.Errors.Count());
            Assert.AreEqual(0, r.Warnings.Count());
            Assert.AreEqual(1, r.AddedFiles);
            var pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
            if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
                throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));

            // Remove the database
            File.Delete(testopts["dbpath"]);

            // Get a second between the two backups
            Thread.Sleep(2000);

            // Recreate
            var rr = c.Repair();
            Assert.AreEqual(0, rr.Errors.Count());
            Assert.AreEqual(0, rr.Warnings.Count());

            // Because the timestamps are restored with lower precision
            // this will trigger a rescan of the files
            r = c.Backup(new string[] { DATAFOLDER });
            Assert.AreEqual(0, r.Errors.Count());
            Assert.AreEqual(0, r.Warnings.Count());
            Assert.AreEqual(0, r.AddedFiles);
            Assert.AreEqual(0, r.ModifiedFiles);
            Assert.AreEqual(1, r.OpenedFiles);
            pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
            if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
                throw new Exception(string.Format("Looks like the metadata scan failed: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));

            // Make a backup again, and ensure that no files are opened
            r = c.Backup(new string[] { DATAFOLDER });
            Assert.AreEqual(0, r.Errors.Count());
            Assert.AreEqual(0, r.Warnings.Count());
            Assert.AreEqual(0, r.AddedFiles);
            Assert.AreEqual(0, r.ModifiedFiles);
            Assert.AreEqual(0, r.OpenedFiles);
            pr = (Library.Interface.IParsedBackendStatistics)r.BackendStatistics;
            if (pr.KnownFileSize == 0 || pr.KnownFileCount != 3 || pr.BackupListCount != 1)
                throw new Exception(string.Format("Failed to get stats from remote backend: {0}, {1}, {2}", pr.KnownFileSize, pr.KnownFileCount, pr.BackupListCount));
        }

    }
}
