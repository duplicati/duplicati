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
using System.Runtime.Versioning;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Snapshots;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The WMI snapshot provider used to be called Wmic, as it still is in the 2.4 stable releases, and
/// jobs saved with --snapshot-provider=Wmic still say so. The name was not known any more, so such
/// a job got a validation warning on every run and silently used the default provider instead.
/// </summary>
public class SnapshotProviderOldNameTests : BasicSetupHelper
{
    [TestCase("Wmic")]
    [TestCase("wmic")]
    [SupportedOSPlatform("windows")]
    public void TheOldWmicNameSelectsTheWmiProvider(string name)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The snapshot provider is a Windows option");

        var options = new Options(new Dictionary<string, string?> { ["snapshot-provider"] = name });

        Assert.That(options.SnapShotProvider, Is.EqualTo(WindowsSnapshotProvider.Wmi));
        // The snapshot is made from the raw options, so they must say Wmi as well
        Assert.That(options.RawOptions["snapshot-provider"], Is.EqualTo(nameof(WindowsSnapshotProvider.Wmi)));
    }

    [Test]
    public async Task ABackupWithTheOldWmicNameHasNoWarningAsync()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The snapshot provider is a Windows option");

        File.WriteAllText(Path.Combine(DATAFOLDER, "file.txt"), "data");
        var options = TestOptions.Expand(new
        {
            no_encryption = true,
            snapshot_policy = "off",
            snapshot_provider = "Wmic"
        });

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
    }
}
