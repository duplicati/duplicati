﻿#region Disclaimer / License
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
using System.Linq;
using System.Text;
using Duplicati.Library.Interface;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace Duplicati.Library.Backend
{
    public class SSHv2 : IStreamingBackend, IRenameEnabledBackend, IBackend, IDisposable
    {
        public const string SSH_KEYFILE_OPTION = "ssh-keyfile";
        public const string SSH_KEYFILE_INLINE = "ssh-key";
        public const string SSH_FINGERPRINT_OPTION = "ssh-fingerprint";
        public const string SSH_FINGERPRINT_ACCEPT_ANY_OPTION = "ssh-accept-any-fingerprints";
        public const string KEYFILE_URI = "sshkey://";
        public const string SSH_TIMEOUT_OPTION = "ssh-operation-timeout";
        public const string SSH_KEEPALIVE_OPTION = "ssh-keepalive";

        readonly Dictionary<string, string> m_options;

        private readonly string m_server;
        private readonly string m_path;
        private readonly string m_username;
        private readonly string m_password;
        private readonly string m_fingerprint;
        private readonly bool m_fingerprintallowall;
        private readonly TimeSpan m_operationtimeout;
        private readonly TimeSpan m_keepaliveinterval;

        private readonly int m_port = 22;

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
            if (options.ContainsKey(SSH_FINGERPRINT_OPTION))
                m_fingerprint = options[SSH_FINGERPRINT_OPTION];
            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;

            m_fingerprintallowall = Utility.Utility.ParseBoolOption(options, SSH_FINGERPRINT_ACCEPT_ANY_OPTION);

            m_path = uri.Path;

            if (!string.IsNullOrWhiteSpace(m_path))
            {
                m_path = Duplicati.Library.Utility.Utility.AppendDirSeparator(m_path, "/");
            }

            if (!m_path.StartsWith("/", StringComparison.Ordinal))
                m_path = "/" + m_path;

            m_server = uri.Host;

            if (uri.Port > 0)
                m_port = uri.Port;

            string timeoutstr;
            options.TryGetValue(SSH_TIMEOUT_OPTION, out timeoutstr);

            if (!string.IsNullOrWhiteSpace(timeoutstr))
                m_operationtimeout = Library.Utility.Timeparser.ParseTimeSpan(timeoutstr);

            options.TryGetValue(SSH_KEEPALIVE_OPTION, out timeoutstr);

            if (!string.IsNullOrWhiteSpace(timeoutstr))
                m_keepaliveinterval = Library.Utility.Timeparser.ParseTimeSpan(timeoutstr);

        }

        #region IBackend Members

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
            CreateConnection();
            m_con.CreateDirectory(m_path);
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
            try
            {
                CreateConnection();
                ChangeDirectory(m_path);
                m_con.DeleteFile(remotename);
            }
            catch (SftpPathNotFoundException ex)
            {
                throw new FileMissingException(ex);
            }

        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.SSHv2Backend.DescriptionAuthPasswordShort, Strings.SSHv2Backend.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.SSHv2Backend.DescriptionAuthUsernameShort, Strings.SSHv2Backend.DescriptionAuthUsernameLong),
                    new CommandLineArgument(SSH_FINGERPRINT_OPTION, CommandLineArgument.ArgumentType.String, Strings.SSHv2Backend.DescriptionFingerprintShort, Strings.SSHv2Backend.DescriptionFingerprintLong),
                    new CommandLineArgument(SSH_FINGERPRINT_ACCEPT_ANY_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.SSHv2Backend.DescriptionAnyFingerprintShort, Strings.SSHv2Backend.DescriptionAnyFingerprintLong),
                    new CommandLineArgument(SSH_KEYFILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.SSHv2Backend.DescriptionSshkeyfileShort, Strings.SSHv2Backend.DescriptionSshkeyfileLong),
                    new CommandLineArgument(SSH_KEYFILE_INLINE, CommandLineArgument.ArgumentType.Password, Strings.SSHv2Backend.DescriptionSshkeyShort, Strings.SSHv2Backend.DescriptionSshkeyLong(KEYFILE_URI)),
                    new CommandLineArgument(SSH_TIMEOUT_OPTION, CommandLineArgument.ArgumentType.Timespan, Strings.SSHv2Backend.DescriptionSshtimeoutShort, Strings.SSHv2Backend.DescriptionSshtimeoutLong, "0"),
                    new CommandLineArgument(SSH_KEEPALIVE_OPTION, CommandLineArgument.ArgumentType.Timespan, Strings.SSHv2Backend.DescriptionSshkeepaliveShort, Strings.SSHv2Backend.DescriptionSshkeepaliveLong, "0"),
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
                try
                {
                    m_con.Dispose();
                }
                catch (System.Net.Sockets.SocketException)
                {
                    //If the operating system sometimes close socket before disposal of connection following exception is thrown
                    //System.Net.Sockets.SocketException (0x80004005): An existing connection was forcibly closed by the remote host 
                }
                finally
                {
                    m_con = null;
                }
            }
        }

        #endregion

        #region IStreamingBackend Implementation

        public void Put(string remotename, System.IO.Stream stream)
        {
            CreateConnection();
            ChangeDirectory(m_path);
            m_con.UploadFile(stream, remotename);
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            CreateConnection();
            ChangeDirectory(m_path);
            m_con.DownloadFile(remotename, stream);
        }

        #endregion

        #region IRenameEnabledBackend Implementation

        public void Rename(string source, string target)
        {
            CreateConnection();
            ChangeDirectory(m_path);
            m_con.RenameFile(source, target);
        }

        #endregion

        #region Implementation

        private void CreateConnection()
        {
            if (m_con != null && m_con.IsConnected)
                return;

            if (m_con != null && !m_con.IsConnected)
            {
                m_con.Connect();
                return;
            }

            SftpClient con;

            string keyfile;
            m_options.TryGetValue(SSH_KEYFILE_OPTION, out keyfile);
            if (string.IsNullOrWhiteSpace(keyfile))
                m_options.TryGetValue(SSH_KEYFILE_INLINE, out keyfile);

            if (!string.IsNullOrWhiteSpace(keyfile))
                con = new SftpClient(m_server, m_port, m_username, ValidateKeyFile(keyfile, m_password));
            else
                con = new SftpClient(m_server, m_port, m_username, m_password ?? string.Empty);

            con.HostKeyReceived += delegate (object sender, HostKeyEventArgs e)
            {
                e.CanTrust = false;

                if (m_fingerprintallowall)
                {
                    e.CanTrust = true;
                    return;
                }

                string hostFingerprint = e.HostKeyName + " " + e.KeyLength.ToString() + " " + BitConverter.ToString(e.FingerPrint).Replace('-', ':');

                if (string.IsNullOrEmpty(m_fingerprint))
                    throw new Library.Utility.HostKeyException(Strings.SSHv2Backend.FingerprintNotSpecifiedManagedError(hostFingerprint.ToLower(), SSH_FINGERPRINT_OPTION, SSH_FINGERPRINT_ACCEPT_ANY_OPTION), hostFingerprint, m_fingerprint);

                if (!String.Equals(hostFingerprint, m_fingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new Library.Utility.HostKeyException(Strings.SSHv2Backend.FingerprintNotMatchManagedError(hostFingerprint.ToLower()), hostFingerprint, m_fingerprint);
                else
                    e.CanTrust = true;
            };

            if (m_operationtimeout.Ticks != 0)
                con.OperationTimeout = m_operationtimeout;
            if (m_keepaliveinterval.Ticks != 0)
                con.KeepAliveInterval = m_keepaliveinterval;

            con.Connect();

            m_con = con;
        }

        private void ChangeDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string working_dir = Duplicati.Library.Utility.Utility.AppendDirSeparator(m_con.WorkingDirectory, "/");
            if (working_dir == path)
                return;

            try
            {
                m_con.ChangeDirectory(path);
            }
            catch (Exception ex)
            {
                throw new Interface.FolderMissingException(Strings.SSHv2Backend.FolderNotFoundManagedError(path, ex.Message), ex);
            }
        }

        public IEnumerable<IFileEntry> List()
        {
            string path = ".";

            CreateConnection();
            ChangeDirectory(m_path);

            foreach (Renci.SshNet.Sftp.SftpFile ls in m_con.ListDirectory(path))
                if (ls.Name.ToString() != "." && ls.Name.ToString() != "..")
                    yield return new FileEntry(ls.Name, ls.Length, ls.LastAccessTime, ls.LastWriteTime) { IsFolder = ls.Attributes.IsDirectory };
        }

        public static Renci.SshNet.PrivateKeyFile ValidateKeyFile(string filename, string password)
        {
            try
            {
                if (filename.StartsWith(KEYFILE_URI, StringComparison.OrdinalIgnoreCase))
                {
                    using (var ms = new System.IO.MemoryStream())
                    using (var sr = new System.IO.StreamWriter(ms))
                    {
                        sr.Write(Duplicati.Library.Utility.Uri.UrlDecode(filename.Substring(KEYFILE_URI.Length)));
                        sr.Flush();

                        ms.Position = 0;

                        if (String.IsNullOrEmpty(password))
                            return new Renci.SshNet.PrivateKeyFile(ms);
                        else
                            return new Renci.SshNet.PrivateKeyFile(ms, password);
                    }
                }
                else
                {
                    if (String.IsNullOrEmpty(password))
                        return new Renci.SshNet.PrivateKeyFile(filename);
                    else
                        return new Renci.SshNet.PrivateKeyFile(filename, password);
                }
            }
            catch (Exception ex)
            {
                throw new UserInformationException(string.Format("Failed to parse the keyfile, check the key format and passphrase. Error message was {0}", ex.Message), "SSHFailedToParseKeyFile", ex);
            }
        }

        #endregion

        internal SftpClient Client
        {
            get
            {
                return m_con;
            }
        }

        public string[] DNSName
        {
            get { return new string[] { m_server }; }
        }
    }
}
