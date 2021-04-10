﻿#region Disclaimer / License
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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Duplicati.Library.Common;
using Duplicati.Library.Common.IO;
using Microsoft.Win32.SafeHandles;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Class that encapsulates USN journal access to a single volume
    /// </summary>
    public sealed class USNJournal : IDisposable
    {
        /// <summary>
        /// The log tag to use
        /// </summary>
        private static readonly string FILTER_LOGTAG = Logging.Log.LogTagFromType(typeof(USNJournal));

        [Flags]
        public enum ChangeReason
        {
            None = 0,
            Modified = 1,
            Created = 2,
            Deleted = 4,
            RenamedFrom = 8,
            RenamedTo = 16,
            Any = Modified | Created | Deleted | RenamedFrom | RenamedTo
        }

        [Flags]
        public enum EntryType
        {
            Directory = 1,
            File = 2,
            Any = Directory | File
        }

        /// <summary>
        /// The volume this USN points to
        /// </summary>
        private readonly string m_volume;

        /// <summary>
        /// The FileNameReferenceNumber for the root folder
        /// </summary>
        private readonly ulong m_volumeRootRefNumber;

        /// <summary>
        /// This is a cache of all the filesystem entries found on the drive
        /// </summary>
        private IReadOnlyCollection<Record> m_entryList;

        /// <summary>
        /// The current USN journal from the device
        /// </summary>
        private readonly Win32USN.USN_JOURNAL_DATA_V0 m_journal;

        /// <summary>
        /// Start USN of current cached m_entryList
        /// </summary>
        private long m_startUsn;

        /// <summary>
        /// The safe filehandle
        /// </summary>
        private SafeFileHandle m_volumeHandle;

        /// <summary>
        /// Constructs a new USN helper instance
        /// </summary>
        /// <param name="volumeRoot">The root volume where the USN lookup is performed</param>
        internal USNJournal(string volumeRoot)
        {
            if (Platform.IsClientPosix)
                throw new Interface.UserInformationException(Strings.USNHelper.LinuxNotSupportedError, "UsnOnLinuxNotSupported");

            m_volume = Util.AppendDirSeparator(volumeRoot);

            try
            {
                var device = GetDeviceNameFromPath(m_volume);

                m_volumeHandle = Win32USN.CreateFile(device, Win32USN.FileAccess.GenericRead,
                    Win32USN.FileShare.ReadWrite, IntPtr.Zero, Win32USN.CreationDisposition.OpenExisting,
                    Win32USN.FileAttributes.BackupSemantics, IntPtr.Zero);

                if (m_volumeHandle == null || m_volumeHandle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                Win32USN.ControlWithOutput(m_volumeHandle, Win32USN.FsCtl.QueryUSNJournal, ref m_journal);

                m_volumeRootRefNumber = GetFileRefNumber(volumeRoot);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        /// <summary>
        /// Gets the USN JournalID for the current volume
        /// </summary>
        public long JournalId => m_journal.UsnJournalID;

        /// <summary>
        /// Gets the next USN number for the volume
        /// </summary>
        public long NextUsn => m_journal.NextUsn;

        /// <summary>
        /// Returns a list of files or folders that have changed since the recorded USN
        /// </summary>
        /// <param name="sourceFileOrFolder">The file or folder to find entries for</param>
        /// <param name="startUsn">USN of first entry to consider</param>
        /// <returns>A list of tuples with changed files and folders and their type</returns>
        public IEnumerable<Tuple<string, EntryType>> GetChangedFileSystemEntries(string sourceFileOrFolder, long startUsn)
        {
            return GetChangedFileSystemEntries(sourceFileOrFolder, startUsn, ChangeReason.Any);
        }

        /// <summary>
        /// Returns a list of files or folders that have changed since the recorded USN
        /// </summary>
        /// <param name="sourceFileOrFolder">The file or folder to find entries for</param>
        /// <param name="startUsn">USN of first entry to consider</param>
        /// <param name="reason">Filter expression for change reason</param>
        /// <returns>A list of tuples with changed files and folders and their type</returns>
        public IEnumerable<Tuple<string, EntryType>> GetChangedFileSystemEntries(string sourceFileOrFolder, long startUsn, ChangeReason reason)
        {
            Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "UsnInitialize", "Determine file system changes from USN for: {0}", sourceFileOrFolder);

            var isFolder = sourceFileOrFolder.EndsWith(Util.DirectorySeparatorString, StringComparison.Ordinal);

            foreach (var r in GetRecords(startUsn))
            {
                if (r.UsnRecord.Usn >= startUsn
                    && (reason == ChangeReason.Any || (MapChangeReason(r.UsnRecord.Reason) & reason) != 0)
                    && (r.FullPath.Equals(sourceFileOrFolder, Utility.Utility.ClientFilenameStringComparison)
                        || isFolder && Utility.Utility.IsPathBelowFolder(r.FullPath, sourceFileOrFolder)))
                {
                    yield return Tuple.Create(r.FullPath,
                        r.UsnRecord.FileAttributes.HasFlag(Win32USN.FileAttributes.Directory)
                            ? EntryType.Directory
                            : EntryType.File);
                }
            }
        }

        public static string GetVolumeRootFromPath(string path)
        {
            if (path == null)
                throw new Exception(Strings.USNHelper.UnexpectedPathFormat);

            return SystemIO.IO_WIN.GetPathRoot(path);
        }

        public static string GetDeviceNameFromPath(string path)
        {
            return @"\\.\" + GetVolumeRootFromPath(path).TrimEnd('\\');
        }

        /// <summary>
        /// Internal method to initially create and then access the cached version of the file entry list
        /// <param name="startUsn">USN of first entry to consider</param>
        /// </summary>       
        private IEnumerable<Record> GetRecords(long startUsn)
        {
            const Win32USN.USNReason InclusionFlags = Win32USN.USNReason.USN_REASON_ANY
                & ~(Win32USN.USNReason.USN_REASON_INDEXABLE_CHANGE | Win32USN.USNReason.USN_REASON_COMPRESSION_CHANGE |
                    Win32USN.USNReason.USN_REASON_ENCRYPTION_CHANGE | Win32USN.USNReason.USN_REASON_EA_CHANGE |
                    Win32USN.USNReason.USN_REASON_REPARSE_POINT_CHANGE | Win32USN.USNReason.USN_REASON_CLOSE);

            if (m_entryList == null || m_startUsn != startUsn)
            {
                m_startUsn = startUsn;
                m_entryList = ResolveFullPaths(GetRawRecords(startUsn, rec => (rec.UsnRecord.Reason & InclusionFlags) != 0));
            }

            return m_entryList;
        }

        /// <summary>
        /// Get NTFS file reference number (FRN) from path
        /// </summary>
        /// <param name="filePath">Input path</param>
        /// <returns>NTFS file reference number</returns>
        private static ulong GetFileRefNumber(string filePath)
        {
            Win32USN.BY_HANDLE_FILE_INFORMATION fileInfo;
            using (var driveHandle = Win32USN.CreateFile(filePath, Win32USN.FileAccess.GenericRead,
                Win32USN.FileShare.ReadWrite, IntPtr.Zero, Win32USN.CreationDisposition.OpenExisting,
                Win32USN.FileAttributes.BackupSemantics, IntPtr.Zero))
            {
                if (!Win32USN.GetFileInformationByHandle(driveHandle, out fileInfo))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            return ((ulong)fileInfo.FileIndexHigh << 32) | fileInfo.FileIndexLow;
        }


        /// <summary>
        /// Extract USN_RECORD_V2 from buffer
        /// </summary>
        /// <param name="bufferPointer"></param>
        /// <param name="offset"></param>
        /// <param name="fileName">Entry filename</param>
        /// <returns></returns>
        private static Win32USN.USN_RECORD_V2 GetBufferedEntry(IntPtr bufferPointer, long offset, out string fileName)
        {
            var entryPointer = new IntPtr(bufferPointer.ToInt64() + offset);
            var nativeEntry = (Win32USN.USN_RECORD_V2)Marshal.PtrToStructure(entryPointer, typeof(Win32USN.USN_RECORD_V2));

            //TODO: add support for V3 records
            if (nativeEntry.MajorVersion != 2)
                throw new Exception(Strings.USNHelper.UnsupportedUsnVersion);

            var filenamePointer = new IntPtr(bufferPointer.ToInt64() + offset + nativeEntry.FileNameOffset);
            fileName = Marshal.PtrToStringUni(filenamePointer, nativeEntry.FileNameLength / sizeof(char));
            return nativeEntry;
        }

        /// <summary>
        /// Explicit implementation of the EnumerateRecords method,
        /// as it currently crashes the Mono compiler ....
        /// </summary>
        private class RecordEnumerator : IEnumerable<Record>
        {
            private sealed class RecordEnumeratorImpl : IEnumerator<Record>
            {
                private readonly IReadOnlyCollection<byte> m_entryData;
                private readonly IntPtr m_bufferPointer;
                private readonly GCHandle m_bufferHandle;
                private long m_offset;

                public RecordEnumeratorImpl(IReadOnlyCollection<byte> entryData)
                {
                    m_entryData = entryData;
                    m_bufferHandle = GCHandle.Alloc(entryData, GCHandleType.Pinned);
                    m_bufferPointer = m_bufferHandle.AddrOfPinnedObject();
                    Reset();
                }

                public Record Current { get; private set; }

                object IEnumerator.Current => this.Current;

                public void Dispose()
                {
                    m_bufferHandle.Free();
                }

                public bool MoveNext()
                {
                    if (m_entryData.Count <= sizeof(long))
                        return false;

                    if (m_offset >= m_entryData.Count)
                        return false;
                    
                    var entry = GetBufferedEntry(m_bufferPointer, m_offset, out var fileName);
                    Current = new Record(entry, fileName);
                    m_offset += entry.RecordLength;

                    return true;
                }

                public void Reset()
                {
                    m_offset = sizeof(long);
                }
            }

            private readonly IReadOnlyCollection<byte> m_entryData;

            public RecordEnumerator(IReadOnlyCollection<byte> entryData)
            {
                m_entryData = entryData;
            }

            public IEnumerator<Record> GetEnumerator()
            {
                return new RecordEnumeratorImpl(m_entryData);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this.GetEnumerator();
            }
        }

        /// <summary>
        /// Enumerates all USN records in a raw data block
        /// </summary>
        /// <param name="entryData">Data block</param>
        /// <returns>Entries</returns>
        private static IEnumerable<Record> EnumerateRecords(IReadOnlyCollection<byte> entryData)
        {
            return new RecordEnumerator(entryData);
        }

        /// <summary>
        /// Returns collection of USN records, starting at startUSN
        /// </summary>
        /// <param name="startUsn">The USN number to start the list from, set to zero to get all</param>
        /// <returns>A list of files and folders changed since the USN</returns>
        private ICollection<Record> GetRawRecords(long startUsn, Func<Record, bool> inclusionPredicate)
        {
            var records = new List<Record>();

            var readData = new Win32USN.READ_USN_JOURNAL_DATA_V0
            {
                StartUsn = Math.Max(startUsn, m_journal.LowestValidUsn),
                ReasonMask = Win32USN.USNReason.USN_REASON_BASIC_INFO_CHANGE |
                             Win32USN.USNReason.USN_REASON_DATA_EXTEND |
                             Win32USN.USNReason.USN_REASON_DATA_OVERWRITE |
                             Win32USN.USNReason.USN_REASON_DATA_TRUNCATION |
                             Win32USN.USNReason.USN_REASON_FILE_CREATE |
                             Win32USN.USNReason.USN_REASON_FILE_DELETE |
                             Win32USN.USNReason.USN_REASON_HARD_LINK_CHANGE |
                             Win32USN.USNReason.USN_REASON_NAMED_DATA_EXTEND |
                             Win32USN.USNReason.USN_REASON_NAMED_DATA_OVERWRITE |
                             Win32USN.USNReason.USN_REASON_NAMED_DATA_TRUNCATION |
                             Win32USN.USNReason.USN_REASON_RENAME_NEW_NAME |
                             Win32USN.USNReason.USN_REASON_RENAME_OLD_NAME |
                             Win32USN.USNReason.USN_REASON_STREAM_CHANGE,
                ReturnOnlyOnClose = 0,
                Timeout = 0,
                BytesToWaitFor = 0,
                UsnJournalID = m_journal.UsnJournalID
            };

            var bufferSize = 4096; // larger buffer returns more record, but prevents user from cancelling operation for a longer time
            while (readData.StartUsn < m_journal.NextUsn)
            {
                if (!Win32USN.ControlWithInput(m_volumeHandle, Win32USN.FsCtl.ReadUSNJournal,
                    ref readData, bufferSize, out var entryData))
                {
                    var e = Marshal.GetLastWin32Error();
                    if (e == Win32USN.ERROR_HANDLE_EOF || e == Win32USN.ERROR_SUCCESS)
                        break;

                    if (e == Win32USN.ERROR_INSUFFICIENT_BUFFER)
                    {
                        bufferSize *= 2;
                        continue;
                    }

                    if (e == Win32USN.ERROR_JOURNAL_ENTRY_DELETED)
                        throw new UsnJournalSoftFailureException(Strings.USNHelper.JournalEntriesDeleted, new Win32Exception(e));

                    throw new Win32Exception(e);
                }

                records.AddRange(EnumerateRecords(entryData)
                                    .TakeWhile(rec => rec.UsnRecord.Usn < m_journal.NextUsn)
                                    .Where(rec => rec.UsnRecord.Usn >= startUsn && (inclusionPredicate == null || inclusionPredicate(rec))));
                readData.StartUsn = Marshal.ReadInt64(entryData, 0);
            }

            return records;
        }

        /// <summary>
        /// Retrieves a USN_RECORD_V2 by file reference number (FRN, not USN!)
        /// </summary>
        /// <param name="frn">File reference number</param>
        /// <returns>Returned entry if successful; null otherwise</returns>
        private Record GetRecordByFileRef(ulong frn)
        {
            var enumData = new Win32USN.MFT_ENUM_DATA
            {
                StartFileReferenceNumber = frn,
                LowUsn = 0,
                HighUsn = m_journal.NextUsn
            };

            var bufferSize = 512;
            byte[] entryData;
            while (!Win32USN.ControlWithInput(m_volumeHandle, Win32USN.FsCtl.EnumUSNData,
                ref enumData, bufferSize, out entryData))
            {
                var e = Marshal.GetLastWin32Error();
                if (e != Win32USN.ERROR_INSUFFICIENT_BUFFER) 
                    return null;

                // retry, increasing buffer size
                bufferSize *= 2;
            }

            // not really a foreach: we only check the first record
            foreach (var rec in EnumerateRecords(entryData))
            {
                if (rec.UsnRecord.FileReferenceNumber == frn)
                    return rec;
                break;
            }

            return null;
        }

        /// <summary>
        /// Calculates the full path of each entry in the USN table
        /// </summary>
        /// <param name="records">The list of records with local names</param>
        /// <returns>A list of USN entries with full path</returns>
        private IReadOnlyCollection<Record> ResolveFullPaths(ICollection<Record> records)
        {
            // initialize file ref-nr (FRN) to path/parent-FRN look-up table
            var cache = new Dictionary<ulong, SortedRecords>();
            foreach (var rec in records)
            {
                if (rec.UsnRecord.FileAttributes.HasFlag(Win32USN.FileAttributes.Directory))
                {
                    if (!cache.TryGetValue(rec.UsnRecord.FileReferenceNumber, out var e))
                    {
                        e = new SortedRecords();
                        cache.Add(rec.UsnRecord.FileReferenceNumber, e);
                    }

                    e.Add(rec);
                }
            }

            // List of unresolved USN records, with FileReferenceNumber as a key            
            Dictionary<ulong, List<Record>> recordsByFileRefNumber = new Dictionary<ulong, List<Record>>();

            // iterate through USN records
            var result = new Dictionary<string, Record>();
            foreach (var rec in records)
            {
                // Add entry to list of unresolved entries, and try to resolve them at the end of the scan
                if (!recordsByFileRefNumber.TryGetValue(rec.UsnRecord.FileReferenceNumber, out List<Record> fileRefHistory))
                {
                    fileRefHistory = new List<Record>();
                    recordsByFileRefNumber.Add(rec.UsnRecord.FileReferenceNumber, fileRefHistory);
                }
                fileRefHistory.Add(rec);

                var pathList = new LinkedList<Record>();
                pathList.AddFirst(rec);

                // walk back up the chain as far as we can go
                var cur = rec;
                while (true)
                {
                    var parentRefNr = cur.UsnRecord.ParentFileReferenceNumber;
                    if (parentRefNr == m_volumeRootRefNumber)
                        break; // done

                    if (!cache.TryGetValue(parentRefNr, out var parents))
                    {
                        // parent FRN not found in look-up table, fetch it from change journal
                        var parentRecord = GetRecordByFileRef(parentRefNr);

                        if (parentRecord == null)
                        {
                            pathList.Clear();
                            break;
                        }
                        else
                        {
                            parents = new SortedRecords(new List<Record> { parentRecord });
                            cache.Add(parentRefNr, parents);
                        }
                    }

                    // take parent entry having next smaller USN
                    var parent = parents.GetParentOf(cur);
                    if (parent == null)
                        throw new UsnJournalSoftFailureException(Strings.USNHelper.PathResolveError);

                    pathList.AddFirst(parent);

                    cur = parent;
                }

                if (pathList.Count > 0)
                {
                    // generate full path
                    Debug.Assert(m_volume != null, nameof(m_volume) + " != null");
                    var path = m_volume;
                    foreach (var r in pathList)
                    {
                        path = SystemIO.IO_WIN.PathCombine(path, r.FileName);
                    }

                    if (rec.UsnRecord.FileAttributes.HasFlag(Win32USN.FileAttributes.Directory))
                    {
                        path = Util.AppendDirSeparator(path);
                    }

                    // set resolved path
                    rec.FullPath = path;
                }
            }

            // parse all records
            foreach (var entry in recordsByFileRefNumber)
            {
                bool wasCreated = false;
                var tempRecords = new List<Record>();

                foreach (var rec in entry.Value)
                {
                    // add entry to intermediate result set
                    tempRecords.Add(rec);

                    var reason = rec.UsnRecord.Reason;

                    if (reason.HasFlag(Win32USN.USNReason.USN_REASON_FILE_CREATE) || reason.HasFlag(Win32USN.USNReason.USN_REASON_RENAME_NEW_NAME))
                    {
                        wasCreated = true;
                    }

                    if (reason.HasFlag(Win32USN.USNReason.USN_REASON_FILE_DELETE) || reason.HasFlag(Win32USN.USNReason.USN_REASON_RENAME_OLD_NAME))
                    {
                        if (!wasCreated)
                        {
                            FlushRecords(tempRecords, result);
                        }

                        tempRecords.Clear();
                        wasCreated = false;
                    }
                }

                FlushRecords(tempRecords, result);
            }

            return result.Values;
        }

        private static void FlushRecords(List<Record> tempRecords, Dictionary<string, Record> resultRecords)
        {
            foreach (var rec in tempRecords)
            {
                if (!string.IsNullOrEmpty(rec.FullPath))
                {
                    resultRecords[rec.FullPath] = rec;
                }
                // Ignore entries below \$Extend\. A clean implementation would now 
                // parse the MFT and look up the actual entry. But for for now, we check against
                // the well-known file names.
                else if (!(rec.FileName.Length == 24
                            && rec.UsnRecord.FileReferenceNumber.ToString("X16") == rec.FileName.Substring(0, 16)
                    || rec.FileName.Equals("$TxfLog")
                    || rec.FileName.Equals("$TxfLog.blf")))
                {
                    Logging.Log.WriteVerboseMessage(FILTER_LOGTAG, "UsnInitialize",
                                                    "Unable to use USN due to unresolvable entry \"{0}\" with ParentFileReferenceNumber {0:X24}",
                                                    rec.FileName, rec.UsnRecord.ParentFileReferenceNumber);

                    throw new UsnJournalSoftFailureException(Strings.USNHelper.PathResolveError);
                }
            }

            tempRecords.Clear();
        }

        private static ChangeReason MapChangeReason(Win32USN.USNReason reason)
        {
            if (reason.HasFlag(Win32USN.USNReason.USN_REASON_FILE_CREATE))
                return ChangeReason.Created;

            if (reason.HasFlag(Win32USN.USNReason.USN_REASON_FILE_DELETE))
                return ChangeReason.Deleted;

            if (reason.HasFlag(Win32USN.USNReason.USN_REASON_RENAME_OLD_NAME))
                return ChangeReason.RenamedFrom;

            if (reason.HasFlag(Win32USN.USNReason.USN_REASON_RENAME_NEW_NAME))
                return ChangeReason.RenamedTo;

            return ChangeReason.Modified;
        }

        #region IDisposable Members

        /// <summary>
        /// Cleans up any resources held, including the volume handle
        /// </summary>
        public void Dispose()
        {
            if (m_volumeHandle != null)
            {
                m_volumeHandle.Dispose();
                m_volumeHandle = null;
            }
        }

        #endregion

        private class SortedRecords
        {
            private bool m_isSorted;
            private readonly List<Record> m_records;

            public SortedRecords(List<Record> recs = null)
            {
                m_records = recs ?? new List<Record>();
                m_isSorted = false;
            }

            public void Add(Record rec)
            {
                m_records.Add(rec);
                m_isSorted = false;
            }

            private void Sort()
            {
                if (!m_isSorted)
                {
                    m_records.Sort((lhs, rhs) => lhs.UsnRecord.Usn.CompareTo(rhs.UsnRecord.Usn));
                    m_isSorted = true;
                }
            }

            public Record GetParentOf(Record usnRecord)
            {
                Sort();

                // perform binary search
                int index = m_records.BinarySearch(usnRecord, 
                    Comparer<Record>.Create(
                        (left, right) =>
                        {
                            if (left == null && right == null)
                                return 0;
                            if (left == null)
                                return -1;
                            if (right == null)
                                return 1;
                            return left.UsnRecord.Usn.CompareTo(right.UsnRecord.Usn);
                        }));

                if (index >= 0)
                {
                    if (usnRecord.UsnRecord.Usn == 0)
                        return m_records[index];

                    throw new ArgumentException(nameof(usnRecord)); // exact match not possible unless dummy USN
                }

                // obtain (MSDN) "the index of the next element that is larger than item"
                index = ~index;

                if (index > 0)
                    return m_records[index - 1]; // return next smaller record

                if (index < m_records.Count && !m_records[index].UsnRecord.Reason
                        .HasFlag(Win32USN.USNReason.USN_REASON_RENAME_NEW_NAME))
                    return m_records[index]; // return next larger record, unless it's a filename change

                if (index == m_records.Count)
                    return null; //TODO: possibly use other means to find record

                return null;
            }
        }

        private class Record
        {
            public Record(Win32USN.USN_RECORD_V2 record, string fileName)
            {
                UsnRecord = record;
                FileName = fileName;
                FullPath = null;
            }

            public Win32USN.USN_RECORD_V2 UsnRecord { get; }

            public string FileName { get; }

            public string FullPath { get; set; }
        }
    }

    [Serializable]
    public class UsnJournalSoftFailureException : Exception
    {
        public UsnJournalSoftFailureException()
        {
        }

        public UsnJournalSoftFailureException(string message) : base(message)
        {
        }

        public UsnJournalSoftFailureException(string message, Exception innerException) : base(message, innerException)
        {
        }

        protected UsnJournalSoftFailureException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}
