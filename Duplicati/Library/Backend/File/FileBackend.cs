#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
    public class File : IBackend_v2, IStreamingBackend, IBackendGUI
    {
        private const string OPTION_DESTINATION_MARKER = "alternate-destination-marker";
        private const string OPTION_ALTERNATE_PATHS = "alternate-target-paths";

        private string m_path;
        private string m_username;
        private string m_password;
        Dictionary<string, string> m_options;

        public File()
        {
        }

        public File(string url, Dictionary<string, string> options)
        {
            m_options = options;
            m_path = url.Substring("file://".Length);

            if (m_path.IndexOf("@") > 0)
            {
                m_username = m_path.Substring(0, m_path.IndexOf("@"));
                m_path = m_path.Substring(m_path.IndexOf("@") + 1);

                if (m_username.IndexOf(":") > 0)
                {
                    m_password = m_username.Substring(0, m_username.IndexOf(":"));
                    m_username = m_username.Substring(m_username.IndexOf(":") + 1);
                }
                else
                {
                    if (options.ContainsKey("ftp-password"))
                        m_password = options["ftp-password"];
                }
            }
            else
            {
                if (options.ContainsKey("ftp-username"))
                    m_username = options["ftp-username"];
                if (options.ContainsKey("ftp-password"))
                    m_password = options["ftp-password"];
            }

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
                    throw new Exception(string.Format(Strings.FileBackend.NoDestinationWithMarkerFileError, markerfile, string.Join(System.IO.Path.PathSeparator.ToString(), paths.ToArray())));
            }
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

            //Attempt to apply credentials
            if (!string.IsNullOrEmpty(m_username) && m_password != null)
                Win32.PreAuthenticate(m_path, m_username, m_password);

            if (!System.IO.Directory.Exists(m_path))
                    throw new FolderMissingException(string.Format(Strings.FileBackend.FolderMissingError, m_path));

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

        public void Put(string remotename, System.IO.Stream stream)
        {
            if (!System.IO.Directory.Exists(m_path))
                throw new FolderMissingException(string.Format(Strings.FileBackend.FolderMissingError, m_path));

            string path = System.IO.Path.Combine(m_path, remotename);
            using (System.IO.FileStream writestream = System.IO.File.Open(path, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Utility.Utility.CopyStream(stream, writestream);
        }

        public void Put(string remotename, string filename)
        {
            using (System.IO.FileStream readstream = System.IO.File.Open(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, readstream);
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            if (!System.IO.Directory.Exists(m_path))
                throw new FolderMissingException(string.Format(Strings.FileBackend.FolderMissingError, m_path));

            string path = System.IO.Path.Combine(m_path, remotename);
            using (System.IO.FileStream readstream = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Utility.Utility.CopyStream(readstream, stream);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream writestream = System.IO.File.Open(filename, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, writestream);
        }

        public void Delete(string remotename)
        {
            string path = System.IO.Path.Combine(m_path, remotename);
            System.IO.File.Delete(path);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, Strings.FileBackend.DescriptionFTPPasswordShort, Strings.FileBackend.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.FileBackend.DescriptionFTPUsernameShort, Strings.FileBackend.DescriptionFTPUsernameLong),
                    new CommandLineArgument(OPTION_DESTINATION_MARKER, CommandLineArgument.ArgumentType.String, Strings.FileBackend.AlternateDestinationMarkerShort, string.Format(Strings.FileBackend.AlternateDestinationMarkerLong, OPTION_ALTERNATE_PATHS)),
                    new CommandLineArgument(OPTION_ALTERNATE_PATHS, CommandLineArgument.ArgumentType.Path, Strings.FileBackend.AlternateTargetPathsShort, string.Format(Strings.FileBackend.AlternateTargetPathsLong, OPTION_DESTINATION_MARKER, System.IO.Path.PathSeparator)),
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

        #endregion

        #region IBackend_v2 Members

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
            if (m_options != null)
                m_options = null;
            if (m_username != null)
                m_username = null;
            if (m_password != null)
                m_password = null;
        }

        #endregion

        public static bool PreAuthenticate(string path, string username, string password)
        {
            return Win32.PreAuthenticate(path, username, password);
        }

        #region IBackendGUI Members

        public string PageTitle
        {
            get { return FileUI.PageTitle; }
        }

        public string PageDescription
        {
            get { return FileUI.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new FileUI(options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((FileUI)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((FileUI)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return FileUI.GetConfiguration(guiOptions, commandlineOptions);
        }

        #endregion
    }
}
