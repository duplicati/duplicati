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
    public class LinuxSnapshot : ISnapshotService
    {
        protected readonly SystemIOLinux _sysIO = new SystemIOLinux();
        
        /// <summary>
        /// Internal helper class for keeping track of a single snapshot volume
        /// </summary>
        private class SnapShot : IDisposable
        {
            /// <summary>
            /// The unique id of the snapshot
            /// </summary>
            private string m_name;
            /// <summary>
            /// The directory that the snapshot represents
            /// </summary>
            private string m_realDir;
            /// <summary>
            /// The temporary folder that the snapshot is mounted to
            /// </summary>
            private string m_tmpDir;
            /// <summary>
            /// The temporary folder that corresponds to the original folder
            /// </summary>
            private string m_mountPoint;
            /// <summary>
            /// The device name for the entry, used to prevent duplicate entries
            /// </summary>
            private string m_device;

            /// <summary>
            /// Constructs a new snapshot for the given folder
            /// </summary>
            /// <param name="path"></param>
            public SnapShot(string path)
            {
                m_name = string.Format("duplicati-{0}", Guid.NewGuid().ToString());
                m_realDir = System.IO.Directory.Exists(path) ? Utility.Utility.AppendDirSeparator(path) : path;
                GetVolumeName(m_realDir);
            }

            /// <summary>
            /// Gets the path of the folder that this snapshot represents
            /// </summary>
            public string LocalPath { get { return m_realDir; } }

            /// <summary>
            /// Gets a value representing the volume on which the folder resides
            /// </summary>
            public string DeviceName { get { return m_device; } }

            /// <summary>
            /// Gets the path where the snapshot is mounted
            /// </summary>
            public string SnapshotPath { get { return m_tmpDir; } }

            /// <summary>
            /// Gets the path the source disk is originally mounted
            /// </summary>
            public string MountPoint { get { return m_mountPoint; } }

            /// <summary>
            /// Converts a snapshot path to a local path
            /// </summary>
            /// <param name="path">The snapshot path</param>
            /// <returns>The local path</returns>
            public string ConvertToLocalPath(string path)
            {
                if (!path.StartsWith(m_mountPoint))
                    throw new InvalidOperationException();

                return m_tmpDir + path.Substring(m_mountPoint.Length);
            }

            /// <summary>
            /// Converts a local path to a snapshot path
            /// </summary>
            /// <param name="path">The local path</param>
            /// <returns>The snapshot path</returns>
            public string ConvertToSnapshotPath(string path)
            {
                if (!path.StartsWith(m_tmpDir))
                    throw new InvalidOperationException();

                return m_mountPoint + path.Substring(m_tmpDir.Length);
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
                program = System.IO.Path.Combine(System.IO.Path.Combine(Duplicati.Library.AutoUpdater.UpdaterManager.InstalledBaseDir, "lvm-scripts"), program);
                System.Diagnostics.ProcessStartInfo inf = new System.Diagnostics.ProcessStartInfo(program, commandline);
                inf.CreateNoWindow = true;
                inf.RedirectStandardError = true;
                inf.RedirectStandardOutput = true;
                inf.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
                inf.UseShellExecute = false;

                try
                {
                    System.Diagnostics.Process p = System.Diagnostics.Process.Start(inf);

                    //Allow up 20 seconds for the execution
                    if (!p.WaitForExit(30 * 1000))
                    {
                        //Attempt to close down semi-nicely
                        p.Kill();
                        p.WaitForExit(5 * 1000); //This should work, and if it does, prevents a race with any cleanup invocations

                        throw new Duplicati.Library.Interface.UserInformationException(Strings.LinuxSnapshot.ExternalProgramTimeoutError(program, commandline));
                    }

                    //Build the output string. Since the process has exited, these cannot block
                    string output = string.Format("Exit code: {1}{0}{2}{0}{3}", Environment.NewLine, p.ExitCode, p.StandardOutput.ReadToEnd(), p.StandardError.ReadToEnd());

                    //Throw an exception if something went wrong
                    if (p.ExitCode != expectedExitCode)
                        throw new Duplicati.Library.Interface.UserInformationException(Strings.LinuxSnapshot.ScriptExitCodeError(p.ExitCode, expectedExitCode, output));

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
            private void GetVolumeName(string folder)
            {
                //Figure out what logical volume the path is located on
                string output = ExecuteCommand("find-volume.sh", string.Format("\"{0}\"", folder), 0);

                System.Text.RegularExpressions.Regex rex = new System.Text.RegularExpressions.Regex("device=\"(?<device>[^\"]+)\"");
                System.Text.RegularExpressions.Match m = rex.Match(output);

                if (!m.Success)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("device", output));

                m_device = rex.Match(output).Groups["device"].Value;

                if (string.IsNullOrEmpty(m_device) || m_device.Trim().Length == 0)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("device", output));

                rex = new System.Text.RegularExpressions.Regex("mountpoint=\"(?<mountpoint>[^\"]+)\"");
                m = rex.Match(output);

                if (!m.Success)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("mountpoint", output));

                m_mountPoint = rex.Match(output).Groups["mountpoint"].Value;

                if (string.IsNullOrEmpty(m_mountPoint) || m_mountPoint.Trim().Length == 0)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("mountpoint", output));

                m_mountPoint = Utility.Utility.AppendDirSeparator(m_mountPoint);
            }

            /// <summary>
            /// Create the snapshot and mount it, this is not done in the constructor,
            /// because we want to see if some folders are on the same volume
            /// </summary>
            public void CreateSnapshotVolume()
            {
                if (m_device == null)
                    throw new InvalidOperationException();
                if (m_tmpDir != null)
                    throw new InvalidOperationException();

                //Create the snapshot volume
                string output = ExecuteCommand("create-lvm-snapshot.sh", string.Format("\"{0}\" \"{1}\" \"{2}\"", m_name, m_device, Utility.Utility.AppendDirSeparator(Utility.TempFolder.SystemTempPath)), 0);

                System.Text.RegularExpressions.Regex rex = new System.Text.RegularExpressions.Regex("tmpdir=\"(?<tmpdir>[^\"]+)\"");
                System.Text.RegularExpressions.Match m = rex.Match(output);

                if (!m.Success)
                    throw new Exception(Strings.LinuxSnapshot.ScriptOutputError("tmpdir", output));

                m_tmpDir = rex.Match(output).Groups["tmpdir"].Value;

                if (!System.IO.Directory.Exists(m_tmpDir))
                    throw new Exception(Strings.LinuxSnapshot.MountFolderMissingError(m_tmpDir, output));

                m_tmpDir = Utility.Utility.AppendDirSeparator(m_tmpDir);
            }

            #region IDisposable Members

            /// <summary>
            /// Cleanup any used resources
            /// </summary>
            public void Dispose()
            {
                if (m_tmpDir != null && System.IO.Directory.Exists(m_tmpDir))
                {
                    string output = ExecuteCommand("remove-lvm-snapshot.sh", string.Format("\"{0}\" \"{1}\" \"{2}\"", m_name, m_device, m_tmpDir), 0);
                    if (System.IO.Directory.Exists(m_tmpDir))
                        throw new Exception(Strings.LinuxSnapshot.MountFolderNotRemovedError(m_tmpDir, output));

                    m_tmpDir = null;
                    m_device = null;
                }
            }

            #endregion
        }

        /// <summary>
        /// This is the list of the snapshots we have created, which must be disposed
        /// </summary>
        private List<SnapShot> m_activeSnapShots;

        /// <summary>
        /// This is a looup, mapping each source folder to the corresponding snapshot
        /// </summary>
        private List<KeyValuePair<string, SnapShot>> m_entries;

        /// <summary>
        /// Constructs a new snapshot module using LVM
        /// </summary>
        /// <param name="folders">The list of folders to create snapshots for</param>
        /// <param name="options">A set of commandline options</param>
        public LinuxSnapshot(string[] sources, Dictionary<string, string> options)
        {
            try
            {
                m_entries = new List<KeyValuePair<string,SnapShot>>();

                //Make sure we do not create more snapshots than we have to
                Dictionary<string, SnapShot> snaps = new Dictionary<string, SnapShot>();
                foreach (string s in sources)
                {
                    SnapShot sn = new SnapShot(s);
                    if (!snaps.ContainsKey(sn.DeviceName))
                        snaps.Add(sn.DeviceName, sn);

                    m_entries.Add(new KeyValuePair<string, SnapShot>(s, snaps[sn.DeviceName]));
                }

                m_activeSnapShots = new List<SnapShot>(snaps.Values);

                //We have all the snapshots that we need, lets activate them
                foreach (SnapShot s in m_activeSnapShots)
                    s.CreateSnapshotVolume();
            }
            catch
            {
                //If something goes wrong, try to clean up
                try { Dispose(); }
                catch { }

                throw;
            }
        }

        #region Private functions

        /// <summary>
        /// A callback function that takes a non-snapshot path to a folder,
        /// and returns all folders found in a non-snapshot path format.
        /// </summary>
        /// <param name="folder">The non-snapshot path of the folder to list</param>
        /// <returns>A list of non-snapshot paths</returns>
        private string[] ListFolders(string folder)
        {
            KeyValuePair<string, SnapShot> snap = FindSnapShotByLocalPath(folder);

            string[] tmp = System.IO.Directory.GetDirectories(ConvertToSnapshotPath(snap, folder));
            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = ConvertToLocalPath(snap, tmp[i]);

            return tmp;
        }


        /// <summary>
        /// A callback function that takes a non-snapshot path to a folder,
        /// and returns all files found in a non-snapshot path format.
        /// </summary>
        /// <param name="folder">The non-snapshot path of the folder to list</param>
        /// <returns>A list of non-snapshot paths</returns>
        private string[] ListFiles(string folder)
        {
            KeyValuePair<string, SnapShot> snap = FindSnapShotByLocalPath(folder);

            string[] tmp = System.IO.Directory.GetFiles(ConvertToSnapshotPath(snap, folder));
            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = ConvertToLocalPath(snap, tmp[i]);
            return tmp;
        }

        /// <summary>
        /// Locates the snapshot instance that maps the path
        /// </summary>
        /// <param name="name">The file or folder name to match</param>
        /// <returns>The matching snapshot</returns>
        private KeyValuePair<string, SnapShot> FindSnapShotByLocalPath(string name)
        {
            KeyValuePair<string, SnapShot>? best = null;

            foreach (KeyValuePair<string, SnapShot> s in m_entries)
                if (name.StartsWith(s.Key) && (best == null || s.Key.Length > best.Value.Key.Length))
                    best = s;

            if (best == null)
            {
                StringBuilder sb = new StringBuilder();
                sb.Append(Environment.NewLine);

                foreach (KeyValuePair<string, SnapShot> s in m_entries)
                    sb.AppendFormat("{0} ({1} -> {2}){3}", s.Key, s.Value.MountPoint, s.Value.SnapshotPath, Environment.NewLine);

                throw new InvalidOperationException(Strings.LinuxSnapshot.InvalidFilePathError(name, sb.ToString()));
            }

            return best.Value;
        }

        /// <summary>
        /// Converts a local path to a snapshot path
        /// </summary>
        /// <param name="snap">The snapshot that represents the mapping</param>
        /// <param name="file">The filename to convert</param>
        /// <returns>The converted path</returns>
        private string ConvertToSnapshotPath(KeyValuePair<string, SnapShot> snap, string file)
        {
            return snap.Value.SnapshotPath + file.Substring(snap.Value.MountPoint.Length);
        }

        /// <summary>
        /// Converts a snapshot path to a local path
        /// </summary>
        /// <param name="snap">The snapshot that represents the mapping</param>
        /// <param name="file">The filename to convert</param>
        /// <returns>The converted path</returns>
        private string ConvertToLocalPath(KeyValuePair<string, SnapShot> snap, string file)
        {
            return snap.Value.MountPoint + file.Substring(snap.Value.SnapshotPath.Length);
        }

        #endregion

        #region ISnapshotService Members

        /// <summary>
        /// Enumerates all files and folders in the snapshot
        /// </summary>
        /// <param name="callback">The callback to invoke with each found path</param>
        /// <param name="errorCallback">The callback used to report errors</param>
        public IEnumerable<string> EnumerateFilesAndFolders(Duplicati.Library.Utility.Utility.EnumerationFilterDelegate callback, Duplicati.Library.Utility.Utility.ReportAccessError errorCallback)
        {
            return m_entries.SelectMany(
                s => Utility.Utility.EnumerateFileSystemEntries(s.Key, callback, this.ListFolders, this.ListFiles, this.GetAttributes, errorCallback)
            );
        }
        
        /// <summary>
        /// Gets the last write time of a given file in UTC
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetLastWriteTimeUtc(string file)
        {
            return System.IO.File.GetLastWriteTimeUtc(ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file));
        }

        /// <summary>
        /// Gets the creation time of a given file in UTC
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The last write time of the file</returns>
        public DateTime GetCreationTimeUtc(string file)
        {
            return System.IO.File.GetLastWriteTimeUtc(ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file));
        }

        /// <summary>
        /// Opens a file for reading
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>An open filestream that can be read</returns>
        public System.IO.Stream OpenRead(string file)
        {
            return System.IO.File.OpenRead(ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file));
        }

        /// <summary>
        /// Returns the size of a file
        /// </summary>
        /// <param name="file">The full path to the file in non-snapshot format</param>
        /// <returns>The lenth of the file</returns>
        public long GetFileSize(string file)
        {
            return new System.IO.FileInfo(ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file)).Length;
        }

        /// <summary>
        /// Gets the attributes for the given file or folder
        /// </summary>
        /// <returns>The file attributes</returns>
        /// <param name="file">The file or folder to examine</param>
        public System.IO.FileAttributes GetAttributes(string file)
        {
            return System.IO.File.GetAttributes(ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file));
        }

        /// <summary>
        /// Returns the symlink target if the entry is a symlink, and null otherwise
        /// </summary>
        /// <param name="file">The file or folder to examine</param>
        /// <returns>The symlink target</returns>
        public string GetSymlinkTarget(string file)
        {
            var local = ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file);
            return UnixSupport.File.GetSymlinkTarget(NoSnapshot.NormalizePath(local));
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
            var local = ConvertToSnapshotPath(FindSnapShotByLocalPath(file), file);
            return _sysIO.GetMetadata(local, isSymlink, followSymlink);
        }
        
        /// <summary>
        /// Gets a value indicating if the path points to a block device
        /// </summary>
        /// <returns><c>true</c> if this instance is a block device; otherwise, <c>false</c>.</returns>
        /// <param name="file">The file or folder to examine</param>
        public bool IsBlockDevice(string file)
        {
            var n = UnixSupport.File.GetFileType(NoSnapshot.NormalizePath(file));
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

        /// <summary>
        /// Gets a unique hardlink target ID
        /// </summary>
        /// <returns>The hardlink ID</returns>
        /// <param name="file">The file or folder to examine</param>
        public string HardlinkTargetID(string path)
        {
            var local = ConvertToSnapshotPath(FindSnapShotByLocalPath(path), path);
            local = NoSnapshot.NormalizePath(local);
            
            if (UnixSupport.File.GetHardlinkCount(local) <= 1)
                return null;
            
            return UnixSupport.File.GetInodeTargetID(local);
        }
        #endregion

        #region IDisposable Members

        /// <summary>
        /// Releases any held resources
        /// </summary>
        public void Dispose()
        {
            if (m_activeSnapShots != null)
            {
                Exception exs = null;

                //Attempt to clean out as many as possible
                foreach(SnapShot s in m_activeSnapShots)
                    try { s.Dispose(); }
                    catch (Exception ex) { exs = ex; }

                //Don't try this again
                m_activeSnapShots = null;

                //Report errors, if any
                if (exs != null)
                    throw exs;
            }
        }

        #endregion
    }
}
