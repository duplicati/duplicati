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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
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
    private readonly IMessageSink _messageSink;
    private readonly string _backendUrl;
    private readonly Dictionary<string, string?> _options;

    private JsonRpc Rpc => _rpc ?? throw new InvalidOperationException("RPC connection not established");

    /// <inheritdoc />
    public Action<IBasicResults>? OnOperationStarted { get; set; }

    /// <inheritdoc />
    public Action<IBasicResults, Exception?>? OnOperationCompleted { get; set; }

    /// <inheritdoc />
    public Task SetLastCompactAsync(DateTime lastCompact)
        => Rpc.InvokeAsync(nameof(IController.SetLastCompactAsync), lastCompact).WaitAsync(TimeSpan.FromSeconds(5));

    /// <inheritdoc />
    public Task SetLastVacuumAsync(DateTime lastVacuum)
        => Rpc.InvokeAsync(nameof(IController.SetLastVacuumAsync), lastVacuum).WaitAsync(TimeSpan.FromSeconds(5));

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
    /// Starts running the remote controller process
    /// </summary>
    /// <returns>An awaitable task</returns>
    private async Task StartRemoteProcessAsync()
    {
        var exePath = GetControllerExecutablePath();

        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Controller executable not found: {exePath}");

        var psi = new ProcessStartInfo(exePath, "controller-server")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath)
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start controller process");

        // Setup StreamJsonRpc over stdio
        var formatter = new SystemTextJsonFormatter { JsonSerializerOptions = RpcJsonOptions.Options };
        var handler = new NewLineDelimitedMessageHandler(
            _process.StandardInput.BaseStream,
            _process.StandardOutput.BaseStream,
            formatter);

        _rpc = new JsonRpc(handler, this);
        _rpc.StartListening();

        // Start stderr reader for logging
        _ = Task.Run(ReadStderrAsync);

        // Initialize the remote controller
        await _rpc.InvokeAsync("InitializeAsync", _backendUrl, _options).ConfigureAwait(false);
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
    private async Task ReadStderrAsync()
    {
        try
        {
            if (_process?.StandardError == null) return;

            string? line;
            while ((line = await _process.StandardError.ReadLineAsync()) != null)
            {
                Library.Logging.Log.WriteInformationMessage(LOGTAG, "ControllerStderr", line);
            }
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "StderrReadError", ex, "Error reading controller stderr");
        }
    }

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
        => await Rpc.InvokeAsync<IBackupResults>(nameof(IController.BackupAsync), inputsources, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRestoreResults> RestoreAsync(string[]? paths, IFilter? filter = null)
        => await Rpc.InvokeAsync<IRestoreResults>(nameof(IController.RestoreAsync), paths, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public Task<IListResults> ListAsync()
        => ListAsync(null, null);

    /// <inheritdoc />
    public Task<IListResults> ListAsync(string? filterstring)
        => ListAsync(filterstring == null ? null : new[] { filterstring }, null);

    /// <inheritdoc />
    public async Task<IListResults> ListAsync(IEnumerable<string>? filterstrings, IFilter? filter)
        => await Rpc.InvokeAsync<IListResults>(nameof(IController.ListAsync), filterstrings, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IDeleteResults> DeleteAsync()
        => await Rpc.InvokeAsync<IDeleteResults>(nameof(IController.DeleteAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRepairResults> RepairAsync(IFilter? filter = null)
        => await Rpc.InvokeAsync<IRepairResults>(nameof(IController.RepairAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ICompactResults> CompactAsync()
        => await Rpc.InvokeAsync<ICompactResults>(nameof(IController.CompactAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IVacuumResults> VacuumAsync()
        => await Rpc.InvokeAsync<IVacuumResults>(nameof(IController.VacuumAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISyncResults> SyncAsync(string[] sources, IFilter? filter)
        => await Rpc.InvokeAsync<ISyncResults>(nameof(IController.SyncAsync), sources, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ITestResults> TestAsync(long samples = 1)
        => await Rpc.InvokeAsync<ITestResults>(nameof(IController.TestAsync), samples).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListFilesetResults> ListFilesetsAsync()
        => await Rpc.InvokeAsync<IListFilesetResults>(nameof(IController.ListFilesetsAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListFolderResults> ListFolderAsync(string[]? folders, long offset, long limit, bool extendedData)
        => await Rpc.InvokeAsync<IListFolderResults>(nameof(IController.ListFolderAsync), folders, offset, limit, extendedData).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListFileVersionsResults> ListFileVersionsAsync(string[]? files, long offset, long limit)
        => await Rpc.InvokeAsync<IListFileVersionsResults>(nameof(IController.ListFileVersionsAsync), files, offset, limit).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISearchFilesResults> SearchEntriesAsync(string[]? pathprefixes, IFilter? filter, long offset, long limit, bool extendedData)
        => await Rpc.InvokeAsync<ISearchFilesResults>(nameof(IController.SearchEntriesAsync), pathprefixes, filter, offset, limit, extendedData).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ICreateLogDatabaseResults> CreateLogDatabaseAsync(string targetpath)
        => await Rpc.InvokeAsync<ICreateLogDatabaseResults>(nameof(IController.CreateLogDatabaseAsync), targetpath).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListRemoteResults> ListRemoteAsync()
        => await Rpc.InvokeAsync<IListRemoteResults>(nameof(IController.ListRemoteAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListRemoteResults> DeleteAllRemoteFilesAsync()
        => await Rpc.InvokeAsync<IListRemoteResults>(nameof(IController.DeleteAllRemoteFilesAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISetLockResults> SetLocksAsync()
        => await Rpc.InvokeAsync<ISetLockResults>(nameof(IController.SetLocksAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadLockInfoResults> ReadLockInfoAsync()
        => await Rpc.InvokeAsync<IReadLockInfoResults>(nameof(IController.ReadLockInfoAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRecreateDatabaseResults> UpdateDatabaseWithVersionsAsync(IFilter? filter = null)
        => await Rpc.InvokeAsync<IRecreateDatabaseResults>(nameof(IController.UpdateDatabaseWithVersionsAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IPurgeFilesResults> PurgeFilesAsync(IFilter? filter)
        => await Rpc.InvokeAsync<IPurgeFilesResults>(nameof(IController.PurgeFilesAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListBrokenFilesResults> ListBrokenFilesAsync(IFilter? filter, Func<long, DateTime, long, string, long, bool>? callbackhandler = null)
    {
        // Note: callbacks for progress are not supported in the RPC model
        return await Rpc.InvokeAsync<IListBrokenFilesResults>(nameof(IController.ListBrokenFilesAsync), filter).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IPurgeBrokenFilesResults> PurgeBrokenFilesAsync(IFilter? filter)
        => await Rpc.InvokeAsync<IPurgeBrokenFilesResults>(nameof(IController.PurgeBrokenFilesAsync), filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISendMailResults> SendMailAsync()
        => await Rpc.InvokeAsync<ISendMailResults>(nameof(IController.SendMailAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ISystemInfoResults> SystemInfoAsync()
        => await Rpc.InvokeAsync<ISystemInfoResults>(nameof(IController.SystemInfoAsync)).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<ITestFilterResults> TestFilterAsync(string[] paths, IFilter? filter = null)
        => await Rpc.InvokeAsync<ITestFilterResults>(nameof(IController.TestFilterAsync), paths, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListChangesResults> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string>? filterstrings = null, IFilter? filter = null, Action<IListChangesResults, IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>>>? callback = null)
        => await Rpc.InvokeAsync<IListChangesResults>(nameof(IController.ListChangesAsync), baseVersion, targetVersion, filterstrings, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListAffectedResults> ListAffectedAsync(List<string>? args, Action<IListAffectedResults>? callback = null)
        => await Rpc.InvokeAsync<IListAffectedResults>(nameof(IController.ListAffectedAsync), args).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IRestoreControlFilesResults> RestoreControlFilesAsync(IEnumerable<string>? files = null, IFilter? filter = null)
        => await Rpc.InvokeAsync<IRestoreControlFilesResults>(nameof(IController.RestoreControlFilesAsync), files, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IListResults> ListControlFilesAsync(IEnumerable<string>? filterstrings, IFilter? filter)
        => await Rpc.InvokeAsync<IListResults>(nameof(IController.ListControlFilesAsync), filterstrings, filter).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task AbortAsync()
    {
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
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_rpc != null)
            await _rpc.InvokeAsync(nameof(IController.StopAsync)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PauseAsync(bool alsoTransfers)
    {
        if (_rpc != null)
            await _rpc.InvokeAsync(nameof(IController.PauseAsync), alsoTransfers).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeAsync()
    {
        if (_rpc != null)
            await _rpc.InvokeAsync(nameof(IController.ResumeAsync)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetThrottleSpeedsAsync(long maxUploadPrSecond, long maxDownloadPrSecond)
    {
        if (_rpc != null)
            await _rpc.InvokeAsync(nameof(IController.SetThrottleSpeedsAsync), maxUploadPrSecond, maxDownloadPrSecond).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetSecretProviderAsync(ISecretProvider? secretProvider)
    {
        if (_rpc != null)
            await _rpc.InvokeAsync(nameof(IController.SetSecretProviderAsync), secretProvider).ConfigureAwait(false);
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
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch { }

        _process?.Dispose();
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
        }

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
