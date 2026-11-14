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

using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A file that was appended to after the backup holds every block of the backed up file,
/// so a restore that overwrites only has to cut it back. That changes the file, and the
/// restore results should count it as restored and not as unmodified.
/// </summary>
public class RestoreTruncatedFileCountTests : BasicSetupHelper
{
    /// <summary>The file that is backed up</summary>
    private string SourceFile => Path.Combine(this.DATAFOLDER, "file.txt");

    /// <summary>The content of the file when it is backed up</summary>
    private const string Content = "the content of the file that is backed up";

    /// <summary>
    /// Backs up the file and restores it over itself
    /// </summary>
    /// <param name="change">What happens to the file between the backup and the restore</param>
    /// <returns>The results of the restore.</returns>
    private async Task<IRestoreResults> BackupChangeAndRestoreAsync(System.Action change)
    {
        File.WriteAllText(SourceFile, Content);
        using (var c = new Controller("file://" + this.TARGETFOLDER, new Dictionary<string, string>(this.TestOptions), null))
            TestUtils.AssertResults(await c.BackupAsync([this.DATAFOLDER]));

        change();

        var options = new Dictionary<string, string>(this.TestOptions) { ["restore-legacy"] = "false", ["overwrite"] = "true" };
        using (var c = new Controller("file://" + this.TARGETFOLDER, options, null))
        {
            var r = await c.RestoreAsync([SourceFile]);
            Assert.That(r.Errors, Is.Empty, "the restore should not fail");
            return r;
        }
    }

    /// <summary>
    /// The file has grown, so the restore cuts it back and counts it as restored
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFileThatIsCutBackIsCountedAsRestored()
    {
        var r = await BackupChangeAndRestoreAsync(() => File.AppendAllText(SourceFile, " and what was added later"));

        Assert.That(File.ReadAllText(SourceFile), Is.EqualTo(Content), "the file should have been restored");
        Assert.That(r.RestoredFiles, Is.EqualTo(1), "the file was changed by the restore");
        Assert.That(r.UnmodifiedFiles, Is.EqualTo(0), "the file was not what the backup holds");
        Assert.That(r.SizeOfRestoredFiles, Is.EqualTo(Content.Length));
    }

    /// <summary>
    /// The file is as the backup holds it, so the restore leaves it and counts it as unmodified
    /// </summary>
    [Test]
    [Category("RestoreHandler")]
    public async Task AFileThatIsUnchangedIsCountedAsUnmodified()
    {
        var r = await BackupChangeAndRestoreAsync(() => { });

        Assert.That(File.ReadAllText(SourceFile), Is.EqualTo(Content));
        Assert.That(r.RestoredFiles, Is.EqualTo(0), "the restore had nothing to do");
        Assert.That(r.UnmodifiedFiles, Is.EqualTo(1));
    }
}
