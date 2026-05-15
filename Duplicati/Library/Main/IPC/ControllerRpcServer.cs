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

#nullable enable

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Server-side implementation of the controller RPC interface.
/// The will be spawned in a separate process and call back to the server process.
/// </summary>
public class ControllerRpcServer : IControllerRpc, IDisposable
{
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<ControllerRpcServer>();

    private Controller? _controller;
    private readonly IControllerRpcCallbacks _callbacks;
    private CallbackMessageSink? _callbackSink;

    /// <summary>
    /// Creates a new controller RPC server
    /// </summary>
    public ControllerRpcServer(IControllerRpcCallbacks callbacks)
    {
        _callbacks = callbacks;
    }

    /// <inheritdoc />
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
                },

            OnOperationCompleted = (results, exception) =>
                {
                    var resultDto = OperationResultDto.FromResults(results);
                    var exceptionDto = exception != null ? ExceptionDto.FromException(exception) : null;
                    _callbacks?.OnOperationCompletedAsync(resultDto, exceptionDto).FireAndForget();
                }
        };

        return Task.CompletedTask;
    }

    private Controller Controller => _controller ?? throw new InvalidOperationException("Controller is not initialized");

    #region Operation Methods

    /// <inheritdoc />
    public async Task<BackupResultsDto> BackupAsync(string[] inputsources, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.BackupAsync(inputsources, filterObj).ConfigureAwait(false);
        return BackupResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<RestoreResultsDto> RestoreAsync(string[] paths, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.RestoreAsync(paths, filterObj).ConfigureAwait(false);
        return RestoreResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListResultsDto> ListAsync(IEnumerable<string> filterstrings, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.ListAsync(filterstrings, filterObj).ConfigureAwait(false);
        return ListResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<DeleteResultsDto> DeleteAsync()
    {
        var results = await Controller.DeleteAsync().ConfigureAwait(false);
        return DeleteResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<RepairResultsDto> RepairAsync(FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.RepairAsync(filterObj).ConfigureAwait(false);
        return RepairResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<CompactResultsDto> CompactAsync()
    {
        var results = await Controller.CompactAsync().ConfigureAwait(false);
        return CompactResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<VacuumResultsDto> VacuumAsync()
    {
        var results = await Controller.VacuumAsync().ConfigureAwait(false);
        return VacuumResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<TestResultsDto> TestAsync(long samples)
    {
        var results = await Controller.TestAsync(samples).ConfigureAwait(false);
        return TestResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListFilesetResultsDto> ListFilesetsAsync()
    {
        var results = await Controller.ListFilesetsAsync().ConfigureAwait(false);
        return ListFilesetResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListFolderResultsDto> ListFolderAsync(string[] folders, long offset, long limit, bool extendedData)
    {
        var results = await Controller.ListFolderAsync(folders, offset, limit, extendedData).ConfigureAwait(false);
        return ListFolderResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListFileVersionsResultsDto> ListFileVersionsAsync(string[] files, long offset, long limit)
    {
        var results = await Controller.ListFileVersionsAsync(files, offset, limit).ConfigureAwait(false);
        return ListFileVersionsResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<SearchFilesResultsDto> SearchEntriesAsync(string[] pathprefixes, FilterDto filter, long offset, long limit, bool extendedData)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.SearchEntriesAsync(pathprefixes, filterObj, offset, limit, extendedData).ConfigureAwait(false);
        return SearchFilesResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<CreateLogDatabaseResultsDto> CreateLogDatabaseAsync(string targetpath)
    {
        var results = await Controller.CreateLogDatabaseAsync(targetpath).ConfigureAwait(false);
        return CreateLogDatabaseResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListRemoteResultsDto> ListRemoteAsync()
    {
        var results = await Controller.ListRemoteAsync().ConfigureAwait(false);
        return ListRemoteResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListRemoteResultsDto> DeleteAllRemoteFilesAsync()
    {
        var results = await Controller.DeleteAllRemoteFilesAsync().ConfigureAwait(false);
        return ListRemoteResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<SetLockResultsDto> SetLocksAsync()
    {
        var results = await Controller.SetLocksAsync().ConfigureAwait(false);
        return SetLockResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ReadLockInfoResultsDto> ReadLockInfoAsync()
    {
        var results = await Controller.ReadLockInfoAsync().ConfigureAwait(false);
        return ReadLockInfoResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<RecreateDatabaseResultsDto> UpdateDatabaseWithVersionsAsync(FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.UpdateDatabaseWithVersionsAsync(filterObj).ConfigureAwait(false);
        return RecreateDatabaseResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<PurgeFilesResultsDto> PurgeFilesAsync(FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.PurgeFilesAsync(filterObj).ConfigureAwait(false);
        return PurgeFilesResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListBrokenFilesResultsDto> ListBrokenFilesAsync(FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.ListBrokenFilesAsync(filterObj).ConfigureAwait(false);
        return ListBrokenFilesResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<PurgeBrokenFilesResultsDto> PurgeBrokenFilesAsync(FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.PurgeBrokenFilesAsync(filterObj).ConfigureAwait(false);
        return PurgeBrokenFilesResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<SendMailResultsDto> SendMailAsync()
    {
        var results = await Controller.SendMailAsync().ConfigureAwait(false);
        return SendMailResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<SystemInfoResultsDto> SystemInfoAsync()
    {
        var results = await Controller.SystemInfoAsync().ConfigureAwait(false);
        return SystemInfoResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<TestFilterResultsDto> TestFilterAsync(string[] paths, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.TestFilterAsync(paths, filterObj).ConfigureAwait(false);
        return TestFilterResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListChangesResultsDto> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string> filterstrings, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.ListChangesAsync(baseVersion, targetVersion, filterstrings, filterObj).ConfigureAwait(false);
        return ListChangesResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListAffectedResultsDto> ListAffectedAsync(List<string> args)
    {
        var results = await Controller.ListAffectedAsync(args).ConfigureAwait(false);
        return ListAffectedResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<RestoreControlFilesResultsDto> RestoreControlFilesAsync(IEnumerable<string> files, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.RestoreControlFilesAsync(files, filterObj).ConfigureAwait(false);
        return RestoreControlFilesResultsDto.FromResults(results);
    }

    /// <inheritdoc />
    public async Task<ListResultsDto> ListControlFilesAsync(IEnumerable<string> filterstrings, FilterDto filter)
    {
        var filterObj = filter?.ToFilter();
        var results = await Controller.ListControlFilesAsync(filterstrings, filterObj).ConfigureAwait(false);
        return ListResultsDto.FromResults(results);
    }

    #endregion

    #region Control Methods

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
    public async Task SetSecretProviderAsync(SecretProviderDto secretProvider)
    {
        // The secret provider is set through the controller interface
        // For RPC, this may need special handling
        var provider = secretProvider?.ToProvider();
        if (_controller != null)
            await _controller.SetSecretProviderAsync(provider).ConfigureAwait(false);
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
    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<BackendProgressDto> GetBackendProgressAsync()
    {
        var progress = _callbackSink?.BackendProgress;
        return Task.FromResult(BackendProgressDto.FromProgress(progress)!);
    }

    /// <inheritdoc />
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

