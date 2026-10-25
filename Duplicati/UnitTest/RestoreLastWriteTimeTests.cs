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
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A restored file and folder should get the last write time they had, whether that is before
/// or after the time they were created.
/// </summary>
[TestFixture]
public class RestoreLastWriteTimeTests : BasicSetupHelper
{
    [Test]
    [Category("Restore")]
    public async Task RestoredEntriesKeepTheirLastWriteTimeAsync(
        [Values(true, false)] bool writtenAfterCreation,
        [Values(true, false)] bool legacyRestore)
    {
        // A file edited after it was made has a last write time after its creation time, and a
        // file copied with its times kept has one before it
        var lastWrite = writtenAfterCreation
            ? new DateTime(2099, 1, 2, 3, 4, 5, DateTimeKind.Utc)
            : new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);

        var folder = Path.Combine(DATAFOLDER, "folder");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "file.txt");
        File.WriteAllText(file, "data");
        File.SetLastWriteTimeUtc(file, lastWrite);
        Directory.SetLastWriteTimeUtc(folder, lastWrite);

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var backupResults = await c.BackupAsync(new[] { DATAFOLDER });
            Assert.That(backupResults.Errors.Count(), Is.EqualTo(0));
            Assert.That(backupResults.Warnings.Count(), Is.EqualTo(0));
        }

        var restoreOptions = new Dictionary<string, string>(TestOptions)
        {
            ["restore-path"] = RESTOREFOLDER,
            ["restore-legacy"] = legacyRestore.ToString()
        };

        using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
        {
            var restoreResults = await c.RestoreAsync(null);
            Assert.That(restoreResults.Errors.Count(), Is.EqualTo(0), string.Join("; ", restoreResults.Errors));
            Assert.That(restoreResults.Warnings.Count(), Is.EqualTo(0), string.Join("; ", restoreResults.Warnings));
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.GetLastWriteTimeUtc(Path.Combine(RESTOREFOLDER, "folder", "file.txt")), Is.EqualTo(lastWrite),
                "the restored file did not get its last write time");
            Assert.That(Directory.GetLastWriteTimeUtc(Path.Combine(RESTOREFOLDER, "folder")), Is.EqualTo(lastWrite),
                "the restored folder did not get its last write time");
        });
    }
}
