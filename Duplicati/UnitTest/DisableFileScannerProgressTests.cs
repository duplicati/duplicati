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
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// With --disable-file-scanner the files are not counted, and the progress takes the number
/// and size of the files from the previous backup instead (issue #6252). It read them from the
/// fileset of the backup that was just started, which is empty, so the progress showed
/// 0 files and -1 bytes, and the UI showed a negative number of files to go.
/// </summary>
public class DisableFileScannerProgressTests : BasicSetupHelper
{
    /// <summary>
    /// Keeps the progress object the backup reports to, so it can be read after the backup
    /// </summary>
    private sealed class ProgressSink : IMessageSink
    {
        public IOperationProgress? Progress { get; private set; }

        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size) { }
        public void SetBackendProgress(IBackendProgress progress) { }
        public void SetOperationProgress(IOperationProgress progress) => Progress = progress;
        public void WriteMessage(LogEntry entry) { }
    }

    /// <summary>
    /// Runs a backup without the file scanner, and returns the file count and size it showed
    /// while the files were processed. When the files are all processed, the backup sets them
    /// to what it examined, so they are read when the first file is listed.
    /// </summary>
    private async Task<(long Count, long Size)> BackupAndReadTotalsAsync()
    {
        var options = new Dictionary<string, string>(TestOptions) { ["disable-file-scanner"] = "true" };
        var sink = new ProgressSink();
        (long Count, long Size)? totals = null;
        using (var c = new Controller("file://" + TARGETFOLDER, options, sink))
        {
            c.OnOperationStarted = r =>
            {
#if DEBUG
                ((ITaskControlProvider)r).TaskControl.TestMethodCallback = path =>
                {
                    if (totals == null && sink.Progress != null)
                    {
                        sink.Progress.UpdateOverall(out _, out _, out _, out _, out var count, out var size, out _);
                        totals = (count, size);
                    }
                };
#endif
            };
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        }

#if !DEBUG
        Assert.Ignore("Reading the progress while the files are processed needs a Debug build");
#endif
        Assert.That(totals, Is.Not.Null, "the progress was not read while the files were processed");
        return totals!.Value;
    }

    [Test]
    [Category("Targeted")]
    public async Task TheTotalsComeFromThePreviousBackup()
    {
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), new string('a', 10));
        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), new string('b', 20));
        Directory.CreateDirectory(Path.Combine(DATAFOLDER, "sub"));
        File.WriteAllText(Path.Combine(DATAFOLDER, "sub", "c.txt"), new string('c', 30));

        var first = await BackupAndReadTotalsAsync();
        var second = await BackupAndReadTotalsAsync();

        Assert.Multiple(() =>
        {
            // There is no previous backup to take the totals from
            Assert.That(first, Is.EqualTo((0L, 0L)), "the first backup has no previous totals");
            // The previous backup holds three files of 60 bytes, the folders not counted
            Assert.That(second, Is.EqualTo((3L, 60L)), "the totals of the previous backup");
        });
    }
}
