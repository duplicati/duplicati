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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A backup reads several files in parallel. Besides the single current file, which is kept for
/// existing clients, the progress lists all the files being processed.
/// </summary>
public class ActiveFilesProgressTests : BasicSetupHelper
{
    /// <summary>
    /// Keeps the progress object the backup reports to, so it can be read while the backup runs
    /// </summary>
    private sealed class ProgressSink : IMessageSink
    {
        public volatile IOperationProgress? Progress;

        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size) { }
        public void SetBackendProgress(IBackendProgress progress) { }
        public void SetOperationProgress(IOperationProgress progress) => Progress = progress;
        public void WriteMessage(LogEntry entry) { }
    }

    [Test]
    [Category("Targeted")]
    public async Task ParallelFilesAreListedAndTheCurrentFileIsStillReported()
    {
        const int fileCount = 6;
        const int fileSize = 48 * 1024 * 1024;
        var rnd = new Random(42);
        var buffer = new byte[fileSize];
        var sourceFiles = new HashSet<string>();
        for (var i = 0; i < fileCount; i++)
        {
            rnd.NextBytes(buffer);
            var path = Path.Combine(DATAFOLDER, $"file{i}.bin");
            File.WriteAllBytes(path, buffer);
            sourceFiles.Add(path);
        }

        var options = new Dictionary<string, string>(TestOptions)
        {
            ["concurrency-block-hashers"] = "4",
            ["concurrency-fileprocessors"] = "4",
        };

        var sink = new ProgressSink();
        var maxActive = 0;
        var seenActive = new ConcurrentDictionary<string, bool>();
        var seenCurrent = new ConcurrentDictionary<string, bool>();
        var invalidOffsets = new ConcurrentBag<ActiveFileProgress>();

        using var stop = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var progress = sink.Progress;
                if (progress != null)
                {
                    var active = progress.GetActiveFiles();
                    maxActive = Math.Max(maxActive, active.Length);
                    foreach (var f in active)
                    {
                        seenActive[f.Path] = true;
                        if (f.Offset < 0 || f.Offset > f.Size)
                            invalidOffsets.Add(f);
                    }

                    progress.UpdateFile(out var filename, out _, out _, out _);
                    if (!string.IsNullOrEmpty(filename))
                        seenCurrent[filename] = true;
                }

                await Task.Delay(5);
            }
        });

        using (var c = new Controller("file://" + TARGETFOLDER, options, sink))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        stop.Cancel();
        await poller;

        Assert.Multiple(() =>
        {
            Assert.That(maxActive, Is.GreaterThan(1), "several files should be listed while they are read in parallel");
            Assert.That(seenActive.Keys, Is.SubsetOf(sourceFiles), "only source files are listed");
            Assert.That(invalidOffsets, Is.Empty, "the offsets are within the files");
            Assert.That(seenCurrent.Keys.Intersect(sourceFiles), Is.Not.Empty, "the single current file is still reported");
            Assert.That(sink.Progress!.GetActiveFiles(), Is.Empty, "the list is cleared when the files are processed");
        });
    }

    /// <summary>
    /// The bytes read leave out files that are skipped because they have not changed,
    /// which the processed size counts, so a read speed from it does not spike on unchanged files.
    /// </summary>
    [Test]
    [Category("Targeted")]
    public async Task SourceBytesReadLeavesOutUnchangedFiles()
    {
        var rnd = new Random(7);
        var total = 0L;
        for (var i = 0; i < 5; i++)
        {
            var data = new byte[200_000 + i * 1000];
            rnd.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file{i}.bin"), data);
            total += data.Length;
        }

        async Task<(long Processed, long? Read)> BackupAsync()
        {
            var sink = new ProgressSink();
            using (var c = new Controller("file://" + TARGETFOLDER, new Dictionary<string, string>(TestOptions), sink))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
            sink.Progress!.UpdateOverall(out _, out _, out _, out var processed, out _, out _, out _);
            return (processed, sink.Progress.GetSourceBytesRead());
        }

        var first = await BackupAsync();
        var second = await BackupAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Read, Is.EqualTo(total), "the first backup reads every file");
            Assert.That(second.Processed, Is.EqualTo(total), "unchanged files count as processed");
            Assert.That(second.Read, Is.EqualTo(0), "unchanged files are not read, and the count starts at zero");
        });
    }
}
