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


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using Duplicati.Library.Interface;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Snapshots
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
                emitFilter == null ? string.Empty : emitFilter.ToString(),
                string.Join("; ", m_snapshot.SourceFolders),
                fileAttributeFilter.ToString(),
                skipFilesLargerThan.ToString()
            }));

            // create lookup for journal data
            var journalDataDict = prevJournalData.ToDictionary(data => data.Volume);

            // iterate over volumes
            foreach (var sourcesPerVolume in SortByVolume(m_snapshot.SourceFolders))
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

            if (volumeData.Files.Contains(path))
                return true; // do not append from previous set, already scanned

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
