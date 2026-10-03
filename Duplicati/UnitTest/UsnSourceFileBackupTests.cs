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
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The case from issue #5175, with the USN journal: a source that is a single file, with hidden
/// and system files excluded. The second backup reads the journal, and the file was left out
/// when the drive root is hidden and a system folder, as it usually is. Reading the journal
/// needs an elevated process.
/// </summary>
public class UsnSourceFileBackupTests : BasicSetupHelper
{
    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
        => new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    [Test]
    [Category("USN")]
    [SupportedOSPlatform("windows")]
    public async Task ASourceFileIsKeptWhenHiddenAndSystemFilesAreExcluded()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The USN journal is Windows only");
        if (!IsElevated())
            Assert.Ignore("Reading the USN journal needs an elevated process");

        var root = Path.GetPathRoot(DATAFOLDER)!;
        var rootAttributes = File.GetAttributes(root);
        TestContext.Progress.WriteLine($"Drive root {root} has attributes {rootAttributes}");

        var folder = Path.Combine(DATAFOLDER, "usntest");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "A.txt");
        File.WriteAllText(file, "A");

        var options = new Dictionary<string, string>(TestOptions)
        {
            ["usn-policy"] = "required",
            ["snapshot-policy"] = "off",
            ["exclude-files-attributes"] = "Hidden,System"
        };

        // The first backup scans the source, as there is no journal data yet
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var res = await c.BackupAsync([file]);
            TestUtils.AssertResults(res);
            Assert.That(res.AddedFiles, Is.EqualTo(1), "the first backup should add the file");
        }

        File.WriteAllText(file, "AB");

        // The second backup takes the changed file from the journal
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var res = await c.BackupAsync([file]);
            TestUtils.AssertResults(res);
            Assert.That((res.ModifiedFiles, res.DeletedFiles), Is.EqualTo((1L, 0L)),
                $"the second backup should keep the changed file (drive root {root}: {rootAttributes})");

            var list = await c.ListAsync("*");
            Assert.That(list.Files.Select(f => f.Path), Has.Member(file), "the latest version should hold the file");
        }
    }
}
