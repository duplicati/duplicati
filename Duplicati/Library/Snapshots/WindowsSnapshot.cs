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
        /// The Hyper-V VSS Writer Guid
        /// </summary>
        private static readonly Guid HyperVWriterGuid = new Guid("66841cd4-6ded-4f4b-8f17-fd23f8ddc3de");
        /// <summary>
        /// A list off Hyper-V Machines that we will back up
        /// </summary>
        private readonly IDictionary<string, string> m_hyperVMachines = null;

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

                if (excludedWriters.Length > 0)
                    m_backup.DisableWriterClasses(excludedWriters.ToArray());

                m_sourcepaths = sourcepaths.Select(x => Directory.Exists(x) ? Utility.Utility.AppendDirSeparator(x) : x).ToList();

                var requestedHyperVMs = new List<string>();
                if (options.ContainsKey("hyperv-backup-vm"))
                    requestedHyperVMs = options["hyperv-backup-vm"].Split(';').Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 0).ToList();
                
                //Check if we are backing up HyperV machines
                if (requestedHyperVMs.Count > 0)
                {
                    var hyperVUtility = new HyperVUtility(requestedHyperVMs);
                    m_hyperVMachines = hyperVUtility.GetHyperVMachines();

                    #region Testing Features
                    if (!Utility.Utility.ParseBoolOption(options, "hyperv-backup-no-merge"))
                        hyperVUtility.MergeVhd(requestedHyperVMs);

                    //Option: Create-new-vm-after-restore, 
                    //hyperVUtility.CreateHyperVMachine("TestVM", xmlpath);
                    #endregion Testing Features
                }

                m_backup.StartSnapshotSet();

                try
                {
                    //Gather information on all Vss writers
                    m_backup.GatherWriterMetadata();

                    //Update the sourcepaths if we're backing up Hyper-V Machines
                    if (requestedHyperVMs.Count > 0)
                    {
                        //Find sourcepaths in Hyper-V WriterMetaData
                        var m_backup_wmd = m_backup.WriterMetadata.FirstOrDefault(o => o.WriterId.Equals(HyperVWriterGuid));
                        if (m_backup_wmd != null)
                            foreach (var component in m_backup_wmd.Components)
                            {
                                //Cross reference the requested Hyper-V Machines to backup and add the sources
                                if (!m_hyperVMachines.ContainsKey(component.ComponentName)) 
                                    continue;
                            
                                foreach (var file in component.Files)
                                {
                                    if (file.FileSpecification.Contains("*")) 
                                        continue;
                                    m_sourcepaths.Add(file.Path + file.FileSpecification);
                                }
                            }
                    }

                    m_backup.FreeWriterMetadata();
                }
                catch
                {
                    try { m_backup.FreeWriterMetadata(); }
                    catch { }

                    throw;
                }

                //Figure out which volumes are in the set
                m_volumes = new Dictionary<string, Guid>(StringComparer.InvariantCultureIgnoreCase);
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

                //Signal that we want to do a backup
                m_backup.SetBackupState(false, true, VssBackupType.Full, false);

                //Make all writers aware that we are going to do the backup
                m_backup.PrepareForBackup();

                //Create the shadow volumes
                m_backup.DoSnapshotSet();

                //Make a little lookup table for faster translation
                m_volumeMap = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase);
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
            return EnumerateFilesAndFolders(null).ToList();
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
        /// <param name="filter">The filter to apply when evaluating files and folders</param>
        /// <param name="callback">The callback to invoke with each found path</param>
        public IEnumerable<string> EnumerateFilesAndFolders(Utility.Utility.EnumerationFilterDelegate callback)
        {
            return m_sourcepaths.SelectMany(
                s => Utility.Utility.EnumerateFileSystemEntries(s, callback, this.ListFolders, this.ListFiles, this.GetAttributes)
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
        public Dictionary<string, string> GetMetadata(string file)
        {
            return _ioWin.GetMetadata(GetSnapshotPath(file));
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
