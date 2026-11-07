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

namespace Duplicati.UnitTest;

/// <summary>
/// A restore to another folder does not make links that point outside that folder. A folder
/// link was checked from inside itself, as its path ends in a separator, so a link one folder
/// further up than allowed was made.
/// </summary>
public class RestoreSymlinkOutsideTargetTests : BasicSetupHelper
{
    [Test]
    [Category("RestoreHandler")]
    public async Task LinksPointingOutsideTheRestoreFolderAreNotMadeAsync([Values] bool legacyRestore)
    {
        var source = Path.Combine(this.DATAFOLDER, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "kept.txt"), "kept");

        var fileLink = Path.Combine(source, "filelink");
        var folderLink = Path.Combine(source, "folderlink");
        try
        {
            File.CreateSymbolicLink(fileLink, Path.Combine("..", "outside.txt"));
            Directory.CreateSymbolicLink(folderLink, Path.Combine("..", "outside"));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            Assert.Ignore($"Symbolic links cannot be made here: {ex.Message}");
        }

        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([source]));

        var options = new Dictionary<string, string>(this.TestOptions)
        {
            ["restore-path"] = this.RESTOREFOLDER,
            ["restore-legacy"] = legacyRestore ? "true" : "false",
        };
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var results = await c.RestoreAsync(null);
            Assert.That(results.Errors, Is.Empty);
            Assert.That(results.Warnings.Count(x => x.Contains("SymlinkTargetOutside")), Is.EqualTo(2), string.Join(Environment.NewLine, results.Warnings));
        }

        Assert.That(File.ReadAllText(Path.Combine(this.RESTOREFOLDER, "kept.txt")), Is.EqualTo("kept"));
        Assert.That(new FileInfo(Path.Combine(this.RESTOREFOLDER, "filelink")).LinkTarget, Is.Null, "The file link was made");
        Assert.That(new DirectoryInfo(Path.Combine(this.RESTOREFOLDER, "folderlink")).LinkTarget, Is.Null, "The folder link was made");
    }
}
