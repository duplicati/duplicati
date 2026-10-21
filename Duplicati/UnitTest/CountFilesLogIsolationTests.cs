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
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// While a backup runs, a second pass goes through the same source paths to count the files for
/// the progress bar. Its log messages repeated every message of the backup's own pass, so each
/// warning about a path was given twice.
/// </summary>
public class CountFilesLogIsolationTests : BasicSetupHelper
{
    [Test]
    [Category("Backup")]
    public async Task EachPathIsLoggedOnceWhileTheFilesAreCounted()
    {
        var files = Enumerable.Range(0, 20).Select(i => Path.Combine(DATAFOLDER, $"file{i}.txt")).ToList();
        foreach (var f in files)
            File.WriteAllText(f, f);

        var messages = new List<string>();
        using (Log.StartScope(e => { lock (messages) messages.Add(e.FormattedMessage); }, e => e.Id == "IncludingPath"))
        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions) { ["disable-file-scanner"] = "false" }, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        foreach (var f in files)
            Assert.That(messages.Count(m => m.EndsWith(f)), Is.EqualTo(1), $"Messages about {f}");
    }

    [Test]
    [Category("Backup")]
    public async Task EachPathIsLoggedOnceWithoutTheCount()
    {
        var file = Path.Combine(DATAFOLDER, "file.txt");
        File.WriteAllText(file, file);

        var messages = new List<string>();
        using (Log.StartScope(e => { lock (messages) messages.Add(e.FormattedMessage); }, e => e.Id == "IncludingPath"))
        using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions) { ["disable-file-scanner"] = "true" }, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        Assert.That(messages.Count(m => m.EndsWith(file)), Is.EqualTo(1));
    }
}
