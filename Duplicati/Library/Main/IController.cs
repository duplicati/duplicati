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
using Duplicati.Library.Utility;
using Duplicati.Library.Interface;

#nullable enable

namespace Duplicati.Library.Main;

/// <summary>
/// Interface for the controller
/// </summary>
public interface IController : IDisposable
{
    /// <summary>
    /// Gets or sets the callback for when an operation is started
    /// </summary>
    Action<IBasicResults>? OnOperationStarted { get; set; }

    /// <summary>
    /// Gets or sets the callback for when an operation is completed
    /// </summary>
    Action<IBasicResults, Exception>? OnOperationCompleted { get; set; }

    /// <summary>
    /// Sets the time of the last compact operation
    /// </summary>
    /// <param name="lastCompact">The time of the last compact operation</param>
    Task SetLastCompactAsync(DateTime lastCompact);

    /// <summary>
    /// Sets the time of the last vacuum operation
    /// </summary>
    /// <param name="lastVacuum">The time of the last vacuum operation</param>
    Task SetLastVacuumAsync(DateTime lastVacuum);

    /// <summary>
    /// Aborts the current operation
    /// </summary>
    Task AbortAsync();

    /// <summary>
    /// Performs a backup operation
    /// </summary>
    /// <param name="inputsources">The sources to back up</param>
    /// <param name="filter">An optional filter to apply</param>
    /// <returns>The backup results</returns>
    Task<IBackupResults> BackupAsync(string[] inputsources, IFilter? filter = null);

    /// <summary>
    /// Performs a compact operation
    /// </summary>
    /// <returns>The compact results</returns>
    Task<ICompactResults> CompactAsync();

    /// <summary>
    /// Creates a log database at the specified path
    /// </summary>
    /// <param name="targetpath">The target path for the log database</param>
    /// <returns>The results of the log database creation</returns>
    Task<ICreateLogDatabaseResults> CreateLogDatabaseAsync(string targetpath);

    /// <summary>
    /// Deletes the backup data
    /// </summary>
    /// <returns>The delete results</returns>
    Task<IDeleteResults> DeleteAsync();

    /// <summary>
    /// Deletes all remote files
    /// </summary>
    /// <returns>The list of remote results</returns>
    Task<IListRemoteResults> DeleteAllRemoteFilesAsync();

    /// <summary>
    /// Lists the backup contents
    /// </summary>
    /// <returns>The list results</returns>
    Task<IListResults> ListAsync();

    /// <summary>
    /// Lists the backup contents filtered by a filter string
    /// </summary>
    /// <param name="filterstring">The filter string</param>
    /// <returns>The list results</returns>
    Task<IListResults> ListAsync(string? filterstring);

    /// <summary>
    /// Lists the backup contents filtered by multiple filter strings and a filter
    /// </summary>
    /// <param name="filterstrings">The filter strings</param>
    /// <param name="filter">The filter to apply</param>
    /// <returns>The list results</returns>
    Task<IListResults> ListAsync(IEnumerable<string>? filterstrings, IFilter? filter);

    /// <summary>
    /// Lists the files affected by the specified arguments
    /// </summary>
    /// <param name="args">The arguments to check</param>
    /// <param name="callback">An optional callback for progress updates</param>
    /// <returns>The list of affected results</returns>
    Task<IListAffectedResults> ListAffectedAsync(List<string>? args, Action<IListAffectedResults>? callback = null);

    /// <summary>
    /// Lists broken files in the backup
    /// </summary>
    /// <param name="filter">The filter to apply</param>
    /// <param name="callbackhandler">An optional callback handler for progress updates</param>
    /// <returns>The list of broken files results</returns>
    Task<IListBrokenFilesResults> ListBrokenFilesAsync(IFilter? filter, Func<long, DateTime, long, string, long, bool>? callbackhandler = null);

    /// <summary>
    /// Lists the changes between two versions
    /// </summary>
    /// <param name="baseVersion">The base version</param>
    /// <param name="targetVersion">The target version</param>
    /// <param name="filterstrings">Optional filter strings</param>
    /// <param name="filter">Optional filter to apply</param>
    /// <param name="callback">Optional callback for progress updates</param>
    /// <returns>The list changes results</returns>
    Task<IListChangesResults> ListChangesAsync(string baseVersion, string targetVersion, IEnumerable<string>? filterstrings = null, IFilter? filter = null, Action<IListChangesResults, IEnumerable<Tuple<ListChangesChangeType, ListChangesElementType, string>>>? callback = null);

    /// <summary>
    /// Lists control files in the backup
    /// </summary>
    /// <param name="filterstrings">The filter strings</param>
    /// <param name="filter">The filter to apply</param>
    /// <returns>The list results</returns>
    Task<IListResults> ListControlFilesAsync(IEnumerable<string>? filterstrings, IFilter? filter);

    /// <summary>
    /// Lists all filesets in the backup
    /// </summary>
    /// <returns>The fileset results</returns>
    Task<IListFilesetResults> ListFilesetsAsync();

    /// <summary>
    /// Lists file versions for the specified files
    /// </summary>
    /// <param name="files">The files to list versions for</param>
    /// <param name="offset">The offset to start listing from</param>
    /// <param name="limit">The maximum number of results to return</param>
    /// <returns>The file versions results</returns>
    Task<IListFileVersionsResults> ListFileVersionsAsync(string[]? files, long offset, long limit);

    /// <summary>
    /// Lists the contents of folders in the backup
    /// </summary>
    /// <param name="folders">The folders to list</param>
    /// <param name="offset">The offset to start listing from</param>
    /// <param name="limit">The maximum number of entries to list</param>
    /// <param name="extendedData">Whether to include extended data</param>
    /// <returns>The folder contents results</returns>
    Task<IListFolderResults> ListFolderAsync(string[]? folders, long offset, long limit, bool extendedData);

    /// <summary>
    /// Lists remote files in the backend
    /// </summary>
    /// <returns>The list of remote results</returns>
    Task<IListRemoteResults> ListRemoteAsync();

    /// <summary>
    /// Purges broken files from the backup
    /// </summary>
    /// <param name="filter">The filter to apply</param>
    /// <returns>The purge broken files results</returns>
    Task<IPurgeBrokenFilesResults> PurgeBrokenFilesAsync(IFilter? filter);

    /// <summary>
    /// Purges files from the backup
    /// </summary>
    /// <param name="filter">The filter to apply</param>
    /// <returns>The purge files results</returns>
    Task<IPurgeFilesResults> PurgeFilesAsync(IFilter? filter);

    /// <summary>
    /// Reads lock information
    /// </summary>
    /// <returns>The lock information results</returns>
    Task<IReadLockInfoResults> ReadLockInfoAsync();

    /// <summary>
    /// Repairs the backup
    /// </summary>
    /// <param name="filter">An optional filter to apply</param>
    /// <returns>The repair results</returns>
    Task<IRepairResults> RepairAsync(IFilter? filter = null);

    /// <summary>
    /// Restores files from the backup
    /// </summary>
    /// <param name="paths">The paths to restore</param>
    /// <param name="filter">An optional filter to apply</param>
    /// <returns>The restore results</returns>
    Task<IRestoreResults> RestoreAsync(string[]? paths, IFilter? filter = null);

    /// <summary>
    /// Restores control files from the backup
    /// </summary>
    /// <param name="files">The control files to restore</param>
    /// <param name="filter">An optional filter to apply</param>
    /// <returns>The restore control files results</returns>
    Task<IRestoreControlFilesResults> RestoreControlFilesAsync(IEnumerable<string>? files = null, IFilter? filter = null);



    /// <summary>
    /// Searches for entries in the backup
    /// </summary>
    /// <param name="pathprefixes">The path prefixes to search in</param>
    /// <param name="filter">The filter to apply</param>
    /// <param name="offset">The offset to start searching from</param>
    /// <param name="limit">The maximum number of results to return</param>
    /// <param name="extendedData">Whether to include extended data</param>
    /// <returns>The search results</returns>
    Task<ISearchFilesResults> SearchEntriesAsync(string[]? pathprefixes, IFilter? filter, long offset, long limit, bool extendedData);

    /// <summary>
    /// Sends an email notification
    /// </summary>
    /// <returns>The send mail results</returns>
    Task<ISendMailResults> SendMailAsync();

    /// <summary>
    /// Sets the locks on the backup
    /// </summary>
    /// <returns>The set lock results</returns>
    Task<ISetLockResults> SetLocksAsync();

    /// <summary>
    /// Sets the secret provider to use for all operations
    /// </summary>
    /// <param name="secretProvider">The secret provider to use</param>
    Task SetSecretProviderAsync(ISecretProvider? secretProvider);

    /// <summary>
    /// Sets the throttle speeds for uploads and downloads
    /// </summary>
    /// <param name="maxUploadPrSecond">Maximum upload speed in bytes per second</param>
    /// <param name="maxDownloadPrSecond">Maximum download speed in bytes per second</param>
    Task SetThrottleSpeedsAsync(long maxUploadPrSecond, long maxDownloadPrSecond);

    /// <summary>
    /// Stops the current operation
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Pauses the current operation
    /// </summary>
    /// <param name="alsoTransfers">Whether to also pause transfers</param>
    Task PauseAsync(bool alsoTransfers);

    /// <summary>
    /// Resumes a paused operation
    /// </summary>
    Task ResumeAsync();

    /// <summary>
    /// Appends a message sink to the controller
    /// </summary>
    /// <param name="sink">The message sink to append</param>
    Task AppendSinkAsync(IMessageSink sink);

    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns>The system information results</returns>
    Task<ISystemInfoResults> SystemInfoAsync();

    /// <summary>
    /// Tests the backup by verifying data integrity
    /// </summary>
    /// <param name="samples">The number of samples to test</param>
    /// <returns>The test results</returns>
    Task<ITestResults> TestAsync(long samples = 1);

    /// <summary>
    /// Tests the filter against the backup
    /// </summary>
    /// <param name="paths">The paths to test</param>
    /// <param name="filter">An optional filter to apply</param>
    /// <returns>The test filter results</returns>
    Task<ITestFilterResults> TestFilterAsync(string[] paths, IFilter? filter = null);

    /// <summary>
    /// Updates the database with version information
    /// </summary>
    /// <param name="filter">An optional filter to apply</param>
    /// <returns>The recreate database results</returns>
    Task<IRecreateDatabaseResults> UpdateDatabaseWithVersionsAsync(IFilter? filter = null);

    /// <summary>
    /// Performs a vacuum operation on the database
    /// </summary>
    /// <returns>The vacuum results</returns>
    Task<IVacuumResults> VacuumAsync();
}
