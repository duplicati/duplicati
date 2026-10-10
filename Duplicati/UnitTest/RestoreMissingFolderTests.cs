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

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Restoring some of the files makes the folders they need, which are not restored themselves.
/// Making them is expected, so it should not be a warning (issue #5853). The folders that
/// are restored are made before any file, and a warning is given if one cannot be made.
/// </summary>
public class RestoreMissingFolderTests : BasicSetupHelper
{
    [TestCase(false)]
    [TestCase(true)]
    [Category("RestoreHandler")]
    public async Task RestoringSomeFilesMakesTheirFoldersWithoutWarnings(bool legacy)
    {
        var a = Path.Combine(DATAFOLDER, "a");
        Directory.CreateDirectory(Path.Combine(a, "b"));
        Directory.CreateDirectory(Path.Combine(a, "c"));
        var f = Path.Combine(a, "b", "f.txt");
        var g = Path.Combine(a, "c", "g.txt");
        File.WriteAllText(f, "f");
        File.WriteAllText(g, "g");
        File.WriteAllText(Path.Combine(a, "b", "not-restored.txt"), "x");

        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // Only the two files: their folders b and c are not in the restore set
        var options = new Dictionary<string, string>(TestOptions)
        {
            ["restore-path"] = RESTOREFOLDER,
            ["restore-legacy"] = legacy ? "true" : "false"
        };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync([f, g]));

        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "b", "f.txt")), Is.EqualTo("f"));
        Assert.That(File.ReadAllText(Path.Combine(RESTOREFOLDER, "c", "g.txt")), Is.EqualTo("g"));
        Assert.That(File.Exists(Path.Combine(RESTOREFOLDER, "b", "not-restored.txt")), Is.False);
    }
}
