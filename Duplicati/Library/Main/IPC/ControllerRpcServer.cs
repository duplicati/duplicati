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
using System.Threading.Tasks;
using CoCoL;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.IPC.Dto;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Server-side implementation of the controller RPC interface.
/// This will be spawned in a separate process and call back to the server process.
/// </summary>
public class ControllerRpcServer : IController, IDisposable
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<ControllerRpcServer>();

    private Controller? _controller;
    private readonly IControllerRpcCallbacks _callbacks;
    private CallbackMessageSink? _callbackSink;

    /// <summary>
    /// Gets or sets the callback for when an operation is started
    /// </summary>
    public Action<IBasicResults>? OnOperationStarted { get; set; }

    /// <summary>
    /// Gets or sets the callback for when an operation is completed
    /// </summary>
    public Action<IBasicResults, Exception>? OnOperationCompleted { get; set; }

    /// <summary>
    /// Creates a new controller RPC server
    /// </summary>
    public ControllerRpcServer(IControllerRpcCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <summary>
    /// Initializes the controller with the given backend URL and options
    /// </summary>
    public Task InitializeAsync(string backendUrl, Dictionary<string, string> options)
    {
        if (_controller != null)
            throw new InvalidOperationException("Controller is already initialized");

        _callbackSink = new CallbackMessageSink(_callbacks);
        _controller = new Controller(backendUrl, options, _callbackSink)
        {
            // Wire up events
            OnOperationStarted = (results) =>
                {
                    // Try to get the operation mode from the results
                    OperationMode operation = OperationMode.Backup;
                    if (results is IBackupResults)
                        operation = OperationMode.Backup;
                    else if (results is IRestoreResults)
                        operation = OperationMode.Restore;
                    else if (results is IListResults)
                        operation = OperationMode.List;
                    else if (results is IDeleteResults)
                        operation = OperationMode.Delete;
                    else if (results is IRepairResults)
                        operation = OperationMode.Repair;
                    else if (results is ICompactResults)
                        operation = OperationMode.Compact;
                    else if (results is IVacuumResults)
                        operation = OperationMode.Vacuum;
                    else if (results is ITestResults)
                        operation = OperationMode.Test;
                    else if (results is ICreateLogDatabaseResults)
                        operation = OperationMode.CreateLogDb;
                    else if (results is IListRemoteResults)
                        operation = OperationMode.ListRemote;
                    else if (results is ISetLockResults)
                        operation = OperationMode.SetLock;
                    else if (results is IReadLockInfoResults)
                        operation = OperationMode.ReadLockInfo;

                    _callbacks?.OnOperationStartedAsync(operation).FireAndForget();
                    OnOperationStarted?.Invoke(results);
                },

            OnOperationCompleted = (results, exception) =>
                {
                    var resultDto = OperationResultDto.FromResults(results);
                    var exceptionDto = exception != null ? ExceptionDto.FromException(exception) : null;
                    _callbacks?.OnOperationCompletedAsync(resultDto, exceptionDto).FireAndForget();
                    OnOperationCompleted?.Invoke(results, exception);
                }
        };

        return Task.CompletedTask;
    }

    private Controller Controller => _controller ?? throw new InvalidOperationException("Controller is not initialized");

    #region IController Implementation

    /// <inheritdoc />
    public Task<IBackupResults> BackupAsync(string[] inputsources, IFilter? filter = null)
        => Controller.BackupAsync(inputsources, filter);

    /// <inheritdoc />
    public Task<IRestoreResults> RestoreAsync(string[]? paths, IFilter? filter = null)
        => Controller.RestoreAsync(paths, filter);

    /// <inheritdoc />
    public Task<IListResults> ListAsync()
        => Controller.ListAsync();

    /// <inheritdoc />
    public Task<IListResults> ListAsync(string? filterstring)
        => Controller.ListAsync(filterstring);

    /// <inheritdoc />
    public Task<IListResults> ListAsync(IEnumerable<string>? filterstrings, IFilter? filter)
        => Controller.ListAsync(filterstrings, filter);

    /// <inheritdoc />
    public Task<IDeleteResults> DeleteAsync()
        => Controller.DeleteAsync();

    /// <inheritdoc />
    public Task<IRepairResults> RepairAsync(IFilter? filter = null)
        => Controller.RepairAsync(filter);

    /// <inheritdoc />
    public Task<ICompactResults> CompactAsync()
        => Controller.CompactAsync();

    /// <inheritdoc />
    public Task<IVacuumResults> VacuumAsync()
        => Controller.VacuumAsync();

    /// <inheritdoc />
    public Task<ITestResults> TestAsync(long samples = 1)
        => Controller.TestAsync(samples);

    /// <inheritdoc />
    public Task<IListFilesetResults> ListFilesetsAsync()
        => Controller.ListFilesetsAsync();

    /// <inheritdoc />
    public Task<IListFolderResults> ListFolderAsync(string[]? folders, long offset, long limit, bool extendedData)
        => Controller.ListFolderAsync(folders, offset, limit, extendedData);

    /// <inheritdoc />
    public Task<IListFileVersionsResults> ListFileVersionsAsync(string[]? files, long offset, long limit)
        => Controller.ListFileVersionsAsync(files, offset, limit);

    /// <inheritdoc />
    public Task<ISearchFilesResults> SearchEntriesAsync(string[]? pathprefixes, IFilter? filter, long offset, long limit, bool extendedData)
        => Controller.SearchEntriesAsync(pathprefixes, filter, offset, limit, extendedData);

    /// <inheritdoc />
    public Task<ICreateLogDatabaseResults> CreateLogDatabaseAsync(string targetpath)
        => Controller.CreateLogDatabaseAsync(targetpath);

    /// <inheritdoc />
    public Task<IListRemoteResults> ListRemoteAsync()
        => Controller.ListRemoteAsync();

    /// <inheritdoc />
    public Task<IListRemoteResults> DeleteAllRemoteFilesAsync()
        => Controller.DeleteAllRemoteFilesAsync();

    /// <inheritdoc />
    public Task<ISetLockResults> SetLocksAsync()
        => Controller.SetLocksAsync();

    /// <inheritdoc />
    public Task<IReadLockInfoResults> ReadLockInfoAsync()
        => Controller.ReadLockInfoAsync();

    /// <inheritdoc />
    public Task<IRecreateDatabaseResults> UpdateDatabaseWithVersionsAsync(IFilter? filter = null)
        => Controller.UpdateDatabaseWithVersionsAsync(filter);

    /// <inheritdoc />
    public Task<IPurgeFilesResults> PurgeFilesAsync(IFilter? filter)
        => Controller.PurgeFilesAsync(filter);

    /// <inheritdoc />
    public Task<IListBrokenFilesResults> ListBrokenFilesAsync(IFilter? filter, Func<long, DateTime, long, string, long, bool>? callbackhandler = null)
        => Controller.ListBrokenFilesAsync(filter, null);

    /// <inheritdoc />
    public Task<IPurgeBrokenFilesResults> PurgeBrokenFilesAsync(IFilter? filter)
        => Controller.PurgeBrokenFilesAsync(filter);

    /// <inheritdoc />
    public Task<ISendMailResults> SendMailAsync()
        => Controller.SendMailAsync();

    /// <inheritdoc />
    public Task<ISystemInfoResults> SystemInfoAsync()
        => Controller.SystemInfoAsync();

    /// <inheritdoc />
    public Task<ITestFilterResults> TestFilterAsync(string[] paths, IFilter? filter = null)
        => Controller.TestFilterAsync(paths, filter);

    /// <inheritdoc />
    public Task<IListChangesResults> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string>? filterstrings = null, IFilter? filter = null, Action<IListChangesResults, IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>>>? callback = null)
        => Controller.ListChangesAsync(baseVersion, targetVersion, filterstrings, filter, null);

    /// <inheritdoc />
    public Task<IListAffectedResults> ListAffectedAsync(List<string>? args, Action<IListAffectedResults>? callback = null)
        => Controller.ListAffectedAsync(args, null);

    /// <inheritdoc />
    public Task<IRestoreControlFilesResults> RestoreControlFilesAsync(IEnumerable<string>? files = null, IFilter? filter = null)
        => Controller.RestoreControlFilesAsync(files, filter);

    /// <inheritdoc />
    public Task<IListResults> ListControlFilesAsync(IEnumerable<string>? filterstrings, IFilter? filter)
        => Controller.ListControlFilesAsync(filterstrings, filter);

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_controller != null)
            await _controller.StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AbortAsync()
    {
        if (_controller != null)
            await _controller.AbortAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PauseAsync(bool alsoTransfers)
    {
        if (_controller != null)
            await _controller.PauseAsync(alsoTransfers).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ResumeAsync()
    {
        if (_controller != null)
            await _controller.ResumeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetThrottleSpeedsAsync(long maxUploadPrSecond, long maxDownloadPrSecond)
    {
        if (_controller != null)
            await _controller.SetThrottleSpeedsAsync(maxUploadPrSecond, maxDownloadPrSecond).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetSecretProviderAsync(ISecretProvider? secretProvider)
    {
        if (_controller != null)
            await _controller.SetSecretProviderAsync(secretProvider).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetLastCompactAsync(DateTime value)
    {
        if (_controller != null)
            await _controller.SetLastCompactAsync(value).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetLastVacuumAsync(DateTime value)
    {
        if (_controller != null)
            await _controller.SetLastVacuumAsync(value).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task AppendSinkAsync(IMessageSink sink)
    {
        // In out-of-process mode, additional sinks cannot be added dynamically
        Library.Logging.Log.WriteWarningMessage(LOGTAG, "AppendSinkNotSupported", null, "AppendSink is not supported in out-of-process mode");
        return Task.CompletedTask;
    }

    #endregion

    #region Progress Queries

    /// <summary>
    /// Gets the current backend progress
    /// </summary>
    public Task<BackendProgressDto> GetBackendProgressAsync()
    {
        var progress = _callbackSink?.BackendProgress;
        return Task.FromResult(BackendProgressDto.FromProgress(progress)!);
    }

    /// <summary>
    /// Gets the current operation progress
    /// </summary>
    public Task<OperationProgressDto> GetOperationProgressAsync()
    {
        var progress = _callbackSink?.OperationProgress;
        return Task.FromResult(OperationProgressDto.FromProgress(progress)!);
    }

    #endregion

    #region IDisposable

    /// <inheritdoc />
    public void Dispose()
    {
        _callbackSink?.Dispose();
        _callbackSink = null;
        _controller?.Dispose();
        _controller = null;
    }

    #endregion

    /// <summary>
    /// Disposes the server asynchronously
    /// </summary>
    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Message sink that forwards messages to the RPC callbacks
/// </summary>
public class CallbackMessageSink : IMessageSink, IDisposable
{
    private readonly IControllerRpcCallbacks _callbacks;

    /// <summary>
    /// The current backend progress
    /// </summary>
    public IBackendProgress? BackendProgress { get; private set; }

    /// <summary>
    /// The current operation progress
    /// </summary>
    public IOperationProgress? OperationProgress { get; private set; }

    public CallbackMessageSink(IControllerRpcCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <inheritdoc />
    public void WriteMessage(Logging.LogEntry entry)
    {
        if (_callbacks != null)
        {
            var dto = LogEntryDto.FromEntry(entry);
            _callbacks.OnLogMessageAsync(dto).FireAndForget();
        }
    }

    /// <inheritdoc />
    public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
    {
        _callbacks?.OnBackendEventAsync(action, type, path, size).FireAndForget();
    }

    /// <inheritdoc />
    public void SetBackendProgress(IBackendProgress progress)
    {
        BackendProgress = progress;
        _callbacks?.OnBackendProgressAsync(BackendProgressDto.FromProgress(progress)).FireAndForget();
    }

    /// <inheritdoc />
    public void SetOperationProgress(IOperationProgress progress)
    {
        if (OperationProgress != null)
            OperationProgress.PhaseChanged -= OnPhaseChanged;

        OperationProgress = progress;

        if (OperationProgress != null)
        {
            OperationProgress.PhaseChanged += OnPhaseChanged;
            _callbacks?.OnOperationProgressAsync(OperationProgressDto.FromProgress(progress)).FireAndForget();
        }
    }

    private void OnPhaseChanged(OperationPhase phase, OperationPhase previousPhase)
    {
        _callbacks?.OnPhaseChangedAsync(phase, previousPhase).FireAndForget();
        _callbacks?.OnOperationProgressAsync(OperationProgressDto.FromProgress(OperationProgress)).FireAndForget();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (OperationProgress != null)
        {
            OperationProgress.PhaseChanged -= OnPhaseChanged;
            OperationProgress = null;
        }
    }
}
