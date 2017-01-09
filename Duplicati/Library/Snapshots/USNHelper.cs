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
using System.Text;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.ComponentModel;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// Class that encapsulates USN access to a single volume
    /// </summary>
    public class USNHelper : IDisposable
    {
        /// <summary>
        /// The path this USN points to
        /// </summary>
        private string m_path;

        /// <summary>
        /// The safe filehandle
        /// </summary>
        private SafeFileHandle m_volumeHandle;

        /// <summary>
        /// The current USN journal from the device
        /// </summary>
        private Win32USN.USN_JOURNAL_DATA m_journal;

        /// <summary>
        /// The FileNameReferenceNumber for the root folder
        /// </summary>
        private ulong m_volumeRootFileNameReferenceNumber;

        /// <summary>
        /// This is a cache of all the filesystem entries found on the drive
        /// </summary>
        private List<KeyValuePair<string, Win32USN.USN_RECORD>> m_entryList;

        /// <summary>
        /// Constructs a new USN helper instance
        /// </summary>
        /// <param name="path">The path to the folder to perform USN services</param>
        public USNHelper(string path)
            : this(path, null)
        {
        }


        /// <summary>
        /// Constructs a new USN helper instance
        /// </summary>
        /// <param name="path">The path to the folder to perform USN services</param>
        /// <param name="volumeRoot">The root volume where the USN lookup is performed</param>
        internal USNHelper(string path, string volumeRoot)
        {
            if (Utility.Utility.IsClientLinux)
                throw new Duplicati.Library.Interface.UserInformationException(Strings.USNHelper.LinuxNotSupportedError);

            if (!System.IO.Path.IsPathRooted(path))
                throw new Exception(string.Format("Path {0} is not rooted", path));

            m_path = Utility.Utility.AppendDirSeparator(path);

            try
            {
                string devicename = @"\\.\" + System.IO.Path.GetPathRoot(path).TrimEnd('\\');
                if (volumeRoot != null)
                    volumeRoot = volumeRoot.TrimEnd('\\');

                m_volumeHandle = Win32USN.CreateFile(volumeRoot == null ? devicename : volumeRoot, Win32USN.EFileAccess.GenericRead, Win32USN.EFileShare.ReadWrite, IntPtr.Zero, Win32USN.ECreationDisposition.OpenExisting, Win32USN.EFileAttributes.BackupSemantics, IntPtr.Zero);
                if (m_volumeHandle == null || m_volumeHandle.IsInvalid)
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                uint bytesReturned = 0;
                if (!Win32USN.DeviceIoControl(m_volumeHandle, Win32USN.EIOControlCode.FsctlQueryUsnJournal, null, 0, out m_journal, (uint)Marshal.SizeOf(typeof(Win32USN.USN_JOURNAL_DATA)), ref bytesReturned, IntPtr.Zero))
                    throw new Win32Exception(Marshal.GetLastWin32Error());

                Win32USN.BY_HANDLE_FILE_INFORMATION fileInfo;
                using (SafeFileHandle driveHandle = Win32USN.CreateFile(System.IO.Path.GetPathRoot(path), Win32USN.EFileAccess.GenericRead, Win32USN.EFileShare.ReadWrite, IntPtr.Zero, Win32USN.ECreationDisposition.OpenExisting, Win32USN.EFileAttributes.BackupSemantics, IntPtr.Zero))
                    if (!Win32USN.GetFileInformationByHandle(driveHandle, out fileInfo))
                        throw new Win32Exception(Marshal.GetLastWin32Error());

                m_volumeRootFileNameReferenceNumber = ((ulong)fileInfo.FileIndexHigh << 32) | ((ulong)fileInfo.FileIndexLow);
            }
            catch
            {
                if (m_volumeHandle != null)
                {
                    m_volumeHandle.Dispose();
                    m_volumeHandle = null;
                }

                throw;
            }

            if (this.FileSystemEntries.Count == 0)
                throw new Exception(Strings.USNHelper.SafeGuardError);
        }

        /// <summary>
        /// Helper function to support the Duplicati enumeration callback system
        /// </summary>
        /// <param name="rootpath">The root path to look in and use as filter base</param>
        /// <param name="callback">The callback function that collects the output</param>
        public void EnumerateFilesAndFolders(string rootpath, Duplicati.Library.Utility.Utility.EnumerationFilterDelegate callback)
        {
            //Under normal enumeration, the filter will prevent visiting subfolders for excluded folders
            //But when using USN all files/folders are present in the list, so we have to maintain
            // a list of subfolders that are excluded from the set
            System.Text.StringBuilder local_filter = new StringBuilder();
            System.Text.RegularExpressions.Regex excludedFolders = null;

            foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                if (r.Key.StartsWith(rootpath, Utility.Utility.ClientFilenameStringComparision))
                {
                    bool isFolder = (r.Value.FileAttributes & Win32USN.EFileAttributes.Directory) != 0;

                    if (excludedFolders != null && excludedFolders.Match(r.Key).Success)
                        continue;

                    if (isFolder)
                    {
                        if (!callback(rootpath, r.Key, (System.IO.FileAttributes)((int)r.Value.FileAttributes & 0xff)))
                        {
                            if (local_filter.Length != 0)
                                local_filter.Append("|");
                            local_filter.Append("(");
                            local_filter.Append(Utility.Utility.ConvertGlobbingToRegExp(r.Key + "*"));
                            local_filter.Append(")");

                            if (Utility.Utility.IsFSCaseSensitive)
                                excludedFolders = new System.Text.RegularExpressions.Regex(local_filter.ToString());
                            else
                                excludedFolders = new System.Text.RegularExpressions.Regex(local_filter.ToString(), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                        }
                    }
                    else
                    {
                        if (local_filter.Length == 0 || !excludedFolders.IsMatch(r.Key))
                            callback(rootpath, r.Key, (System.IO.FileAttributes)((int)r.Value.FileAttributes & 0xff));
                    }
                }
        }

        /// <summary>
        /// Gets the USN JournalID for the current volume
        /// </summary>
        public long JournalID
        {
            get
            {
                return m_journal.UsnJournalID;
            }
        }

        /// <summary>
        /// Gets the next USN number for the volume
        /// </summary>
        public long USN
        {
            get
            {
                return m_journal.NextUsn;
            }
        }


        /// <summary>
        /// Internal property to access the cached version of the file entry list
        /// </summary>
        private List<KeyValuePair<string, Win32USN.USN_RECORD>> Records
        {
            get
            {
                if (m_entryList == null)
                    m_entryList = BuildUSNTable(0);
                return m_entryList;
            }
        }

        /// <summary>
        /// Returns a list of files or folders that have changed since the recorded USN
        /// </summary>
        /// <param name="sourceFolder">The folder to find entries for</param>
        /// <param name="lastUsn">The last known USN</param>
        /// <returns>A list of changed files and folders</returns>
        public List<string> GetChangedFileSystemEntries(string sourceFolder, long lastUsn)
        {
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                if (r.Value.Usn >= lastUsn && r.Key.StartsWith(sourceFolder, Utility.Utility.ClientFilenameStringComparision))
                    result.Add(r.Key);

            return result;
        }

        public string GetChangeFlags(string entry)
        {
            foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                if (r.Key.Equals(entry, Utility.Utility.ClientFilenameStringComparision))
                    return r.Value.Reason.ToString();

            return "<not found>";
            
        }

        public List<string> GetRenamedFileSystemEntries(string sourceFolder, long lastUsn)
        {
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                if (r.Value.Usn >= lastUsn && ((r.Value.Reason & Win32USN.USNReason.USN_REASON_RENAME_NEW_NAME) != 0) &&  r.Key.StartsWith(sourceFolder, Utility.Utility.ClientFilenameStringComparision))
                    result.Add(r.Key);

            return result;
        }


        /// <summary>
        /// Returns a list of all files and folders in a given subfolder
        /// </summary>
        /// <param name="sourceFolder">The folder to enumerat</param>
        /// <returns>A list of folders</returns>
        public List<string> GetFileSystemEntries(string sourceFolder)
        {
            List<string> result = new List<string>();
            foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                if (r.Key.StartsWith(sourceFolder, Utility.Utility.ClientFilenameStringComparision))
                    result.Add(r.Key);

            return result;
        }

        /// <summary>
        /// Returns a list of files and folders found in the folder
        /// </summary>
        public List<string> FileSystemEntries
        {
            get
            {
                List<string> result = new List<string>();
                foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                    if (r.Key.StartsWith(m_path, Utility.Utility.ClientFilenameStringComparision))
                        result.Add(r.Key);

                return result;
            }
        }

        /// <summary>
        /// Returns a list of files found in the folder
        /// </summary>
        public List<string> Files
        {
            get
            {
                List<string> result = new List<string>();
                foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                    if (r.Key.StartsWith(m_path, Utility.Utility.ClientFilenameStringComparision) && (r.Value.FileAttributes & Win32USN.EFileAttributes.Directory) == 0)
                        result.Add(r.Key);

                return result;
            }
        }

        /// <summary>
        /// Returns a list of folders found in the folder
        /// </summary>
        public List<string> Folders
        {
            get
            {
                List<string> result = new List<string>();
                foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in this.Records)
                    if (r.Key.StartsWith(m_path, Utility.Utility.ClientFilenameStringComparision) && (r.Value.FileAttributes & Win32USN.EFileAttributes.Directory) != 0)
                        result.Add(r.Key);

                return result;
            }
        }

        /// <summary>
        /// Cached copy of the USN_RECORD size
        /// </summary>
        static readonly int USN_RECORD_SIZE = Marshal.SizeOf(typeof(Win32USN.USN_RECORD));

        /// <summary>
        /// Helper function to work with unmanaged memory
        /// </summary>
        /// <param name="bytesRead">The number of bytes in buffer</param>
        /// <param name="startOffset">The pointer to the start of the buffer</param>
        /// <param name="records">The list of records to update with results</param>
        /// <param name="moreEntries">A flag indicating if the end of the list is reached</param>
        /// <returns>The next USN or FRN number, depending on what pointer was passed</returns>
        private long ExtractUsnEntries(uint bytesRead, IntPtr startOffset, List<KeyValuePair<string, Win32USN.USN_RECORD>> records, out bool moreEntries)
        {
            int bytesLeft = (int)bytesRead;
            IntPtr curPtr = startOffset;

            if (bytesLeft < 8)
                throw new Exception(Strings.USNHelper.EmptyResponseError);

            long nextUsn = Marshal.ReadInt64(curPtr);
            curPtr = new IntPtr(curPtr.ToInt64() + 8);
            bytesLeft -= 8;

            moreEntries = (bytesLeft > USN_RECORD_SIZE);

            while (bytesLeft > USN_RECORD_SIZE)
            {
                Win32USN.USN_RECORD r = (Win32USN.USN_RECORD)Marshal.PtrToStructure(curPtr, typeof(Win32USN.USN_RECORD));

                //According to MSDN, apps should not handle data less than 2.0, http://msdn.microsoft.com/en-us/library/aa365722.aspx
                if (r.MajorVersion >= 2)
                {
                    string filename = Marshal.PtrToStringUni(new IntPtr(curPtr.ToInt64() + r.FileNameOffset), r.FileNameLength / sizeof(char));

                    if (r.Usn > m_journal.NextUsn)
                    {
                        moreEntries = false;
                        break;
                    }

                    records.Add(new KeyValuePair<string, Win32USN.USN_RECORD>(filename, r));
                }
                bytesLeft -= (int)r.RecordLength;
                curPtr = new IntPtr(curPtr.ToInt64() + r.RecordLength);
            }

            return nextUsn;
        }

        /// <summary>
        /// Returns a list of all files and folders changed since the USN
        /// </summary>
        /// <param name="startUsn">The USN number to start the list from, set to zero to get all</param>
        /// <returns>A list of files and folders changed since the USN</returns>
        private List<KeyValuePair<string, Win32USN.USN_RECORD>> BuildUSNTable(long startUsn)
        {
            const int ALLOCATED_MEMORY = 64 * 1024;

            IntPtr allocatedMemory = IntPtr.Zero;
            List<KeyValuePair<string, Win32USN.USN_RECORD>> records = new List<KeyValuePair<string, Win32USN.USN_RECORD>>();

            try
            {
                uint bytesRead = 0;
                bool more = true;
                allocatedMemory = Marshal.AllocHGlobal(ALLOCATED_MEMORY);

                Win32USN.MFT_ENUM_DATA startParams = new Win32USN.MFT_ENUM_DATA();
                startParams.StartFileReferenceNumber = 0;
                startParams.LowUsn = Math.Max(startUsn, m_journal.LowestValidUsn);
                startParams.HighUsn = m_journal.NextUsn;

                while (more)
                {
                    if (!Win32USN.DeviceIoControl(m_volumeHandle, Win32USN.EIOControlCode.FsctlEnumUsnData,
                            ref startParams, (uint)Marshal.SizeOf(typeof(Win32USN.MFT_ENUM_DATA)),
                                allocatedMemory, ALLOCATED_MEMORY,
                                ref bytesRead, IntPtr.Zero))
                    {
                        int errorCode = Marshal.GetLastWin32Error();

                        //If we get no error or EOF the enumeration is completed
                        if (errorCode == Win32USN.ERROR_HANDLE_EOF || errorCode == Win32USN.ERROR_SUCCESS)
                            break;
                        else
                            throw new Win32Exception(errorCode);
                    }

                    startParams.StartFileReferenceNumber = (ulong)ExtractUsnEntries(bytesRead, allocatedMemory, records, out more);
                }

                return ParseRecordList(records);
            }
            finally
            {
                if (allocatedMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(allocatedMemory);
                    allocatedMemory = IntPtr.Zero;
                }
            }
        }

        /// <summary>
        /// Calculates the full path of each entry in the USN table
        /// </summary>
        /// <param name="records">The list of records with local names</param>
        /// <returns>A list of USN entries with full path</returns>
        private List<KeyValuePair<string, Win32USN.USN_RECORD>> ParseRecordList(List<KeyValuePair<string, Win32USN.USN_RECORD>> records)
        {
            string rootDir = System.IO.Path.GetPathRoot(m_path);

            List<KeyValuePair<string, Win32USN.USN_RECORD>> result = new List<KeyValuePair<string,Win32USN.USN_RECORD>>();
            Dictionary<ulong, int> folderLookup = new Dictionary<ulong, int>();

            for (int i = 0; i < records.Count; i++)
                if ((records[i].Value.FileAttributes & Win32USN.EFileAttributes.Directory) != 0)
                    folderLookup.Add(records[i].Value.FileReferenceNumber, i);

            //Loop through each record
            foreach (KeyValuePair<string, Win32USN.USN_RECORD> r in records)
            {
                //Build the path, starting at the file/folder entry
                List<string> items = new List<string>();
                items.Add(r.Key + (((r.Value.FileAttributes & Win32USN.EFileAttributes.Directory) == 0) ? "" : "\\"));

                KeyValuePair<string, Win32USN.USN_RECORD> cur = r;

                //Walk back up the chain as far as we can go
                while (folderLookup.ContainsKey(cur.Value.ParentFileReferenceNumber))
                {
                    cur = records[folderLookup[cur.Value.ParentFileReferenceNumber]];
                    items.Insert(0, cur.Key);
                }

                //If the parent entry is indeed the drive, record this entry
                if (cur.Value.ParentFileReferenceNumber == m_volumeRootFileNameReferenceNumber)
                    result.Add(new KeyValuePair<string, Win32USN.USN_RECORD>(System.IO.Path.Combine(rootDir, string.Join(System.IO.Path.DirectorySeparatorChar.ToString(), items.ToArray())), r.Value));
            }

            return result;
        }

        /// <summary>
        /// Unused internal function that can be used to read all USN records
        /// </summary>
        /// <param name="lastUsn">The USN number to start from</param>
        private void GetChangedItems(long lastUsn)
        {
            const int ALLOCATED_MEMORY = 64 * 1024;

            IntPtr allocatedMemory = IntPtr.Zero;
            List<KeyValuePair<string, Win32USN.USN_RECORD>> records = new List<KeyValuePair<string, Win32USN.USN_RECORD>>();

            try
            {
                uint bytesRead = 0;
                bool more = true;
                allocatedMemory = Marshal.AllocHGlobal(ALLOCATED_MEMORY);

                Win32USN.READ_USN_JOURNAL_DATA startParams = new Win32USN.READ_USN_JOURNAL_DATA();
                startParams.UsnJournalID = m_journal.UsnJournalID;
                startParams.StartUsn = lastUsn;
                startParams.ReasonMask = Win32USN.USNReason.USN_REASON_ANY;
                startParams.ReturnOnlyOnClose = 0;
                startParams.Timeout = 0;
                startParams.BytesToWaitFor = 0;

                while (more)
                {
                    if (!Win32USN.DeviceIoControl(m_volumeHandle, Win32USN.EIOControlCode.FsctlReadUsnJournal,
                            startParams, (uint)Marshal.SizeOf(typeof(Win32USN.READ_USN_JOURNAL_DATA)),
                                allocatedMemory, ALLOCATED_MEMORY,
                                ref bytesRead, IntPtr.Zero))
                    {
                        int errorCode = Marshal.GetLastWin32Error();

                        //If we get no error or EOF the enumeration is completed
                        if (errorCode == Win32USN.ERROR_HANDLE_EOF || errorCode == Win32USN.ERROR_SUCCESS)
                            break;
                        else
                            throw new Win32Exception(errorCode);
                    }

                    startParams.StartUsn = ExtractUsnEntries(bytesRead, allocatedMemory, records, out more);
                }

                //Records now contains all Usn entries
            }
            finally
            {
                if (allocatedMemory != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(allocatedMemory);
                    allocatedMemory = IntPtr.Zero;
                }
            }
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
    }
}
