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
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;
using Renci.SshNet;

namespace Duplicati.Library.Backend
{
    public class SSHv2 : IBackend, IStreamingBackend
    {
        public const string SSH_KEYFILE_OPTION = "ssh-keyfile";

        Dictionary<string, string> m_options;

        private string m_server;
        private string m_path;
        private string m_username;
        private string m_password;

        private int m_port = 22;
		
		private SftpClient m_con;

        public SSHv2()
        {
        }

        public SSHv2(string url, Dictionary<string, string> options)
            : this()
        {
            m_options = options;
            var uri = new Utility.Uri(url);
            uri.RequireHost();
            
            if (options.ContainsKey("auth-username"))
                m_username = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                m_password = options["auth-password"];
            if (!string.IsNullOrEmpty(uri .Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri .Password))
                m_password = uri.Password;

            m_path = uri.Path;

            if (!m_path.EndsWith("/"))
                m_path += "/";

            m_server = uri.Host;

            if (uri.Port > 0)
                m_port = uri.Port;
        }

        #region IBackend Members
        
        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            using (SftpClient con = CreateConnection(false)) 
            {
                //Bugfix, some SSH servers do not like a trailing slash
                string p = m_path;
                if (p.EndsWith("/"))
                    p.Substring(0, p.Length - 1);
                con.CreateDirectory(p);
            }
        }

        public string DisplayName
        {
            get { return Strings.SSHv2Backend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "ssh"; }
        }

        public void Put(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, fs);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            CreateConnection(true).DeleteFile(remotename);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.SSHv2Backend.DescriptionAuthPasswordShort, Strings.SSHv2Backend.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.SSHv2Backend.DescriptionAuthUsernameShort, Strings.SSHv2Backend.DescriptionAuthUsernameLong),
                    new CommandLineArgument(SSH_KEYFILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.SSHv2Backend.DescriptionSshkeyfileShort, Strings.SSHv2Backend.DescriptionSshkeyfileLong),
                });

            }
        }

        public string Description
        {
            get { return Strings.SSHv2Backend.Description; }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
			if (m_con != null)
			{
				m_con.Dispose();
				m_con = null;
			}
        }

        #endregion

        #region IStreamingBackend Implementation

        public void Put(string remotename, System.IO.Stream stream)
        {
            CreateConnection(true).UploadFile(stream, remotename);
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            CreateConnection(true).DownloadFile(remotename, stream);
        }

        #endregion

        #region Implementation

        private SftpClient CreateConnection(bool changeDir)
        {
			if (changeDir && m_con != null)
				return m_con;
			
            SftpClient con;

            string keyfile;
            m_options.TryGetValue(SSH_KEYFILE_OPTION, out keyfile);

            if ((keyfile ?? "").Trim().Length > 0)
                con = new SftpClient(m_server, m_port, m_username, ValidateKeyFile(m_options[SSH_KEYFILE_OPTION], m_password));
            else
                con = new SftpClient(m_server, m_username, m_password);

            con.Connect();

            try
            {
                if (!string.IsNullOrEmpty(m_path) && changeDir)
                    con.ChangeDirectory(m_path);
            }
            catch (Exception ex)
            {
                throw new Interface.FolderMissingException(string.Format(Strings.SSHv2Backend.FolderNotFoundManagedError, m_path, ex.Message), ex);
            }
			
			if (changeDir)
				m_con = con;
			
            return con;
        }

        public List<IFileEntry> List()
        {
            List<IFileEntry> files = new List<IFileEntry>();

            string path = ".";

            foreach (Renci.SshNet.Sftp.SftpFile ls in CreateConnection(true).ListDirectory(path))
                if (ls.Name.ToString() != "." && ls.Name.ToString() != "..")
                    files.Add(new FileEntry(ls.Name.ToString(), ls.Length, ls.LastAccessTime, ls.LastWriteTime));

            return files;
        }

        public static Renci.SshNet.PrivateKeyFile ValidateKeyFile(string filename, string password)
        {
            if (String.IsNullOrEmpty(password))
                return new Renci.SshNet.PrivateKeyFile(filename);
            else
                return new Renci.SshNet.PrivateKeyFile(filename, password);
        }
        
        #endregion
    }
}


 