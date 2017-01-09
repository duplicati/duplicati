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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class File : IBackend, IStreamingBackend, IQuotaEnabledBackend, IRenameEnabledBackend
    {
        private const string OPTION_DESTINATION_MARKER = "alternate-destination-marker";
        private const string OPTION_ALTERNATE_PATHS = "alternate-target-paths";
        private const string OPTION_MOVE_FILE = "use-move-for-put";
        private const string OPTION_FORCE_REAUTH = "force-smb-authentication";

        private string m_path;
        private string m_username;
        private string m_password;
        private bool m_moveFile;
        private bool m_hasAutenticated;
        private bool m_forceReauth;

        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];


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
                m_path = System.IO.Path.GetFullPath(m_path);

            if (options.ContainsKey(OPTION_ALTERNATE_PATHS))
            {
                List<string> paths = new List<string>();
                paths.Add(m_path);
                paths.AddRange(options[OPTION_ALTERNATE_PATHS].Split(new string[] { System.IO.Path.PathSeparator.ToString() }, StringSplitOptions.RemoveEmptyEntries));

                //On windows we expand the drive letter * to all drives
                if (!Utility.Utility.IsClientLinux)
                {
                    System.IO.DriveInfo[] drives = System.IO.DriveInfo.GetDrives();
                    
                    for (int i = 0; i < paths.Count; i++)
                    {
                        if (paths[i].StartsWith("*:"))
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
                        if (System.IO.Directory.Exists(p) && (markerfile == null || System.IO.File.Exists(System.IO.Path.Combine(p, markerfile))))
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
                    throw new UserInformationException(Strings.FileBackend.NoDestinationWithMarkerFileError(markerfile, paths.ToArray()));
            }

            m_moveFile = Utility.Utility.ParseBoolOption(options, OPTION_MOVE_FILE);
            m_forceReauth = Utility.Utility.ParseBoolOption(options, OPTION_FORCE_REAUTH);
            m_hasAutenticated = false;
        }

        private void PreAuthenticate()
        {
            try
            {
                if (!string.IsNullOrEmpty(m_username) && m_password != null)
                {
                    if (!m_hasAutenticated)
                    {
                        Win32.PreAuthenticate(m_path, m_username, m_password, m_forceReauth);
                        m_hasAutenticated = true;
                    }
                }
            }
            catch
            { }
        }

        private string GetRemoteName(string remotename)
        {
            PreAuthenticate();

            if (!System.IO.Directory.Exists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            return System.IO.Path.Combine(m_path, remotename);
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

        public List<IFileEntry> List()
        {
            List<IFileEntry> ls = new List<IFileEntry>();

            PreAuthenticate();

            if (!System.IO.Directory.Exists(m_path))
                throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));

            foreach (string s in System.IO.Directory.GetFiles(m_path))
            {
                System.IO.FileInfo fi = new System.IO.FileInfo(s);
                ls.Add(new FileEntry(fi.Name, fi.Length, fi.LastAccessTime, fi.LastWriteTime));
            }

            foreach (string s in System.IO.Directory.GetDirectories(m_path))
            {
                System.IO.DirectoryInfo di = new System.IO.DirectoryInfo(s);
                FileEntry fe = new FileEntry(di.Name, 0, di.LastAccessTime, di.LastWriteTime);
                fe.IsFolder = true;
                ls.Add(fe);
            }

            return ls;
        }

#if DEBUG_RETRY
        private static Random random = new Random();
        public void Put(string remotename, System.IO.Stream stream)
        {
            using(System.IO.FileStream writestream = System.IO.File.Open(GetRemoteName(remotename), System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
            {
                if (random.NextDouble() > 0.6666)
                    throw new Exception("Random upload failure");
                Utility.Utility.CopyStream(stream, writestream);
            }
        }
#else
        public void Put(string remotename, System.IO.Stream stream)
        {
            using(System.IO.FileStream writestream = System.IO.File.Open(GetRemoteName(remotename), System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Utility.Utility.CopyStream(stream, writestream, true, m_copybuffer);
        }
#endif

        public void Get(string remotename, System.IO.Stream stream)
        {
            using (System.IO.FileStream readstream = System.IO.File.Open(GetRemoteName(remotename), System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Utility.Utility.CopyStream(readstream, stream, true, m_copybuffer);
        }

        public void Put(string remotename, string filename)
        {
            string path = GetRemoteName(remotename);
            if (m_moveFile)
            {
                if (System.IO.File.Exists(path))
                    System.IO.File.Delete(path);
                
                System.IO.File.Move(filename, path);
            }
            else
                System.IO.File.Copy(filename, path, true);
        }

        public void Get(string remotename, string filename)
        {
            System.IO.File.Copy(GetRemoteName(remotename), filename, true);
        }

        public void Delete(string remotename)
        {
            System.IO.File.Delete(GetRemoteName(remotename));
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
            List();
        }

        public void CreateFolder()
        {
            if (System.IO.Directory.Exists(m_path))
                throw new FolderAreadyExistedException();

            System.IO.Directory.CreateDirectory(m_path);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            if (m_username != null)
                m_username = null;
            if (m_password != null)
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
            if (Utility.Utility.IsClientLinux)
            {
                string path = Utility.Utility.AppendDirSeparator(System.IO.Path.GetFullPath(m_path));
                root = "/";

                //Find longest common prefix from mounted devices
                //TODO: Can trick this with symlinks, where the symlink is on one mounted volume,
                // and the actual storage is on another
                foreach (System.IO.DriveInfo di in System.IO.DriveInfo.GetDrives())
                    if (path.StartsWith(Utility.Utility.AppendDirSeparator(di.Name)) && di.Name.Length > root.Length)
                        root = di.Name;
            }
            else
            {
                root = System.IO.Path.GetPathRoot(m_path);
            }

            return new System.IO.DriveInfo(root);
        }

        public long TotalQuotaSpace
        {
            get
            {
                try { return GetDrive().TotalSize; }
                catch { }

                return -1;
            }
        }


        public long FreeQuotaSpace
        {
            get
            {
                try { return GetDrive().AvailableFreeSpace; }
                catch { }

                return -1;
            }
        }

        public void Rename(string oldname, string newname)
        {
            var source = GetRemoteName(oldname);
            var target = GetRemoteName(newname);
            if (System.IO.File.Exists(target))
                System.IO.File.Delete(target);
            System.IO.File.Move(source, target);
        }
    }
}
