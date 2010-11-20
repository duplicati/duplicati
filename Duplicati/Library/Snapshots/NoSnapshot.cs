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
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sourcepaths">The folders that are about to be backed up</param>
        public NoSnapshot(string[] folders)
            : this(folders, new Dictionary<string, string>())
        {
        }

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="folders">The folders that are about to be backed up</param>
        /// <param name="attemptDirtyAccess">True if the file may be opened with read/write permissions</param>
        public NoSnapshot(string[] folders, Dictionary<string, string> options)
        {
            m_sourcefolders = new string[folders.Length];
            for (int i = 0; i < m_sourcefolders.Length; i++)
                m_sourcefolders[i] = Core.Utility.AppendDirSeparator(folders[i]);
        }

        #region ISnapshotService Members

        /// <summary>
        /// Cleans up any resources and closes the backup set
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="startpath">The path from which to retrieve files and folders</param>
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public void EnumerateFilesAndFolders(string startpath, Duplicati.Library.Core.FilenameFilter filter, Duplicati.Library.Core.Utility.EnumerationCallbackDelegate callback)
        {
            foreach (string s in m_sourcefolders)
                if (s.Equals(startpath, Core.Utility.ClientFilenameStringComparision))
                {
                    Core.Utility.EnumerateFileSystemEntries(s, filter, callback);
                    return;
                }

            throw new InvalidOperationException(string.Format(Strings.Shared.InvalidEnumPathError, startpath));
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
            return System.IO.File.OpenRead(file);
        }

        /// <summary>
        /// Opens a locked file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenLockedRead(string file)
        {
            return System.IO.File.Open(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
        }

        #endregion
    }
}
