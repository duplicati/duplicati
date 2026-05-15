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
using Duplicati.Library.Main.IPC.Dto;

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// RPC contract for controller operations.
/// This mirrors the IController interface but uses DTOs for data transfer.
/// </summary>
public interface IControllerRpc
{
    // Initialization
    Task InitializeAsync(string backendUrl, Dictionary<string, string> options);

    // Operation methods - all return Task for async RPC
    Task<BackupResultsDto> BackupAsync(string[] inputsources, FilterDto filter);
    Task<RestoreResultsDto> RestoreAsync(string[] paths, FilterDto filter);
    Task<ListResultsDto> ListAsync(IEnumerable<string> filterstrings, FilterDto filter);
    Task<DeleteResultsDto> DeleteAsync();
    Task<RepairResultsDto> RepairAsync(FilterDto filter);
    Task<CompactResultsDto> CompactAsync();
    Task<VacuumResultsDto> VacuumAsync();
    Task<TestResultsDto> TestAsync(long samples);
    Task<ListFilesetResultsDto> ListFilesetsAsync();
    Task<ListFolderResultsDto> ListFolderAsync(string[] folders, long offset, long limit, bool extendedData);
    Task<ListFileVersionsResultsDto> ListFileVersionsAsync(string[] files, long offset, long limit);
    Task<SearchFilesResultsDto> SearchEntriesAsync(string[] pathprefixes, FilterDto filter, long offset, long limit, bool extendedData);
    Task<CreateLogDatabaseResultsDto> CreateLogDatabaseAsync(string targetpath);
    Task<ListRemoteResultsDto> ListRemoteAsync();
    Task<ListRemoteResultsDto> DeleteAllRemoteFilesAsync();
    Task<SetLockResultsDto> SetLocksAsync();
    Task<ReadLockInfoResultsDto> ReadLockInfoAsync();
    Task<RecreateDatabaseResultsDto> UpdateDatabaseWithVersionsAsync(FilterDto filter);
    Task<PurgeFilesResultsDto> PurgeFilesAsync(FilterDto filter);
    Task<ListBrokenFilesResultsDto> ListBrokenFilesAsync(FilterDto filter);
    Task<PurgeBrokenFilesResultsDto> PurgeBrokenFilesAsync(FilterDto filter);
    Task<SendMailResultsDto> SendMailAsync();
    Task<SystemInfoResultsDto> SystemInfoAsync();
    Task<TestFilterResultsDto> TestFilterAsync(string[] paths, FilterDto filter);
    Task<ListChangesResultsDto> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string> filterstrings, FilterDto filter);
    Task<ListAffectedResultsDto> ListAffectedAsync(List<string> args);
    Task<RestoreControlFilesResultsDto> RestoreControlFilesAsync(IEnumerable<string> files, FilterDto filter);
    Task<ListResultsDto> ListControlFilesAsync(IEnumerable<string> filterstrings, FilterDto filter);

    // Control methods
    Task StopAsync();
    Task AbortAsync();
    Task PauseAsync(bool alsoTransfers);
    Task ResumeAsync();
    Task SetThrottleSpeedsAsync(long maxUploadPrSecond, long maxDownloadPrSecond);
    Task SetSecretProviderAsync(SecretProviderDto secretProvider);

    // Property setters
    Task SetLastCompactAsync(DateTime value);
    Task SetLastVacuumAsync(DateTime value);

    // Progress queries
    Task<BackendProgressDto> GetBackendProgressAsync();
    Task<OperationProgressDto> GetOperationProgressAsync();

    // Disposal
    Task DisposeAsync();
}

/// <summary>
/// Callback interface for server to call client (events)
/// </summary>
public interface IControllerRpcCallbacks
{
    // Log message forwarding
    Task OnLogMessageAsync(LogEntryDto entry);

    // Progress updates
    Task OnBackendEventAsync(BackendActionType action, BackendEventType type, string path, long size);
    Task OnBackendProgressAsync(BackendProgressDto progress);
    Task OnOperationProgressAsync(OperationProgressDto progress);
    Task OnPhaseChangedAsync(OperationPhase phase, OperationPhase previousPhase);

    // Operation lifecycle
    Task OnOperationStartedAsync(OperationMode operation);
    Task OnOperationCompletedAsync(OperationResultDto result, ExceptionDto exception);
}
