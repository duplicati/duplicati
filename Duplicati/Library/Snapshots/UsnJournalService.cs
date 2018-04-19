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
using System.ComponentModel;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Snapshots
{
    public class UsnJournalService : IChangeJournalService
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType<UsnJournalService>();

        private readonly Utility.Utility.ReportAccessError m_errorCallback;
        private readonly Action m_cancelHandler;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="errorCallback">Callback for handling errors</param>
        /// <param name="cancelHandler">Callback for aborting operation. Throw OperationCancelledException() to abort.</param>
        public UsnJournalService(Utility.Utility.ReportAccessError errorCallback, Action cancelHandler)
        {
            m_errorCallback = errorCallback;
            m_cancelHandler = cancelHandler;
        }

        #region IChangeJournalService Members

        /// <inheritdoc />
        public FilterData FilterSources(IEnumerable<string> sources, string filterHash, IEnumerable<USNJournalDataEntry> journalData)
        {
            // create lookup for journal data
            var journalDataDict = journalData.ToDictionary(data => data.Volume);

            // prepare result
            var result = new FilterData
            {
                Files = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer),
                Folders = new List<string>(),
                JournalData = new List<USNJournalDataEntry>()
            };

            // iterate over volumes
            foreach (var sourcesPerVolume in SortByVolume(sources))
            {
                var volume = sourcesPerVolume.Key;

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

                    result.JournalData.Add(nextData);

                    // only use change journal if:
                    // - journal ID hasn't changed
                    // - nextUsn isn't zero (we use this as magic value to force a rescan)
                    // - the exclude filter hash hasn't changed
                    if (!journalDataDict.TryGetValue(volume, out var data)
                        || data.JournalId != nextData.JournalId
                        || data.NextUsn == 0
                        || data.ConfigHash != nextData.ConfigHash)
                    {
                        ScheduleFullScan(sourcesPerVolume, volume, result);
                    }
                    else
                    {
                        var changedFiles = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);
                        var changedFolders = new HashSet<string>(Utility.Utility.ClientFilenameStringComparer);

                        // iterate over sources and retrieve *renamed* directories
                        foreach (var source in sourcesPerVolume.Value)
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

                        var reducedFolderList = Utility.Utility.SimplifyFolderList(changedFolders).ToList();
                        var reducedFileList = Utility.Utility.GetFilesNotInFolders(changedFiles, reducedFolderList);
                        result.Folders.AddRange(reducedFolderList);
                        result.Files.UnionWith(reducedFileList);
                    }
                }
                catch (UsnJournalSoftFailureException)
                {
                    // journal is fine, but we cannot recover gapless changes since last time
                    // => schedule full scan
                    ScheduleFullScan(sourcesPerVolume, volume, result);
                }
                catch (Exception e)
                {
                    m_errorCallback(volume, volume, e);
                    ScheduleFullScan(sourcesPerVolume, volume, result);
                }
            }

            return result;
        }

        /// <summary>
        /// Add ALL sources for volume to result set
        /// </summary>
        /// <param name="sourcesPerVolume">Sources</param>
        /// <param name="volume">Volume</param>
        /// <param name="result">Result set</param>
        private static void ScheduleFullScan(KeyValuePair<string, List<string>> sourcesPerVolume, string volume, FilterData result)
        {
            foreach (var src in sourcesPerVolume.Value)
            {
                if (src.EndsWith(Utility.Utility.DirectorySeparatorString, StringComparison.Ordinal))
                {
                    result.Folders.Add(src);
                }
                else
                {
                    result.Files.Add(src);
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

        #endregion

    }
}
