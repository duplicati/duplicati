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

using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;

namespace Duplicati.UpdateHelper;

/// <summary>
/// Waits for the new instance to report that it has started, over a pipe or a marker file.
/// Reports are only accepted when <see cref="AcceptPid"/> approves the reporting process;
/// other reports are logged and ignored, and the listener keeps waiting.
/// </summary>
internal sealed class StartedListener : IDisposable
{
    private readonly HelperLog _log;
    private readonly string _markerFile;
    private readonly NamedPipeServerStream? _pipe;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task<bool> _pipeTask;

    /// <summary>
    /// The name of the pipe the listener accepts connections on
    /// </summary>
    public string PipeName { get; }

    /// <summary>
    /// Decides whether a started report from the given process id is accepted; accepts everything until set
    /// </summary>
    public Func<int, bool> AcceptPid { get; set; } = _ => true;

    public StartedListener(HelperLog log, string markerFolder)
    {
        _log = log;
        PipeName = UpdateHelperProtocol.HelperListenPipePrefix + Environment.ProcessId;
        _markerFile = Path.Combine(markerFolder, UpdateHelperProtocol.StartedMarkerPrefix + Environment.ProcessId + ".json");

        try
        {
            if (File.Exists(_markerFile))
                File.Delete(_markerFile);
        }
        catch { }

        try
        {
            _pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            _pipeTask = ListenAsync(_pipe, _cts.Token);
        }
        catch (Exception ex)
        {
            log.Write($"Could not open the started pipe {PipeName}, relying on the marker file: {ex.Message}");
            _pipeTask = Task.FromResult(false);
        }
    }

    private async Task<bool> ListenAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                try
                {
                    using var reader = new StreamReader(pipe, System.Text.Encoding.UTF8, false, 4096, leaveOpen: true);
                    var line = await reader.ReadLineAsync(token).ConfigureAwait(false);
                    if (line != null && Accept(line, "pipe") == true)
                        return true;
                }
                finally
                {
                    try { pipe.Disconnect(); } catch { }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.Write($"Error on the started pipe: {ex.Message}");
        }

        return false;
    }

    /// <summary>
    /// Parses a started report and checks it with <see cref="AcceptPid"/>
    /// </summary>
    /// <param name="line">The report line</param>
    /// <param name="source">Where the report came from, for the log</param>
    /// <returns>True if the report is accepted, false if it was rejected, null if it could not be parsed</returns>
    private bool? Accept(string line, string source)
    {
        HelperMessage? msg = null;
        try { msg = JsonSerializer.Deserialize(line, HelperJsonContext.Default.HelperMessage); }
        catch (JsonException) { }

        if (msg == null || msg.Type != UpdateHelperProtocol.TypeStarted)
        {
            _log.Write($"Unexpected message on the started {source}: {line}");
            return null;
        }

        if (!AcceptPid(msg.Pid))
        {
            _log.Write($"Ignoring a started report over the {source} from unrelated pid {msg.Pid}");
            return false;
        }

        _log.Write($"New instance reported started over the {source}, version {msg.Version}, pid {msg.Pid}");
        return true;
    }

    /// <summary>
    /// Waits for the started signal
    /// </summary>
    /// <param name="timeout">The maximum time to wait</param>
    /// <param name="abort">Returns true if waiting should stop early, e.g. because the spawned process died</param>
    /// <returns>True if the signal was received</returns>
    public bool Wait(TimeSpan timeout, Func<bool> abort)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_pipeTask.IsCompleted && _pipeTask.Result)
                return true;

            if (File.Exists(_markerFile))
            {
                string? line = null;
                try { line = File.ReadAllText(_markerFile).Trim(); } catch { }

                var accepted = string.IsNullOrEmpty(line) ? null : Accept(line, "marker file");
                if (accepted == true)
                    return true;

                // A marker from an unrelated process is removed so a later, legitimate one can be seen.
                // An unreadable or unparseable marker may still be being written, so it is read again on the next pass.
                if (accepted == false)
                    try { File.Delete(_markerFile); } catch { }
            }

            if (abort())
                return false;

            Thread.Sleep(500);
        }

        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
        try { if (File.Exists(_markerFile)) File.Delete(_markerFile); } catch { }
        _cts.Dispose();
    }
}
