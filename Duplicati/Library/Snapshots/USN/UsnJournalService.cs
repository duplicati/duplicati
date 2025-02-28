// Copyright (C) 2025, The Duplicati Team
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
using System.Runtime.Versioning;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Snapshots.USN
{
    [SupportedOSPlatform("windows")]
    public class UsnJournalService
    {
        /// <summary>
        /// The log tag to use
        /// </summary>
        private static readonly string FILTER_LOGTAG = Logging.Log.LogTagFromType(typeof(UsnJournalService));

        /// <summary>
        /// The snapshot service
        /// </summary>
        private readonly ISnapshotService m_snapshot;
        /// <summary>
        /// The volume data dictionary
        /// </summary>
        private readonly Dictionary<string, VolumeData> m_volumeDataDict;
        /// <summary>
        /// The cancellation token
        /// </summary>
        private readonly CancellationToken m_token;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="emitFilter">Emit filter</param>
        /// <param name="fileAttributeFilter"></param>
        /// <param name="skipFilesLargerThan"></param>
        /// <param name="prevJournalData">Journal-data of previous fileset</param>
        /// <param name="token"></param>
        public UsnJournalService(ISnapshotService snapshot, IFilter emitFilter, FileAttributes fileAttributeFilter,
            long skipFilesLargerThan, IEnumerable<USNJournalDataEntry> prevJournalData, CancellationToken token)
        {
            m_snapshot = snapshot;
            m_volumeDataDict = Initialize(emitFilter, fileAttributeFilter, skipFilesLargerThan, prevJournalData);
            m_token = token;
        }

        /// <summary>
        /// The volume data lists for the mapped root drives
        /// </summary>
        public IEnumerable<VolumeData> VolumeDataList => m_volumeDataDict.Select(e => e.Value);

        /// <summary>
        /// Initialize list of modified files / folder for each volume
        /// </summary>
        /// <param name="emitFilter"></param>
        /// <param name="fileAttributeFilter"></param>
        /// <param name="skipFilesLargerThan"></param>
        /// <param name="prevJournalData"></param>
        /// <returns></returns>
        private Dictionary<string, VolumeData> Initialize(IFilter emitFilter, FileAttributes fileAttributeFilter, long skipFilesLargerThan,
            IEnumerable<USNJournalDataEntry> prevJournalData)
        {
            if (prevJournalData == null)
                throw new UsnJournalSoftFailureException(Strings.USNHelper.PreviousBackupNoInfo);

            var result = new Dictionary<string, VolumeData>();

            // get hash identifying current source filter / sources configuration
            var configHash = Utility.Utility.ByteArrayAsHexString(MD5HashHelper.GetHash(new string[] {
                emitFilter?.ToString() ?? string.Empty,
                string.Join("; ", m_snapshot.SourceEntries),
                fileAttributeFilter.ToString(),
                skipFilesLargerThan.ToString()
            }));

            // create lookup for journal data
            var journalDataDict = prevJournalData.ToDictionary(data => data.Volume);

            // iterate over volumes
            foreach (var sourcesPerVolume in SortByVolume(m_snapshot.SourceEntries))
            {
                Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "UsnInitialize", "Reading USN journal for volume: {0}", sourcesPerVolume.Key);

                if (m_token.IsCancellationRequested) break;

                var volume = sourcesPerVolume.Key;
                var volumeSources = sourcesPerVolume.Value;
                var volumeData = new VolumeData
                {
                    Volume = volume,
                    JournalData = null
                };
                result[volume] = volumeData;

                try
                {
                    // prepare journal data entry to store with current fileset
                    if (!OperatingSystem.IsWindows())
                        throw new Interface.UserInformationException(Strings.USNHelper.LinuxNotSupportedError, "UsnOnLinuxNotSupported");

                    var journal = new USNJournal(volume);
                    var nextData = new USNJournalDataEntry
                    {
                        Volume = volume,
                        JournalId = journal.JournalId,
                        NextUsn = journal.NextUsn,
                        ConfigHash = configHash
                    };

                    // add new data to result set
                    volumeData.JournalData = nextData;

                    // only use change journal if:
                    // - journal ID hasn't changed
                    // - nextUsn isn't zero (we use this as magic value to force a rescan)
                    // - the configuration (sources or filters) hasn't changed
                    if (!journalDataDict.TryGetValue(volume, out var prevData))
                        throw new UsnJournalSoftFailureException(Strings.USNHelper.PreviousBackupNoInfo);

                    if (prevData.JournalId != nextData.JournalId)
                        throw new UsnJournalSoftFailureException(Strings.USNHelper.JournalIdChanged);

                    if (prevData.NextUsn == 0)
                        throw new UsnJournalSoftFailureException(Strings.USNHelper.NextUsnZero);

                    if (prevData.ConfigHash != nextData.ConfigHash)
                        throw new UsnJournalSoftFailureException(Strings.USNHelper.ConfigHashChanged);

                    var changedFiles = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);
                    var changedFolders = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);

                    // obtain changed files and folders, per volume
                    foreach (var source in volumeSources)
                    {
                        if (m_token.IsCancellationRequested) break;

                        foreach (var entry in journal.GetChangedFileSystemEntries(source, prevData.NextUsn))
                        {
                            if (m_token.IsCancellationRequested) break;

                            if (entry.Item2.HasFlag(USNJournal.EntryType.File))
                            {
                                changedFiles.Add(entry.Item1);
                            }
                            else
                            {
                                changedFolders.Add(Util.AppendDirSeparator(entry.Item1));
                            }
                        }
                    }

                    // At this point we have:
                    //  - a list of folders (changedFolders) that were possibly modified 
                    //  - a list of files (changedFiles) that were possibly modified
                    //
                    // With this, we need still need to do the following:
                    //
                    // 1. Simplify the folder list, such that it only contains the parent-most entries 
                    //     (e.g. { "C:\A\B\", "C:\A\B\C\", "C:\A\B\D\E\" } => { "C:\A\B\" }
                    volumeData.Folders = Utility.Utility.SimplifyFolderList(changedFolders).ToList();

                    // 2. Our list of files may contain entries inside one of the simplified folders (from step 1., above).
                    //    Since that folder is going to be fully scanned, those files can be removed.
                    //    Note: it would be wrong to use the result from step 2. as the folder list! The entries removed
                    //          between 1. and 2. are *excluded* folders, and files below them are to be *excluded*, too.
                    volumeData.Files = [.. Utility.Utility.GetFilesNotInFolders(changedFiles, volumeData.Folders)];

                    // Record success for volume
                    volumeData.IsFullScan = false;
                }
                catch (Exception e)
                {
                    // full scan is required this time (e.g. due to missing journal entries)
                    volumeData.Exception = e;
                    volumeData.IsFullScan = true;
                    volumeData.Folders = new List<string>();
                    volumeData.Files = new HashSet<string>();

                    // use original sources
                    foreach (var path in volumeSources)
                    {
                        var isFolder = path.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal);
                        if (isFolder)
                        {
                            volumeData.Folders.Add(path);
                        }
                        else
                        {
                            volumeData.Files.Add(path);
                        }
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Returns all sources that should be fully scanned
        /// </summary>
        /// <param name="token">Cancellation token</param>
        /// <returns>Filtered sources</returns>
        public async IAsyncEnumerable<ISourceProviderEntry> GetFullScanSources([EnumeratorCancellation] CancellationToken token)
        {
            // Make the method async enumerable
            await Task.CompletedTask;

            foreach (var volumeData in m_volumeDataDict.Values.Where(x => x.IsFullScan))
            {
                if (volumeData.Folders != null)
                {
                    foreach (var folderPath in volumeData.Folders)
                    {
                        if (token.IsCancellationRequested)
                            yield break;

                        var folder = m_snapshot.GetFilesystemEntry(folderPath, true);
                        if (folder != null)
                            yield return folder;
                        else
                            Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "FolderLookupError", null, "No entry while locating source folder {0}", folderPath);
                    }
                }

                if (volumeData.Files != null)
                {
                    foreach (var filePath in volumeData.Files)
                    {
                        if (token.IsCancellationRequested)
                            yield break;

                        var file = m_snapshot.GetFilesystemEntry(filePath, false);
                        if (file != null)
                            yield return file;
                        else
                            Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "FolderLookupError", null, "No entry while locating source file {0}", filePath);
                    }
                }
            }
        }


        /// <summary>
        /// Filters sources, returning sub-set having been modified since last
        /// change, as specified by <c>journalData</c>.
        /// </summary>
        /// <param name="filter">Filter callback to exclude filtered items</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Filtered sources</returns>
        public async IAsyncEnumerable<ISourceProviderEntry> GetModifiedSources(Func<ISourceProviderEntry, ValueTask<bool>> filter, [EnumeratorCancellation] CancellationToken token)
        {
            foreach (var volumeData in m_volumeDataDict.Values.Where(x => !x.IsFullScan))
            {
                // prepare cache for includes (value = true) and excludes (value = false, will be populated
                // on-demand)
                var cache = new Dictionary<string, bool>();
                foreach (var source in m_snapshot.SourceEntries)
                {
                    if (token.IsCancellationRequested)
                        yield break;

                    cache[source] = true;
                }

                // Check the simplified folders, and their parent folders  against the exclusion filter.
                // This is needed because the filter may exclude "C:\A\", but this won't match the more
                // specific "C:\A\B\" in our list, even though it's meant to be excluded.
                // The reason why the filter doesn't exclude it is because during a regular (non-USN) full scan, 
                // FilterHandler.EnumerateFilesAndFolders() works top-down, and won't even enumerate child
                // folders. 
                // The sources are needed to stop evaluating parent folders above the specified source folders
                await foreach (var folder in FilterExcludedFolders(volumeData.Folders, m_snapshot, filter, cache, token).ConfigureAwait(false))
                {
                    if (token.IsCancellationRequested)
                        yield break;

                    if (!m_snapshot.DirectoryExists(folder.Path))
                        continue;

                    yield return folder;
                }

                // The simplified file list also needs to be checked against the exclusion filter, as it 
                // may contain entries excluded due to attributes, but also because they are below excluded
                // folders, which themselves aren't in the folder list from step 1.
                // Note that the simplified file list may contain entries that have been deleted! They need to 
                // be kept in the list (unless excluded by the filter) in order for the backup handler to record their 
                // deletion.
                await foreach (var files in FilterExcludedFiles(volumeData.Files, m_snapshot, filter, cache, token).ConfigureAwait(false))
                {
                    if (token.IsCancellationRequested)
                        yield break;

                    if (!m_snapshot.FileExists(files.Path))
                        continue;

                    yield return files;
                }
            }
        }

        /// <summary>
        /// Filter supplied <c>files</c>, removing any files which itself, or one
        /// of its parent folders, is excluded by the <c>filter</c>.
        /// </summary>
        /// <param name="files">Files to filter</param>
        /// <param name="snapshot">Snapshot service</param>
        /// <param name="filter">Exclusion filter</param>
        /// <param name="cache">Cache of included and excluded files / folders</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Filtered files</returns>
        private static async IAsyncEnumerable<ISourceProviderEntry> FilterExcludedFiles(
            IEnumerable<string>? files,
            ISnapshotService snapshot,
            Func<ISourceProviderEntry, ValueTask<bool>> filter,
            IDictionary<string, bool> cache,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (files == null)
                yield break;

            foreach (var filePath in files)
            {
                ISourceProviderEntry? file;
                try
                {
                    file = snapshot.GetFilesystemEntry(filePath, false);
                    if (file == null)
                        continue;

                    if (!await filter(file))
                        continue;

                    var parentPath = Utility.Utility.GetParent(file.Path, true);
                    if (!string.IsNullOrWhiteSpace(parentPath))
                    {
                        var parent = snapshot.GetFilesystemEntry(parentPath, true);
                        if (parent == null)
                            continue;

                        if (await IsFolderOrAncestorsExcluded(parent, snapshot, filter, cache, token))
                            continue;
                    }
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "FilterExcludedFilesError", ex, "Error while filtering file: {0}", ex.Message);
                    continue;
                }

                if (file != null)
                    yield return file;
            }
        }

        /// <summary>
        /// Filter supplied <c>folders</c>, removing any folder which itself, or one
        /// of its ancestors, is excluded by the <c>filter</c>.
        /// </summary>
        /// <param name="folders">Folder to filter</param>
        /// <param name="snapshot">Snapshot service</param>
        /// <param name="filter">Exclusion filter</param>
        /// <param name="cache">Cache of excluded folders</param>
        /// <param name="token">Cancellation token</param>
        /// <returns>Filtered folders</returns>
        private static async IAsyncEnumerable<ISourceProviderEntry> FilterExcludedFolders(
            IEnumerable<string>? folders,
            ISnapshotService snapshot,
            Func<ISourceProviderEntry, ValueTask<bool>> filter,
            IDictionary<string, bool> cache,
            [EnumeratorCancellation] CancellationToken token)
        {
            if (folders == null)
                yield break;

            foreach (var folderPath in folders)
            {
                ISourceProviderEntry? folder;
                try
                {
                    folder = snapshot.GetFilesystemEntry(folderPath, true);
                    if (folder == null)
                        continue;
                    if (await IsFolderOrAncestorsExcluded(folder, snapshot, filter, cache, token))
                        continue;
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(FILTER_LOGTAG, "FilterExcludedFoldersError", ex, "Error while filtering folder: {0}", ex.Message);
                    continue;
                }

                yield return folder;
            }
        }

        /// <summary>
        /// Tests if specified folder, or any of its ancestors, is excluded by the filter
        /// </summary>
        /// <param name="folder">Folder to test</param>
        /// <param name="filter">Filter</param>
        /// <param name="cache">Cache of excluded folders (optional)</param>
        /// <returns>True if excluded, false otherwise</returns>
        private static async ValueTask<bool> IsFolderOrAncestorsExcluded(
            ISourceProviderEntry inputFolder,
            ISnapshotService snapshot,
            Func<ISourceProviderEntry, ValueTask<bool>> filter,
            IDictionary<string, bool> cache,
            CancellationToken token)
        {
            List<string>? parents = null;
            ISourceProviderEntry? folder = inputFolder;

            while (folder != null)
            {
                if (token.IsCancellationRequested)
                    break;

                // first check cache
                if (cache.TryGetValue(folder.Path, out var include))
                {
                    if (include)
                        return false;

                    break; // hit!
                }

                parents ??= []; // create on-demand

                // remember folder for cache
                parents.Add(folder.Path);


                if (!await filter(folder))
                    break; // excluded

                var parentPath = Utility.Utility.GetParent(folder.Path, true);
                if (string.IsNullOrWhiteSpace(parentPath))
                    break;

                folder = snapshot.GetFilesystemEntry(parentPath, true);
            }

            if (folder != null)
            {
                // update cache
                parents?.ForEach(p => cache[p] = false);
            }

            return folder != null;
        }

        /// <summary>
        /// Sort sources by root volume
        /// </summary>
        /// <param name="sources">List of sources</param>
        /// <returns>Dictionary of volumes, with list of sources as values</returns>
        private static Dictionary<string, List<string>> SortByVolume(IEnumerable<string> sources)
        {
            var sourcesByVolume = new Dictionary<string, List<string>>();
            foreach (var path in sources)
            {
                // get NTFS volume root
                var volumeRoot = USNJournal.GetVolumeRootFromPath(path);

                if (!sourcesByVolume.TryGetValue(volumeRoot, out var list))
                {
                    list = new List<string>();
                    sourcesByVolume.Add(volumeRoot, list);
                }

                list.Add(path);
            }

            return sourcesByVolume;
        }

        /// <summary>
        /// Returns true if path was enumerated by journal service
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public bool IsPathEnumerated(string path)
        {
            // get NTFS volume root
            var volumeRoot = USNJournal.GetVolumeRootFromPath(path);

            // get volume data
            if (!m_volumeDataDict.TryGetValue(volumeRoot, out var volumeData))
                return false;

            if (volumeData.IsFullScan)
                return true; // do not append from previous set, already scanned

            if (volumeData.Files != null && volumeData.Files.Contains(path))
                return true; // do not append from previous set, already scanned

            if (volumeData.Folders == null)
                return false;

            foreach (var folder in volumeData.Folders)
            {
                if (m_token.IsCancellationRequested)
                {
                    break;
                }

                if (path.Equals(folder, Utility.Utility.ClientFilenameStringComparison))
                    return true; // do not append from previous set, already scanned

                if (Utility.Utility.IsPathBelowFolder(path, folder))
                    return true; // do not append from previous set, already scanned
            }

            return false; // append from previous set
        }
    }

    /// <summary>
    /// Filtered sources
    /// </summary>
    public class VolumeData
    {
        /// <summary>
        /// Volume
        /// </summary>
        public required string Volume { get; set; }

        /// <summary>
        /// Set of potentially modified files
        /// </summary>
        public HashSet<string>? Files { get; internal set; }

        /// <summary>
        /// Set of folders that are potentially modified, or whose children
        /// are potentially modified
        /// </summary>
        public List<string>? Folders { get; internal set; }

        /// <summary>
        /// Journal data to use for next backup
        /// </summary>
        public USNJournalDataEntry? JournalData { get; internal set; }

        /// <summary>
        /// If true, a full scan for this volume was required
        /// </summary>
        public bool IsFullScan { get; internal set; }

        /// <summary>
        /// Optional exception message for volume
        /// </summary>
        public Exception? Exception { get; internal set; }
    }
}
