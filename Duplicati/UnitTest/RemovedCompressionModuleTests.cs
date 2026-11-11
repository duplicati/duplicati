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
using System.Threading.Tasks;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A destination can still hold volumes written with 7z, which was removed in 2.1.0.2
/// (forum topic 21698). Recreating the database from it must say what the 7z files are
/// and how to convert them, not only that a compression module failed to load.
/// </summary>
public class RemovedCompressionModuleTests : BasicSetupHelper
{
    private string LogFile => Path.Combine(BASEFOLDER, "removed-compression.log");

    private Dictionary<string, string> Options => TestOptions.Expand(new
    {
        no_encryption = true,
        log_file = LogFile,
        log_file_log_level = "Warning",
    });

    [Test]
    [Category("Targeted")]
    public async Task RecreateExplainsA7zVolumeAsync()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "a");
        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // The name decides the compression module, so a renamed zip dlist stands for a 7z one
        var dlist = Directory.GetFiles(TARGETFOLDER, "*.dlist.zip").Single();
        var sevenZip = Path.ChangeExtension(dlist, ".7z");
        File.Move(dlist, sevenZip);

        File.Delete(DBFILE);
        if (File.Exists(LogFile))
            File.Delete(LogFile);

        using (var c = new Library.Main.Controller("file://" + TARGETFOLDER, Options, null))
            Assert.CatchAsync(async () => await c.RepairAsync(), "The recreate should fail on the 7z volume");

        var log = File.ReadAllText(LogFile);
        Assert.That(log, Does.Contain(Path.GetFileName(sevenZip)), log);
        Assert.That(log, Does.Contain("is compressed with 7z, which Duplicati no longer reads"), log);
        Assert.That(log, Does.Contain("RecoveryTool in Duplicati 2.0.8.1"), log);
        Assert.That(File.Exists(sevenZip), Is.True, "The 7z volume must stay on the destination");
    }
}
