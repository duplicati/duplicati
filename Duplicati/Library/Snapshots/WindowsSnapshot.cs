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
using System.Linq;


#endregion
using System;
using System.Collections.Generic;
using System.IO;
using Alphaleonis.Win32.Vss;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// This class encapsulates all access to the Windows Volume Shadow copy Services,
    /// implementing the disposable patterns to ensure correct release of resources.
    /// 
    /// The class presents all files and folders with their regular filenames to the caller,
    /// and internally handles the conversion to the shadow path.
    /// </summary>
    public class WindowsSnapshot : ISnapshotService
    {
        /// <summary>
        /// The main reference to the backup controller
        /// </summary>
        private IVssBackupComponents m_backup;
        /// <summary>
        /// The list of paths that will be shadow copied
        /// </summary>
        private List<string> m_sourcepaths;
        /// <summary>
        /// The list of snapshot ids for each volume, key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private Dictionary<string, Guid> m_volumes;
        /// <summary>
        /// The mapping of snapshot sources to their snapshot entries , key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private Dictionary<string, string> m_volumeMap;
        /// <summary>
        /// A list of mapped drives
        /// </summary>
        private List<DefineDosDevice> m_mappedDrives;
        /// <summary>
        /// Commonly used string element
        /// </summary>
        private static string SLASH = Path.DirectorySeparatorChar.ToString();
        /// <summary>
        /// A cached lookup for windows methods for dealing with long filenames
        /// </summary>
        private static readonly SystemIOWindows _ioWin = new SystemIOWindows();
        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sourcepaths">The folders that are about to be backed up</param>
        /// <param name="options">A set of commandline options</param>
        public WindowsSnapshot(string[] sourcepaths, Dictionary<string, string> options)
        {
            try
            {
                //Substitute for calling VssUtils.LoadImplementation(), as we have the dlls outside the GAC
                string alphadir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "alphavss");
                string alphadll = System.IO.Path.Combine(alphadir, VssUtils.GetPlatformSpecificAssemblyShortName() + ".dll");
                IVssImplementation vss = (IVssImplementation)System.Reflection.Assembly.LoadFile(alphadll).CreateInstance("Alphaleonis.Win32.Vss.VssImplementation");

                var excludedWriters = new Guid[0];
                if (options.ContainsKey("vss-exclude-writers"))
                    excludedWriters = options["vss-exclude-writers"].Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 0).Select(x => new Guid(x)).ToArray();

                //Check if we should map any drives
                bool useSubst = Utility.Utility.ParseBoolOption(options, "vss-use-mapping");

                //Prepare the backup
                m_backup = vss.CreateVssBackupComponents();
                m_backup.InitializeForBackup(null);
                m_backup.SetContext(VssSnapshotContext.Backup);
                m_backup.SetBackupState(false, true, VssBackupType.Full, false);

                if (excludedWriters.Length > 0)
                    m_backup.DisableWriterClasses(excludedWriters.ToArray());

                m_sourcepaths = sourcepaths.Select(x => Directory.Exists(x) ? Utility.Utility.AppendDirSeparator(x) : x).ToList();
                
                try
                {
                    m_backup.GatherWriterMetadata();
                }
                finally
                {
                    m_backup.FreeWriterMetadata();
                }

                //Sanity check for duplicate files/folders
                var pathDuplicates = m_sourcepaths.GroupBy(x => x, Utility.Utility.ClientFilenameStringComparer)
                      .Where(g => g.Count() > 1).Select(y => y.Key).ToList();

                foreach(var pathDuplicate in pathDuplicates)
                    Logging.Log.WriteMessage(string.Format("Removing duplicate source: {0}", pathDuplicate), Logging.LogMessageType.Information);

                if(pathDuplicates.Count > 0)
                    m_sourcepaths = m_sourcepaths.Distinct(Utility.Utility.ClientFilenameStringComparer).OrderBy(a => a).ToList();

                //Sanity check for multiple inclusions of the same files/folders
                var pathIncludedPaths = m_sourcepaths.Where(x => m_sourcepaths.Where(y => y != x).Any(z => x.StartsWith(z, Utility.Utility.ClientFilenameStringComparision))).ToList();

                foreach (var pathIncluded in pathIncludedPaths)
                    Logging.Log.WriteMessage(string.Format("Removing already included source: {0}", pathIncluded), Logging.LogMessageType.Information);

                if (pathIncludedPaths.Count > 0)
                    m_sourcepaths = m_sourcepaths.Except(pathIncludedPaths, Utility.Utility.ClientFilenameStringComparer).ToList();

                m_backup.StartSnapshotSet();

                //Figure out which volumes are in the set
                m_volumes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                foreach (string s in m_sourcepaths)
                {
                    string drive = Alphaleonis.Win32.Filesystem.Path.GetPathRoot(s);
                    if (!m_volumes.ContainsKey(drive))
                    {
                        if (!m_backup.IsVolumeSupported(drive))
                            throw new VssVolumeNotSupportedException(drive);

                        m_volumes.Add(drive, m_backup.AddToSnapshotSet(drive));
                    }
                }

                //Make all writers aware that we are going to do the backup
                m_backup.PrepareForBackup();

                //Create the shadow volumes
                m_backup.DoSnapshotSet();

                //Make a little lookup table for faster translation
                m_volumeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, Guid> kvp in m_volumes)
                    m_volumeMap.Add(kvp.Key, m_backup.GetSnapshotProperties(kvp.Value).SnapshotDeviceObject);

                //If we should map the drives, we do that now and update the volumeMap
                if (useSubst)
                {
                    m_mappedDrives = new List<DefineDosDevice>();
                    foreach (string k in new List<string>(m_volumeMap.Keys))
                    {
                        try
                        {
                            DefineDosDevice d;
                            m_mappedDrives.Add(d = new DefineDosDevice(m_volumeMap[k]));
                            m_volumeMap[k] = Utility.Utility.AppendDirSeparator(d.Drive);
                        }
                        catch { }
                    }
                }
            }
            catch
            {
                //In case we fail in the constructor, we do not want a snapshot to be active
                try { Dispose(); }
                catch { }

                throw;
            }
        }

#if DEBUG
        /// <summary>
        /// Returns all files found in the shadow copies
        /// </summary>
        /// <returns>A list of filenames to files found in the shadow volumes</returns>
        public List<string> AllFiles()
        {
            return EnumerateFilesAndFolders(null, null).ToList();
        }

#endif

        #region Private functions

        /// <summary>
        /// A callback function that takes a non-shadow path to a folder,
        /// and returns all folders found in a non-shadow path format.
        /// </summary>
        /// <param name="folder">The non-shadow path of the folder to list</param>
        /// <returns>A list of non-shadow paths</returns>
        private string[] ListFolders(string folder)
        {
            string root = Utility.Utility.AppendDirSeparator(Alphaleonis.Win32.Filesystem.Path.GetPathRoot(folder));
            string volumePath = Utility.Utility.AppendDirSeparator(GetSnapshotPath(root));

            string[] tmp = null;
            string spath = GetSnapshotPath(folder);

            if (SystemIOWindows.IsPathTooLong(spath))
                try { tmp = Alphaleonis.Win32.Filesystem.Directory.GetDirectories(spath); }
                catch (PathTooLongException) { }
                catch (DirectoryNotFoundException) { }
            else
                try { tmp = System.IO.Directory.GetDirectories(spath); }
                catch (PathTooLongException) { }

            if (tmp == null)
            {
                spath = SystemIOWindows.PrefixWithUNC(spath);
                volumePath = SystemIOWindows.PrefixWithUNC(volumePath);
                tmp = Alphaleonis.Win32.Filesystem.Directory.GetDirectories(spath);
            }

            volumePath = SystemIOWindows.PrefixWithUNC(volumePath);

            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = root + SystemIOWindows.PrefixWithUNC(tmp[i]).Substring(volumePath.Length);
            return tmp;
        }


        /// <summary>
        /// A callback function that takes a non-shadow path to a folder,
        /// and returns all files found in a non-shadow path format.
        /// </summary>
        /// <param name="folder">The non-shadow path of the folder to list</param>
        /// <returns>A list of non-shadow paths</returns>
        private string[] ListFiles(string folder)
        {
            string root = Utility.Utility.AppendDirSeparator(Alphaleonis.Win32.Filesystem.Path.GetPathRoot(folder));
            string volumePath = Utility.Utility.AppendDirSeparator(GetSnapshotPath(root));

            string[] tmp = null;
            string spath = GetSnapshotPath(folder);

            if (SystemIOWindows.IsPathTooLong(spath))
                try { tmp = Alphaleonis.Win32.Filesystem.Directory.GetFiles(spath); }
                catch (PathTooLongException) { }
                catch (DirectoryNotFoundException) { }
            else
                try { tmp = System.IO.Directory.GetFiles(spath); }
                catch (PathTooLongException) { }

            if (tmp == null)
            {
                spath = SystemIOWindows.PrefixWithUNC(spath);
                volumePath = SystemIOWindows.PrefixWithUNC(volumePath);
                tmp = Alphaleonis.Win32.Filesystem.Directory.GetFiles(spath);
            }

            volumePath = SystemIOWindows.PrefixWithUNC(volumePath);

            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = root + SystemIOWindows.PrefixWithUNC(tmp[i]).Substring(volumePath.Length);
            return tmp;
        }

        /// <summary>
        /// Helper function to translate a local path into the corresponding shadow volume path
        /// </summary>
        /// <param name="localPath">The local path to convert</param>
        /// <returns>The corresponding shadow volume path</returns>
        private string GetSnapshotPath(string localPath)
        {
            if (!Path.IsPathRooted(localPath))
                throw new InvalidOperationException();

            string root = Alphaleonis.Win32.Filesystem.Path.GetPathRoot(localPath);

            string volumePath;
            if (!m_volumeMap.TryGetValue(root, out volumePath))
                throw new InvalidOperationException();

            localPath = localPath.Replace(root, String.Empty);

            if (!volumePath.EndsWith(SLASH) && !localPath.StartsWith(SLASH))
                localPath = localPath.Insert(0, SLASH);
            localPath = localPath.Insert(0, volumePath);

            return localPath;
        }
        #endregion

        #region ISnapshotService Members

        /// <summary>
        /// Enumerates all files and folders in the shadow copy
        /// </summary>
        /// <param name="callback">The callback to invoke with each found path</param>
        /// <param name="errorCallback">The callback used to report errors</param>
        public IEnumerable<string> EnumerateFilesAndFolders(Duplicati.Library.Utility.Utility.EnumerationFilterDelegate callback, Duplicati.Library.Utility.Utility.ReportAccessError errorCallback)
        {
            return m_sourcepaths.SelectMany(
                s => Utility.Utility.EnumerateFileSystemEntries(s, callback, this.ListFolders, this.ListFiles, this.GetAttributes, errorCallback)
            );
        }

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="file">The full path to the file in non-shadow format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetLastWriteTimeUtc(string file)
        {
            string spath = GetSnapshotPath(file);
            if (!SystemIOWindows.IsPathTooLong(spath))
                try
                {
                    return File.GetLastWriteTimeUtc(spath);
                }
                catch (PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.GetLastWriteTimeUtc(SystemIOWindows.PrefixWithUNC(spath));
        }

        /// <summary>
        /// Gets the creation of a given file in UTC
        /// </summary>
        /// <param name="file">The full path to the file in non-shadow format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetCreationTimeUtc(string file)
        {
            string spath = GetSnapshotPath(file);
            if (!SystemIOWindows.IsPathTooLong(spath))
                try
                {
                    return File.GetCreationTimeUtc(spath);
                }
                catch (PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.GetCreationTimeUtc(SystemIOWindows.PrefixWithUNC(spath));
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-shadow format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return _ioWin.FileOpenRead(GetSnapshotPath(file));
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public long GetFileSize(string file)
        {
            return _ioWin.FileLength(GetSnapshotPath(file));
        }

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="file">The file or folder to examine</param>
        public System.IO.FileAttributes GetAttributes(string file)
        {
            return _ioWin.GetFileAttributes(GetSnapshotPath(file));
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public string GetSymlinkTarget(string file)
        {
            string spath = GetSnapshotPath(file);
            try
            {
                return Alphaleonis.Win32.Filesystem.File.GetLinkTargetInfo(spath).PrintName;
            }
            catch (PathTooLongException) { }

            return Alphaleonis.Win32.Filesystem.File.GetLinkTargetInfo(SystemIOWindows.PrefixWithUNC(spath)).PrintName;
        }

        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="file">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public Dictionary<string, string> GetMetadata(string file, bool isSymlink, bool followSymlink)
        {
            return _ioWin.GetMetadata(GetSnapshotPath(file), isSymlink, followSymlink);
        }

        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="file">The file or folder to examine</param>
        public bool IsBlockDevice(string file)
        {
            return false;
        }

        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="path">The file or folder to examine</param>
        public string HardlinkTargetID(string path)
        {
            return null;
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Cleans up any resources and closes the backup set
        /// </summary>
        public void Dispose()
        {
            try
            {
                if (m_mappedDrives != null)
                {
                    foreach (DefineDosDevice d in m_mappedDrives)
                        d.Dispose();
                    m_mappedDrives = null;
                    m_volumeMap = null;
                }
            }
            catch { }

            try
            {
                if (m_backup != null)
                    m_backup.BackupComplete();
            }
            catch { }

            try
            {
                if (m_backup != null)
                    foreach (Guid g in m_volumes.Values)
                        try { m_backup.DeleteSnapshot(g, false); }
                        catch { }
            }
            catch { }

            if (m_backup != null)
            {
                m_backup.Dispose();
                m_backup = null;
            }

        }

        #endregion

    }
}