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
using AlphaFS = Alphaleonis.Win32.Filesystem;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// This class encapsulates all access to the Windows Volume Shadow copy Services,
    /// implementing the disposable patterns to ensure correct release of resources.
    /// 
    /// The class presents all files and folders with their regular filenames to the caller,
    /// and internally handles the conversion to the shadow path.
    /// </summary>
    public sealed class WindowsSnapshot : SnapshotBase
    {
		/// <summary>
        /// The tag used for logging messages
        /// </summary>
		public static readonly string LOGTAG = Logging.Log.LogTagFromType<WindowsSnapshot>();

        /// <summary>
        /// The main reference to the backup controller
        /// </summary>
        private IVssBackupComponents m_backup;

        /// <summary>
        /// The list of snapshot ids for each volume, key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private readonly Dictionary<string, Guid> m_volumes;

        /// <summary>
        /// The mapping of snapshot sources to their snapshot entries , key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private readonly Dictionary<string, string> m_volumeMap;

        /// <summary>
        /// The mapping of snapshot sources to their snapshot entries , key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private readonly Dictionary<string, string> m_volumeReverseMap;

        /// <summary>
        /// A list of mapped drives
        /// </summary>
        private List<DefineDosDevice> m_mappedDrives;

        /// <summary>
        /// A cached lookup for windows methods for dealing with long filenames
        /// </summary>
        private static SystemIOWindows IO_WIN = new SystemIOWindows();

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sources">Sources to determine which volumes to include in snapshot</param>
        /// <param name="options">A set of commandline options</param>
        public WindowsSnapshot(IEnumerable<string> sources, IDictionary<string, string> options)
        {
            try
            {
                // Substitute for calling VssUtils.LoadImplementation(), as we have the dlls outside the GAC
                var assemblyLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
                if (assemblyLocation == null)
                    throw new InvalidOperationException();

                var alphadir = Path.Combine(assemblyLocation, "alphavss");
                var alphadll = Path.Combine(alphadir, VssUtils.GetPlatformSpecificAssemblyShortName() + ".dll");
                var vss = (IVssImplementation)System.Reflection.Assembly.LoadFile(alphadll).CreateInstance("Alphaleonis.Win32.Vss.VssImplementation");
                if (vss == null)
                    throw new InvalidOperationException();

                // Default to exclude the System State writer
                var excludedWriters = new Guid[] { new Guid("{e8132975-6f93-4464-a53e-1050253ae220}") };
                if (options.ContainsKey("vss-exclude-writers"))
                {
                    excludedWriters = options["vss-exclude-writers"]
                        .Split(';')
                        .Where(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length > 0)
                        .Select(x => new Guid(x))
                        .ToArray();
                }

                //Check if we should map any drives
                var useSubst = Utility.Utility.ParseBoolOption(options, "vss-use-mapping");

                //Prepare the backup
                m_backup = vss.CreateVssBackupComponents();
                m_backup.InitializeForBackup(null);
                m_backup.SetContext(VssSnapshotContext.Backup);
                m_backup.SetBackupState(false, true, VssBackupType.Full, false);

                if (excludedWriters.Length > 0)
                    m_backup.DisableWriterClasses(excludedWriters.ToArray());
                
                try
                {
                    m_backup.GatherWriterMetadata();
                }
                finally
                {
                    m_backup.FreeWriterMetadata();
                }

                m_backup.StartSnapshotSet();

                //Figure out which volumes are in the set
                m_volumes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
                foreach (var s in sources)
                {
                    var drive = AlphaFS.Path.GetPathRoot(s);
                    if (!m_volumes.ContainsKey(drive))
                    {
                        //TODO: that seems a bit harsh... we could fall-back to not using VSS for that volume only
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
                foreach (var kvp in m_volumes)
                {
                    m_volumeMap.Add(kvp.Key, m_backup.GetSnapshotProperties(kvp.Value).SnapshotDeviceObject);
                }

                m_volumeReverseMap = m_volumeMap.ToDictionary(x => x.Value, x => x.Key);

                //If we should map the drives, we do that now and update the volumeMap
                if (useSubst)
                {
                    m_mappedDrives = new List<DefineDosDevice>();
                    foreach (var k in new List<string>(m_volumeMap.Keys))
                    {
                        try
                        {
                            DefineDosDevice d;
                            m_mappedDrives.Add(d = new DefineDosDevice(m_volumeMap[k]));
                            m_volumeMap[k] = Utility.Utility.AppendDirSeparator(d.Drive);
                        }
						catch(Exception ex)
                        {
							Logging.Log.WriteVerboseMessage(LOGTAG, "SubstMappingfailed", ex, "Failed to map VSS path {0} to drive", k);
                        }
                    }
                }
            }
            catch
            {
                //In case we fail in the constructor, we do not want a snapshot to be active
                try
                {
                    Dispose();
                }
				catch(Exception ex)
                {
					Logging.Log.WriteVerboseMessage(LOGTAG, "VSSCleanupOnError", ex, "Failed during VSS error cleanup");
                }

                throw;
            }
        }

        #region Private functions

        /// <summary>
        /// A callback function that takes a non-shadow path to a folder,
        /// and returns all folders found in a non-shadow path format.
        /// </summary>
        /// <param name="localFolderPath">The non-shadow path of the folder to list</param>
        /// <returns>A list of non-shadow paths</returns>
        protected override string[] ListFolders(string localFolderPath)
        {
            var root = Utility.Utility.AppendDirSeparator(AlphaFS.Path.GetPathRoot(localFolderPath));
            var volumePath = Utility.Utility.AppendDirSeparator(ConvertToSnapshotPath(root));

            string[] tmp = null;
            var spath = ConvertToSnapshotPath(localFolderPath);

            if (SystemIOWindows.IsPathTooLong(spath))
            {
                try { tmp = AlphaFS.Directory.GetDirectories(spath); }
                catch (PathTooLongException) { }
                catch (DirectoryNotFoundException) { }
            }
            else
            {
                try { tmp = Directory.GetDirectories(spath); }
                catch (PathTooLongException) { }
            }

            if (tmp == null)
            {
                spath = SystemIOWindows.PrefixWithUNC(spath);
                tmp = AlphaFS.Directory.GetDirectories(spath);
            }

            volumePath = SystemIOWindows.PrefixWithUNC(volumePath);

            for (var i = 0; i < tmp.Length; i++)
            {
                tmp[i] = root + SystemIOWindows.PrefixWithUNC(tmp[i]).Substring(volumePath.Length);
            }

            return tmp;
        }


        /// <summary>
        /// A callback function that takes a non-shadow path to a folder,
        /// and returns all files found in a non-shadow path format.
        /// </summary>
        /// <param name="localFolderPath">The non-shadow path of the folder to list</param>
        /// <returns>A list of non-shadow paths</returns>
        protected override string[] ListFiles(string localFolderPath)
        {
            var root = Utility.Utility.AppendDirSeparator(AlphaFS.Path.GetPathRoot(localFolderPath));
            var volumePath = Utility.Utility.AppendDirSeparator(ConvertToSnapshotPath(root));

            string[] tmp = null;
            var spath = ConvertToSnapshotPath(localFolderPath);

            if (SystemIOWindows.IsPathTooLong(spath))
            {
                try { tmp = AlphaFS.Directory.GetFiles(spath); }
                catch (PathTooLongException) { }
                catch (DirectoryNotFoundException) { }
            }
            else
            {
                try { tmp = Directory.GetFiles(spath); }
                catch (PathTooLongException) { }
            }

            if (tmp == null)
            {
                spath = SystemIOWindows.PrefixWithUNC(spath);
                tmp = AlphaFS.Directory.GetFiles(spath);
            }

            volumePath = SystemIOWindows.PrefixWithUNC(volumePath);

            for (var i = 0; i < tmp.Length; i++)
            {
                tmp[i] = root + SystemIOWindows.PrefixWithUNC(tmp[i]).Substring(volumePath.Length);
            }

            return tmp;
        }
        #endregion

        #region ISnapshotService Members

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-shadow format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetLastWriteTimeUtc(string localPath)
        {
            var spath = ConvertToSnapshotPath(localPath);
            if (!SystemIOWindows.IsPathTooLong(spath))
            {
                try
                {
                    return File.GetLastWriteTimeUtc(spath);
                }
                catch (PathTooLongException) { }
            }

            return AlphaFS.File.GetLastWriteTimeUtc(SystemIOWindows.PrefixWithUNC(spath));
        }

        /// <summary>
        /// Gets the creation of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-shadow format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetCreationTimeUtc(string localPath)
        {
            var spath = ConvertToSnapshotPath(localPath);
            if (!SystemIOWindows.IsPathTooLong(spath))
            {
                try
                {
                    return File.GetCreationTimeUtc(spath);
                }
                catch (PathTooLongException) { }
            }

            return AlphaFS.File.GetCreationTimeUtc(SystemIOWindows.PrefixWithUNC(spath));
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-shadow format</param>
        /// <returns>An open filestream that can be read</returns>
        public override Stream OpenRead(string localPath)
        {
            return IO_WIN.FileOpenRead(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public override long GetFileSize(string localPath)
        {
            return IO_WIN.FileLength(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override FileAttributes GetAttributes(string localPath)
        {
            return IO_WIN.GetFileAttributes(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            var spath = ConvertToSnapshotPath(localPath);
            return IO_WIN.GetSymlinkTarget(spath);
        }

        /// <summary>
        /// Gets the metadata for the given file or folder
        /// </summary>
        /// <returns>The metadata for the given file or folder</returns>
        /// <param name="localPath">The file or folder to examine</param>
        /// <param name="isSymlink">A flag indicating if the target is a symlink</param>
        /// <param name="followSymlink">A flag indicating if a symlink should be followed</param>
        public override Dictionary<string, string> GetMetadata(string localPath, bool isSymlink, bool followSymlink)
        {
            return IO_WIN.GetMetadata(ConvertToSnapshotPath(localPath), isSymlink, followSymlink);
        }

        /// <inheritdoc />
        public override bool IsBlockDevice(string localPath)
        {
            return false;
        }

        /// <inheritdoc />
        public override string HardlinkTargetID(string localPath)
        {
            return null;
        }

        /// <inheritdoc />
        public override string ConvertToLocalPath(string snapshotPath)
        {
            if (!Path.IsPathRooted(snapshotPath))
                throw new InvalidOperationException();

            foreach (var kvp in m_volumeReverseMap)
            {
				if (snapshotPath.StartsWith(kvp.Key, Utility.Utility.ClientFilenameStringComparison))
                    return Path.Combine(kvp.Value, snapshotPath.Substring(kvp.Key.Length));
            }

            throw new InvalidOperationException();
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath)
        {
            if (!Path.IsPathRooted(localPath))
                throw new InvalidOperationException();

            var root = AlphaFS.Path.GetPathRoot(localPath);

            if (!m_volumeMap.TryGetValue(root, out var volumePath))
                throw new InvalidOperationException();

            return Path.Combine(volumePath, localPath.Substring(root.Length));
        }

        /// <inheritdoc />
        public override bool FileExists(string localFilePath)
        {
            return IO_WIN.FileExists(ConvertToSnapshotPath(localFilePath));
        }

        /// <inheritdoc />
        public override bool DirectoryExists(string localFolderPath)
        {
            return IO_WIN.DirectoryExists(ConvertToSnapshotPath(localFolderPath));
        }

        /// <inheritdoc />
        public override bool IsSnapshot => true;

        #endregion

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try
                {
                    if (m_mappedDrives != null)
                    {
                        foreach (var d in m_mappedDrives)
                        {
                            d.Dispose();
                        }

                        m_mappedDrives = null;
                    }
                }
				catch (Exception ex)
                {
					Logging.Log.WriteVerboseMessage(LOGTAG, "MappedDriveCleanupError", ex, "Failed during VSS mapped drive unmapping");
                }

				try
				{
					m_backup?.BackupComplete();
				}
				catch (Exception ex)
				{
					Logging.Log.WriteVerboseMessage(LOGTAG, "VSSTerminateError", ex, "Failed to signal VSS completion");
				}

                try
                {
                    if (m_backup != null)
                    {
                        foreach (var g in m_volumes.Values)
                        {
							try
							{
								m_backup.DeleteSnapshot(g, false);
							}
							catch (Exception ex)
							{
								Logging.Log.WriteVerboseMessage(LOGTAG, "VSSSnapShotDeleteError", ex, "Failed to close VSS snapshot");
							}
                        }
                    }
                }
				catch (Exception ex)
                {
					Logging.Log.WriteVerboseMessage(LOGTAG, "VSSSnapShotDeleteCleanError", ex, "Failed during VSS esnapshot closing");
                }

                if (m_backup != null)
                {
                    m_backup.Dispose();
                    m_backup = null;
                }
            }

            base.Dispose(disposing);
        }

    }
}