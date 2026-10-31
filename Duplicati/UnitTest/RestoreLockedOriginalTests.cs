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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// With --restore-with-local-blocks, a restore reads the blocks it can from the file at the
/// original path, which is only a shortcut: every block can also come from the backup. When
/// another program had that file locked, as it can be while it is backed up through a snapshot,
/// the restore stopped on it with an error instead of downloading the blocks (#5573).
/// </summary>
public class RestoreLockedOriginalTests : BasicSetupHelper
{
    private string OriginalFile => Path.Combine(DATAFOLDER, "locked.bin");

    /// <summary>
    /// Backs up a file of several blocks, then restores it to another folder while the original
    /// is held as given
    /// </summary>
    /// <param name="hold">Opens the original as the other program would, and returns the handle</param>
    /// <returns>The content that was backed up</returns>
    private async Task<byte[]> RestoreWhileHeldAsync(Func<string, FileStream> hold)
    {
        var data = new byte[350 * 1024];
        new Random(1).NextBytes(data);
        File.WriteAllBytes(OriginalFile, data);

        var options = new Dictionary<string, string>(TestOptions) { ["blocksize"] = "100KB" };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        options["restore-path"] = RESTOREFOLDER;
        options["restore-with-local-blocks"] = "true";
        using (hold(OriginalFile))
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.RestoreAsync(null));

        return data;
    }

    [Test]
    [Category("Targeted")]
    public async Task AnOriginalOpenedExclusivelyIsRestoredFromTheBackupAsync()
    {
        var data = await RestoreWhileHeldAsync(path => new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        Assert.That(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "locked.bin")), Is.EqualTo(data));
    }

    [Test]
    [Category("Targeted")]
    public async Task AnOriginalWithALockedRangeIsRestoredFromTheBackupAsync()
    {
        // Only Windows enforces byte range locks on other readers
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Byte range locks are advisory outside Windows");

        var data = await RestoreWhileHeldAsync(path =>
        {
            // The second and third blocks, so the first one is read from the original
            var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            if (OperatingSystem.IsWindows())
                stream.Lock(150 * 1024, 100 * 1024);
            return stream;
        });
        Assert.That(File.ReadAllBytes(Path.Combine(RESTOREFOLDER, "locked.bin")), Is.EqualTo(data));
    }
}
