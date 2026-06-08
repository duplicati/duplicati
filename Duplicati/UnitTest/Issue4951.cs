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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

public class Issue4951 : BasicSetupHelper
{
    [Test]
    [Category("USN")]
    public async Task RenameCaseChangeUSNAsync([Values(true, false)] bool useUSN)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("USN journal is only supported on Windows");

        var testopts = TestOptions;
        if (useUSN)
            testopts["usn-policy"] = "required";

        // Step 1: Create a directory and a file
        var dataFolder = Path.Combine(DATAFOLDER, "usn_case_test");
        if (Directory.Exists(dataFolder))
            Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        var originalFileName = "testFILE.txt";
        var renamedFileName = "testfile.txt";

        var filePath = Path.Combine(dataFolder, originalFileName);
        File.WriteAllText(filePath, "Some initial content");

        // Step 2: Initial backup
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            var res = await c.BackupAsync([dataFolder]);
            Assert.AreEqual(0, res.Errors.Count(), "Errors in initial backup");
            Assert.AreEqual(0, res.Warnings.Count(), $"Warnings in initial backup: {string.Join(Environment.NewLine, res.Warnings)}");
            Assert.AreEqual(1, res.AddedFiles, "Expected 1 added file");
        }

        // Step 3: Rename file by changing case
        var renamedFilePath = Path.Combine(dataFolder, renamedFileName);
        File.Move(filePath, renamedFilePath);

        // Step 4: Backup again
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, testopts, null))
        {
            var res = await c.BackupAsync([dataFolder]);
            Assert.AreEqual(0, res.Errors.Count(), "Errors in second backup");
            Assert.AreEqual(0, res.Warnings.Count(), $"Added:{res.AddedFiles}, Warnings in second backup: {string.Join(Environment.NewLine, res.Warnings)}");

            var list = await c.ListAsync("*");
            // Case sensitive compare to check we have the new file
            Assert.IsTrue(list.Files.Any(x => x.Path.EndsWith("testfile.txt")),
                $"Incorrect file list:{string.Join(", ", list.Files.Select(x => x.Path))}");

            // Rename is path delete+add
            Assert.IsTrue(
                res.AddedFiles == 1,
                $"Backup did not record the case change, added: {res.AddedFiles}, deleted: {res.DeletedFiles} , opened files {res.OpenedFiles}, modified files: {res.ModifiedFiles}, examinedFiles: {res.ExaminedFiles}."
            );
        }
    }
}
