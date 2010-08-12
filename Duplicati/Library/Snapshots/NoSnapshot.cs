using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// A simple implementation of snapshots that only uses the regular System.IO operations, 
    /// used to simplify code that uses snapshots, but also handles a non-snapshot enabled systems
    /// </summary>
    public class NoSnapshot : ISnapshotService
    {
        /// <summary>
        /// The list of all folders in the snapshot
        /// </summary>
        private string[] m_sourcefolders;

        /// <summary>
        /// A flag that toggles backupRead attempts if the file is locked
        /// </summary>
        private bool m_attemptBackupRead;

        /// <summary>
        /// A flag indicating if the file should be opened in read/write mode if read mode fails
        /// </summary>
        private bool m_attemptDirtyAccess;

        /// <summary>
        /// A falg that indicates if the backup privilege has been enabled and should be disabled when disposing
        /// </summary>
        private bool m_disableBackupPrivilege;

                /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sourcepaths">The folders that are about to be backed up</param>
        public NoSnapshot(string[] folders)
            : this(folders, false)
        {
        }

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="folders">The folders that are about to be backed up</param>
        /// <param name="attemptDirtyAccess">True if the file may be opened with read/write permissions</param>
        public NoSnapshot(string[] folders, bool attemptDirtyAccess)
        {
            m_sourcefolders = new string[folders.Length];
            for (int i = 0; i < m_sourcefolders.Length; i++)
                m_sourcefolders[i] = Core.Utility.AppendDirSeperator(folders[i]);

            m_attemptDirtyAccess = attemptDirtyAccess;
            m_attemptBackupRead = false;

            if (m_attemptDirtyAccess && !Core.Utility.IsClientLinux)
            {
                try
                {
                    //If we know we cannot enable the privilege, don't try
                    if (WinNativeMethods.CanEnableBackupPrivilege)
                    {
                        if (!WinNativeMethods.BackupPrivilege)
                        {
                            //Activate the privilege
                            WinNativeMethods.BackupPrivilege = true;
                            
                            //We have set the privilege, remember to unset it
                            m_disableBackupPrivilege = true;
                        }

                        //If the flag setting succeeded, this should be true
                        if (WinNativeMethods.BackupPrivilege)
                            m_attemptBackupRead = true;
                    }
                }
                catch
                {
                }
            }

        }

        #region ISnapshotService Members

        /// <summary>
        /// Cleans up any resources and closes the backup set
        /// </summary>
        public void Dispose()
        {
            if (m_disableBackupPrivilege)
                try { WinNativeMethods.BackupPrivilege = false; }
                catch { }
        }

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(Duplicati.Library.Core.FilenameFilter filter, Duplicati.Library.Core.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcefolders)
                Core.Utility.EnumerateFileSystemEntries(s, filter, callback);
        }

        /// <summary>
        /// Gets the last write time of a given file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetLastWriteTime(string file)
        {
            return System.IO.File.GetLastWriteTime(file);
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenRead(string file)
        {
            try
            {
                return System.IO.File.OpenRead(file);
            }
            catch
            {
                if (m_attemptDirtyAccess)
                {
                    //Lets try a little softer, allowing file modification while reading :(
                    try { return System.IO.File.Open(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite); }
                    catch { }

                    //If we have SeBackupPrivilege (and are on Windows), try some extra tricks
                    if (m_attemptBackupRead)
                    {
                        //Lets see if we can open it for reading with the BackupSemantics flag set
                        try { return WinNativeMethods.OpenAsBackupFile(file); }
                        catch { }
                    }
                }

                throw; //None of the tricks worked, throw original error
            }
        }

        #endregion
    }
}
