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

using Duplicati.Library.Common.IO;

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
        private readonly VssBackupComponents _vssBackupComponents;

        /// <summary>
        /// Constructs a new backup snapshot, using all the required disks
        /// </summary>
        /// <param name="sources">Sources to determine which volumes to include in snapshot</param>
        /// <param name="options">A set of commandline options</param>
        public WindowsSnapshot(IEnumerable<string> sources, IDictionary<string, string> options)
        {
            try
            {
                _vssBackupComponents = new VssBackupComponents();

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
                _vssBackupComponents.SetupWriters(null, excludedWriters);

                _vssBackupComponents.InitShadowVolumes(sources);

                _vssBackupComponents.MapVolumesToSnapShots();

                //If we should map the drives, we do that now and update the volumeMap
                if (Utility.Utility.ParseBoolOption(options, "vss-use-mapping"))
                {
                    _vssBackupComponents.MapDrives();
                }
            }
            catch (Exception ex1)
            {

                Logging.Log.WriteVerboseMessage(LOGTAG, "WindowsSnapshotCreation", ex1, "Failed to initialize windows snapshot instance");

                //In case we fail in the constructor, we do not want a snapshot to be active
                try
                {
                    Dispose();
                }
				catch(Exception ex2)
                {
					Logging.Log.WriteVerboseMessage(LOGTAG, "VSSCleanupOnError", ex2, "Failed during VSS error cleanup");
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
            string[] tmp = null;
            var spath = ConvertToSnapshotPath(localFolderPath);

            try
            {
                tmp = SystemIO.IO_WIN.GetDirectories(spath); 
            }
            catch (DirectoryNotFoundException) //TODO: Do we really need this??
            {
                spath = SystemIOWindows.PrefixWithUNC(spath);
                tmp = SystemIO.IO_WIN.GetDirectories(spath);
            }

            var root = Util.AppendDirSeparator(SystemIO.IO_WIN.GetPathRoot(localFolderPath));
            var volumePath = Util.AppendDirSeparator(ConvertToSnapshotPath(root));
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

            string[] files = null;
            var spath = ConvertToSnapshotPath(localFolderPath);

            // First try without long UNC prefixed
            try
            {
                files = SystemIO.IO_WIN.GetFiles(spath);
            }
            catch (DirectoryNotFoundException) //TODO: Do we really need this??
            {
                spath = SystemIOWindows.PrefixWithUNC(spath);
                files = SystemIO.IO_WIN.GetFiles(spath);
            }

            // convert back to non-shadow, i.e., non-vss version
            var root = Util.AppendDirSeparator(SystemIO.IO_WIN.GetPathRoot(localFolderPath));
            var volumePath = Util.AppendDirSeparator(ConvertToSnapshotPath(root));
            volumePath = SystemIOWindows.PrefixWithUNC(volumePath);

            for (var i = 0; i < files.Length; i++)
            {
                files[i] = root + SystemIOWindows.PrefixWithUNC(files[i]).Substring(volumePath.Length);
            }

            return files;
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

            return SystemIO.IO_WIN.GetLastWriteTimeUtc(SystemIOWindows.PrefixWithUNC(spath));
        }

        /// <summary>
        /// Gets the creation of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-shadow format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetCreationTimeUtc(string localPath)
        {
            var spath = ConvertToSnapshotPath(localPath);

            return SystemIO.IO_WIN.GetCreationTimeUtc(SystemIOWindows.PrefixWithUNC(spath));
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-shadow format</param>
        /// <returns>An open filestream that can be read</returns>
        public override Stream OpenRead(string localPath)
        {
            return SystemIO.IO_WIN.FileOpenRead(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The length of the file</returns>
        public override long GetFileSize(string localPath)
        {
            return SystemIO.IO_WIN.FileLength(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override FileAttributes GetAttributes(string localPath)
        {
            return SystemIO.IO_WIN.GetFileAttributes(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            var spath = ConvertToSnapshotPath(localPath);
            return SystemIO.IO_WIN.GetSymlinkTarget(spath);
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
            return SystemIO.IO_WIN.GetMetadata(ConvertToSnapshotPath(localPath), isSymlink, followSymlink);
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

            foreach (var kvp in _vssBackupComponents.SnapshotDeviceAndVolumes)
            {
				if (snapshotPath.StartsWith(kvp.Key, Utility.Utility.ClientFilenameStringComparison))
                    return SystemIO.IO_WIN.PathCombine(kvp.Value, snapshotPath.Substring(kvp.Key.Length));
            }

            throw new InvalidOperationException();
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath)
        {
            if (!Path.IsPathRooted(localPath))
                throw new InvalidOperationException();

            var root = SystemIO.IO_WIN.GetPathRoot(localPath);
            var volumePath = _vssBackupComponents.GetVolumeFromCache(root);

            // Note: Do NOT use Path.Combine as it strips the UNC path prefix
            var subPath = localPath.Substring(root.Length);
            if (!subPath.StartsWith(Util.DirectorySeparatorString, StringComparison.Ordinal))
            {
                volumePath = Util.AppendDirSeparator(volumePath, Util.DirectorySeparatorString);
            }

            var mappedPath = volumePath + subPath;
            return mappedPath;
        }

        /// <inheritdoc />
        public override bool FileExists(string localFilePath)
        {
            return SystemIO.IO_WIN.FileExists(ConvertToSnapshotPath(localFilePath));
        }

        /// <inheritdoc />
        public override bool DirectoryExists(string localFolderPath)
        {
            return SystemIO.IO_WIN.DirectoryExists(ConvertToSnapshotPath(localFolderPath));
        }

        /// <inheritdoc />
        public override bool IsSnapshot => true;

        #endregion

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _vssBackupComponents.Dispose();
            }

            base.Dispose(disposing);
        }

    }
}