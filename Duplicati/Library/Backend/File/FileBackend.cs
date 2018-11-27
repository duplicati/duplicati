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
using System.Runtime.InteropServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Common;

namespace Duplicati.Library.Backend
{
    public class File : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string OPTION_DESTINATION_MARKER = "alternate-destination-marker";
        private const string OPTION_ALTERNATE_PATHS = "alternate-target-paths";
        private const string OPTION_MOVE_FILE = "use-move-for-put";
        private const string OPTION_FORCE_REAUTH = "force-smb-authentication";

        private readonly string m_path;
        private string m_username;
        private string m_password;
        private readonly bool m_moveFile;
        private bool m_hasAutenticated;
        private readonly bool m_forceReauth;

        private readonly byte[] m_copybuffer = new byte[Utility.Utility.DEFAULT_BUFFER_SIZE];

        private static ISystemIO systemIO = SystemIO.IO_OS;

        public File()
        {
        }

        public File(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            m_path = uri.HostAndPath;
            
            if (options.ContainsKey("auth-username"))
                m_username = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                m_password = options["auth-password"];
            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;
            
            if (!System.IO.Path.IsPathRooted(m_path))
                m_path = systemIO.PathGetFullPath(m_path);

            if (options.ContainsKey(OPTION_ALTERNATE_PATHS))
            {
                List<string> paths = new List<string>
                {
                    m_path
                };
                paths.AddRange(options[OPTION_ALTERNATE_PATHS].Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries));

                //On windows we expand the drive letter * to all drives
                if (!Platform.IsClientPosix)
                {
                    System.IO.DriveInfo[] drives = System.IO.DriveInfo.GetDrives();
                    
                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (paths[i].StartsWith("*:", StringComparison.Ordinal))
                        {
                            string rpl_path = paths[i].Substring(1);
                            paths.RemoveAt(i);
                            i--;
                            foreach (System.IO.DriveInfo di in drives)
                                paths.Insert(++i, di.Name[0] + rpl_path);
                        }
                    }
                }

                string markerfile = null;

                //If there is a marker file, we do not allow the primary target path
                // to be accepted, unless it contains the marker file
                if (options.ContainsKey(OPTION_DESTINATION_MARKER))
                {
                    markerfile = options[OPTION_DESTINATION_MARKER];
                    m_path = null;
                }

                foreach (string p in paths)
                {
                    try
                    {
                        if (systemIO.DirectoryExists(p) && (markerfile == null || systemIO.FileExists(systemIO.PathCombine(p, markerfile))))
                        {
                            m_path = p;
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (m_path == null)
                    throw new UserInformationException(Strings.FileBackend.NoDestinationWithMarkerFileError(markerfile, paths.ToArray()), "NoDestinationWithMarker");
            }

            m_moveFile = Utility.Utility.ParseBoolOption(options, OPTION_MOVE_FILE);
            m_forceReauth = Utility.Utility.ParseBoolOption(options, OPTION_FORCE_REAUTH);
            m_hasAutenticated = false;
        }

        private void PreAuthenticate()
        {
            try
            {
                if (!string.IsNullOrEmpty(m_username) && m_password != null && !m_hasAutenticated)
                {
                    Win32.PreAuthenticate(m_path, m_username, m_password, m_forceReauth);
                    m_hasAutenticated = true;
                }
            }
            catch
            { }
        }

        private string GetRemoteName(string remotename)
        {
            PreAuthenticate();

            if (!systemIO.DirectoryExists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            return systemIO.PathCombine(m_path, remotename);
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return Strings.FileBackend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "file"; }
        }

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public IEnumerable<IFileEntry> List()
        {
            PreAuthenticate();

            if (!systemIO.DirectoryExists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            foreach (string s in systemIO.EnumerateFileSystemEntries(m_path))
            {
                yield return systemIO.FileEntry(s);
            }

            foreach (string s in systemIO.EnumerateDirectories(m_path))
            {
                yield return systemIO.DirectoryEntry(s);
            }
        }

#if DEBUG_RETRY
        private static Random random = new Random();
        public void Put(string remotename, System.IO.Stream stream)
        {
            using(System.IO.FileStream writestream = systemIO.FileOpenWrite(GetRemoteName(remotename)))
            {
                if (random.NextDouble() > 0.6666)
                    throw new Exception("Random upload failure");
                Utility.Utility.CopyStream(stream, writestream);
            }
        }
#else
        public void Put(string remotename, System.IO.Stream stream)
        {
            using(System.IO.FileStream writestream = systemIO.FileOpenWrite(GetRemoteName(remotename)))
                Utility.Utility.CopyStream(stream, writestream, true, m_copybuffer);
        }
#endif

        public void Get(string remotename, System.IO.Stream stream)
        {
            // FileOpenRead has flags System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read
            using (System.IO.FileStream readstream = systemIO.FileOpenRead(GetRemoteName(remotename)))
                Utility.Utility.CopyStream(readstream, stream, true, m_copybuffer);
        }

        public void Put(string remotename, string filename)
        {
            string path = GetRemoteName(remotename);
            if (m_moveFile)
            {
                if (systemIO.FileExists(path))
                    systemIO.FileDelete(path);
                
                systemIO.FileMove(filename, path);
            }
            else
                systemIO.FileCopy(filename, path, true);
        }

        public void Get(string remotename, string filename)
        {
            systemIO.FileCopy(GetRemoteName(remotename), filename, true);
        }

        public void Delete(string remotename)
        {
            systemIO.FileDelete(GetRemoteName(remotename));
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.FileBackend.DescriptionAuthPasswordShort, Strings.FileBackend.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.FileBackend.DescriptionAuthUsernameShort, Strings.FileBackend.DescriptionAuthUsernameLong),
                    new CommandLineArgument(OPTION_DESTINATION_MARKER, CommandLineArgument.ArgumentType.String, Strings.FileBackend.AlternateDestinationMarkerShort, Strings.FileBackend.AlternateDestinationMarkerLong(OPTION_ALTERNATE_PATHS)),
                    new CommandLineArgument(OPTION_ALTERNATE_PATHS, CommandLineArgument.ArgumentType.Path, Strings.FileBackend.AlternateTargetPathsShort, Strings.FileBackend.AlternateTargetPathsLong(OPTION_DESTINATION_MARKER, System.IO.Path.PathSeparator)),
                    new CommandLineArgument(OPTION_MOVE_FILE, CommandLineArgument.ArgumentType.Boolean, Strings.FileBackend.UseMoveForPutShort, Strings.FileBackend.UseMoveForPutLong),
                    new CommandLineArgument(OPTION_FORCE_REAUTH, CommandLineArgument.ArgumentType.Boolean, Strings.FileBackend.ForceReauthShort, Strings.FileBackend.ForceReauthLong),
                });

            }
        }

        public string Description
        {
            get
            {
                return Strings.FileBackend.Description;
            }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            if (systemIO.DirectoryExists(m_path))
                throw new FolderAreadyExistedException();

            systemIO.DirectoryCreate(m_path);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            m_username = null;
            m_password = null;
        }

        #endregion

        public static bool PreAuthenticate(string path, string username, string password, bool forceReauth)
        {
            return Win32.PreAuthenticate(path, username, password, forceReauth);
        }

        private System.IO.DriveInfo GetDrive()
        {
            string root;
            if (Platform.IsClientPosix)
            {
                string path = Util.AppendDirSeparator(systemIO.PathGetFullPath(m_path));
                root = "/";

                //Find longest common prefix from mounted devices
                //TODO: Can trick this with symlinks, where the symlink is on one mounted volume,
                // and the actual storage is on another
                foreach (System.IO.DriveInfo di in System.IO.DriveInfo.GetDrives())
                    if (path.StartsWith(Util.AppendDirSeparator(di.Name), StringComparison.Ordinal) && di.Name.Length > root.Length)
                        root = di.Name;
            }
            else
            {
                root = systemIO.GetPathRoot(m_path);
            }

            // On Windows, DriveInfo is only valid for lettered drives. (e.g., not for UNC paths and shares)
            // So only attempt to get it if we aren't on Windows or if the root starts with a letter.
            if (!Platform.IsClientWindows || (root.Length > 0 && char.IsLetter(root[0])))
            {
                try
                {
                    return new System.IO.DriveInfo(root);
                }
                catch (ArgumentException)
                {
                    // If there was a problem, fall back to returning null
                }
            }

            return null;
        }

        public IQuotaInfo Quota
        {
            get
            {
                System.IO.DriveInfo driveInfo = this.GetDrive();
                if (driveInfo != null)
                {
                    return new QuotaInfo(driveInfo.TotalSize, driveInfo.AvailableFreeSpace);
                }

                if (Platform.IsClientWindows)
                {
                    // If we can't get the DriveInfo on Windows, fallback to GetFreeDiskSpaceEx
                    // https://stackoverflow.com/questions/2050343/programmatically-determining-space-available-from-unc-path
                    return GetDiskFreeSpace(m_path);
                }

                return null;
            }
        }

        public string[] DNSName
        {
            get { return null; }
        }

        public void Rename(string oldname, string newname)
        {
            var source = GetRemoteName(oldname);
            var target = GetRemoteName(newname);
            if (systemIO.FileExists(target))
                systemIO.FileDelete(target);
            systemIO.FileMove(source, target);
        }

        /// <summary>
        /// Get the disk free space using the Win32 API's GetDiskFreeSpaceEx function.
        /// </summary>
        /// <param name="directory">Directory</param>
        /// <returns>Quota info</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public static QuotaInfo GetDiskFreeSpace(string directory)
        {
            ulong available;
            ulong total;
            ulong free;
            if (WindowsDriveHelper.GetDiskFreeSpaceEx(directory, out available, out total, out free))
            {
                return new QuotaInfo((long)total, (long)available);
            }
            else
            {
                return null;
            }
        }

        private static class WindowsDriveHelper
        {
            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool GetDiskFreeSpaceEx(
                string lpDirectoryName,
                out ulong lpFreeBytesAvailable,
                out ulong lpTotalNumberOfBytes,
                out ulong lpTotalNumberOfFreeBytes);
        }
    }
}
