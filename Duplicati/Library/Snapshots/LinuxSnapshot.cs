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
using System.Diagnostics;
using System.Text;

namespace Duplicati.Library.Snapshots
{
    /// <summary>
    /// This class encapsulates all access to the Linux LVM snapshot feature,
    /// implementing the disposable patterns to ensure correct release of resources.
    /// 
    /// The class presents all files and folders with their regular filenames to the caller,
    /// and internally handles the conversion to the snapshot path.
    /// </summary>
    public sealed class LinuxSnapshot : SnapshotBase
    {
		/// <summary>
        /// The tag used for logging messages
        /// </summary>
        public static readonly string LOGTAG = Logging.Log.LogTagFromType<WindowsSnapshot>();

        /// <summary>
        /// Helper to have access to the System.IO calls without the interface layer
        /// </summary>
        private static SystemIOLinux SYS_IO = new SystemIOLinux();

        /// <summary>
        /// This is a lookup, mapping each source folder to the corresponding snapshot
        /// </summary>
        private readonly List<KeyValuePair<string, SnapShot>> m_entries;

        /// <summary>
        /// This is the list of the snapshots we have created, which must be disposed
        /// </summary>
        private List<SnapShot> m_snapShots;

        /// <summary>
        /// Constructs a new snapshot module using LVM
        /// </summary>
        /// <param name="sources">The list of folders to create snapshots for</param>
        public LinuxSnapshot(IEnumerable<string> sources)
        {
            try
            {
                m_entries = new List<KeyValuePair<string, SnapShot>>();

                // Make sure we do not create more snapshots than we have to
                var snaps = new Dictionary<string, SnapShot>();
                foreach (var path in sources)
                {
                    var tmp = new SnapShot(path);
                    if (!snaps.TryGetValue(tmp.DeviceName, out var snap))
                    {
                        snaps.Add(tmp.DeviceName, tmp);
                        snap = tmp;
                    }

                    m_entries.Add(new KeyValuePair<string, SnapShot>(path, snap));
                }

                m_snapShots = new List<SnapShot>(snaps.Values);

                // We have all the snapshots that we need, lets activate them
                foreach (var snap in m_snapShots)
                {
                    snap.CreateSnapshotVolume();
                }
            }
            catch
            {
                // If something goes wrong, try to clean up
                try
                {
                    Dispose();
                }
				catch (Exception ex)
                {
					Logging.Log.WriteVerboseMessage(LOGTAG, "SnapshotCleanupError", ex, "Failed to clean up after error");
                }

                throw;
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (m_snapShots != null)
            {
                if (disposing)
                {
                    // Attempt to clean out as many as possible
                    foreach(var s in m_snapShots)
                    {
                        try { s.Dispose(); }
						catch (Exception ex) { Logging.Log.WriteVerboseMessage(LOGTAG, "SnapshotCloseError", ex, "Failed to close a snapshot"); }
                    }
                }

                // Don't try this again
                m_snapShots = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Internal helper class for keeping track of a single snapshot volume
        /// </summary>
        private sealed class SnapShot : IDisposable
        {
            /// <summary>
            /// The unique id of the snapshot
            /// </summary>
            private readonly string m_name;

            /// <summary>
            /// Constructs a new snapshot for the given folder
            /// </summary>
            /// <param name="path"></param>
            public SnapShot(string path)
            {
                m_name = $"duplicati-{Guid.NewGuid().ToString()}";
                LocalPath = System.IO.Directory.Exists(path) ? Utility.Utility.AppendDirSeparator(path) : path;
                Initialize(LocalPath);
            }

            /// <summary>
            /// Gets the path of the folder that this snapshot represents
            /// </summary>
            public string LocalPath { get; }

            /// <summary>
            /// Gets a value representing the volume on which the folder resides
            /// </summary>
            public string DeviceName { get; private set; }

            /// <summary>
            /// Gets the path where the snapshot is mounted
            /// </summary>
            public string SnapshotPath { get; private set; }

            /// <summary>
            /// Gets the path the source disk is originally mounted
            /// </summary>
            public string MountPoint { get; private set; }

            #region IDisposable Members

            /// <summary>
            /// Cleanup any used resources
            /// </summary>
            public void Dispose()
            {
                if (SnapshotPath != null && System.IO.Directory.Exists(SnapshotPath))
                {
                    var output = ExecuteCommand("remove-lvm-snapshot.sh", $"\"{m_name}\" \"{DeviceName}\" \"{SnapshotPath}\"", 0);
                    if (System.IO.Directory.Exists(SnapshotPath))
                        throw new Exception(Strings.LinuxSnapshot.MountFolderNotRemovedError(SnapshotPath, output));

                    SnapshotPath = null;
                    DeviceName = null;
                }
            }

            #endregion

            /// <summary>
            /// Converts a local path to a snapshot path
            /// </summary>
            /// <param name="localPath">The local path</param>
            /// <returns>The snapshot path</returns>
            public string ConvertToSnapshotPath(string localPath)
            {
                if (!localPath.StartsWith(MountPoint, StringComparison.Ordinal))
                    throw new InvalidOperationException();

                return SystemIOLinux.NormalizePath(SnapshotPath + localPath.Substring(MountPoint.Length));
            }

            /// <summary>
            /// Converts a snapshot path to a local path
            /// </summary>
            /// <param name="snapshotPath">The snapshot path</param>
            /// <returns>The local path</returns>
            public string ConvertToLocalPath(string snapshotPath)
            {
                if (!snapshotPath.StartsWith(SnapshotPath, StringComparison.Ordinal))
                    throw new InvalidOperationException();

                return MountPoint + snapshotPath.Substring(SnapshotPath.Length);
            }

            /// <summary>
            /// Helper function to execute a script
            /// </summary>
            /// <param name="program">The name of the lvm-script to execute</param>
            /// <param name="commandline">The arguments to pass to the executable</param>
            /// <param name="expectedExitCode">The exitcode that is expected</param>
            /// <returns>A string with the combined output of the stdout and stderr</returns>
            private static string ExecuteCommand(string program, string commandline, int expectedExitCode)
            {
                program = System.IO.Path.Combine(System.IO.Path.Combine(AutoUpdater.UpdaterManager.InstalledBaseDir, "lvm-scripts"), program);
                var inf = new ProcessStartInfo(program, commandline)
                {
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    RedirectStandardInput = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    UseShellExecute = false
                };

                try
                {
                    var p = Process.Start(inf);

                    //Allow up 20 seconds for the execution
                    if (!p.WaitForExit(30 * 1000))
                    {
                        //Attempt to close down semi-nicely
                        p.Kill();
                        p.WaitForExit(5 * 1000); //This should work, and if it does, prevents a race with any cleanup invocations

                        throw new Interface.UserInformationException(Strings.LinuxSnapshot.ExternalProgramTimeoutError(program, commandline), "LvmScriptTimeout");
                    }

                    //Build the output string. Since the process has exited, these cannot block
                    var output = string.Format("Exit code: {1}{0}{2}{0}{3}", Environment.NewLine, p.ExitCode, p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd());

                    //Throw an exception if something went wrong
                    if (p.ExitCode != expectedExitCode)
                        throw new Interface.UserInformationException(Strings.LinuxSnapshot.ScriptExitCodeError(p.ExitCode, expectedExitCode, output), "LvmScriptWrongExitCode");

                    return output;
                }
                catch (Exception ex)
                {
                    throw new Exception(Strings.LinuxSnapshot.ExternalProgramLaunchError(ex.ToString(), program, commandline));
                }
            }

            /// <summary>
            /// Finds the LVM id of the volume id where the folder is placed
            /// </summary>
            private void Initialize(string folder)
            {
                //Figure out what logical volume the path is located on
                var output = ExecuteCommand("find-volume.sh", $"\"{folder}\"", 0);

                var rex = new System.Text.RegularExpressions.Regex("device=\"(?<device>[^\"]+)\"");
                var m = rex.Match(output);

                if (!m.Success)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("device", output));

                DeviceName = rex.Match(output).Groups["device"].Value;

                if (string.IsNullOrEmpty(DeviceName) || DeviceName.Trim().Length == 0)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("device", output));

                rex = new System.Text.RegularExpressions.Regex("mountpoint=\"(?<mountpoint>[^\"]+)\"");
                m = rex.Match(output);

                if (!m.Success)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("mountpoint", output));

                MountPoint = rex.Match(output).Groups["mountpoint"].Value;

                if (string.IsNullOrEmpty(MountPoint) || MountPoint.Trim().Length == 0)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("mountpoint", output));

                MountPoint = Utility.Utility.AppendDirSeparator(MountPoint);
            }

            /// <summary>
            /// Create the snapshot and mount it, this is not done in the constructor,
            /// because we want to see if some folders are on the same volume
            /// </summary>
            public void CreateSnapshotVolume()
            {
                if (DeviceName == null)
                    throw new InvalidOperationException();
                if (SnapshotPath != null)
                    throw new InvalidOperationException();

                //Create the snapshot volume
                var output = ExecuteCommand("create-lvm-snapshot.sh", $"\"{m_name}\" \"{DeviceName}\" \"{Utility.Utility.AppendDirSeparator(Utility.TempFolder.SystemTempPath)}\"", 0);

                var rex = new System.Text.RegularExpressions.Regex("tmpdir=\"(?<tmpdir>[^\"]+)\"");
                var m = rex.Match(output);

                if (!m.Success)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("tmpdir", output));

                SnapshotPath = rex.Match(output).Groups["tmpdir"].Value;

                if (!System.IO.Directory.Exists(SnapshotPath))
                    throw new Exception(Strings.LinuxSnapshot.MountFolderMissingError(SnapshotPath, output));

                SnapshotPath = Utility.Utility.AppendDirSeparator(SnapshotPath);
            }
        }

        #region Private functions

        /// <summary>
        /// A callback function that takes a non-snapshot path to a folder,
        /// and returns all folders found in a non-snapshot path format.
        /// </summary>
        /// <param name="localFolderPath">The non-snapshot path of the folder to list</param>
        /// <returns>A list of non-snapshot paths</returns>
        protected override string[] ListFolders(string localFolderPath)
        {
            var snap = FindSnapshotByLocalPath(localFolderPath);

            var tmp = System.IO.Directory.GetDirectories(snap.ConvertToSnapshotPath(localFolderPath));
            for (var i = 0; i < tmp.Length; i++)
                tmp[i] = snap.ConvertToLocalPath(tmp[i]);

            return tmp;
        }


        /// <summary>
        /// A callback function that takes a non-snapshot path to a folder,
        /// and returns all files found in a non-snapshot path format.
        /// </summary>
        /// <param name="localFolderPath">The non-snapshot path of the folder to list</param>
        /// <returns>A list of non-snapshot paths</returns>
        protected override string[] ListFiles(string localFolderPath)
        {
            var snap = FindSnapshotByLocalPath(localFolderPath);

            var tmp = System.IO.Directory.GetFiles(snap.ConvertToSnapshotPath(localFolderPath));
            for (var i = 0; i < tmp.Length; i++)
                tmp[i] = snap.ConvertToLocalPath(tmp[i]);
            return tmp;
        }

        /// <summary>
        /// Locates the snapshot instance that maps the path
        /// </summary>
        /// <param name="localPath">The file or folder name to match</param>
        /// <returns>The matching snapshot</returns>
        private SnapShot FindSnapshotByLocalPath(string localPath)
        {
            KeyValuePair<string, SnapShot>? best = null;
            foreach (var s in m_entries)
            {
                if (localPath.StartsWith(s.Key, StringComparison.Ordinal) && (best == null || s.Key.Length > best.Value.Key.Length))
                {
                    best = s;
                }
            }

            if (best != null)
                return best.Value.Value;

            var sb = new StringBuilder();
            sb.Append(Environment.NewLine);

            foreach (var s in m_entries)
            {
                sb.Append($"{s.Key} ({s.Value.MountPoint} -> {s.Value.SnapshotPath}){Environment.NewLine}");
            }

            throw new InvalidOperationException(Strings.LinuxSnapshot.InvalidFilePathError(localPath, sb.ToString()));
        }

        /// <summary>
        /// Locates the snapshot containing the snapshot path
        /// </summary>
        /// <param name="snapshotPath"></param>
        /// <returns>Snapshot containing snapshotPath</returns>
        private SnapShot FindSnapshotBySnapshotPath(string snapshotPath)
        {
            foreach (var snap in m_snapShots)
            {
                if (snapshotPath.StartsWith(snap.SnapshotPath, StringComparison.Ordinal))
                    return snap;
            }

            throw new InvalidOperationException();
        }

        #endregion

        #region ISnapshotService Members

        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetLastWriteTimeUtc(string localPath)
        {
            return System.IO.File.GetLastWriteTimeUtc(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Gets the creation time of a given file in UTC
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public override DateTime GetCreationTimeUtc(string localPath)
        {
            return System.IO.File.GetLastWriteTimeUtc(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public override System.IO.Stream OpenRead(string localPath)
        {
            return System.IO.File.OpenRead(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="localPath">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public override long GetFileSize(string localPath)
        {
            return new System.IO.FileInfo(ConvertToSnapshotPath(localPath)).Length;
        }

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override System.IO.FileAttributes GetAttributes(string localPath)
        {
            return System.IO.File.GetAttributes(ConvertToSnapshotPath(localPath));
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="localPath">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public override string GetSymlinkTarget(string localPath)
        {
            return SYS_IO.GetSymlinkTarget(ConvertToSnapshotPath(localPath));
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
            return SYS_IO.GetMetadata(ConvertToSnapshotPath(localPath), isSymlink, followSymlink);
        }

        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override bool IsBlockDevice(string localPath)
        {
            try
            {
                var n = UnixSupport.File.GetFileType(SystemIOLinux.NormalizePath(localPath));
                switch (n)
                {
                    case UnixSupport.File.FileType.Directory:
                    case UnixSupport.File.FileType.Symlink:
                    case UnixSupport.File.FileType.File:
                        return false;
                    default:
                        return true;
                }
            } 
            catch 
            {
                if (!System.IO.File.Exists(SystemIOLinux.NormalizePath(localPath)))
                    return false;

                throw;
            }
        }
        
        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="localPath">The file or folder to examine</param>
        public override string HardlinkTargetID(string localPath)
        {
            var snapshotPath = ConvertToSnapshotPath(localPath);
            
            if (UnixSupport.File.GetHardlinkCount(snapshotPath) <= 1)
                return null;
            
            return UnixSupport.File.GetInodeTargetID(snapshotPath);
        }

        /// <inheritdoc />
        public override string ConvertToLocalPath(string snapshotPath)
        {
            return FindSnapshotBySnapshotPath(snapshotPath).ConvertToLocalPath(snapshotPath);
        }

        /// <inheritdoc />
        public override string ConvertToSnapshotPath(string localPath)
        {
            return FindSnapshotByLocalPath(localPath).ConvertToSnapshotPath(localPath);
        }

        /// <inheritdoc />
        public override bool IsSnapshot => true;

        #endregion
    }
}
