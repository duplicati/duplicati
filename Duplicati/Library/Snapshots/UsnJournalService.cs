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
using Duplicati.Library.Utility;

namespace Duplicati.Library.Snapshots
{
    public class UsnJournalService
    {
        private readonly ISnapshotService m_snapshot;
        private readonly IEnumerable<string> m_sources;
        private readonly Dictionary<string, VolumeData> m_volumeDataDict;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="sources">Sources to filter</param>
        /// <param name="snapshot"></param>
        /// <param name="emitFilter">Emit filter</param>
        /// <param name="prevJournalData">Journal-data of previous fileset</param>
        public UsnJournalService(IEnumerable<string> sources, ISnapshotService snapshot, IFilter emitFilter,
            IEnumerable<USNJournalDataEntry> prevJournalData)
        {
            m_sources = sources;
            m_snapshot = snapshot;
            m_volumeDataDict = Initialize(emitFilter, prevJournalData);
        }

        public IEnumerable<VolumeData> VolumeDataList => m_volumeDataDict.Select(e => e.Value);

        /// <summary>
        /// Initialize list of modified files / folder for each volume
        /// </summary>
        /// <param name="emitFilter"></param>
        /// <param name="prevJournalData"></param>
        /// <returns></returns>
        private Dictionary<string, VolumeData> Initialize(IFilter emitFilter, IEnumerable<USNJournalDataEntry> prevJournalData)
        {
            var result = new Dictionary<string, VolumeData>();

            // get filter identifying current source filter / sources configuration
            // ReSharper disable once PossibleMultipleEnumeration
            var configHash = (emitFilter == null ? string.Empty : emitFilter.GetFilterHash()) + Utility.Utility.ByteArrayAsHexString(MD5HashHelper.GetHash(m_sources));

            // create lookup for journal data
            var journalDataDict = prevJournalData.ToDictionary(data => data.Volume);

            // iterate over volumes
            foreach (var sourcesPerVolume in SortByVolume(m_sources))
            {
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
                    // - the exclude filter hash hasn't changed
                    if (!journalDataDict.TryGetValue(volume, out var prevData) ||
                        prevData.JournalId != nextData.JournalId || prevData.NextUsn == 0 ||
                        prevData.ConfigHash != nextData.ConfigHash)
                    {
                        throw new UsnJournalSoftFailureException();
                    }

                    var changedFiles = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);
                    var changedFolders = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);

                    // obtain changed files and folders, per volume
                    foreach (var source in volumeSources)
                    {
                        foreach (var entry in journal.GetChangedFileSystemEntries(source, prevData.NextUsn))
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

                    // At this point we have:
                    //  - a list of folders (changedFolders) that were possibly modified 
                    //  - a list of files (changedFiles) that were possibly modified
                    //
                    // With this, we need still need to do the following:
                    //
                    // 1. Simplify the folder list, such that it only contains the parent-most entries 
                    //     (eg. { "C:\A\B\", "C:\A\B\C\", "C:\A\B\D\E\" } => { "C:\A\B\" }
                    volumeData.Folders = Utility.Utility.SimplifyFolderList(changedFolders).ToList();

                    // 2. Our list of files may contain entries inside one of the simplified folders (from step 1., above).
                    //    Since that folder is going to be fully scanned, those files can be removed.
                    //    Note: it would be wrong to use the result from step 2. as the folder list! The entries removed
                    //          between 1. and 2. are *excluded* folders, and files below them are to be *excluded*, too.
                    volumeData.Files =
                        new HashSet<string>(Utility.Utility.GetFilesNotInFolders(changedFiles, volumeData.Folders));

                    // Record success for volume
                    volumeData.IsFullScan = false;
                }
                catch (Exception e)
                {
                    // full scan is required this time (eg. due to missing journal entries)
                    volumeData.Exception = e;
                    volumeData.IsFullScan = true;
                    volumeData.Folders = new List<string>();
                    volumeData.Files = new HashSet<string>();

                    // use original sources
                    foreach (var path in volumeSources)
                    {
                        var isFolder = path.EndsWith(Utility.Utility.DirectorySeparatorString, StringComparison.Ordinal);
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
        /// Filters sources, returning sub-set having been modified since last
        /// change, as specified by <c>journalData</c>.
        /// </summary>
        /// <param name="filter">Filter callback to exclude filtered items</param>
        /// <returns>Filtered sources</returns>
        public IEnumerable<string> GetModifiedSources(Utility.Utility.EnumerationFilterDelegate filter)
        {
            // iterate over volumes
            foreach (var volumeData in m_volumeDataDict)
            {
                // prepare cache for includes (value = true) and excludes (value = false, will be populated
                // on-demand)
                var cache = new Dictionary<string, bool>();
                foreach (var source in m_sources)
                {
                    cache[source] = true;
                }

                // Check the simplified folders, and their parent folders  against the exclusion filter.
                // This is needed because the filter may exclude "C:\A\", but this won't match the more
                // specific "C:\A\B\" in our list, even though it's meant to be excluded.
                // The reason why the filter doesn't exclude it is because during a regular (non-USN) full scan, 
                // FilterHandler.EnumerateFilesAndFolders() works top-down, and won't even enumerate child
                // folders. 
                // The sources are needed to stop evaluating parent folders above the specified source folders
                if (volumeData.Value.Folders != null)
                {
                    foreach (var folder in FilterExcludedFolders(volumeData.Value.Folders, filter, cache).Where(m_snapshot.DirectoryExists))
                        yield return folder;
                }

                // The simplified file list also needs to be checked against the exclusion filter, as it 
                // may contain entries excluded due to attributes, but also because they are below excluded
                // folders, which themselves aren't in the folder list from step 1.
                // Note that the simplified file list may contain entries that have been deleted! They need to 
                // be kept in the list (unless excluded by the filter) in order for the backup handler to record their 
                // deletion.
                if (volumeData.Value.Files != null)
                {
                    foreach (var files in FilterExcludedFiles(volumeData.Value.Files, filter, cache).Where(m_snapshot.FileExists))
                        yield return files;
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
        /// <param name="errorCallback"></param>
        /// <returns>Filtered files</returns>
        private IEnumerable<string> FilterExcludedFiles(IEnumerable<string> files,
            Utility.Utility.EnumerationFilterDelegate filter, IDictionary<string, bool> cache, Utility.Utility.ReportAccessError errorCallback = null)
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
                    errorCallback?.Invoke(file, file, ex);
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
        /// <param name="errorCallback"></param>
        /// <returns>Filtered folders</returns>
        private IEnumerable<string> FilterExcludedFolders(IEnumerable<string> folders,
            Utility.Utility.EnumerationFilterDelegate filter, IDictionary<string, bool> cache, Utility.Utility.ReportAccessError errorCallback = null)
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
                    errorCallback?.Invoke(folder, folder, ex);
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

            if (volumeData.Files.Contains(path))
                return true; // do not append from previous set, already scanned

            foreach (var folder in volumeData.Folders)
            {
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
        public string Volume { get; set; }

        /// <summary>
        /// Set of potentially modified files
        /// </summary>
        public HashSet<string> Files { get; internal set; }

        /// <summary>
        /// Set of folders that are potentially modified, or whose children
        /// are potentially modified
        /// </summary>
        public List<string> Folders { get; internal set; }

        /// <summary>
        /// Journal data to use for next backup
        /// </summary>
        public USNJournalDataEntry JournalData { get; internal set; }

        /// <summary>
        /// If true, a full scan for this volume was required
        /// </summary>
        public bool IsFullScan { get; internal set; }

        /// <summary>
        /// Optional exception message for volume
        /// </summary>
        public Exception Exception { get; internal set; }
    }
}
