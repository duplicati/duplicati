#region Disclaimer / License

// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 

#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Snapshots
{
    public class UsnJournalService
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType<UsnJournalService>();

        private readonly ISnapshotService m_snapshot;
        private readonly Utility.Utility.ReportAccessError m_errorCallback;
        private readonly Action m_cancelHandler;
        private readonly IEnumerable<USNJournalDataEntry> m_prevJournalData;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="snapshot"></param>
        /// <param name="journalData">Journal-data of previous fileset</param>
        /// <param name="errorCallback">Callback for handling errors</param>
        /// <param name="cancelHandler">Callback for aborting operation. Throw OperationCancelledException() to abort.</param>
        public UsnJournalService(ISnapshotService snapshot, IEnumerable<USNJournalDataEntry> journalData, Utility.Utility.ReportAccessError errorCallback,
            Action cancelHandler)
        {
            m_snapshot = snapshot;
            m_errorCallback = errorCallback;
            m_cancelHandler = cancelHandler;
            m_prevJournalData = journalData;
        }

        public FilterData Result { get; private set; }

        /// <summary>
        /// Filters sources, returning sub-set having been modified since last
        /// change, as specified by <c>journalData</c>.
        /// </summary>
        /// <param name="sources">Sources to filter</param>
        /// <param name="filter">Filter callback to exclude filtered items</param>
        /// <param name="filterHash">A hash value representing current exclusion filter. A full scan is triggered if hash has changed.</param>
        /// <returns>Filtered sources</returns>
        public void FilterSources(IEnumerable<string> sources, Utility.Utility.EnumerationFilterDelegate filter, string filterHash)
        {
            // create lookup for journal data
            var journalDataEntries = m_prevJournalData.ToList();
            var journalDataDict = journalDataEntries.ToDictionary(data => data.Volume);

            // prepare result   
            Result = new FilterData
            {
                Files = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer),
                Folders = new List<string>(),
                JournalData = journalDataEntries.ConvertAll(p => p) // clone source data as default value
            };

            // iterate over volumes
            foreach (var sourcesPerVolume in SortByVolume(sources))
            {
                var volume = sourcesPerVolume.Key;
                var volumeSources = sourcesPerVolume.Value;

                try
                {
                    // prepare journal data entry to store with current fileset
                    var journal = new USNJournal(volume, m_cancelHandler);
                    var nextData = new USNJournalDataEntry
                    {
                        Volume = volume,
                        JournalId = journal.JournalId,
                        NextUsn = journal.NextUsn,
                        ConfigHash = filterHash
                    };

                    // remove default data
                    Result.JournalData.RemoveAll(e => e.Volume == volume);

                    // add new data
                    Result.JournalData.Add(nextData);

                    // only use change journal if:
                    // - journal ID hasn't changed
                    // - nextUsn isn't zero (we use this as magic value to force a rescan)
                    // - the exclude filter hash hasn't changed
                    if (!journalDataDict.TryGetValue(volume, out var data)
                        || data.JournalId != nextData.JournalId
                        || data.NextUsn == 0
                        || data.ConfigHash != nextData.ConfigHash)
                    {
                        ScheduleFullScan(sourcesPerVolume, volume);
                    }
                    else
                    {
                        var changedFiles = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);
                        var changedFolders = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);

                        // obtain changed files and folders, per volume
                        foreach (var source in volumeSources)
                        {
                            foreach (var entry in journal.GetChangedFileSystemEntries(source, data.NextUsn))
                            {
                                if (entry.Item2.HasFlag(USNJournal.EntryType.File))
                                {
                                    changedFiles.Add(entry.Item1);
                                }
                                else
                                {
                                    changedFolders.Add(Utility.Utility.AppendDirSeparator(entry.Item1));
                                }
                            }
                        }

                        // prepare cache for includes (value = true) and excludes (value = false, will be populated
                        // on-demand)
                        var cache = new Dictionary<string, bool>();
                        volumeSources.ForEach(p => cache[p] = true);

                        // At this point we have:
                        //  - a list of folders (changedFolders) that were possibly modified 
                        //  - a list of files (changedFiles) that were possibly modified
                        //
                        // With this, we need still need to do the following:
                        //
                        //  1. Simplify the folder list, such that it only contains the parent-most entries 
                        //     (eg. { "C:\A\B\", "C:\A\B\C\", "C:\A\B\D\E\" } => { "C:\A\B\" }
                        var simplifiedFolders = Utility.Utility.SimplifyFolderList(changedFolders).ToList();

                        // 2. Check the simplified folders, and their parent folders  against the exclusion filter.
                        // This is needed because the filter may exclude "C:\A\", but this won't match the more
                        // specific "C:\A\B\" in our list, even though it's meant to be excluded.
                        // The reason why the filter doesn't exclude it is because during a regular (non-USN) full scan, 
                        // FilterHandler.EnumerateFilesAndFolders() works top-down, and won't even enumerate child
                        // folders. 
                        // The sources are needed to stop evaluating parent folders above the specified source folders

                        Result.Folders.AddRange(FilterExcludedFolders(simplifiedFolders, filter, cache));

                        // 3. Our list of files may contain entries inside one of the simplified folders (from step 1., above).
                        //    Since that folder is going to be fully scanned, those files can be removed.
                        //    Note: it would be wrong to use the result from step 2. as the folder list! The entries removed
                        //          between 1. and 2. are *excluded* folders, and files below them are to be *excluded*, too.
                        var simplifiedFiles = Utility.Utility.GetFilesNotInFolders(changedFiles, simplifiedFolders);

                        // 4. The simplified file list still needs to be checked against the exclusion filter, as it 
                        //    may contain entries excluded due to attributes, but also because they are below excluded
                        //    folders, which themselves aren't in the folder list from step 1.
                        //    Note that the simplified file list may contain entries that have been deleted! They need to 
                        //    be kept in the list (unless excluded by the filter) in order for the backup handler to record their 
                        //    deletion.
                        Result.Files.UnionWith(FilterExcludedFiles(simplifiedFiles, filter, cache));
                    }
                }
                catch (UsnJournalSoftFailureException)
                {
                    // journal is fine, but we cannot recover gapless changes since last time
                    // => schedule full scan
                    ScheduleFullScan(sourcesPerVolume, volume);
                }
                catch (Exception e)
                {
                    m_errorCallback(volume, volume, e);
                    ScheduleFullScan(sourcesPerVolume, volume);
                }
            }
        }

        /// <summary>
        /// Filter supplied <c>files</c>, removing any files which itself, or one
        /// of its parent folders, is excluded by the <c>filter</c>.
        /// </summary>
        /// <param name="files">Files to filter</param>
        /// <param name="filter">Exclusion filter</param>
        /// <param name="cache">Cache of included and exculded files / folders</param>
        /// <returns>Filtered files</returns>
        private IEnumerable<string> FilterExcludedFiles(IEnumerable<string> files,
            Utility.Utility.EnumerationFilterDelegate filter, IDictionary<string, bool> cache)
        {
            var result = new List<string>();

            foreach (var file in files)
            {                
                var attr = m_snapshot.FileExists(file) ? m_snapshot.GetAttributes(file) : FileAttributes.Normal;
                try
                {
                    if (!filter(file, file, attr))
                        continue;

                    if (!IsFolderOrAncestorsExcluded(Utility.Utility.GetParent(file, true), filter, cache))
                    {
                        result.Add(file);
                    }
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_errorCallback?.Invoke(file, file, ex);
                    filter(file, file, attr | Utility.Utility.ATTRIBUTE_ERROR);
                }
            }

            return result;
        }

        /// <summary>
        /// Filter supplied <c>folders</c>, removing any folder which itself, or one
        /// of its ancestors, is excluded by the <c>filter</c>.
        /// </summary>
        /// <param name="folders">Folder to filter</param>
        /// <param name="filter">Exclusion filter</param>
        /// <param name="cache">Cache of excluded folders (optional)</param>
        /// <returns>Filtered folders</returns>
        private IEnumerable<string> FilterExcludedFolders(IEnumerable<string> folders,
            Utility.Utility.EnumerationFilterDelegate filter, IDictionary<string, bool> cache)
        {
            var result = new List<string>();

            foreach (var folder in folders)
            {
                try
                {
                    if (!IsFolderOrAncestorsExcluded(folder, filter, cache))
                    {
                        result.Add(folder);
                    }
                }
                catch (System.Threading.ThreadAbortException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    m_errorCallback?.Invoke(folder, folder, ex);
                    filter(folder, folder, FileAttributes.Directory | Utility.Utility.ATTRIBUTE_ERROR);
                }
            }

            return result;
        }

        /// <summary>
        /// Tests if specified folder, or any of its ancestors, is excluded by the filter
        /// </summary>
        /// <param name="folder">Folder to test</param>
        /// <param name="filter">Filter</param>
        /// <param name="cache">Cache of excluded folders (optional)</param>
        /// <returns>True if excluded, false otherwise</returns>
        private bool IsFolderOrAncestorsExcluded(string folder, Utility.Utility.EnumerationFilterDelegate filter, IDictionary<string, bool> cache)
        {
            List<string> parents = null;
            while (folder != null)
            {
                // first check cache
                if (cache.TryGetValue(folder, out var include))
                {
                    if (include)
                        return false;

                    break; // hit!
                }

                // remember folder for cache
                if (parents == null)
                {
                    parents = new List<string>(); // create on-demand
                }
                parents.Add(folder);


                var attr = m_snapshot.DirectoryExists(folder) ? m_snapshot.GetAttributes(folder) : FileAttributes.Directory;

                if (!filter(folder, folder, attr))
                    break; // excluded

                folder = Utility.Utility.GetParent(folder, true);
            }

            if (folder != null)
            {
                // update cache
                parents?.ForEach(p => cache[p] = false);
            }
 
            return folder != null;
        }

        /// <summary>
        /// Add ALL sources for volume to result set
        /// </summary>
        /// <param name="sourcesPerVolume">Sources</param>
        /// <param name="volume">Volume</param>
        private void ScheduleFullScan(KeyValuePair<string, List<string>> sourcesPerVolume, string volume)
        {
            foreach (var src in sourcesPerVolume.Value)
            {
                if (src.EndsWith(Utility.Utility.DirectorySeparatorString, StringComparison.Ordinal))
                {
                    Result.Folders.Add(src);
                }
                else
                {
                    Result.Files.Add(src);
                }
            }

            Log.WriteInformationMessage(LOGTAG, "SkipUsnForVolume", $"Performing full scan for volume \"{volume}\"");
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
                var root = USNJournal.GetVolumeRootFromPath(path);

                if (!sourcesByVolume.TryGetValue(root, out var list))
                {
                    list = new List<string>();
                    sourcesByVolume.Add(root, list);
                }

                list.Add(path);
            }

            return sourcesByVolume;
        }

    }
}
