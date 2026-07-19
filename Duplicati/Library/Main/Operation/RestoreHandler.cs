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
using System.Linq;
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using CoCoL;
using System.Threading.Tasks;
using System.Threading;

namespace Duplicati.Library.Main.Operation
{
    internal class RestoreHandler
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<RestoreHandler>();

        private readonly Options m_options;
        private byte[]? m_blockbuffer;
        private readonly RestoreResults m_result;
        private string? m_selectedRestoreFolder;

        /// <summary>
        /// When non-null, names a persistent scratch table (in the local database file) that
        /// records the file hashes already restored in previous versions during a
        /// <c>--restore-all-files=unique</c> run. The per-version file-list preparation
        /// excludes files whose hash is present in this table, and each completed version
        /// appends its restored hashes to it. The table is created once at the start of the
        /// multi-version run and dropped at the end. Keeping this in the database (rather than
        /// in memory) avoids unbounded memory growth when many files/versions are involved.
        /// </summary>
        private string? m_restoredHashesTable;

        public RestoreHandler(Options options, RestoreResults result)
        {
            m_options = options;
            m_result = result;
        }

        /// <summary>
        /// Checks if the tempdir has enough free space relative to the volume size.
        /// Logs a warning if free space is less than 4 times the volume size.
        /// </summary>
        private void CheckTempDirFreeSpace()
        {
            try
            {
                // Get the tempdir path
                var tempDir = TempFolder.SystemTempPath;

                // Get free space in tempdir using the Utility method
                var spaceInfo = Library.Utility.Utility.GetFreeSpaceForPath(tempDir);
                if (spaceInfo == null)
                {
                    // Could not determine free space, skip the check
                    return;
                }

                // Get the volume size from options
                var volumeSize = m_options.VolumeSize;

                // Check if free space is less than 4 times the volume size
                if (spaceInfo.Value.FreeSpace < volumeSize * Library.Utility.Utility.VOLUME_SIZE_FREE_SPACE_MULTIPLIER)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "TempDirLowSpace", null,
                        $"The temporary folder '{tempDir}' has limited free space ({Library.Utility.Utility.FormatSizeString(spaceInfo.Value.FreeSpace)}). " +
                        $"It is recommended to have at least {Library.Utility.Utility.VOLUME_SIZE_FREE_SPACE_MULTIPLIER} times the volume size ({Library.Utility.Utility.FormatSizeString(volumeSize * Library.Utility.Utility.VOLUME_SIZE_FREE_SPACE_MULTIPLIER)}) available for optimal restore operation.");
                }
            }
            catch
            {
                // Ignore errors during free space check
            }
        }

        /// <summary>
        /// Gets the compression module by parsing the filename
        /// </summary>
        /// <param name="filename">The filename to parse</param>
        /// <returns>The compression module</returns>
        public static string GetCompressionModule(string filename)
        {
            var tmp = VolumeBase.ParseFilename(filename);
            if (tmp == null)
                throw new UserInformationException(string.Format("Unable to parse filename to valid entry: {0}", filename), "FailedToParseRemoteName");

            return tmp.CompressionModule;
        }

        public static RecreateDatabaseHandler.NumberedFilterFilelistDelegate? GetNumberedFilelistFilterDelegate(DateTime time, long[]? versions, bool singleTimeMatch = false)
        {
            versions ??= [];
            if (versions.Length == 0 && time.Ticks == 0)
                return null;

            return (filelist) => FilterNumberedFilelist(filelist, time, versions, singleTimeMatch);
        }

        public static IEnumerable<KeyValuePair<long, IParsedVolume>> FilterNumberedFilelist(IEnumerable<IParsedVolume> filelist, DateTime time, long[] versions, bool singleTimeMatch = false)
        {
            if (time.Kind == DateTimeKind.Unspecified)
                throw new Exception("Unspecified datetime instance, must be either local or UTC");

            // Make sure the resolution is the same (i.e. no milliseconds)
            if (time.Ticks > 0)
                time = Library.Utility.Utility.DeserializeDateTime(Library.Utility.Utility.SerializeDateTime(time)).ToUniversalTime();

            versions ??= [];
            // Unwrap, so we do not query the remote storage twice
            var lst = (from n in filelist
                       where n.FileType == RemoteVolumeType.Files
                       orderby n.Time descending
                       select n).ToArray();

            var numbers = lst.Zip(Enumerable.Range(0, lst.Length), (a, b) => new KeyValuePair<long, IParsedVolume>(b, a)).ToList();

            if (time.Ticks > 0 && versions.Length > 0)
                return from n in numbers
                       where (singleTimeMatch ? n.Value.Time == time : n.Value.Time <= time) && versions.Contains(n.Key)
                       select n;
            else if (time.Ticks > 0)
                return from n in numbers
                       where (singleTimeMatch ? n.Value.Time == time : n.Value.Time <= time)
                       select n;
            else if (versions.Length > 0)
                return from n in numbers
                       where versions.Contains(n.Key)
                       select n;
            else
                return numbers;
        }

        public async Task RunAsync(string[] paths, IBackendManager backendManager, Library.Utility.IFilter? filter, IRestoreDestinationProvider restoreDestination)
        {
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Begin);

            // Check tempdir free space before starting restore
            CheckTempDirFreeSpace();

            // Set the restore path in the results for logging purposes
            var restorePath = restoreDestination.TargetDestination;
            m_result.RestorePath = string.IsNullOrEmpty(restorePath)
                ? null
                : restorePath;

            // Folder paths in the file list always have a trailing directory separator.
            // Remember an explicitly selected single folder so its own name can be retained
            // when mapping the restore paths to a different destination.
            m_selectedRestoreFolder = GetSelectedRestoreFolder(paths);

            // If we have both target paths and a filter, combine them into a single filter
            filter = JoinedFilterExpression.Join(new FilterExpression(paths), filter);

            // The --restore-all-files option activates a multi-version restore. When active,
            // every targeted version is restored into its own timestamp-named subfolder below
            // the restore target, instead of restoring a single version. The single-version
            // restore logic below is reused verbatim for each version.
            if (m_options.RestoreAllFiles is RestoreAllFilesMode.True or RestoreAllFilesMode.Unique)
            {
                await RunRestoreAllFilesAsync(backendManager, filter, restoreDestination).ConfigureAwait(false);
                return;
            }

            await RunSingleVersionRestoreAsync(backendManager, filter, restoreDestination, m_options.Time, m_options.Version, m_result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Performs a normal single-version restore, recreating the local database (if needed)
        /// for the given time/versions and dispatching to the legacy or new restore flow.
        /// </summary>
        private async Task RunSingleVersionRestoreAsync(IBackendManager backendManager, Library.Utility.IFilter? filter, IRestoreDestinationProvider restoreDestination, DateTime time, long[]? versions, CancellationToken cancellationToken)
        {
            LocalRestoreDatabase? db = null;
            TempFile? tmpdb = null;
            try
            {
                var dbpath = m_options.Dbpath;
                if (!m_options.NoLocalDb && SystemIO.IO_OS.FileExists(dbpath))
                {
                    if (string.IsNullOrWhiteSpace(dbpath))
                        throw new InvalidOperationException("Unexpected empty database path");
                    db = await LocalRestoreDatabase.CreateAsync(dbpath, null, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoLocalDatabase", "No local database, building a temporary database");
                    tmpdb = new TempFile();
                    var filelistfilter = GetNumberedFilelistFilterDelegate(time, versions);
                    db = await LocalRestoreDatabase.CreateAsync(tmpdb, null, m_result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);
                    m_result.RecreateDatabaseResults = new RecreateDatabaseResults(m_result);
                    using (new Logging.Timer(LOGTAG, "RecreateTempDbForRestore", "Recreate temporary database for restore"))
                        await new RecreateDatabaseHandler(m_options, (RecreateDatabaseResults)m_result.RecreateDatabaseResults)
                            .DoRunAsync(backendManager, db, false, filter, filelistfilter, null)
                            .ConfigureAwait(false);

                    if (!m_options.SkipMetadata)
                        await ApplyStoredMetadataAsync(m_options, new RestoreHandlerMetadataStorage(), restoreDestination, m_result.TaskControl.ProgressToken);

                    //If we have --version set, we need to adjust, as the db has only the required versions
                    //TODO: Bit of a hack to set options that way
                    if (versions != null && versions.Length > 0)
                        m_options.RawOptions["version"] = string.Join(",", Enumerable.Range(0, versions.Length).Select(x => x.ToString()));
                }

                // Resolve the backup version index (0 = newest) and timestamp (UTC) for the
                // version being restored, forwarding them to the restore callback modules so
                // the engine methods below do not need to look them up. When the caller already
                // knows the timestamp (the --restore-all-files path), it is used directly.
                var (restoreVersion, restoreBackupTimestamp) = await ResolveRestoreVersionAndTimestampAsync(db, versions, time, cancellationToken).ConfigureAwait(false);

                if (m_options.RestoreLegacy)
                    await DoRunAsync(backendManager, db, filter, restoreDestination, restoreVersion, restoreBackupTimestamp, cancellationToken).ConfigureAwait(false);
                else
                    await DoRunNewAsync(backendManager, db, filter, restoreDestination, restoreVersion, restoreBackupTimestamp, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (db != null)
                    await db.DisposeAsync().ConfigureAwait(false);
                tmpdb?.Dispose();
            }
        }

        /// <summary>
        /// Implements the <c>--restore-all-files</c> multi-version restore.
        /// The set of targeted versions is computed using the same logic as a normal
        /// restore (the versions matching <c>--time</c>/<c>--version</c>, newest first).
        /// Each targeted version is restored into its own timestamp-named subfolder below
        /// the restore target. The first version restores all (non-filtered) files.
        /// Subsequent versions apply the usual filter and, when the mode is
        /// <see cref="RestoreAllFilesMode.Unique"/>, additionally skip any file whose
        /// content (file hash) was already restored in a previous version.
        /// </summary>
        private async Task RunRestoreAllFilesAsync(IBackendManager backendManager, Library.Utility.IFilter? filter, IRestoreDestinationProvider restoreDestination)
        {
            var cancellationToken = m_result.TaskControl.ProgressToken;

            // The option requires a definitive restore target.
            if (string.IsNullOrEmpty(restoreDestination.TargetDestination))
                throw new UserInformationException("The --restore-all-files option requires --restore-path to be set", "RestoreAllFilesRequiresRestorePath");

            var dbpath = m_options.Dbpath;
            // Enumerating the targeted versions requires the local database. The per-version
            // restore below can recreate a database when none exists, but discovering the
            // available filesets needs an existing database.
            if (m_options.NoLocalDb || string.IsNullOrWhiteSpace(dbpath) || !SystemIO.IO_OS.FileExists(dbpath))
                throw new UserInformationException("The --restore-all-files option requires a local database", "RestoreAllFilesRequiresLocalDb");

            // Snapshot the original version/time options so they can be restored after the loop.
            // The per-version loop below drives selection solely via the per-iteration "version"
            // option, so "time" is cleared for the duration of the loop and restored afterwards.
            var originalVersionValue = m_options.RawOptions.GetValueOrDefault("version");
            var originalTimeValue = m_options.RawOptions.GetValueOrDefault("time");

            // Resolve the targeted filesets using the same logic as a normal restore. The
            // filesets are returned ordered by timestamp descending (newest first), which
            // matches the version-index ordering used by --version (version 0 = newest).
            KeyValuePair<long, DateTime>[] allFilesets;
            using (var db = await LocalRestoreDatabase.CreateAsync(dbpath, null, cancellationToken).ConfigureAwait(false))
            {
                allFilesets = await db
                    .FilesetTimesAsync(cancellationToken)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            // Apply the same time/version selection as GetFilesetIDsAsync would, producing
            // the list of filesets to restore, newest first. This selects the full set of
            // versions to restore up front, before "time" is cleared for the per-version loop.
            // Restore oldest version first, to be more true to the "unique" mode, 
            // where the first time a file existed, it is restored.
            var selectedFilesets = SelectTargetedFilesets(allFilesets, m_options.Time, m_options.Version)
                .OrderByDescending(x => x.VersionIndex)
                .ToList();

            if (selectedFilesets.Count == 0)
                throw new UserInformationException("No backup versions matched the restore selection", "NoBackupAtDate");

            Logging.Log.WriteInformationMessage(LOGTAG, "RestoreAllFiles", "Restoring {0} version(s) with --restore-all-files={1}", selectedFilesets.Count, m_options.RestoreAllFiles);

            // For "unique" mode, the file hashes restored across previous versions are
            // accumulated in a persistent scratch table in the local database (not in memory,
            // to avoid unbounded growth). The table is created once here and dropped in the
            // finally below. Each completed version appends its restored hashes to it, and
            // subsequent versions exclude files whose hash is already present.
            if (m_options.RestoreAllFiles == RestoreAllFilesMode.Unique)
            {
                m_restoredHashesTable = $"RestoredHashes-{Library.Utility.Utility.GetHexGuid()}";
                using (var setupDb = await LocalRestoreDatabase.CreateAsync(dbpath, null, cancellationToken).ConfigureAwait(false))
                    await setupDb.CreateRestoredHashesTableAsync(m_restoredHashesTable, cancellationToken).ConfigureAwait(false);
            }

            // Clear the "time" option for the duration of the per-version loop. The per-version
            // restore below selects its fileset via the per-iteration "version" option, but the
            // underlying file-list query combines "time" and "version" with OR. If "time"
            // remained set, every iteration would also pull in all filesets at or before that
            // time, breaking the per-version isolation (each subfolder would contain files from
            // multiple versions). "time" is restored in the finally below.
            if (originalTimeValue != null)
                m_options.RawOptions.Remove("time");

            try
            {
                for (var i = 0; i < selectedFilesets.Count; i++)
                {
                    var fileset = selectedFilesets[i];
                    var timestampFolder = fileset.Timestamp.ToString("yyyyMMdd-HHmmss");
                    var versionTarget = SystemIO.IO_OS.PathCombine(restoreDestination.TargetDestination, timestampFolder);

                    Logging.Log.WriteInformationMessage(LOGTAG, "RestoreAllFilesVersion", "Restoring version {0} ({1}) into {2}", i, fileset.Timestamp, versionTarget);

                    // Target this single version only. The version index is the position in the
                    // newest-first fileset list (0 = newest), which is what --version uses.
                    m_options.RawOptions["version"] = fileset.VersionIndex.ToString();

                    // Build the per-version restore destination. A thin wrapper remaps
                    // TargetDestination to the timestamp subfolder; all other operations are
                    // delegated to the original provider. The mapped paths handed to the
                    // provider are already inside the original target, so the underlying
                    // provider's path validation continues to accept them.
                    var versionDestination = new VersionedRestoreDestinationProvider(restoreDestination, versionTarget);

                    // Forward the already-known version index so the engine methods and restore
                    // callback modules receive it without any lookup. The backup timestamp is
                    // resolved from the fileset list inside RunSingleVersionRestoreAsync.
                    await RunSingleVersionRestoreAsync(backendManager, filter, versionDestination, m_options.Time, [fileset.VersionIndex], cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                // Drop the persistent restored-hashes scratch table if it was created. Use a
                // dedicated timeout token (30s) rather than the restore's cancellation token so
                // cleanup still runs if the restore was cancelled, and does not hang forever.
                if (!string.IsNullOrEmpty(m_restoredHashesTable))
                {
                    try
                    {
                        using var cleanupCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        using (var cleanupDb = await LocalRestoreDatabase.CreateAsync(dbpath, null, cleanupCts.Token).ConfigureAwait(false))
                            await cleanupDb.DropRestoredHashesTableAsync(m_restoredHashesTable, cleanupCts.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "DropRestoredHashesFailed", ex, "Failed to drop restored-hashes scratch table: {0}", ex.Message);
                    }
                    m_restoredHashesTable = null;
                }

                // Restore the original options.
                if (originalVersionValue != null)
                    m_options.RawOptions["version"] = originalVersionValue;
                else
                    m_options.RawOptions.Remove("version");

                if (originalTimeValue != null)
                    m_options.RawOptions["time"] = originalTimeValue;
                else
                    m_options.RawOptions.Remove("time");
            }
        }

        /// <summary>
        /// Appends the file hashes of the files that were successfully restored (or verified
        /// in place) in the just-completed version into the persistent restored-hashes scratch
        /// table (<see cref="m_restoredHashesTable"/>). Called by the restore flow after a
        /// version's file list is processed (and before the temp tables are dropped), so the
        /// next version can skip files with the same content. Only files whose
        /// <c>DataVerified</c> flag is 1 are recorded; files whose restore failed are not
        /// harvested and remain eligible for restore in subsequent versions. Only the file hash
        /// is recorded, not metadata. The hashes are stored in the database rather than in
        /// memory to avoid unbounded memory growth across many files/versions.
        /// </summary>
        private async Task HarvestRestoredHashesAsync(LocalRestoreDatabase database, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(m_restoredHashesTable))
                return;

            try
            {
                var added = await database
                    .AddCurrentFilesToRestoredHashesAsync(m_restoredHashesTable, cancellationToken)
                    .ConfigureAwait(false);
                if (added > 0)
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RecordedRestoredHashes", "Recorded {0} file hash(es) restored in this version for de-duplication", added);
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "HarvestRestoredHashesFailed", ex, "Failed to harvest restored file hashes for de-duplication: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Selects the targeted filesets from the full newest-first fileset list, applying the
        /// same time/version selection logic as <see cref="LocalDatabase.GetFilesetIDsAsync"/>.
        /// Returns the selected filesets as a list of (filesetId, timestamp, versionIndex)
        /// triples, newest first.
        /// </summary>
        private static List<FilesetSelection> SelectTargetedFilesets(IReadOnlyList<KeyValuePair<long, DateTime>> allFilesets, DateTime time, long[]? versions)
        {
            var result = new List<FilesetSelection>();

            // Version indices take precedence: if specific versions are requested, select those
            // indices from the newest-first list (matching GetFilelistWhereClauseAsync which
            // selects filesets by ID derived from the version index).
            if (versions != null && versions.Length > 0)
            {
                foreach (var v in versions)
                {
                    if (v >= 0 && v < allFilesets.Count)
                        result.Add(new FilesetSelection(allFilesets[(int)v].Key, allFilesets[(int)v].Value, (int)v));
                    else
                        Logging.Log.WriteWarningMessage(LOGTAG, "SkipInvalidVersion", null, "Skipping invalid version: {0}", v);
                }

                if (result.Count > 0)
                    return result;
            }

            // Otherwise, select by time (all filesets at or before the given time), or, when no
            // time is set either, the single newest fileset (the normal default restore).
            if (time.Ticks > 0)
            {
                // Compare in UTC to avoid kind mismatches; allFilesets timestamps are local
                // (as returned by FilesetTimesAsync) and `time` may be local or UTC.
                var compareTime = time.ToUniversalTime();
                for (var idx = 0; idx < allFilesets.Count; idx++)
                {
                    var fs = allFilesets[idx];
                    if (fs.Value.ToUniversalTime() <= compareTime)
                        result.Add(new FilesetSelection(fs.Key, fs.Value, idx));
                }
            }
            else
            {
                // No version or time filter: target every fileset. The --restore-all-files
                // option is "restore all files across all versions", so without an explicit
                // version/time selection every available version is targeted.
                for (var idx = 0; idx < allFilesets.Count; idx++)
                    result.Add(new FilesetSelection(allFilesets[idx].Key, allFilesets[idx].Value, idx));
            }

            return result;
        }

        /// <summary>
        /// A thin <see cref="IRestoreDestinationProvider"/> wrapper that reports a per-version
        /// <see cref="TargetDestination"/> (the timestamp subfolder) while delegating every
        /// operation to the original provider. The restore path-mapping logic maps all target
        /// paths into the per-version subfolder, and those mapped paths are inside the
        /// original target, so the underlying provider's path validation still accepts them.
        /// </summary>
        private sealed class VersionedRestoreDestinationProvider : IRestoreDestinationProvider
        {
            private readonly IRestoreDestinationProvider _inner;
            private readonly string _targetDestination;

            public VersionedRestoreDestinationProvider(IRestoreDestinationProvider inner, string targetDestination)
            {
                _inner = inner;
                _targetDestination = targetDestination;
            }

            public string TargetDestination => _targetDestination;
            public Task Initialize(CancellationToken cancel) => _inner.Initialize(cancel);
            public Task Finalize(Action<double>? progressCallback, CancellationToken cancel) => _inner.Finalize(progressCallback, cancel);
            public Task Test(CancellationToken cancellationToken) => _inner.Test(cancellationToken);
            public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel) => _inner.CreateFolderIfNotExists(path, cancel);
            public Task<bool> FileExists(string path, CancellationToken cancel) => _inner.FileExists(path, cancel);
            public Task<Stream> OpenWrite(string path, CancellationToken cancel) => _inner.OpenWrite(path, cancel);
            public Task<Stream> OpenRead(string path, CancellationToken cancel) => _inner.OpenRead(path, cancel);
            public Task<Stream> OpenReadWrite(string path, CancellationToken cancel) => _inner.OpenReadWrite(path, cancel);
            public Task<long> GetFileLength(string path, CancellationToken cancel) => _inner.GetFileLength(path, cancel);
            public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel) => _inner.HasReadOnlyAttribute(path, cancel);
            public Task ClearReadOnlyAttribute(string path, CancellationToken cancel) => _inner.ClearReadOnlyAttribute(path, cancel);
            public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel) => _inner.WriteMetadata(path, metadata, restoreSymlinkMetadata, restorePermissions, cancel);
            public Task DeleteFolder(string path, CancellationToken cancel) => _inner.DeleteFolder(path, cancel);
            public Task DeleteFile(string path, CancellationToken cancel) => _inner.DeleteFile(path, cancel);
            public IList<string> GetPriorityFiles() => _inner.GetPriorityFiles();
            public void Dispose() => _inner.Dispose();
        }

        /// <summary>
        /// A selected fileset: its database ID, its timestamp, and its version index (position
        /// in the newest-first fileset list, where 0 is the newest).
        /// </summary>
        private readonly record struct FilesetSelection(long FilesetId, DateTime Timestamp, int VersionIndex);

        private static async Task PatchWithBlocklistAsync(LocalRestoreDatabase database, BlockVolumeReader blocks, Options options, RestoreResults result, byte[] blockbuffer, RestoreHandlerMetadataStorage metadatastorage, IRestoreDestinationProvider restoreDestination, CancellationToken cancellationToken)
        {
            var blocksize = options.Blocksize;
            var updateCounter = 0L;
            var fullblockverification = options.FullBlockVerification;

            using var blockhasher = HashFactory.CreateHasher(options.BlockHashAlgorithm);
            await using var blockmarker = await database.CreateBlockMarkerAsync(cancellationToken).ConfigureAwait(false);
            await using var volumekeeper = await database.GetMissingBlockDataAsync(blocks, options.Blocksize, cancellationToken).ConfigureAwait(false);
            await foreach (var restorelist in volumekeeper.FilesWithMissingBlocksAsync(cancellationToken).ConfigureAwait(false))
            {
                var targetpath = restorelist.Path;

                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchFile", "Would patch file with remote data: {0}", targetpath);
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "PatchingFile", "Patching file with remote data: {0}", targetpath);

                    try
                    {
                        if (!options.Dryrun)
                        {
                            var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                            if (!string.IsNullOrWhiteSpace(folderpath))
                                if (await restoreDestination.CreateFolderIfNotExists(folderpath, cancellationToken).ConfigureAwait(false))
                                    Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for  file {1}", folderpath, targetpath);
                        }

                        // TODO: Much faster if we iterate the volume and checks what blocks are used,
                        // because the compressors usually like sequential reading
                        using (var file = await restoreDestination.OpenWrite(targetpath, cancellationToken).ConfigureAwait(false))
                            await foreach (var targetblock in restorelist.BlocksAsync(cancellationToken).ConfigureAwait(false))
                            {
                                file.Position = targetblock.Offset;
                                var size = blocks.ReadBlock(targetblock.Key, blockbuffer);
                                if (targetblock.Size == size)
                                {
                                    var valid = !fullblockverification;
                                    if (!valid)
                                    {
                                        var key = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));
                                        if (targetblock.Key == key)
                                            valid = true;
                                        else
                                            Logging.Log.WriteWarningMessage(LOGTAG, "InvalidBlock", null, "Invalid block detected for {0}, expected hash: {1}, actual hash: {2}", targetpath, targetblock.Key, key);
                                    }

                                    if (valid)
                                    {
                                        await file.WriteAsync(blockbuffer, 0, size);
                                        await blockmarker
                                            .SetBlockRestoredAsync(restorelist.FileID, targetblock.Offset / blocksize, targetblock.Key, size, false, cancellationToken)
                                            .ConfigureAwait(false);
                                        result.SizeOfRestoredData += size;
                                    }
                                }
                                else
                                {
                                    Logging.Log.WriteWarningMessage(LOGTAG, "WrongBlockSize", null, "Block with hash {0} should have size {1} but has size {2}", targetblock.Key, targetblock.Size, size);
                                }
                            }

                        if ((++updateCounter) % 20 == 0)
                            await blockmarker
                                .UpdateProcessedAsync(result.OperationProgressUpdater, cancellationToken)
                                .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "PatchFailed", ex, "Failed to patch file: \"{0}\", message: {1}, message: {1}", targetpath, ex.Message);
                        if (options.UnittestMode)
                            throw;
                    }
                }
            }

            if (!options.SkipMetadata)
            {
                await foreach (var restoremetadata in volumekeeper.MetadataWithMissingBlocksAsync(cancellationToken).ConfigureAwait(false))
                {
                    var targetpath = restoremetadata.Path;
                    Logging.Log.WriteVerboseMessage(LOGTAG, "RecordingMetadata", "Recording metadata from remote data: {0}", targetpath);

                    try
                    {
                        // TODO: When we support multi-block metadata this needs to deal with it
                        using (var ms = new System.IO.MemoryStream())
                        {
                            await foreach (var targetblock in restoremetadata.BlocksAsync(cancellationToken).ConfigureAwait(false))
                            {
                                ms.Position = targetblock.Offset;
                                var size = blocks.ReadBlock(targetblock.Key, blockbuffer);
                                if (targetblock.Size == size)
                                {
                                    await ms.WriteAsync(blockbuffer, 0, size);
                                    await blockmarker
                                        .SetBlockRestoredAsync(restoremetadata.FileID, targetblock.Offset / blocksize, targetblock.Key, size, true, cancellationToken)
                                        .ConfigureAwait(false);
                                }
                            }

                            ms.Position = 0;
                            metadatastorage.Add(targetpath, ms);
                            //blockmarker.RecordMetadata(restoremetadata.FileID, ms);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "MetatdataRecordFailed", ex, "Failed to record metadata for file: \"{0}\", message: {1}", targetpath, ex.Message);
                        if (options.UnittestMode)
                            throw;
                    }
                }
            }
            await blockmarker
                .UpdateProcessedAsync(result.OperationProgressUpdater, cancellationToken)
                .ConfigureAwait(false);
            await blockmarker.CommitAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task ApplyStoredMetadataAsync(Options options, RestoreHandlerMetadataStorage metadatastorage, IRestoreDestinationProvider restoreDestination, CancellationToken cancellationToken)
        {
            foreach (var metainfo in metadatastorage.Records)
            {
                var targetpath = metainfo.Key;

                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchMetadata", "Would patch metadata with remote data: {0}", targetpath);
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "PatchingMetadata", "Patching metadata with remote data: {0}", targetpath);
                    try
                    {
                        if (!options.Dryrun)
                        {
                            var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                            // Only create the folder if the folder path is different from the target path
                            // (i.e., targetpath is not a root-level folder)
                            // Also skip if the folder is outside the target destination
                            if (!string.IsNullOrEmpty(folderpath) && folderpath != targetpath && (string.IsNullOrWhiteSpace(restoreDestination.TargetDestination) || Util.IsPathInsideTarget(folderpath, restoreDestination.TargetDestination)))
                            {
                                if (await restoreDestination.CreateFolderIfNotExists(folderpath, cancellationToken).ConfigureAwait(false))
                                    Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for target {1}", folderpath, targetpath);
                            }
                        }

                        await ApplyMetadataAsync(targetpath, metainfo.Value, options, restoreDestination, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "MetadataWriteFailed", ex, "Failed to apply metadata to file: \"{0}\", message: {1}", targetpath, ex.Message);
                        if (options.UnittestMode)
                            throw;
                    }
                }
            }
        }

        /// <summary>
        /// The tag used for logging restore-callback module invocations.
        /// </summary>
        private static readonly string RESTORE_CALLBACK_LOGTAG = Logging.Log.LogTagFromType<RestoreHandler>() + ".RestoreCallback";

        /// <summary>
        /// Invokes <see cref="IRestoreCallbackModule.OnFileRestoredAsync"/> on every loaded
        /// <see cref="IRestoreCallbackModule"/> module, passing the backup version index, the backup
        /// timestamp and the restored file's target path. Exceptions are logged and isolated.
        /// </summary>
        /// <param name="modules">The loaded generic modules, in their activation order.</param>
        /// <param name="version">The 0-based backup version index the file was restored from (0 = newest).</param>
        /// <param name="path">The target path of the restored file.</param>
        /// <param name="backupTimestamp">The timestamp of the backup version the file was restored from, in UTC.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous dispatch operation.</returns>
        internal static async Task InvokeFileRestoredAsync(IEnumerable<IGenericModule>? modules, long version, string path, DateTime backupTimestamp, CancellationToken cancellationToken)
        {
            if (modules == null)
                return;
            foreach (var mx in modules)
            {
                if (mx is not IRestoreCallbackModule module)
                    continue;
                try { await module.OnFileRestoredAsync(version, path, backupTimestamp, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(RESTORE_CALLBACK_LOGTAG, $"OnFileRestoredError{mx.Key}", ex, "OnFileRestored callback {0} failed: {1}", mx.Key, ex.Message); }
            }
        }

        /// <summary>
        /// Invokes <see cref="IRestoreCallbackModule.OnPreparePriorityFilesAsync"/> on every loaded
        /// <see cref="IRestoreCallbackModule"/> module, passing the priority-files list by reference
        /// so the modules may modify it, along with the backup version index and timestamp being
        /// restored. Exceptions thrown by a module are logged and isolated so a single misbehaving
        /// module cannot abort the restore.
        /// </summary>
        /// <param name="modules">The loaded generic modules, in their activation order.</param>
        /// <param name="priorityFiles">The priority-files list that modules may modify in place.</param>
        /// <param name="version">The 0-based backup version index being restored (0 = newest).</param>
        /// <param name="backupTimestamp">The timestamp of the backup version being restored, in UTC.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous dispatch operation.</returns>
        internal static async Task InvokePreparePriorityFilesAsync(IEnumerable<IGenericModule>? modules, IList<string> priorityFiles, long version, DateTime backupTimestamp, CancellationToken cancellationToken)
        {
            if (modules == null)
                return;
            foreach (var mx in modules)
            {
                if (mx is not IRestoreCallbackModule module)
                    continue;
                try { await module.OnPreparePriorityFilesAsync(priorityFiles, version, backupTimestamp, cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(RESTORE_CALLBACK_LOGTAG, $"OnPreparePriorityFilesError{mx.Key}", ex, "OnPreparePriorityFiles callback {0} failed: {1}", mx.Key, ex.Message); }
            }
        }

        /// <summary>
        /// Invokes <see cref="IRestoreCallbackModule.OnBulkRestoreStartAsync"/> on every loaded
        /// <see cref="IRestoreCallbackModule"/> module. Exceptions are logged and isolated.
        /// </summary>
        /// <param name="modules">The loaded generic modules, in their activation order.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous dispatch operation.</returns>
        internal static async Task InvokeBulkRestoreStartAsync(IEnumerable<IGenericModule>? modules, CancellationToken cancellationToken)
        {
            if (modules == null)
                return;
            foreach (var mx in modules)
            {
                if (mx is not IRestoreCallbackModule module)
                    continue;
                try { await module.OnBulkRestoreStartAsync(cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) { Logging.Log.WriteWarningMessage(RESTORE_CALLBACK_LOGTAG, $"OnBulkRestoreStartError{mx.Key}", ex, "OnBulkRestoreStart callback {0} failed: {1}", mx.Key, ex.Message); }
            }
        }

        /// <summary>
        /// Resolves the 0-based backup version index (0 = newest) and the backup timestamp
        /// (UTC) for the version being restored, so the value can be forwarded to the restore
        /// callback modules rather than reverse-looked-up inside the engine methods.
        /// </summary>
        /// <remarks>
        /// This is a forward resolution: it reads the fileset list (newest first) once and
        /// picks the entry matching the requested <paramref name="versions"/>/<paramref name="time"/>.
        /// </remarks>
        /// <param name="database">The restore database.</param>
        /// <param name="versions">The requested version indices (0 = newest), or null for a time-based restore.</param>
        /// <param name="time">The restore time, used when <paramref name="versions"/> is null/empty.</param>
        /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
        /// <returns>A tuple with the 0-based backup version index and the backup timestamp (UTC).</returns>
        private static async Task<(long version, DateTime backupTimestamp)> ResolveRestoreVersionAndTimestampAsync(LocalRestoreDatabase database, long[]? versions, DateTime time, CancellationToken cancellationToken)
        {
            try
            {
                // Filesets ordered newest-first; the 0-based position is the version index.
                var filesets = await database.FilesetTimesAsync(cancellationToken)
                    .ToArrayAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (filesets.Length == 0)
                    return (0, DateTime.MinValue);

                // Explicit version indices take precedence: the caller passes the absolute index.
                if (versions is { Length: > 0 })
                {
                    var requestedIndex = versions[0];
                    // On a recreated (no-local-db) database the list may contain fewer entries
                    // than the absolute index; the single present entry is the requested version.
                    var timestamp = requestedIndex >= 0 && requestedIndex < filesets.Length
                        ? filesets[requestedIndex].Value
                        : filesets[0].Value;
                    return (requestedIndex, timestamp.ToUniversalTime());
                }

                // Time-based restore: pick the newest fileset at or before the requested time.
                if (time.Ticks > 0)
                {
                    var compareTime = time.ToUniversalTime();
                    for (int i = 0; i < filesets.Length; i++)
                    {
                        if (filesets[i].Value.ToUniversalTime() <= compareTime)
                            return (i, filesets[i].Value.ToUniversalTime());
                    }
                }

                // Default: the newest backup (version 0).
                return (0, filesets[0].Value.ToUniversalTime());
            }
            catch (Exception ex)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "ResolveRestoreVersionFailed", ex, "Failed to resolve restore version info: {0}", ex.Message);
                return (0, DateTime.MinValue);
            }
        }

        /// <summary>
        /// Perform the restore operation.
        /// This is the new implementation, which utilizes a CSP network of processes to perform the restore.
        /// </summary>
        /// <param name="database">The database containing information about the restore.</param>
        /// <param name="filter">The filter of which files to restore.</param>
        /// <param name="restoreDestination">The destination to restore to.</param>
        /// <param name="restoreVersion">The forwarded 0-based backup version index being restored (0 = newest), reported to restore callback modules.</param>
        /// <param name="restoreBackupTimestamp">The forwarded backup timestamp (UTC) being restored, reported to restore callback modules.</param>
        /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
        private async Task DoRunNewAsync(IBackendManager backendManager, LocalRestoreDatabase database, IFilter? filter, IRestoreDestinationProvider restoreDestination, long restoreVersion, DateTime restoreBackupTimestamp, CancellationToken cancellationToken)
        {
            // Perform initial setup
            await Utility.UpdateOptionsFromDbAsync(database, m_options, cancellationToken)
                .ConfigureAwait(false);
            await Utility.VerifyOptionsAndUpdateDatabaseAsync(database, m_options, cancellationToken)
                .ConfigureAwait(false);

            if (m_options.RestoreChannelBufferSize < 0)
                throw new UserInformationException("Restore channel buffer size must be greater than or equal to 0", "RestoreChannelBufferSizeTooSmall");
            if (m_options.RestoreVolumeDecryptors <= 0)
                throw new UserInformationException("Restore volume decryptors must be greater than 0", "RestoreVolumeDecryptorsTooSmall");
            if (m_options.RestoreVolumeDecompressors <= 0)
                throw new UserInformationException("Restore volume decompressors must be greater than 0", "RestoreVolumeDecompressorsTooSmall");
            if (m_options.RestoreVolumeDownloaders <= 0)
                throw new UserInformationException("Restore volume downloaders must be greater than 0", "RestoreVolumeDownloadersTooSmall");

            // Verify the backend if necessary
            if (!m_options.NoBackendverification)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PreRestoreVerify);
                await FilelistProcessor.VerifyRemoteListAsync(backendManager, m_options, database, m_result.BackendWriter, latestVolumesOnly: false, verifyMode: FilelistProcessor.VerifyMode.VerifyOnly, cancellationToken).ConfigureAwait(false);
            }

            // Prepare the block and file list and create the directory structure
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateFileList);
            using (new Logging.Timer(LOGTAG, "PrepareBlockList", "PrepareBlockList"))
                await PrepareBlockAndFileListAsync(database, m_options, filter, restoreDestination, m_result, m_restoredHashesTable)
                    .ConfigureAwait(false);
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateTargetFolders);
            using (new Logging.Timer(LOGTAG, "CreateDirectory", "CreateDirectory"))
                await CreateDirectoryStructureAsync(database, restoreDestination, string.IsNullOrEmpty(restoreDestination.TargetDestination), m_options, m_result).ConfigureAwait(false);

            // At this point, there should be no more writes to the database, so we have to unlock the database:
            await database.Transaction
                .CommitAsync("CommitBeforeRestore", token: cancellationToken)
                .ConfigureAwait(false);

            using var setup_log_timer = new Logging.Timer(LOGTAG, "RestoreNetworkSetup", "RestoreNetworkSetup");
            var fileprocessor_requests = Enumerable.Range(0, m_options.RestoreFileProcessors).Select(_ => ChannelManager.CreateChannel<Restore.BlockRequest>(buffersize: Math.Max(m_options.RestoreChannelBufferSize, 1))).ToArray();
            var fileprocessor_responses = Enumerable.Range(0, m_options.RestoreFileProcessors).Select(_ => ChannelManager.CreateChannel<Task<Restore.DataBlock>>(buffersize: m_options.RestoreChannelBufferSize)).ToArray();

            // Configure channels and process parameters
            Restore.Channels channels = new(m_options);
            // Set the deadlock timer threshold to 1 minute per 10 MB of volume size
            var volsize = await database.GetLargestVolumeAsync(cancellationToken).ConfigureAwait(false);
            volsize = volsize > 0 ? volsize : m_options.VolumeSize;
            Restore.DeadlockTimer.initial_threshold = (int)TimeSpan.FromMinutes(1).TotalMilliseconds * Math.Max(1, (int)(volsize / (10L * 1024L * 1024L)));
            Restore.FileProcessor.file_processors_restoring_files = m_options.RestoreFileProcessors;
            // Reset the restore synchronization barriers for this operation. These are process-wide
            // static fields: the counters are reset per run, but the TaskCompletionSources were only
            // created once at field initialization and were never reset. Without resetting them here
            // (before any FileLister/FileProcessor tasks are started below, so there is no race),
            // they stay in the completed state from a previous restore in the same process. On the
            // second and later restores the barriers then no longer wait - both the priority-files
            // barrier and the folder-metadata rendezvous return immediately - so folder metadata
            // (including Windows ACLs) can be applied before all file content is restored. That lost
            // ordering guarantee causes intermittent metadata/permission restore failures.
            Restore.FileProcessor.file_processor_continue = new();
            Restore.FileProcessor.priority_files_completed = new();

            // Initialize priority files synchronization.
            // Wrap in a List<> so restore callback modules can freely modify the list
            // (the destination provider may return a fixed-size array).
            var priorityFiles = new List<string>(restoreDestination.GetPriorityFiles());
            // Allow restore callback modules to inspect and modify the priority-files list
            // before any files are restored, passing the forwarded version and backup timestamp.
            await InvokePreparePriorityFilesAsync(m_options.LoadedModules, priorityFiles, restoreVersion, restoreBackupTimestamp, cancellationToken).ConfigureAwait(false);

            // Create the process network
            var filelister = Restore.FileLister.RunAsync(channels, database, m_options, m_result, priorityFiles, restoreVersion, restoreBackupTimestamp, m_options.LoadedModules);
            var fileprocessors = Enumerable.Range(0, m_options.RestoreFileProcessors).Select(i => Restore.FileProcessor.RunAsync(channels, database, fileprocessor_requests[i], fileprocessor_responses[i], restoreDestination, m_options, m_result, m_options.LoadedModules)).ToArray();
            var blockmanager = Restore.BlockManager.RunAsync(channels, database, fileprocessor_requests, fileprocessor_responses, m_options, m_result);
            var volumecache = Restore.VolumeManager.RunAsync(channels, m_options, m_result);
            var volumedownloaders = Enumerable.Range(0, m_options.RestoreVolumeDownloaders).Select(i => Restore.VolumeDownloader.RunAsync(channels, database, backendManager, m_options, m_result)).ToArray();
            var volumedecryptors = Enumerable.Range(0, m_options.RestoreVolumeDecryptors).Select(i => Restore.VolumeDecryptor.RunAsync(channels, backendManager, m_options)).ToArray();
            var volumedecompressors = Enumerable.Range(0, m_options.RestoreVolumeDecompressors).Select(i => Restore.VolumeDecompressor.RunAsync(channels, m_options)).ToArray();

            setup_log_timer.Dispose();

            // Wait for the network to complete
            Task[] all =
                [
                    filelister,
                    ..fileprocessors,
                    blockmanager,
                    volumecache,
                    ..volumedownloaders,
                    ..volumedecryptors,
                    ..volumedecompressors
                ];

            // Start the progress updater
            using (new Logging.Timer(LOGTAG, "RestoreNetworkWait", "RestoreNetworkWait"))
            using (var kill_updater = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                var updater = Task.Run(async () =>
                {
                    while (!kill_updater.Token.IsCancellationRequested)
                    {
                        m_result.OperationProgressUpdater.UpdatefilesProcessed(m_result.RestoredFiles, m_result.SizeOfRestoredFiles);
                        await Task.Delay(1000, kill_updater.Token).ConfigureAwait(false);
                    }
                }, kill_updater.Token);

                await Task.WhenAll(all).ConfigureAwait(false);
                await kill_updater.CancelAsync();
            }

            await database.Transaction
                .CommitAsync("CommitAfterRestore", token: cancellationToken)
                .ConfigureAwait(false);

            await database.DisposePoolAsync().ConfigureAwait(false);

            if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                return;

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PostRestoreVerify);

            // If any errors occurred, log them
            if (m_result.BrokenRemoteFiles.Count > 0 || m_result.BrokenLocalFiles.Count > 0)
            {
                var nl = Environment.NewLine;
                int maxN = 10;
                long remoteFirstN = Math.Min(maxN, m_result.BrokenRemoteFiles.Count);
                string remoteFirst = remoteFirstN < m_result.BrokenRemoteFiles.Count ? $"first {maxN} " : string.Empty;
                long localFirstN = Math.Min(maxN, m_result.BrokenLocalFiles.Count);
                string localFirst = localFirstN < m_result.BrokenLocalFiles.Count ? $"first {maxN} " : string.Empty;

                string remoteMessage = m_result.BrokenRemoteFiles.Count > 0 ? $"Failed to download {m_result.BrokenRemoteFiles.Count} remote files." : string.Empty;
                string remoteList = m_result.BrokenRemoteFiles.Count > 0 ? $"The following {remoteFirst}remote files failed to download, which may be the cause:{nl}{string.Join(nl, m_result.BrokenRemoteFiles.Take(maxN))}{nl}" : string.Empty;
                string localMessage = m_result.BrokenLocalFiles.Count > 0 ? $"Failed to restore {m_result.BrokenLocalFiles.Count} local files." : string.Empty;
                string localList = m_result.BrokenLocalFiles.Count > 0 ? $"The following {localFirst}local files failed to restore:{nl}{string.Join(nl, m_result.BrokenLocalFiles.Take(maxN))}{nl}" : string.Empty;

                Logging.Log.WriteErrorMessage(LOGTAG, "RestoreFailures", null, $"{remoteMessage}{nl}{localMessage}{nl}{remoteList}{nl}{localList}");
            }
            else if (m_result.RestoredFiles == 0)
            {
                if (m_result.UnmodifiedFiles == 0 && m_result.RestoredFolders == 0)
                    Logging.Log.WriteWarningMessage(LOGTAG, "NoFilesOrFoldersRestored", null, "Restore completed without errors but no files or folders were restored");
                else if (m_result.UnmodifiedFiles == 0)
                    Logging.Log.WriteWarningMessage(LOGTAG, "NoFilesRestored", null, "Restore completed without errors but no files were restored");
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoFilesNeededRestore", null, "Restore completed but all files were already present");
            }

            // Harvest restored file hashes for --restore-all-files=unique before the temp
            // tables are dropped, so subsequent versions can skip files with the same content.
            await HarvestRestoredHashesAsync(database, cancellationToken).ConfigureAwait(false);

            // Drop the temp tables
            await database.DropRestoreTableAsync(cancellationToken).ConfigureAwait(false);
            await backendManager.WaitForEmptyAsync(database, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

            // Report that the restore is complete
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Finalize);
            m_result.EndTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Creates a filter that matches only the given priority files.
        /// Priority file names are matched as suffix patterns (e.g., "geometry.json" becomes "*geometry.json").
        /// </summary>
        /// <param name="priorityFiles">The list of priority file names to include.</param>
        /// <returns>A filter that includes only files matching the priority file patterns.</returns>
        private static Library.Utility.IFilter BuildPriorityFilter(IList<string> priorityFiles)
        {
            var priorityPatterns = priorityFiles.Select(pf => "*" + pf);
            return new FilterExpression(priorityPatterns, true);
        }

        /// <summary>
        /// Creates a filter that excludes the given priority files from the original filter.
        /// The exclude filter is placed first so it takes precedence in the joined expression.
        /// </summary>
        /// <param name="priorityFiles">The list of priority file names to exclude.</param>
        /// <param name="originalFilter">The original filter to combine with.</param>
        /// <returns>A filter that matches the original filter but excludes priority files.</returns>
        private static Library.Utility.IFilter? BuildExcludePriorityFilter(IList<string> priorityFiles, Library.Utility.IFilter? originalFilter)
        {
            var excludePatterns = priorityFiles.Select(pf => "*" + pf);
            var excludeFilter = new FilterExpression(excludePatterns, false);
            // Put exclude first so it takes precedence in JoinedFilterExpression
            return JoinedFilterExpression.Join(excludeFilter, originalFilter);
        }

        /// <summary>
        /// Perform the restore operation.
        /// This is the legacy implementation, which performs the restore in a single thread. Kept as in case the new implementation fails.
        /// </summary>
        /// <param name="backendManager">The backend manager for downloading volumes.</param>
        /// <param name="database">The database containing information about the restore.</param>
        /// <param name="filter">The filter of which files to restore.</param>
        /// <param name="restoreDestination">The destination to restore to.</param>
        /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
        private async Task DoRunAsync(IBackendManager backendManager, LocalRestoreDatabase database, Library.Utility.IFilter? filter, IRestoreDestinationProvider restoreDestination, long restoreVersion, DateTime restoreBackupTimestamp, CancellationToken cancellationToken)
        {
            using (var metadatastorage = new RestoreHandlerMetadataStorage())
            {
                // One-time setup
                await Utility.UpdateOptionsFromDbAsync(database, m_options, cancellationToken)
                    .ConfigureAwait(false);
                await Utility.VerifyOptionsAndUpdateDatabaseAsync(database, m_options, cancellationToken)
                    .ConfigureAwait(false);
                m_blockbuffer = new byte[m_options.Blocksize];

                if (!m_options.NoBackendverification)
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PreRestoreVerify);
                    await FilelistProcessor.VerifyRemoteListAsync(backendManager, m_options, database, m_result.BackendWriter, latestVolumesOnly: false, verifyMode: FilelistProcessor.VerifyMode.VerifyOnly, cancellationToken).ConfigureAwait(false);
                }

                // Get priority files from restore destination.
                // Wrap in a List<> so restore callback modules can freely modify the list
                // (the destination provider may return a fixed-size array).
                var priorityFiles = new List<string>(restoreDestination.GetPriorityFiles());
                // Allow restore callback modules to inspect and modify the priority-files list
                // before any files are restored, passing the forwarded version and backup timestamp.
                await InvokePreparePriorityFilesAsync(m_options.LoadedModules, priorityFiles, restoreVersion, restoreBackupTimestamp, cancellationToken).ConfigureAwait(false);

                if (priorityFiles.Count > 0)
                {
                    // Phase 1: Restore priority files only
                    Logging.Log.WriteInformationMessage(LOGTAG, "PriorityFilesRestore", "Restoring {0} priority file(s) first: {1}", priorityFiles.Count, string.Join(", ", priorityFiles));
                    var priorityFilter = BuildPriorityFilter(priorityFiles);
                    var restoredBefore = m_result.RestoredFiles;

                    await RestoreCoreAsync(backendManager, database, priorityFilter, restoreDestination, metadatastorage, cancellationToken)
                        .ConfigureAwait(false);

                    // Warn about any priority files not found in backup
                    var restoredInPhase1 = m_result.RestoredFiles - restoredBefore;
                    if (restoredInPhase1 == 0)
                    {
                        foreach (var pf in priorityFiles)
                            Logging.Log.WriteWarningMessage(LOGTAG, "PriorityFileNotFound", null,
                                "Priority file '{0}' was not found in the backup. This may cause subsequent restore steps to fail.", pf);
                    }

                    // Phase 2: Restore remaining files (excluding priority files)
                    Logging.Log.WriteInformationMessage(LOGTAG, "RemainingFilesRestore", "Restoring remaining files (excluding priority files)");
                    // All priority files have been restored and the bulk restore is starting.
                    await InvokeBulkRestoreStartAsync(m_options.LoadedModules, cancellationToken).ConfigureAwait(false);
                    var phase2Filter = BuildExcludePriorityFilter(priorityFiles, filter);
                    await RestoreCoreAsync(backendManager, database, phase2Filter, restoreDestination, metadatastorage, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    // No priority files — single phase, the bulk restore starts immediately.
                    await InvokeBulkRestoreStartAsync(m_options.LoadedModules, cancellationToken).ConfigureAwait(false);
                    await RestoreCoreAsync(backendManager, database, filter, restoreDestination, metadatastorage, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (m_result.RestoredFiles == 0)
            {
                if (m_result.UnmodifiedFiles == 0 && m_result.RestoredFolders == 0)
                    Logging.Log.WriteWarningMessage(LOGTAG, "NoFilesOrFoldersRestored", null, "Restore completed without errors but no files or folders were restored");
                else if (m_result.UnmodifiedFiles == 0)
                    Logging.Log.WriteWarningMessage(LOGTAG, "NoFilesRestored", null, "Restore completed without errors but no files were restored");
                else
                    Logging.Log.WriteInformationMessage(LOGTAG, "NoFilesNeededRestore", null, "Restore completed but all files were already present");
            }

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Finalize);
            m_result.EndTime = DateTime.UtcNow;
        }

        /// <summary>
        /// Core restore logic extracted from the legacy restore flow.
        /// Performs the full restore pipeline: prepare file/block lists, scan for existing blocks,
        /// download and patch from remote volumes, restore empty files, apply metadata, and verify.
        /// </summary>
        /// <param name="backendManager">The backend manager for downloading volumes.</param>
        /// <param name="database">The database containing information about the restore.</param>
        /// <param name="filter">The filter of which files to restore in this phase.</param>
        /// <param name="restoreDestination">The destination to restore to.</param>
        /// <param name="metadatastorage">Shared metadata storage across phases.</param>
        /// <param name="cancellationToken">The cancellation token to monitor for cancellation requests.</param>
        private async Task RestoreCoreAsync(IBackendManager backendManager, LocalRestoreDatabase database, Library.Utility.IFilter? filter, IRestoreDestinationProvider restoreDestination, RestoreHandlerMetadataStorage metadatastorage, CancellationToken cancellationToken)
        {
            //Figure out what files are to be patched, and what blocks are needed
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateFileList);
            using (new Logging.Timer(LOGTAG, "PrepareBlockList", "PrepareBlockList"))
                await PrepareBlockAndFileListAsync(database, m_options, filter, restoreDestination, m_result, m_restoredHashesTable)
                    .ConfigureAwait(false);

            //Make the entire output setup
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateTargetFolders);
            using (new Logging.Timer(LOGTAG, "CreateDirectory", "CreateDirectory"))
                await CreateDirectoryStructureAsync(database, restoreDestination, string.IsNullOrEmpty(restoreDestination.TargetDestination), m_options, m_result)
                    .ConfigureAwait(false);

            if (m_blockbuffer == null)
                throw new InvalidOperationException("Block buffer has not been initialized");

            //If we are patching an existing target folder, do not touch stuff that is already updated
            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_ScanForExistingFiles);
            using (var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
            using (var filehasher = HashFactory.CreateHasher(m_options.FileHashAlgorithm))
            using (new Logging.Timer(LOGTAG, "ScanForExistingTargetBlocks", "ScanForExistingTargetBlocks"))
                await ScanForExistingTargetBlocksAsync(database, m_blockbuffer, blockhasher, filehasher, m_options, restoreDestination, m_result).ConfigureAwait(false);

            //Look for existing blocks in the original source files only
            if (m_options.UseLocalBlocks && !string.IsNullOrEmpty(restoreDestination.TargetDestination))
            {
                using (var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                using (new Logging.Timer(LOGTAG, "ScanForExistingSourceBlocksFast", "ScanForExistingSourceBlocksFast"))
                {
                    m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_ScanForLocalBlocks);
                    await ScanForExistingSourceBlocksFastAsync(database, m_options, m_blockbuffer, blockhasher, restoreDestination, m_result).ConfigureAwait(false);
                }
            }

            if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
            {
                await backendManager.WaitForEmptyAsync(database, cancellationToken).ConfigureAwait(false);
                return;
            }

            // If other local files already have the blocks we want, we use them instead of downloading
            if (m_options.UseLocalBlocks)
            {
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PatchWithLocalBlocks);
                using (var blockhasher = HashFactory.CreateHasher(m_options.BlockHashAlgorithm))
                using (new Logging.Timer(LOGTAG, "PatchWithLocalBlocks", "PatchWithLocalBlocks"))
                    await ScanForExistingSourceBlocksAsync(database, m_options, m_blockbuffer, blockhasher, m_result, metadatastorage, restoreDestination).ConfigureAwait(false);
            }

            if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
            {
                await backendManager.WaitForEmptyAsync(database, cancellationToken).ConfigureAwait(false);
                return;
            }

            // Fill BLOCKS with remote sources
            List<IRemoteVolume> volumes;
            using (new Logging.Timer(LOGTAG, "GetMissingVolumes", "GetMissingVolumes"))
                volumes = await database
                    .GetMissingVolumesAsync(cancellationToken)
                    .ToListAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

            if (volumes.Count > 0)
            {
                Logging.Log.WriteInformationMessage(LOGTAG, "RemoteFileCount", "{0} remote files are required to restore", volumes.Count);
                m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
            }

            var brokenFiles = new List<string>();

            using (new Logging.Timer(LOGTAG, "PatchWithBlocklist", "PatchWithBlocklist"))
                await foreach (var (tmpfile, _, _, name) in backendManager.GetFilesOverlappedAsync(volumes, allowParityRepair: true, cancellationToken).ConfigureAwait(false))
                {
                    try
                    {
                        if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                        {
                            await backendManager.WaitForEmptyAsync(database, cancellationToken).ConfigureAwait(false);
                            return;
                        }

                        using (tmpfile)
                        using (var blocks = new BlockVolumeReader(GetCompressionModule(name), tmpfile, m_options))
                            await PatchWithBlocklistAsync(database, blocks, m_options, m_result, m_blockbuffer, metadatastorage, restoreDestination, cancellationToken)
                                .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        brokenFiles.Add(name);
                        Logging.Log.WriteErrorMessage(LOGTAG, "PatchingFailed", ex, "Failed to patch with remote file: \"{0}\", message: {1}", name, ex.Message);
                        if (ex.IsAbortException())
                            throw;
                    }
                }

            var fileErrors = 0L;

            // Restore empty files. They might not have any blocks so don't appear in any volume.
            await foreach (var file in database.GetFilesToRestoreAsync(true, cancellationToken).Where(item => item.Length == 0).ConfigureAwait(false))
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "RestoreEmptyFile", "Restoring empty file \"{0}\"", file.Path);

                try
                {
                    var folderpath = SystemIO.IO_OS.PathGetDirectoryName(file.Path);
                    if (!string.IsNullOrEmpty(folderpath))
                    {
                        await restoreDestination.CreateFolderIfNotExists(folderpath, cancellationToken).ConfigureAwait(false);
                    }
                    // Just create the file and close it right away, empty statement is intentional.
                    using (var stream = await restoreDestination.OpenWrite(file.Path, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            stream.SetLength(0);
                        }
                        catch (NotSupportedException)
                        {
                            // Some streams do not support setting length, ignore
                        }
                    }
                }
                catch (Exception ex)
                {
                    fileErrors++;
                    Logging.Log.WriteErrorMessage(LOGTAG, "RestoreFileFailed", ex, "Failed to restore empty file: \"{0}\". Error message was: {1}", file.Path, ex.Message);
                    if (ex.IsAbortException())
                        throw;
                }
            }

            // Enforcing the length of files is now already done during ScanForExistingTargetBlocks
            // and thus not necessary anymore.

            // Apply metadata
            if (!m_options.SkipMetadata)
                await ApplyStoredMetadataAsync(m_options, metadatastorage, restoreDestination, m_result.TaskControl.ProgressToken).ConfigureAwait(false);

            if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                return;

            m_result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_PostRestoreVerify);

            if (m_options.PerformRestoredFileVerification)
            {
                // After all blocks in the files are restored, verify the file hash
                using (var filehasher = HashFactory.CreateHasher(m_options.FileHashAlgorithm))
                using (new Logging.Timer(LOGTAG, "RestoreVerification", "RestoreVerification"))
                    await foreach (var file in database.GetFilesToRestoreAsync(true, cancellationToken).ConfigureAwait(false))
                    {
                        try
                        {
                            if (!await m_result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                            {
                                await backendManager.WaitForEmptyAsync(database, cancellationToken).ConfigureAwait(false);
                                return;
                            }

                            Logging.Log.WriteVerboseMessage(LOGTAG, "TestFileIntegrity", "Testing restored file integrity: {0}", file.Path);

                            string key;
                            long size;
                            using (var fs = await restoreDestination.OpenRead(file.Path, cancellationToken).ConfigureAwait(false))
                            {
                                size = fs.Length;
                                key = Convert.ToBase64String(filehasher.ComputeHash(fs));
                            }

                            if (key != file.Hash)
                                throw new Exception(string.Format("Failed to restore file: \"{0}\". File hash is {1}, expected hash is {2}", file.Path, key, file.Hash));
                            m_result.RestoredFiles++;
                            m_result.SizeOfRestoredFiles += size;
                        }
                        catch (Exception ex)
                        {
                            fileErrors++;
                            Logging.Log.WriteErrorMessage(LOGTAG, "RestoreFileFailed", ex, "Failed to restore file: \"{0}\". Error message was: {1}", file.Path, ex.Message);
                            if (ex.IsAbortException())
                                throw;
                        }
                    }
            }

            if (fileErrors > 0 && brokenFiles.Count > 0)
                Logging.Log.WriteInformationMessage(LOGTAG, "RestoreFailures", "Failed to restore {0} files, additionally the following files failed to download, which may be the cause:{1}{2}", fileErrors, Environment.NewLine, string.Join(Environment.NewLine, brokenFiles));
            else if (fileErrors > 0)
                Logging.Log.WriteInformationMessage(LOGTAG, "RestoreFailures", "Failed to restore {0} files", fileErrors);

            // Harvest restored file hashes for --restore-all-files=unique before the temp
            // tables are dropped, so subsequent versions can skip files with the same content.
            await HarvestRestoredHashesAsync(database, cancellationToken).ConfigureAwait(false);

            // Drop the temp tables
            await database.DropRestoreTableAsync(cancellationToken).ConfigureAwait(false);
            await backendManager.WaitForEmptyAsync(database, cancellationToken).ConfigureAwait(false);
        }

        public static async Task<bool> ApplyMetadataAsync(string path, System.IO.Stream stream, Options options, IRestoreDestinationProvider restoreDestination, CancellationToken cancellationToken)
        {
            using (var tr = new System.IO.StreamReader(stream))
            using (var jr = new Newtonsoft.Json.JsonTextReader(tr))
            {
                var metadata = new Newtonsoft.Json.JsonSerializer().Deserialize<Dictionary<string, string?>>(jr);
                // If this is dry-run, we stop after having deserialized the metadata
                if (metadata == null || options.Dryrun)
                    return false;

                return await restoreDestination.WriteMetadata(path, metadata, options.RestoreSymlinkMetadata, options.RestorePermissions, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task ScanForExistingSourceBlocksFastAsync(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, IRestoreDestinationProvider restoreDestination, RestoreResults result)
        {
            // Fill BLOCKS with data from known local source files
            await using var blockmarker = await database.CreateBlockMarkerAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);
            var updateCount = 0L;
            await foreach (var entry in database.GetFilesAndSourceBlocksFastAsync(options.Blocksize, result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                var targetpath = entry.TargetPath;
                var targetfileid = entry.TargetFileID;
                var sourcepath = entry.SourcePath;
                var patched = false;

                try
                {
                    if (SystemIO.IO_OS.FileExists(sourcepath))
                    {
                        if (!options.Dryrun)
                        {
                            var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                            if (!string.IsNullOrEmpty(folderpath))
                            {
                                if (await restoreDestination.CreateFolderIfNotExists(folderpath, result.TaskControl.ProgressToken).ConfigureAwait(false))
                                    Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for  file {1}", folderpath, targetpath);
                            }
                        }

                        using (var targetstream = options.Dryrun ? null : await restoreDestination.OpenWrite(targetpath, result.TaskControl.ProgressToken).ConfigureAwait(false))
                        {
                            try
                            {
                                using var sourcestream = SystemIO.IO_OS.FileOpenRead(sourcepath);
                                await foreach (var block in entry.BlocksAsync(result.TaskControl.ProgressToken).ConfigureAwait(false))
                                {
                                    if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                                        return;

                                    //TODO: Handle metadata

                                    if (sourcestream.Length > block.Offset)
                                    {
                                        sourcestream.Position = block.Offset;

                                        int size = Library.Utility.Utility.ForceStreamRead(sourcestream, blockbuffer, blockbuffer.Length);
                                        if (size == block.Size)
                                        {
                                            var key = Convert.ToBase64String(hasher.ComputeHash(blockbuffer, 0, size));
                                            if (key == block.Hash)
                                            {
                                                patched = true;
                                                if (!options.Dryrun)
                                                {
                                                    if (targetstream == null)
                                                        throw new InvalidOperationException("Did not expect stream to be null");
                                                    targetstream.Position = block.Offset;
                                                    await targetstream.WriteAsync(blockbuffer, 0, size);
                                                }

                                                await blockmarker.SetBlockRestoredAsync(targetfileid, block.Index, key, block.Size, false, result.TaskControl.ProgressToken)
                                                    .ConfigureAwait(false);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, sourcepath, ex.Message);
                                if (ex.IsAbortException())
                                    throw;
                            }
                        }

                        if ((++updateCount) % 20 == 0)
                        {
                            await blockmarker
                                .UpdateProcessedAsync(result.OperationProgressUpdater, result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                            if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                                return;
                        }

                    }
                    else
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "LocalSourceMissing", "Local source file not found: {0}", sourcepath);
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message);
                    if (ex.IsAbortException())
                        throw;
                    if (options.UnittestMode)
                        throw;
                }

                if (patched)
                    Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is patched with some local data: {0}", targetpath);
                else
                    Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is not patched any local data: {0}", targetpath);

                if (patched && options.Dryrun)
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchWithLocal", "Would patch file with local data: {0}", targetpath);
            }

            await blockmarker
                .UpdateProcessedAsync(result.OperationProgressUpdater, result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            await blockmarker.CommitAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);
        }

        private static async Task ScanForExistingSourceBlocksAsync(LocalRestoreDatabase database, Options options, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm hasher, RestoreResults result, RestoreHandlerMetadataStorage metadatastorage, IRestoreDestinationProvider restoreDestination)
        {
            // Fill BLOCKS with data from known local source files
            await using var blockmarker = await database.CreateBlockMarkerAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);
            var updateCount = 0L;
            await foreach (var restorelist in database.GetFilesAndSourceBlocksAsync(options.SkipMetadata, options.Blocksize, result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                var targetpath = restorelist.TargetPath;
                var targetfileid = restorelist.TargetFileID;
                var patched = false;
                try
                {
                    if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                        return;

                    if (!options.Dryrun)
                    {
                        var folderpath = SystemIO.IO_OS.PathGetDirectoryName(targetpath);
                        if (!string.IsNullOrEmpty(folderpath))
                        {
                            if (await restoreDestination.CreateFolderIfNotExists(folderpath, result.TaskControl.ProgressToken).ConfigureAwait(false))
                                Logging.Log.WriteWarningMessage(LOGTAG, "CreateMissingFolder", null, "Creating missing folder {0} for file {1}", folderpath, targetpath);
                        }
                    }

                    using (var file = options.Dryrun ? null : await restoreDestination.OpenWrite(targetpath, result.TaskControl.ProgressToken).ConfigureAwait(false))
                        await foreach (var targetblock in restorelist.BlocksAsync(result.TaskControl.ProgressToken).ConfigureAwait(false))
                        {
                            await foreach (var source in targetblock.BlockSourcesAsync(result.TaskControl.ProgressToken).ConfigureAwait(false))
                            {
                                try
                                {
                                    if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                                        return;

                                    if (SystemIO.IO_OS.FileExists(source.Path))
                                    {
                                        if (source.IsMetadata)
                                        {
                                            // TODO: Handle this by reconstructing
                                            // metadata from file and checking the hash

                                            continue;
                                        }
                                        else
                                        {
                                            using var sourcefile = SystemIO.IO_OS.FileOpenRead(source.Path);
                                            sourcefile.Position = source.Offset;
                                            int size = Library.Utility.Utility.ForceStreamRead(sourcefile, blockbuffer, blockbuffer.Length);
                                            if (size == targetblock.Size)
                                            {
                                                var key = Convert.ToBase64String(hasher.ComputeHash(blockbuffer, 0, size));
                                                if (key == targetblock.Hash)
                                                {
                                                    if (!options.Dryrun)
                                                    {
                                                        if (targetblock.IsMetadata)
                                                            metadatastorage.Add(targetpath, new System.IO.MemoryStream(blockbuffer, 0, size));
                                                        else
                                                        {
                                                            if (file == null)
                                                                throw new InvalidOperationException("Did not expect file to be null");

                                                            file.Position = targetblock.Offset;
                                                            await file.WriteAsync(blockbuffer, 0, size);
                                                        }
                                                    }

                                                    await blockmarker
                                                        .SetBlockRestoredAsync(targetfileid, targetblock.Index, key, targetblock.Size, false, result.TaskControl.ProgressToken)
                                                        .ConfigureAwait(false);
                                                    patched = true;
                                                    break;
                                                }
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with data from local file \"{1}\", message: {2}", targetpath, source.Path, ex.Message);
                                    if (ex.IsAbortException())
                                        throw;
                                }
                            }
                        }

                    if ((++updateCount) % 20 == 0)
                        await blockmarker
                            .UpdateProcessedAsync(result.OperationProgressUpdater, result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "PatchingFileLocalFailed", ex, "Failed to patch file: \"{0}\" with local data, message: {1}", targetpath, ex.Message);
                    if (options.UnittestMode)
                        throw;
                }

                if (patched)
                    Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is patched with some local data: {0}", targetpath);
                else
                    Logging.Log.WriteVerboseMessage(LOGTAG, "FilePatchedWithLocal", "Target file is not patched any local data: {0}", targetpath);

                if (patched && options.Dryrun)
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldPatchWithLocal", string.Format("Would patch file with local data: {0}", targetpath));
            }

            await blockmarker
                .UpdateProcessedAsync(result.OperationProgressUpdater, result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            await blockmarker.CommitAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);
        }

        private async Task PrepareBlockAndFileListAsync(LocalRestoreDatabase database, Options options, Library.Utility.IFilter? filter, IRestoreDestinationProvider restoreDestination, RestoreResults result, string? restoredHashesTable)
        {
            // Create a temporary table FILES by selecting the files from fileset that matches a specific operation id
            // Delete all entries from the temp table that are excluded by the filter(s)
            using (new Logging.Timer(LOGTAG, "PrepareRestoreFileList", "PrepareRestoreFileList"))
            {
                var c = await database
                    .PrepareRestoreFilelistAsync(options.Time, options.Version, filter, options.DisableAdsRestore, result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

                // When --restore-all-files=unique is active, remove files already restored in
                // a previous version. The uniqueness check is on the file hash only. The set of
                // already-restored hashes is kept in a persistent database table (named by
                // restoredHashesTable) so it does not grow unbounded in memory.
                if (!string.IsNullOrEmpty(restoredHashesTable) && c.Item1 > 0)
                {
                    using (new Logging.Timer(LOGTAG, "RemoveRestoredHashes", "Removing files already restored in a previous version"))
                    {
                        var removed = await database
                            .RemoveFilesByRestoredHashesAsync(restoredHashesTable, result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        if (removed > 0)
                            Logging.Log.WriteVerboseMessage(LOGTAG, "SkippedAlreadyRestored", "Skipped {0} file(s) already restored in a previous version", removed);

                        // Recompute the file count after removal.
                        c = await database
                            .PrepareRestoreFilelistCountAsync(result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                    }
                }

                // When --skip-files-larger-than is set, remove files whose size exceeds the
                // threshold from the prepared file list. Doing it here (before the missing-blocks
                // table is built and before any file is queued for restore) means neither the new
                // channel-based flow nor the legacy flow attempts to restore them, and the blocks
                // belonging to the skipped files are never requested. Folders and symlinks are not
                // affected (they use special blockset IDs without a matching Blockset row).
                var skipFilesLargerThan = options.SkipFilesLargerThan;
                if (skipFilesLargerThan > 0 && skipFilesLargerThan != long.MaxValue && c.Item1 > 0)
                {
                    using (new Logging.Timer(LOGTAG, "RemoveFilesLargerThan", "Removing files larger than the skip threshold"))
                    {
                        var removed = await database
                            .RemoveFilesLargerThanAsync(skipFilesLargerThan, result.TaskControl.ProgressToken)
                            .ConfigureAwait(false);
                        if (removed > 0)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "SkippedLargeFiles", "Skipped {0} file(s) larger than {1}", removed, Library.Utility.Utility.FormatSizeString(skipFilesLargerThan));

                            // Recompute the file count after removal.
                            c = await database
                                .PrepareRestoreFilelistCountAsync(result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                        }
                    }
                }

                result.OperationProgressUpdater.UpdatefileCount(c.Item1, c.Item2, true);

                // If the selection is completely empty (no files, folders, or symlinks),
                // stop now as this is most likely not what is desired.
                var firstPath = await database
                    .GetFirstPathAsync(result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(firstPath))
                    throw new UserInformationException("Restore selection matched zero files, nothing to restore", "EmptyRestoreOperation");
            }

            if (options.DisableAdsRestore)
            {
                using (new Logging.Timer(LOGTAG, "FilterAds", "Filtering alternate data streams from restore list"))
                    await database.RemoveAlternateDataStreamsAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);
            }

            using (new Logging.Timer(LOGTAG, "SetTargetPaths", "SetTargetPaths"))
                if (!string.IsNullOrEmpty(restoreDestination.TargetDestination))
                {
                    // Find the largest common prefix
                    var largest_prefix = options.DontCompressRestorePaths
                        ? "" :
                        await database.GetLargestPrefixAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);

                    largest_prefix = await GetRestorePathPrefixAsync(database, options, largest_prefix, result.TaskControl.ProgressToken).ConfigureAwait(false);

                    Logging.Log.WriteVerboseMessage(LOGTAG, "MappingRestorePath", "Mapping restore path prefix to \"{0}\" to \"{1}\"", largest_prefix, Util.AppendDirSeparator(restoreDestination.TargetDestination));

                    // Set the target paths, special care with C:\ and /
                    await database
                        .SetTargetPathsAsync(largest_prefix, Util.AppendDirSeparator(restoreDestination.TargetDestination), result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await database.SetTargetPathsAsync("", "", result.TaskControl.ProgressToken).ConfigureAwait(false);
                }

            // Create a temporary table BLOCKS that lists all blocks that needs to be recovered
            using (new Logging.Timer(LOGTAG, "FindMissingBlocks", "FindMissingBlocks"))
                await database
                    .FindMissingBlocksAsync(options.SkipMetadata, result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

            // Create temporary tables and triggers that automatically track progress
            using (new Logging.Timer(LOGTAG, "CreateProgressTracker", "CreateProgressTracker"))
                await database
                    .CreateProgressTrackerAsync(false, result.TaskControl.ProgressToken)
                    .ConfigureAwait(false);

        }

        /// <summary>
        /// Gets a single explicitly selected folder from either a literal folder path or
        /// the escaped regular-expression form generated by the web UI.
        /// </summary>
        private static string? GetSelectedRestoreFolder(string[]? paths)
        {
            if (paths == null || paths.Length != 1)
                return null;

            var path = paths[0];
            if (path.EndsWith("/", StringComparison.Ordinal) || path.EndsWith("\\", StringComparison.Ordinal))
                return path;

            const string regexSuffix = ".*]";
            if (!path.StartsWith("[", StringComparison.Ordinal) || !path.EndsWith(regexSuffix, StringComparison.Ordinal))
                return null;

            var escapedFolder = path.Substring(1, path.Length - regexSuffix.Length - 1);
            try
            {
                var folder = Regex.Unescape(escapedFolder);
                return Regex.Escape(folder).Equals(escapedFolder, StringComparison.Ordinal)
                    && (folder.EndsWith("/", StringComparison.Ordinal) || folder.EndsWith("\\", StringComparison.Ordinal))
                        ? folder
                        : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        /// <summary>
        /// Adjusts the prefix removed from restored paths so an explicitly selected folder
        /// is created below the restore destination instead of only restoring its contents.
        /// </summary>
        private async Task<string> GetRestorePathPrefixAsync(LocalRestoreDatabase database, Options options, string largestPrefix, CancellationToken token)
        {
            var selectedFolder = m_selectedRestoreFolder;
            if (string.IsNullOrEmpty(largestPrefix) || string.IsNullOrEmpty(selectedFolder))
                return largestPrefix;

            var separator = Util.GuessDirSeparator(selectedFolder);
            var normalizedSelectedFolder = Util.AppendDirSeparator(selectedFolder, separator);
            if (!largestPrefix.Equals(normalizedSelectedFolder, Library.Utility.Utility.ClientFilenameStringComparison))
                return largestPrefix;

            var folderWithoutSeparator = selectedFolder.TrimEnd(separator[0]);
            var separatorIndex = folderWithoutSeparator.LastIndexOf(separator, StringComparison.Ordinal);

            // Root folders have no parent path whose removal could retain their name.
            if (separatorIndex < 0)
                return largestPrefix;

            // Only retain the selected folder when the backup contains entries outside it.
            // Selecting the backup's own root (e.g. restoring an entire source) would otherwise
            // gain a spurious leading folder and change where the contents are restored.
            if (!await database.HasEntriesOutsideFolderAsync(options.Time, options.Version, normalizedSelectedFolder, token).ConfigureAwait(false))
                return largestPrefix;

            return folderWithoutSeparator.Substring(0, separatorIndex + 1);
        }

        private static async Task CreateDirectoryStructureAsync(LocalRestoreDatabase database, IRestoreDestinationProvider restoreDestination, bool restoreToOriginalLocation, Options options, RestoreResults result)
        {
            // This part is not protected by try/catch as we need the target folder to exist
            if (!string.IsNullOrEmpty(restoreDestination.TargetDestination))
            {
                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldCreateFolder", "Would create folder: {0}", restoreDestination.TargetDestination);
                }
                else if (await restoreDestination.CreateFolderIfNotExists(restoreDestination.TargetDestination, result.TaskControl.ProgressToken).ConfigureAwait(false))
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "CreateFolder", "Created root restore folder: {0}", restoreDestination.TargetDestination);
                    result.RestoredFolders++;
                }
            }

            await foreach (var folder in database.GetTargetFoldersAsync(result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                try
                {
                    if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                        return;

                    if (options.Dryrun)
                        Logging.Log.WriteDryrunMessage(LOGTAG, "WouldCreateFolder", "Would create folder: {0}", folder);
                    else if (await restoreDestination.CreateFolderIfNotExists(folder, result.TaskControl.ProgressToken).ConfigureAwait(false))
                    {
                        Logging.Log.WriteVerboseMessage(LOGTAG, "CreateFolder", "Created folder: {0}", folder);
                        result.RestoredFolders++;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FolderCreateFailed", ex, "Failed to create folder: \"{0}\", message: {1}", folder, ex.Message);
                    if (options.UnittestMode)
                        throw;
                }
            }
        }

        private static async Task ResetReadOnlyAttributeIfNeededAsync(string path, Options options, IRestoreDestinationProvider restoreDestination, CancellationToken cancellationToken)
        {
            if (await restoreDestination.HasReadOnlyAttribute(path, cancellationToken).ConfigureAwait(false))
            {
                if (options.Dryrun)
                    Logging.Log.WriteDryrunMessage(LOGTAG, "WouldResetReadOnlyAttribute", "Would reset read-only attribute on file: {0}", path);
                else
                    await restoreDestination.ClearReadOnlyAttribute(path, cancellationToken).ConfigureAwait(false);
            }
        }

        private static async Task ScanForExistingTargetBlocksAsync(LocalRestoreDatabase database, byte[] blockbuffer, System.Security.Cryptography.HashAlgorithm blockhasher, System.Security.Cryptography.HashAlgorithm filehasher, Options options, IRestoreDestinationProvider restoreDestination, RestoreResults result)
        {
            // Scan existing files for existing BLOCKS
            await using var blockmarker = await database.CreateBlockMarkerAsync(result.TaskControl.ProgressToken).ConfigureAwait(false);
            var updateCount = 0L;
            await foreach (var restorelist in database.GetExistingFilesWithBlocksAsync(result.TaskControl.ProgressToken).ConfigureAwait(false))
            {
                var rename = !options.Overwrite;
                var targetpath = restorelist.TargetPath;
                var targetfileid = restorelist.TargetFileID;
                var targetfilehash = restorelist.TargetHash;
                var targetfilelength = restorelist.Length;
                var fileExists = false;
                try
                {
                    fileExists = await restoreDestination.FileExists(targetpath, result.TaskControl.ProgressToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "FileExistsFailed", ex, "Failed to check if file exists: \"{0}\", message: {1}", targetpath, ex.Message);
                    if (options.UnittestMode)
                        throw;
                }

                if (fileExists)
                {
                    try
                    {
                        if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                            return;

                        var currentfilelength = await restoreDestination.GetFileLength(targetpath, result.TaskControl.ProgressToken).ConfigureAwait(false);
                        var wasTruncated = false;

                        // Adjust file length in overwrite mode if necessary (smaller is ok, will be extended during restore)
                        // We do it before scanning for blocks. This allows full verification on files that only needs to
                        // be truncated (i.e. forthwritten log files).
                        if (!rename && currentfilelength > targetfilelength)
                        {
                            await ResetReadOnlyAttributeIfNeededAsync(targetpath, options, restoreDestination, result.TaskControl.ProgressToken).ConfigureAwait(false);
                            if (options.Dryrun)
                                Logging.Log.WriteDryrunMessage(LOGTAG, "WouldTruncateFile", "Would truncate file '{0}' to length of {1:N0} bytes", targetpath, targetfilelength);
                            else
                            {
                                using (var file = await restoreDestination.OpenWrite(targetpath, result.TaskControl.ProgressToken).ConfigureAwait(false))
                                    file.SetLength(targetfilelength);
                                currentfilelength = targetfilelength;
                            }
                            wasTruncated = true;
                        }

                        // If file size does not match and we have to rename on conflict,
                        // the whole scan can be skipped here because all blocks have to be restored anyway.
                        // For the other cases, we will check block and and file hashes and look for blocks
                        // to be restored and files that can already be verified.
                        if (!rename || currentfilelength == targetfilelength)
                        {
                            // a file hash for verification will only be necessary if the file has exactly
                            // the wanted size so we have a chance to already mark the file as data-verified.
                            var calcFileHash = currentfilelength == targetfilelength;
                            if (calcFileHash) filehasher.Initialize();

                            using (var file = await restoreDestination.OpenRead(targetpath, result.TaskControl.ProgressToken).ConfigureAwait(false))
                            using (var block = new Blockprocessor(file, blockbuffer))
                                await foreach (var targetblock in restorelist.BlocksAsync(result.TaskControl.ProgressToken).ConfigureAwait(false))
                                {
                                    var size = block.Readblock();
                                    if (size <= 0)
                                        break;

                                    //TODO: Handle Metadata

                                    bool blockhashmatch = false;
                                    if (size == targetblock.Size)
                                    {
                                        // Parallelize file hash calculation on rename. Running read-only on same array should not cause conflicts or races.
                                        // Actually, in future always calculate the file hash and mark the file data as already verified.

                                        var calcFileHashTask = Task.CompletedTask;
                                        if (calcFileHash)
                                            calcFileHashTask = Task.Run(
                                                () => filehasher.TransformBlock(blockbuffer, 0, size, blockbuffer, 0));

                                        var key = Convert.ToBase64String(blockhasher.ComputeHash(blockbuffer, 0, size));

                                        await calcFileHashTask.ConfigureAwait(false); // wait because blockbuffer will be overwritten.

                                        if (key == targetblock.Hash)
                                        {
                                            await blockmarker
                                                .SetBlockRestoredAsync(targetfileid, targetblock.Index, key, size, false, result.TaskControl.ProgressToken)
                                                .ConfigureAwait(false);
                                            blockhashmatch = true;
                                        }
                                    }
                                    if (calcFileHash && !blockhashmatch) // will not be necessary anymore
                                    {
                                        filehasher.TransformFinalBlock(blockbuffer, 0, 0); // So a new initialize will not throw
                                        calcFileHash = false;
                                        if (rename) // file does not match. So break.
                                            break;
                                    }
                                }

                            bool fullfilehashmatch = false;
                            if (calcFileHash) // now check if files are identical
                            {
                                filehasher.TransformFinalBlock(blockbuffer, 0, 0);
                                var filekey = Convert.ToBase64String(filehasher.Hash ?? throw new InvalidDataException("Unexpected null hash result"));
                                fullfilehashmatch = (filekey == targetfilehash);
                            }

                            if (!rename && !fullfilehashmatch && !wasTruncated) // Reset read-only attribute (if set) to overwrite
                                await ResetReadOnlyAttributeIfNeededAsync(targetpath, options, restoreDestination, result.TaskControl.ProgressToken).ConfigureAwait(false);

                            if (fullfilehashmatch)
                            {
                                //TODO: Check metadata to trigger rename? If metadata changed, it will still be restored for the file in-place.
                                await blockmarker
                                    .SetFileDataVerifiedAsync(targetfileid, result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                Logging.Log.WriteVerboseMessage(LOGTAG, "TargetExistsInCorrectVersion", "Target file exists{1} and is correct version: {0}", targetpath, wasTruncated ? " (but was truncated)" : "");
                                rename = false;

                                result.UnmodifiedFiles++;
                                result.SizeOfUnmodifiedFiles += targetfilelength;
                            }
                            else if (rename)
                            {
                                // The new file will have none of the correct blocks,
                                // even if the scanned file had some
                                await blockmarker
                                    .SetAllBlocksMissingAsync(targetfileid, result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                            }
                        }

                        if ((++updateCount) % 20 == 0)
                        {
                            await blockmarker
                                .UpdateProcessedAsync(result.OperationProgressUpdater, result.TaskControl.ProgressToken)
                                .ConfigureAwait(false);
                            if (!await result.TaskControl.ProgressRendevouzAsync().ConfigureAwait(false))
                                return;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "TargetFileReadError", ex, "Failed to read target file: \"{0}\", message: {1}", targetpath, ex.Message);
                        if (ex.IsAbortException())
                            throw;
                        if (options.UnittestMode)
                            throw;
                    }
                }
                else
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "MissingTargetFile", "Target file does not exist: {0}", targetpath);
                    rename = false;
                }

                if (rename)
                {
                    //Select a new filename
                    var ext = SystemIO.IO_OS.PathGetExtension(targetpath) ?? "";
                    if (!string.IsNullOrEmpty(ext) && !ext.StartsWith(".", StringComparison.Ordinal))
                        ext = "." + ext;

                    // First we try with a simple date append, assuming that there are not many conflicts there
                    var newname = SystemIO.IO_OS.PathChangeExtension(targetpath, null) + "." + database.RestoreTime.ToLocalTime().ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    var tr = newname + ext;
                    var c = 0;
                    while (await restoreDestination.FileExists(tr, result.TaskControl.ProgressToken).ConfigureAwait(false) && c < 1000)
                    {
                        try
                        {
                            // If we have a file with the correct name,
                            // it is most likely the file we want
                            filehasher.Initialize();

                            string key;
                            using (var file = await restoreDestination.OpenRead(tr, result.TaskControl.ProgressToken).ConfigureAwait(false))
                                key = Convert.ToBase64String(filehasher.ComputeHash(file));

                            if (key == targetfilehash)
                            {
                                //TODO: Also needs metadata check to make correct decision.
                                //      We stick to the policy to restore metadata in place, if data ok. So, metadata block may be restored.
                                await blockmarker
                                    .SetAllBlocksRestoredAsync(targetfileid, false, result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                await blockmarker
                                    .SetFileDataVerifiedAsync(targetfileid, result.TaskControl.ProgressToken)
                                    .ConfigureAwait(false);
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteWarningMessage(LOGTAG, "FailedToReadRestoreTarget", ex, "Failed to read candidate restore target {0}", tr);
                            if (options.UnittestMode)
                                throw;
                        }
                        tr = newname + " (" + (c++).ToString() + ")" + ext;
                    }

                    newname = tr;

                    Logging.Log.WriteVerboseMessage(LOGTAG, "TargetFileRetargeted", "Target file exists and will be restored to: {0}", newname);
                    await database
                        .UpdateTargetPathAsync(targetfileid, newname, result.TaskControl.ProgressToken)
                        .ConfigureAwait(false);
                }

            }

            await blockmarker
                .UpdateProcessedAsync(result.OperationProgressUpdater, result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
            await blockmarker
                .CommitAsync(result.TaskControl.ProgressToken)
                .ConfigureAwait(false);
        }
    }
}
