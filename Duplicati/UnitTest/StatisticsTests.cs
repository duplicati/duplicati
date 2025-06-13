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
using System.Threading;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class StatisticsTests : BasicSetupHelper
    {
        [Test]
        [Category("Controller")]
        public void RunTest()
        {
            // Add a single file
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file1"), new byte[] { 0, 1, 2 });

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(1, backupResults.AddedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(1, backupResults.AddedFolders); // Root folder
                Assert.AreEqual(0, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            // Add a new file, and a new folder
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file2"), new byte[] { 0, 1, 2 });
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder1"));

            Thread.Sleep(1000);

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(1, backupResults.AddedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(1, backupResults.AddedFolders);
                Assert.AreEqual(1, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            Thread.Sleep(1000);

            // Modify the first file
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file1"), new byte[] { 0, 1, 2, 3 });
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(0, backupResults.AddedFiles);
                Assert.AreEqual(1, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(0, backupResults.AddedFolders);
                Assert.AreEqual(0, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            Thread.Sleep(1000);

            // Delete the second file
            File.Delete(Path.Combine(this.DATAFOLDER, "file2"));

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(0, backupResults.AddedFiles);
                Assert.AreEqual(1, backupResults.DeletedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.AddedFolders);
                Assert.AreEqual(1, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            Thread.Sleep(1000);

            // Modify the folder
            Directory.SetLastWriteTime(Path.Combine(this.DATAFOLDER, "folder1"), DateTime.Now.AddDays(1));

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(0, backupResults.AddedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(0, backupResults.AddedFolders);
                Assert.AreEqual(1, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            Thread.Sleep(1000);

            // Delete the folder
            Directory.Delete(Path.Combine(this.DATAFOLDER, "folder1"), true);
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(0, backupResults.AddedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(0, backupResults.AddedFolders);
                Assert.AreEqual(1, backupResults.ModifiedFolders);
                Assert.AreEqual(1, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            Thread.Sleep(1000);

            // Add two files and three folders
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file3"), new byte[] { 0, 1, 2 });
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "file4"), new byte[] { 0, 1, 2 });
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder2"));
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder3"));
            Directory.CreateDirectory(Path.Combine(this.DATAFOLDER, "folder4"));

            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(2, backupResults.AddedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(3, backupResults.AddedFolders);
                Assert.AreEqual(1, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }

            Thread.Sleep(1000);

            // Modify two folders
            Directory.SetLastWriteTime(Path.Combine(this.DATAFOLDER, "folder2"), DateTime.Now.AddDays(1));
            Directory.SetLastWriteTime(Path.Combine(this.DATAFOLDER, "folder3"), DateTime.Now.AddDays(2));
            using (Controller c = new Controller("file://" + this.TARGETFOLDER, TestOptions, null))
            {
                var backupResults = c.Backup(new[] { this.DATAFOLDER });
                TestUtils.AssertResults(backupResults);
                Assert.AreEqual(0, backupResults.AddedFiles);
                Assert.AreEqual(0, backupResults.ModifiedFiles);
                Assert.AreEqual(0, backupResults.DeletedFiles);
                Assert.AreEqual(0, backupResults.AddedFolders);
                Assert.AreEqual(2, backupResults.ModifiedFolders);
                Assert.AreEqual(0, backupResults.DeletedFolders);
                Assert.AreEqual(0, backupResults.AddedSymlinks);
                Assert.AreEqual(0, backupResults.ModifiedSymlinks);
                Assert.AreEqual(0, backupResults.DeletedSymlinks);
            }
        }
    }
}