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
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.IPC;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that verify IPC backup execution and callback forwarding.
/// </summary>
public class IPCTests : BasicSetupHelper
{
    /// <summary>
    /// A message sink that records all callbacks for verification.
    /// </summary>
    private class RecordingMessageSink : IMessageSink
    {
        public List<Duplicati.Library.Logging.LogEntry> LogMessages { get; } = new();
        public List<(BackendActionType action, BackendEventType type, string path, long size)> BackendEvents { get; } = new();
        public List<IBackendProgress> BackendProgressUpdates { get; } = new();
        public List<IOperationProgress> OperationProgressUpdates { get; } = new();
        public List<(OperationPhase phase, OperationPhase previousPhase)> PhaseChanges { get; } = new();

        public void WriteMessage(Duplicati.Library.Logging.LogEntry entry)
        {
            if (entry != null)
                LogMessages.Add(entry);
        }

        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            BackendEvents.Add((action, type, path, size));
        }

        public void SetBackendProgress(IBackendProgress progress)
        {
            if (progress != null)
                BackendProgressUpdates.Add(progress);
        }

        public void SetOperationProgress(IOperationProgress progress)
        {
            if (progress != null)
            {
                OperationProgressUpdates.Add(progress);
                progress.PhaseChanged += OnPhaseChanged;
            }
        }

        private void OnPhaseChanged(OperationPhase phase, OperationPhase previousPhase)
        {
            PhaseChanges.Add((phase, previousPhase));
        }
    }

    /// <summary>
    /// The proxy passes the filter as an object argument, so the serializer sees the
    /// runtime type. The filter must still reach the controller process intact.
    /// </summary>
    [Test]
    [Category("IPC")]
    public void FilterSurvivesRpcSerializationAsObjectArgument()
    {
        var filter = FilterExpression.Combine((IFilter)new FilterExpression("mp4", true), new FilterExpression("*.tmp", false))!;
        var json = System.Text.Json.JsonSerializer.Serialize(new object?[] { filter }, RpcJsonOptions.Options);
        var element = System.Text.Json.JsonDocument.Parse(json).RootElement[0];
        var roundtripped = System.Text.Json.JsonSerializer.Deserialize<IFilter>(element.GetRawText(), RpcJsonOptions.Options);

        Assert.IsNotNull(roundtripped);
        Assert.IsFalse(roundtripped!.Empty);
        CollectionAssert.AreEquivalent(FilterExpression.Serialize(filter), FilterExpression.Serialize(roundtripped));
    }

    [Test]
    [Category("IPC")]
    public async Task RunBackupViaIPC_ReceivesCallbacksAsync()
    {
        // Prepare test data
        var testFile = Path.Combine(DATAFOLDER, "testfile.txt");
        File.WriteAllText(testFile, "Hello IPC callback test");

        var options = new Dictionary<string, string?>(TestOptions);
        options["rpc-controller"] = "1";
        options["console-log-level"] = nameof(Duplicati.Library.Logging.LogMessageType.Information);
        options["machine-id"] = "unittestid";


        // Remove the flag so the controller doesn't see it
        options.Remove("rpc-controller");

        // If the controller executable is missing, skip the test
        var exePath = typeof(ControllerRpcProxy)
            .GetMethod("GetControllerExecutablePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null) as string;

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Assert.Ignore($"Controller executable not found at '{exePath}', skipping IPC test.");
            return;
        }

        var sink = new RecordingMessageSink();

        IController controller = await ControllerRpcProxy.CreateProxyAsync("file://" + TARGETFOLDER, options, sink);
        try
        {
            var backupResults = await controller.BackupAsync(new[] { DATAFOLDER });

            Assert.AreEqual(0, backupResults.Errors.Count(), "Backup should have no errors");

            // Verify that progress objects were set
            Assert.IsTrue(sink.BackendProgressUpdates.Count > 0, "Expected at least one backend progress update via IPC");
            Assert.IsTrue(sink.OperationProgressUpdates.Count > 0, "Expected at least one operation progress update via IPC");

            // Verify that phase changes occurred (Backup goes through multiple phases)
            Assert.IsTrue(sink.PhaseChanges.Count > 0, "Expected at least one phase change event via IPC");

            // Verify that backend events were forwarded (backup writes files to the backend)
            Assert.IsTrue(sink.BackendEvents.Count > 0, "Expected at least one backend event to be forwarded via IPC");

            // Verify that log messages were forwarded
            Assert.IsTrue(sink.LogMessages.Count > 0, "Expected at least one log message to be forwarded via IPC");

            // Verify that the final phase includes the Completed state
            var finalPhase = sink.PhaseChanges.Last().phase;
            Assert.IsTrue(
                finalPhase == OperationPhase.Backup_WaitForUpload || finalPhase == OperationPhase.Backup_Complete || finalPhase == OperationPhase.Backup_ProcessingFiles,
                $"Expected final phase to be a backup completion phase, but was {finalPhase}");
        }
        finally
        {
            (controller as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The controller process only pushes progress when it is first set and when the phase changes.
    /// The proxy reads it while the operation runs, so the progress within a phase is updated too.
    /// </summary>
    [Test]
    [Category("IPC")]
    public async Task RunBackupViaIPC_ProgressIsUpdatedWithinAPhaseAsync()
    {
        var rnd = new Random(11);
        var data = new byte[1024 * 1024];
        const int fileCount = 30;
        for (var i = 0; i < fileCount; i++)
        {
            rnd.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, $"file{i}.bin"), data);
        }

        var options = new Dictionary<string, string?>(TestOptions)
        {
            ["machine-id"] = "unittestid",
            ["dblock-size"] = "1MB",
            // Keeps the files being processed for a few seconds, as the uploads hold them back
            ["throttle-upload"] = "4MB",
        };

        var exePath = typeof(ControllerRpcProxy)
            .GetMethod("GetControllerExecutablePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null) as string;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Assert.Ignore($"Controller executable not found at '{exePath}', skipping IPC test.");
            return;
        }

        var sink = new RecordingMessageSink();
        var processedWhileProcessing = new ConcurrentDictionary<long, bool>();
        long maxBytesRead = 0;

        using var stop = new CancellationTokenSource();
        var poller = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                var progress = sink.OperationProgressUpdates.LastOrDefault();
                if (progress != null)
                {
                    progress.UpdateOverall(out var phase, out _, out var processed, out _, out _, out _, out _);
                    if (phase == OperationPhase.Backup_ProcessingFiles)
                        processedWhileProcessing[processed] = true;
                    maxBytesRead = Math.Max(maxBytesRead, progress.GetSourceBytesRead() ?? 0);
                }

                await Task.Delay(50);
            }
        });

        IController controller = await ControllerRpcProxy.CreateProxyAsync("file://" + TARGETFOLDER, options, sink);
        try
        {
            var backupResults = await controller.BackupAsync(new[] { DATAFOLDER });
            Assert.AreEqual(0, backupResults.Errors.Count(), "Backup should have no errors");
        }
        finally
        {
            stop.Cancel();
            await poller;
            (controller as IDisposable)?.Dispose();
        }

        var counts = processedWhileProcessing.Keys.OrderBy(x => x).ToList();
        TestContext.Progress.WriteLine($"Processed file counts seen while processing: {string.Join(", ", counts)}");
        Assert.That(counts.Count(x => x > 0 && x < fileCount), NUnit.Framework.Is.GreaterThan(0), "the processed file count was not updated while the files were processed");
        Assert.That(maxBytesRead, NUnit.Framework.Is.GreaterThan(0), "the bytes read were not passed on from the controller process");
    }
}
