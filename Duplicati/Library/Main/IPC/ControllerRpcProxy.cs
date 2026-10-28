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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.IPC.Dto;
using Duplicati.Library.Utility;
using StreamJsonRpc;

#nullable enable

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Client-side proxy for the controller that runs in a separate spawned process
/// </summary>
public class ControllerRpcProxy : IController, IDisposable, IControllerRpcCallbacks
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<ControllerRpcProxy>();

    private Process? _process;
    private JsonRpc? _rpc;
    private NamedPipeServerStream? _pipe;
    private TraceListener? _traceListener;

    /// <summary>
    /// Set once an abort has been requested, so a connection lost to the killed
    /// process is reported as a cancellation rather than a failure
    /// </summary>
    private volatile bool _aborted;

    /// <summary>
    /// The number of console output lines from the controller process kept for error reports
    /// </summary>
    private const int OUTPUT_TAIL_LINES = 20;

    /// <summary>
    /// The last console output lines from the controller process
    /// </summary>
    private readonly ConcurrentQueue<string> _outputTail = new();

    /// <summary>
    /// The time a control call (stop, pause, resume, throttle) may take
    /// </summary>
    private static readonly TimeSpan ControlCallTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The process IDs of the controller processes that are currently running
    /// </summary>
    private static readonly ConcurrentDictionary<int, byte> _activeProcessIds = new();

    /// <summary>
    /// Gets the process IDs of the controller processes that are currently running
    /// </summary>
    public static IReadOnlyCollection<int> ActiveProcessIds => _activeProcessIds.Keys.ToArray();
    private readonly IMessageSink _messageSink;
    private readonly string _backendUrl;
    private readonly Dictionary<string, string?> _options;

    private JsonRpc Rpc => _rpc ?? throw new InvalidOperationException("RPC connection not established");

    /// <summary>
    /// Stops reading progress when the proxy is disposed
    /// </summary>
    private readonly CancellationTokenSource _progressPollCancellation = new();

    /// <inheritdoc />
    public Action<IBasicResults>? OnOperationStarted { get; set; }

    /// <inheritdoc />
    public Action<IBasicResults, Exception?>? OnOperationCompleted { get; set; }

    /// <inheritdoc />
    public Task SetLastCompactAsync(DateTime lastCompact)
        => InvokeAsync(nameof(IController.SetLastCompactAsync), lastCompact).WaitAsync(TimeSpan.FromSeconds(5));

    /// <inheritdoc />
    public Task SetLastVacuumAsync(DateTime lastVacuum)
        => InvokeAsync(nameof(IController.SetLastVacuumAsync), lastVacuum).WaitAsync(TimeSpan.FromSeconds(5));

    /// <inheritdoc />
    public Task SetLastRestoreTestAsync(DateTime lastRestoreTest)
        => InvokeAsync(nameof(IController.SetLastRestoreTestAsync), lastRestoreTest).WaitAsync(TimeSpan.FromSeconds(5));

    /// <summary>
    /// Creates a new controller proxy
    /// </summary>
    private ControllerRpcProxy(
        string backendUrl,
        Dictionary<string, string?> options,
        IMessageSink messageSink)
    {
        _backendUrl = backendUrl;
        _options = options;
        _messageSink = messageSink;
    }

    /// <summary>
    /// Creates a new controller proxy
    /// </summary>
    /// <param name="backendUrl">The backend URL</param>
    /// <param name="options">The options to pass</param>
    /// <param name="messageSink">The message sink to use</param>
    /// <returns>The configured proxy</returns>
    public static async Task<ControllerRpcProxy> CreateProxyAsync(string backendUrl, Dictionary<string, string?> options, IMessageSink messageSink)
    {
        var proxy = new ControllerRpcProxy(backendUrl, options, messageSink);
        try
        {
            await proxy.StartRemoteProcessAsync().ConfigureAwait(false);

            // For debugging purposes, we can also start a mocked remote process
            // await proxy.StartMockedRemoteProcessAsync().ConfigureAwait(false);

            _ = Task.Run(() => proxy.PollProgressAsync(proxy._progressPollCancellation.Token));
        }
        catch
        {
            try { proxy.Dispose(); }
            catch { }

            throw;
        }
        return proxy;
    }

    /// <summary>
    /// Reads the progress from the controller process while it runs, and passes it on
    /// the same way as the progress the controller pushes
    /// </summary>
    /// <param name="token">The token that stops reading</param>
    /// <returns>An awaitable task</returns>
    private async Task PollProgressAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                // The controller only pushes progress when it is first set and when the phase
                // changes, so without reading it, the progress within a phase is not updated
                await Task.Delay(ProgressReporting.Interval, token).ConfigureAwait(false);

                var rpc = _rpc;
                if (rpc == null)
                    continue;

                var backend = await rpc.InvokeWithCancellationAsync<BackendProgressDto?>("GetBackendProgressAsync", null, token).ConfigureAwait(false);
                if (backend != null)
                    await OnBackendProgressAsync(backend).ConfigureAwait(false);

                var operation = await rpc.InvokeWithCancellationAsync<OperationProgressDto?>("GetOperationProgressAsync", null, token).ConfigureAwait(false);
                if (operation != null)
                    await OnOperationProgressAsync(operation).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is ConnectionLostException or ObjectDisposedException)
            {
                // The controller process has exited
                return;
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteVerboseMessage(LOGTAG, "ProgressPollFailed", ex, "Failed to read progress from the controller process");
            }
        }
    }

    /// <summary>
    /// Starts running the remote controller process
    /// </summary>
    /// <returns>An awaitable task</returns>
    private async Task StartRemoteProcessAsync()
    {
        var exePath = GetControllerExecutablePath();

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Controller executable not found: {exePath}");

        // The RPC channel is a named pipe (a Unix domain socket on Linux and macOS) that only
        // the two processes hold. The controller's stdout and stderr are plain log streams, so
        // output from the engine or from tools it starts cannot corrupt the RPC stream, and a
        // lingering grandchild that inherited the console cannot keep the channel open after
        // the controller has died. The pipe name and a one-time token go to the controller on
        // its stdin, and the controller must present the token as the first line on the pipe.
        var pipeName = ControllerPipeHandshake.CreatePipeName();
        var token = ControllerPipeHandshake.CreateToken();
        var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _pipe = pipe;

        var psi = new ProcessStartInfo(exePath, ControllerPipeHandshake.COMMAND)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)
        };

        // Pass the data folder explicitly so the controller process uses the same
        // folder as this process; otherwise a portable-mode default or a different
        // entry assembly location would make it resolve (and probe the machine id
        // from) a different folder. The option-based environment variable is used
        // because it takes precedence over the portable-mode default.
        var serverDatafolderEnvName = $"{Library.AutoUpdater.AutoUpdateSettings.AppName}__{Library.AutoUpdater.DataFolderManager.SERVER_DATAFOLDER_OPTION.Replace('-', '_')}".ToUpperInvariant();
        psi.Environment[serverDatafolderEnvName] = Library.AutoUpdater.DataFolderManager.GetDataFolder(Library.AutoUpdater.DataFolderManager.AccessMode.ProbeOnly);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += OnProcessExited;
        if (!process.Start())
            throw new InvalidOperationException("Failed to start controller process");
        _process = process;
        _activeProcessIds.TryAdd(process.Id, 0);

        // Forward the controller's console output to the log
        _ = Task.Run(() => ForwardOutputAsync(process.StandardOutput, "ControllerStdout"));
        _ = Task.Run(() => ForwardOutputAsync(process.StandardError, "ControllerStderr"));

        ControllerPipeHandshake.WriteHandshake(process.StandardInput, pipeName, token);
        process.StandardInput.Close();

        using var timeout = new CancellationTokenSource(ControllerPipeHandshake.ConnectTimeout);
        var connectTask = pipe.WaitForConnectionAsync(timeout.Token);
        var exitTask = process.WaitForExitAsync(timeout.Token);
        var first = await Task.WhenAny(connectTask, exitTask).ConfigureAwait(false);
        if (first == exitTask && exitTask.IsCompletedSuccessfully && !connectTask.IsCompletedSuccessfully)
            throw new UserInformationException($"The controller process exited with code {process.ExitCode} before connecting to the server.{OutputTail()}", "ControllerProcessExited");

        try
        {
            await connectTask.ConfigureAwait(false);
            var received = await ControllerPipeHandshake.ReadTokenAsync(pipe, timeout.Token).ConfigureAwait(false);
            if (!ControllerPipeHandshake.TokensMatch(token, received))
                throw new UserInformationException("The process that connected to the controller pipe did not present the expected token", "ControllerPipeTokenMismatch");
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new UserInformationException($"The controller process did not connect to the server within {ControllerPipeHandshake.ConnectTimeout.TotalSeconds} seconds.{OutputTail()}", "ControllerProcessConnectTimeout");
        }

        var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
        var handler = new NewLineDelimitedMessageHandler(pipe, pipe, formatter);

        _rpc = new JsonRpc(handler, this);
        _traceListener = RpcTracing.AttachServerListener(_rpc, $"controller process {process.Id} ({exePath})");
        _rpc.StartListening();

        // Initialize the remote controller
        await InvokeAsync(nameof(ControllerRpcServer.InitializeAsync), _backendUrl, _options).WaitAsync(ControllerPipeHandshake.ConnectTimeout).ConfigureAwait(false);
    }

    /// <summary>
    /// Called when the controller process exits. The connection cannot outlive the
    /// process, so any pending call is failed now instead of waiting for the pipe
    /// to report end of stream.
    /// </summary>
    private void OnProcessExited(object? sender, EventArgs e)
    {
        try { _rpc?.Dispose(); }
        catch { }
    }

    /// <summary>
    /// Invokes a method on the controller process, reporting a lost connection
    /// with the reason for it
    /// </summary>
    /// <typeparam name="T">The result type</typeparam>
    /// <param name="method">The method to invoke</param>
    /// <param name="arguments">The arguments to pass</param>
    /// <returns>The result</returns>
    private async Task<T> InvokeAsync<T>(string method, params object?[] arguments)
    {
        try
        {
            return await Rpc.InvokeAsync<T>(method, arguments).ConfigureAwait(false);
        }
        catch (ConnectionLostException ex)
        {
            throw ConnectionLost(method, ex);
        }
    }

    /// <summary>
    /// Invokes a method on the controller process, reporting a lost connection
    /// with the reason for it
    /// </summary>
    /// <param name="method">The method to invoke</param>
    /// <param name="arguments">The arguments to pass</param>
    /// <returns>An awaitable task</returns>
    private async Task InvokeAsync(string method, params object?[] arguments)
    {
        try
        {
            await Rpc.InvokeAsync(method, arguments).ConfigureAwait(false);
        }
        catch (ConnectionLostException ex)
        {
            throw ConnectionLost(method, ex);
        }
    }

    /// <summary>
    /// Creates the exception that reports a lost connection to the controller process
    /// </summary>
    /// <param name="method">The method that was being invoked</param>
    /// <param name="ex">The exception reported by the connection</param>
    private Exception ConnectionLost(string method, ConnectionLostException ex)
    {
        if (_aborted)
            return new OperationCanceledException($"The operation {method} was aborted", ex);

        var process = _process;
        var exitInfo = process != null && process.HasExited
            ? $" The controller process exited with code {process.ExitCode}."
            : "";

        return new UserInformationException($"Lost the connection to the controller process while running {method}.{exitInfo}{OutputTail()}", "ControllerConnectionLost", ex);
    }

    /// <summary>
    /// Forwards console output from the controller process to the log,
    /// keeping the last lines for error reports
    /// </summary>
    /// <param name="reader">The stdout or stderr of the controller process</param>
    /// <param name="id">The log message id to use</param>
    private async Task ForwardOutputAsync(StreamReader reader, string id)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
            {
                _outputTail.Enqueue(line);
                while (_outputTail.Count > OUTPUT_TAIL_LINES && _outputTail.TryDequeue(out _))
                { }

                Library.Logging.Log.WriteInformationMessage(LOGTAG, id, "{0}", line);
            }
        }
        catch (ObjectDisposedException)
        {
            // The process was disposed while the reader was waiting
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "OutputReadError", ex, "Error reading controller output");
        }
    }

    /// <summary>
    /// Returns the last console output lines from the controller process, for error reports
    /// </summary>
    private string OutputTail()
    {
        var lines = _outputTail.ToArray();
        return lines.Length == 0
            ? ""
            : $" Last output from the controller process:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}";
    }

#if DEBUG
    /// <summary>
    /// Starts a mocked remote process for testing purposes.
    /// The code lives in the the same process, but uses the RPC interface to communicate with the server.
    /// </summary>
    /// <returns>An awaitable task</returns>
    private async Task StartMockedRemoteProcessAsync()
    {
        var pipeName = $"dupl-{Guid.NewGuid():N}"[..9];
        var pipeServer = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        _ = Task.Run(async () =>
        {
            try
            {
                await pipeServer.WaitForConnectionAsync().ConfigureAwait(false);

                var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
                var handler = new NewLineDelimitedMessageHandler(
                    pipeServer,
                    pipeServer,
                    formatter);

                var server = new ControllerRpcServer(this);
                var rpc = new JsonRpc(handler, server);
                rpc.StartListening();

                try
                {
                    await rpc.Completion.ConfigureAwait(false);
                }
                catch { }
                finally
                {
                    await server.DisposeAsync().ConfigureAwait(false);
                    await pipeServer.DisposeAsync().ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "MockServerError", ex, "Mocked RPC server encountered an error");
            }
        });

        var pipeClient = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await pipeClient.ConnectAsync().ConfigureAwait(false);

        var clientFormatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
        var clientHandler = new NewLineDelimitedMessageHandler(
            pipeClient,
            pipeClient,
            clientFormatter);

        _rpc = new JsonRpc(clientHandler, this);
        _rpc.StartListening();

        // Initialize the remote controller
        await _rpc.InvokeAsync(nameof(ControllerRpcServer.InitializeAsync), _backendUrl, _options).ConfigureAwait(false);
    }
#endif
#if DEBUG
    private static string? GetControllerExecutablePath()
    {
        // Look in the source tree
        var baseDir = System.Reflection.Assembly.GetEntryAssembly()!.Location;
        // Go from ?/Executables/Project1/bin/Debug/net10/project-filename(.exe)
        // to ?/Executables/Duplicati.CommandLine/bin/Debug/Duplicati.CommandLine(.exe)

        var parts = baseDir.Split(Path.DirectorySeparatorChar);
        parts[parts.Length - 1] = "Duplicati.CommandLine";
        if (OperatingSystem.IsWindows())
            parts[parts.Length - 1] += ".exe";

        // Deal with ?/Duplicati/UnitTest/bin/Debug/net10/testhost.dll
        if (!string.Equals(parts[parts.Length - 6], "Executables", StringComparison.OrdinalIgnoreCase))
            parts[parts.Length - 6] = "Executables";
        parts[parts.Length - 5] = "Duplicati.CommandLine";
        var targetPath = string.Join(Path.DirectorySeparatorChar, parts);
        if (File.Exists(targetPath))
            return targetPath;

        return GetControllerExecutablePathInner();

    }
#else
    private static string GetControllerExecutablePath() => GetControllerExecutablePathInner();
#endif

    private static string GetControllerExecutablePathInner()
    {
        var installDir = Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR;
        var exeName = Library.AutoUpdater.PackageHelper.GetExecutableName(
            Library.AutoUpdater.PackageHelper.NamedExecutable.CommandLine);
        return Path.Combine(installDir, exeName);
    }

    #region IControllerRpcCallbacks Implementation

    /// <summary>
    /// Called by the RPC server when a log message is written
    /// </summary>
    public Task OnLogMessageAsync(LogEntryDto entry)
    {
        if (_messageSink != null && entry != null)
        {
            try
            {
                var logEntry = entry.ToLogEntry();
                _messageSink.WriteMessage(logEntry);
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "LogForwardError", ex, "Error forwarding log message");
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the RPC server for backend events
    /// </summary>
    public Task OnBackendEventAsync(BackendActionType action, BackendEventType type, string path, long size)
    {
        _messageSink?.BackendEvent(action, type, path, size);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the RPC server when an operation starts
    /// </summary>
    public Task OnOperationStartedAsync(OperationMode operation)
    {
        // Note: The actual result object is created locally in the remote process
        // For simplicity, we don't proxy the full callback mechanism
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the RPC server when an operation completes
    /// </summary>
    public Task OnOperationCompletedAsync(OperationResultDto result, ExceptionDto exception)
    {
        // The operation completion is handled by the return value of the RPC call
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the RPC server when backend progress updates
    /// </summary>
    public Task OnBackendProgressAsync(BackendProgressDto progress)
    {
        if (_messageSink != null && progress != null)
            _messageSink.SetBackendProgress(new BackendProgressWrapper(progress));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the RPC server when operation progress updates
    /// </summary>
    public Task OnOperationProgressAsync(OperationProgressDto progress)
    {
        if (_messageSink != null && progress != null)
        {
            if (_operationProgressWrapper == null)
            {
                _operationProgressWrapper = new OperationProgressWrapper();
                _messageSink.SetOperationProgress(_operationProgressWrapper);
            }
            _operationProgressWrapper.UpdateFromDto(progress);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Called by the RPC server when the operation phase changes
    /// </summary>
    public Task OnPhaseChangedAsync(OperationPhase phase, OperationPhase previousPhase)
    {
        _operationProgressWrapper?.SetPhase(phase, previousPhase);
        return Task.CompletedTask;
    }

    #endregion

    #region IController Implementation

    /// <inheritdoc />
    public async Task<IBackupResults> BackupAsync(string[] inputsources, IFilter? filter = null)
        => await InvokeAsync<IBackupResults>(nameof(IController.BackupAsync), inputsources, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRestoreResults> RestoreAsync(string[]? paths, IFilter? filter = null)
        => await InvokeAsync<IRestoreResults>(nameof(IController.RestoreAsync), paths, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<IListResults> ListAsync()
        => ListAsync(null, null);

    /// <inheritdoc />
    public Task<IListResults> ListAsync(string? filterstring)
        => ListAsync(filterstring == null ? null : new[] { filterstring }, null);

    /// <inheritdoc />
    public async Task<IListResults> ListAsync(IEnumerable<string>? filterstrings, IFilter? filter)
        => await InvokeAsync<IListResults>(nameof(IController.ListAsync), filterstrings, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IDeleteResults> DeleteAsync()
        => await InvokeAsync<IDeleteResults>(nameof(IController.DeleteAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRepairResults> RepairAsync(IFilter? filter = null)
        => await InvokeAsync<IRepairResults>(nameof(IController.RepairAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ICompactResults> CompactAsync()
        => await InvokeAsync<ICompactResults>(nameof(IController.CompactAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IVacuumResults> VacuumAsync()
        => await InvokeAsync<IVacuumResults>(nameof(IController.VacuumAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISyncResults> SyncAsync(string[] sources, IFilter? filter)
        => await InvokeAsync<ISyncResults>(nameof(IController.SyncAsync), sources, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ITestResults> TestAsync(long samples = 1)
        => await InvokeAsync<ITestResults>(nameof(IController.TestAsync), samples).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRestoreTestResults> RestoreTestAsync(IFilter? filter = null)
        => await InvokeAsync<IRestoreTestResults>(nameof(IController.RestoreTestAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListFilesetResults> ListFilesetsAsync()
        => await InvokeAsync<IListFilesetResults>(nameof(IController.ListFilesetsAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListFolderResults> ListFolderAsync(string[]? folders, long offset, long limit, bool extendedData)
        => await InvokeAsync<IListFolderResults>(nameof(IController.ListFolderAsync), folders, offset, limit, extendedData).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListFileVersionsResults> ListFileVersionsAsync(string[]? files, long offset, long limit)
        => await InvokeAsync<IListFileVersionsResults>(nameof(IController.ListFileVersionsAsync), files, offset, limit).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISearchFilesResults> SearchEntriesAsync(string[]? pathprefixes, IFilter? filter, bool caseSensitive, long offset, long limit, bool returnExtendedData, bool searchMetadata)
        => await InvokeAsync<ISearchFilesResults>(nameof(IController.SearchEntriesAsync), pathprefixes, filter, caseSensitive, offset, limit, returnExtendedData, searchMetadata).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ICreateLogDatabaseResults> CreateLogDatabaseAsync(string targetpath)
        => await InvokeAsync<ICreateLogDatabaseResults>(nameof(IController.CreateLogDatabaseAsync), targetpath).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListRemoteResults> ListRemoteAsync()
        => await InvokeAsync<IListRemoteResults>(nameof(IController.ListRemoteAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListRemoteResults> DeleteAllRemoteFilesAsync()
        => await InvokeAsync<IListRemoteResults>(nameof(IController.DeleteAllRemoteFilesAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISetLockResults> SetLocksAsync()
        => await InvokeAsync<ISetLockResults>(nameof(IController.SetLocksAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISetVersionLabelResults> SetVersionLabelAsync()
        => await InvokeAsync<ISetVersionLabelResults>(nameof(IController.SetVersionLabelAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadLockInfoResults> ReadLockInfoAsync()
        => await InvokeAsync<IReadLockInfoResults>(nameof(IController.ReadLockInfoAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRecreateDatabaseResults> UpdateDatabaseWithVersionsAsync(IFilter? filter = null)
        => await InvokeAsync<IRecreateDatabaseResults>(nameof(IController.UpdateDatabaseWithVersionsAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IPurgeFilesResults> PurgeFilesAsync(IFilter? filter)
        => await InvokeAsync<IPurgeFilesResults>(nameof(IController.PurgeFilesAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListBrokenFilesResults> ListBrokenFilesAsync(IFilter? filter, Func<long, DateTime, long, string, long, bool>? callbackhandler = null)
    {
        // Note: callbacks for progress are not supported in the RPC model
        return await InvokeAsync<IListBrokenFilesResults>(nameof(IController.ListBrokenFilesAsync), filter).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IPurgeBrokenFilesResults> PurgeBrokenFilesAsync(IFilter? filter)
        => await InvokeAsync<IPurgeBrokenFilesResults>(nameof(IController.PurgeBrokenFilesAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISendMailResults> SendMailAsync()
        => await InvokeAsync<ISendMailResults>(nameof(IController.SendMailAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISystemInfoResults> SystemInfoAsync()
        => await InvokeAsync<ISystemInfoResults>(nameof(IController.SystemInfoAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ITestFilterResults> TestFilterAsync(string[] paths, IFilter? filter = null)
        => await InvokeAsync<ITestFilterResults>(nameof(IController.TestFilterAsync), paths, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListChangesResults> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string>? filterstrings = null, IFilter? filter = null, Action<IListChangesResults, IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>>>? callback = null)
        => await InvokeAsync<IListChangesResults>(nameof(IController.ListChangesAsync), baseVersion, targetVersion, filterstrings, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListAffectedResults> ListAffectedAsync(List<string>? args, Action<IListAffectedResults>? callback = null)
        => await InvokeAsync<IListAffectedResults>(nameof(IController.ListAffectedAsync), args).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRestoreControlFilesResults> RestoreControlFilesAsync(IEnumerable<string>? files = null, IFilter? filter = null)
        => await InvokeAsync<IRestoreControlFilesResults>(nameof(IController.RestoreControlFilesAsync), files, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListResults> ListControlFilesAsync(IEnumerable<string>? filterstrings, IFilter? filter)
        => await InvokeAsync<IListResults>(nameof(IController.ListControlFilesAsync), filterstrings, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AbortAsync()
    {
        _aborted = true;

        if (_rpc != null)
        {
            try
            {
                await _rpc.InvokeAsync(nameof(IController.AbortAsync)).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch
            {
                // Ignore RPC errors during abort
            }
        }

        // Force kill the process
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch
        {
            // Process may already be dead
        }

        // The connection cannot outlive the process; fail any pending call now
        // instead of waiting for the pipe to report end of stream
        try { _rpc?.Dispose(); }
        catch { }
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_rpc != null)
            await InvokeAsync(nameof(IController.StopAsync)).WaitAsync(ControlCallTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PauseAsync(bool alsoTransfers)
    {
        if (_rpc != null)
            await InvokeAsync(nameof(IController.PauseAsync), alsoTransfers).WaitAsync(ControlCallTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeAsync()
    {
        if (_rpc != null)
            await InvokeAsync(nameof(IController.ResumeAsync)).WaitAsync(ControlCallTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetThrottleSpeedsAsync(long maxUploadPrSecond, long maxDownloadPrSecond)
    {
        if (_rpc != null)
            await InvokeAsync(nameof(IController.SetThrottleSpeedsAsync), maxUploadPrSecond, maxDownloadPrSecond).WaitAsync(ControlCallTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetSecretProviderAsync(ISecretProvider? secretProvider)
    {
        if (_rpc != null)
            await InvokeAsync(nameof(IController.SetSecretProviderAsync), secretProvider).WaitAsync(ControlCallTimeout).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AppendSinkAsync(IMessageSink sink)
    {
        // In out-of-process mode, additional sinks cannot be added dynamically
        // This is a limitation of the RPC model
        Library.Logging.Log.WriteWarningMessage(LOGTAG, "AppendSinkNotSupported", null, "AppendSink is not supported in out-of-process mode");
        return Task.CompletedTask;
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        try { _progressPollCancellation.Cancel(); }
        catch { }

        var process = _process;
        if (process != null)
        {
            try { process.Exited -= OnProcessExited; }
            catch { }
        }

        try
        {
            if (_rpc != null)
            {
                try
                {
                    _rpc.InvokeAsync("DisposeAsync").WaitAsync(TimeSpan.FromSeconds(5)).Await();
                }
                catch
                {
                    // Ignore disposal errors
                }
                _rpc.Dispose();
            }
        }
        catch { }

        try
        {
            _traceListener?.Flush();
            _traceListener?.Dispose();
            _traceListener = null;
        }
        catch { }

        try
        {
            _pipe?.Dispose();
            _pipe = null;
        }
        catch { }

        // Closing the connection makes the controller exit; kill it if it does not
        try
        {
            if (process != null && !process.WaitForExit(2000))
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch { }

        if (process != null)
        {
            try { _activeProcessIds.TryRemove(process.Id, out _); }
            catch { }
        }

        process?.Dispose();
        _progressPollCancellation.Dispose();
    }

    #endregion

    private OperationProgressWrapper? _operationProgressWrapper;

    /// <summary>
    /// Wrapper for backend progress DTO that implements IBackendProgress
    /// </summary>
    private class BackendProgressWrapper : IBackendProgress
    {
        private readonly BackendProgressDto _dto;
        public BackendProgressWrapper(BackendProgressDto dto) => _dto = dto;
        public BackendActionProgress[] GetActiveTransfers()
            => _dto.Transfers?.Select(t => new BackendActionProgress(t.Action, t.Path, t.Size, t.Progress, t.BytesPerSecond, t.IsBlocking)).ToArray() ?? [];
    }

    /// <summary>
    /// Wrapper for operation progress DTO that implements IOperationProgress
    /// </summary>
    private class OperationProgressWrapper : IOperationProgress
    {
        private OperationPhase _phase;
        private float _progress;
        private string? _currentFilename;
        private long _currentFileSize;
        private long _currentFileOffset;
        private bool _currentFileComplete;
        private long _filesProcessed;
        private long _fileSizeProcessed;
        private long _fileCount;
        private long _fileSize;
        private bool _countingFiles;
        private int _remoteSyncDestinationIndex;
        private int _remoteSyncDestinationCount;
        private TimeSpan? _estimatedTimeToCompletion;
        private ActiveFileProgress[] _activeFiles = [];
        private long? _sourceBytesRead;

        public event PhaseChangedDelegate? PhaseChanged;

        public void UpdateFromDto(OperationProgressDto dto)
        {
            _phase = dto.Phase;
            _progress = dto.Progress;
            _filesProcessed = dto.FilesProcessed;
            _fileSizeProcessed = dto.FileSizeProcessed;
            _fileCount = dto.FileCount;
            _fileSize = dto.FileSize;
            _countingFiles = dto.CountingFiles;
            _currentFilename = dto.CurrentFilename;
            _currentFileSize = dto.CurrentFileSize;
            _currentFileOffset = dto.CurrentFileOffset;
            _currentFileComplete = dto.CurrentFileComplete;
            _remoteSyncDestinationIndex = dto.RemoteSyncDestinationIndex;
            _remoteSyncDestinationCount = dto.RemoteSyncDestinationCount;
            _estimatedTimeToCompletion = dto.EstimatedTimeToCompletion;
            _activeFiles = dto.ActiveFiles ?? [];
            _sourceBytesRead = dto.SourceBytesRead;
        }

        public ActiveFileProgress[] GetActiveFiles() => _activeFiles;

        public long? GetSourceBytesRead() => _sourceBytesRead;

        public TimeSpan? EstimatedTimeToCompletion => _estimatedTimeToCompletion;

        public void SetPhase(OperationPhase phase, OperationPhase previousPhase)
        {
            _phase = phase;
            PhaseChanged?.Invoke(phase, previousPhase);
        }

        public void UpdateOverall(out OperationPhase phase, out float progress, out long filesprocessed, out long filesizeprocessed, out long filecount, out long filesize, out bool countingfiles)
        {
            phase = _phase;
            progress = _progress;
            filesprocessed = _filesProcessed;
            filesizeprocessed = _fileSizeProcessed;
            filecount = _fileCount;
            filesize = _fileSize;
            countingfiles = _countingFiles;
        }

        public void UpdateFile(out string filename, out long filesize, out long fileoffset, out bool filecomplete)
        {
            filename = _currentFilename ?? string.Empty;
            filesize = _currentFileSize;
            fileoffset = _currentFileOffset;
            filecomplete = _currentFileComplete;
        }

        public void UpdateRemoteSyncDestination(out int destinationIndex, out int destinationCount)
        {
            destinationIndex = _remoteSyncDestinationIndex;
            destinationCount = _remoteSyncDestinationCount;
        }
    }
}
