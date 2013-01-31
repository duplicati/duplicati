#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
using System.Text.RegularExpressions;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class SSH : IBackend_v2, IStreamingBackend, IBackendGUI
    {
        public const string SFTP_PATH_OPTION = "sftp-command";
        public const string USE_UNMANAGED_OPTION = "use-sftp-application";
        public const string SSH_KEYFILE_OPTION = "ssh-keyfile";
        public const string SSH_NO_CD_OPTION = "ssh-no-cd-command";

        private string m_server;
        private string m_path;
        private string m_username;
        private string m_password;
        Dictionary<string, string> m_options;
        private int m_transfer_timeout;

        private string m_sftp;
        private string m_ssh_options;

        private const int SSH_TIMEOUT = 30 * 1000;

        private bool m_write_log_info = false;

        private bool m_useManaged = true;
        private bool m_noCdCommand = false;

        private int m_port = 22;

        /// <summary>
        /// The managed connection
        /// </summary>
        private SFTPCon m_con;
        
        /// <summary>
        /// A value indicating if the *CLIENT* is a linux client.
        /// Fortunately the sftp program seems to be client dependent, and not server dependant like the ftp program.
        /// </summary>
        private bool m_isLinux = false;

        public SSH()
        {
            m_isLinux = Library.Utility.Utility.IsClientLinux;
        }

        public SSH(string url, Dictionary<string, string> options)
            : this()
        {
            m_options = options;
            Uri u = new Uri(url);
            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                if (u.UserInfo.IndexOf(":") >= 0)
                {
                    m_username = u.UserInfo.Substring(0, u.UserInfo.IndexOf(":"));
                    m_password = u.UserInfo.Substring(u.UserInfo.IndexOf(":") + 1);
                }
                else
                {
                    m_username = u.UserInfo;
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

            m_path = u.AbsolutePath;

            m_useManaged = !Utility.Utility.ParseBoolOption(options, USE_UNMANAGED_OPTION);

            m_noCdCommand = Utility.Utility.ParseBoolOption(options, SSH_NO_CD_OPTION);
            
            if (m_isLinux)
            {
                //HACK: "AbsolutePath" strips extra slashes under mono, so we re-add them here
                int ix = url.IndexOf(m_path);
                if (ix > 0 && url[ix - 1] == '/')
                        m_path = "/" + m_path;
            }

            //Remove 1 leading slash so server/path is mapped to "path",
            // and server//path is mapped to "/path"
            m_path = m_path.Substring(1);

            if (!m_path.EndsWith("/"))
                m_path += "/";

            m_server = u.Host;

            if (options.ContainsKey(SFTP_PATH_OPTION))
                m_sftp = options[SFTP_PATH_OPTION];
            
            if (string.IsNullOrEmpty(m_sftp))
            {
                if (m_isLinux)
                    m_sftp = "sftp";
                else
                    m_sftp = "psftp.exe";
            }

            if (options.ContainsKey("ssh-options"))
                m_ssh_options = options["ssh-options"];
            else
                m_ssh_options = "-C";

            if (!u.IsDefaultPort)
            {
                m_ssh_options += " -P " + u.Port;
                m_port = u.Port;
            }

            if (m_options.ContainsKey("transfer-timeout"))
                m_transfer_timeout = Math.Min(1000 * 60 * 60, Math.Max(1000 * 60, (int)Duplicati.Library.Utility.Timeparser.ParseTimeSpan(m_options["transfer-timeout"]).TotalMilliseconds));
            else
                m_transfer_timeout = 1000 * 60 * 15;

            m_write_log_info = Utility.Utility.ParseBoolOption(options, "debug-to-console");
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return Strings.SSHBackend.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "ssh"; }
        }

        public List<IFileEntry> List()
        {
            if (m_useManaged)
                return ListManaged();
            else
                return ListUnmanaged();
        }

        public void Put(string remotename, string filename)
        {
            if (m_useManaged)
                PutManaged(getFullPath(remotename), filename);
            else
                PutUnmanaged(getFullPath(remotename), filename);
        }

        public void Get(string remotename, string filename)
        {
            if (m_useManaged)
                GetManaged(getFullPath(remotename), filename);
            else
                GetUnmanaged(getFullPath(remotename), filename);
        }

        public void Delete(string remotename)
        {
            if (m_useManaged)
                DeleteManaged(getFullPath(remotename));
            else
                DeleteUnmanaged(getFullPath(remotename));
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(SFTP_PATH_OPTION, CommandLineArgument.ArgumentType.Path, Strings.SSHBackend.DescriptionSFTPCommandShort, Strings.SSHBackend.DescriptionSFTPCommandLong, Library.Utility.Utility.IsClientLinux ? "sftp" : "psftp.exe"),
                    new CommandLineArgument("ssh-options", CommandLineArgument.ArgumentType.String, Strings.SSHBackend.DescriptionSSHOptionsShort, Strings.SSHBackend.DescriptionSSHOptionsLong, "-C"),
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.Password, Strings.SSHBackend.DescriptionFTPPasswordShort, Strings.SSHBackend.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.SSHBackend.DescriptionFTPUsernameShort, Strings.SSHBackend.DescriptionFTPUsernameLong),
                    new CommandLineArgument("debug-to-console", CommandLineArgument.ArgumentType.Boolean, Strings.SSHBackend.DescriptionDebugToConsoleShort, Strings.SSHBackend.DescriptionDebugToConsoleLong),
                    new CommandLineArgument("transfer-timeout", CommandLineArgument.ArgumentType.Timespan, Strings.SSHBackend.DescriptionTransferTimeoutShort, Strings.SSHBackend.DescriptionTransferTimeoutLong, "15m"),
                    new CommandLineArgument(USE_UNMANAGED_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.SSHBackend.DescriptionUnmanagedShort, Strings.SSHBackend.DescriptionUnmanagedLong, "false"),
                    new CommandLineArgument(SSH_KEYFILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.SSHBackend.DescriptionSshkeyfileShort, Strings.SSHBackend.DescriptionSshkeyfileLong),
                    new CommandLineArgument(SSH_NO_CD_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.SSHBackend.DescriptionSshnocdShort, Strings.SSHBackend.DescriptionSshnocdLong),
                });

            }
        }

        public string Description
        {
            get
            {
                return Strings.SSHBackend.Description;
            }
        }

        #endregion

        #region IBackend_v2 Implementation

        public void Test()
        {
            List();
        }

        public void CreateFolder()
        {
            if (m_useManaged)
                CreateFolderManaged();
            else
                CreateFolderUnmanaged();
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

        #region Unmanaged Implementation
        
        private List<IFileEntry> ListUnmanaged()
        {

            if (m_write_log_info)
                Console.WriteLine("******** " + Strings.SSHBackend.DebugListHeader + " *******");
            List<IFileEntry> files = new List<IFileEntry>();

            using (SharpExpect.SharpExpectProcess p = GetUnmanagedConnection(true))
            {
                string path = m_noCdCommand ? m_path : ".";

                if (m_isLinux)
                    p.Sendline("ls -la " + path);
                else
                    p.Sendline("ls " + path);
                p.Sendline("exit");

                string s;
                bool first = true;

                int timeout = 30000;
                if (m_options.ContainsKey("transfer-timeout"))
                    timeout = m_transfer_timeout;
                else //No overrides, pick the longest default time
                    timeout = Math.Max(m_transfer_timeout, timeout);


                while ((s = p.GetNextOutputLine(first ? timeout : 5000)) != null)
                {
                    first = false;
                    FileEntry fe = FTP.ParseLine(s.Trim());
                    if (fe != null && fe.Name != "." && fe.Name != "..")
                        files.Add(fe);
                    else if (m_write_log_info)
                        Console.WriteLine(string.Format(Strings.SSHBackend.DebugParseFailed, s));
                }

                if (!p.Process.WaitForExit(5000))
                    throw new Exception(Strings.SSHBackend.CloseTimeoutError + "\r\n" + p.LogKillAndDispose());

                if (m_write_log_info)
                {
                    Console.WriteLine("******** " + Strings.SSHBackend.DebugListFooter + " *******");
                    Console.WriteLine(p.LogKillAndDispose());
                }

                return files;
            }

        }


        private void PutUnmanaged(string remotename, string filename)
        {
            if (m_write_log_info)
                Console.WriteLine("******** " + Strings.SSHBackend.DebugPutHeader + " ********");
            using (SharpExpect.SharpExpectProcess p = GetUnmanagedConnection(true))
            {
                string cmd = "put \"" + filename + "\" \"" + remotename + "\"";

                p.Sendline(cmd);
                p.Sendline("exit");

                //We assume 1kb pr. second
                int timeout = (int)Math.Min(int.MaxValue, (new System.IO.FileInfo(filename).Length / 1024.0) * 1000);

                //Obey user overrides
                if (m_options.ContainsKey("transfer-timeout"))
                    timeout = m_transfer_timeout;
                else //No overrides, pick the longest default time
                    timeout = Math.Max(m_transfer_timeout, timeout);

                //Assume that all went well and wait for the process to exit
                //For some reason the output is sometimes buffered until process exit
                if (!p.Process.WaitForExit(timeout))
                    throw new Exception(Strings.SSHBackend.UploadTimeoutError + "\r\n" + p.LogKillAndDispose());

                //After the process is completed, the output is flushed, and we need to verify that the response was as expected
                if (p.Expect(5000, "local\\:.+", "Uploading .*") < 0)
                    throw new Exception(string.Format(Strings.SSHBackend.UnexpectedResponseError, cmd));

                if ((m_isLinux ? p.Expect(5000, "exit", "sftp> exit") : p.Expect(5000, "Using username .*")) < 0)
                    throw new Exception(Strings.SSHBackend.UnexpectedExitResponseError + "\r\n" + p.LogKillAndDispose());

                if (m_write_log_info)
                {
                    Console.WriteLine("******** " + Strings.SSHBackend.DebugPutFooter + " ********");
                    Console.WriteLine(p.LogKillAndDispose());
                }
            }
        }

        private void GetUnmanaged(string remotename, string filename)
        {
            if (m_write_log_info)
                Console.WriteLine("******** " + Strings.SSHBackend.DebugGetHeader + " ********");

            using (SharpExpect.SharpExpectProcess p = GetUnmanagedConnection(true))
            {
                string cmd = "get \"" + remotename + "\" \"" + filename + "\"";

                p.Sendline(cmd);
                p.Sendline("exit");

                //We assume 1kb pr. second
                int timeout = (int)Math.Min(int.MaxValue, (new System.IO.FileInfo(filename).Length / 1024.0) * 1000);

                //Obey user overrides
                if (m_options.ContainsKey("transfer-timeout"))
                    timeout = m_transfer_timeout;
                else //No overrides, pick the longest default time
                    timeout = Math.Max(m_transfer_timeout, timeout);

                //Assume that all went well and wait for the process to exit
                //For some reason the output is sometimes buffered until process exit
                if (!p.Process.WaitForExit(timeout))
                    throw new Exception(Strings.SSHBackend.DownloadTimeoutError + "\r\n" + p.LogKillAndDispose());

                //After the process is completed, the output is flushed, and we need to verify that the response was as expected
                if (p.Expect(SSH_TIMEOUT, "remote\\:.+", "Downloading .*", "Fetching .*") < 0)
                    throw new Exception(string.Format(Strings.SSHBackend.UnexpectedResponseError, cmd));

                if ((m_isLinux ? p.Expect(5000, "exit", "sftp> exit") : p.Expect(5000, "Using username .*")) < 0)
                    throw new Exception(Strings.SSHBackend.UnexpectedExitResponseError + "\r\n" + p.LogKillAndDispose());

                if (m_write_log_info)
                {
                    Console.WriteLine("******** " + Strings.SSHBackend.DebugGetFooter + " ********");
                    Console.WriteLine(p.LogKillAndDispose());
                }
            }
        }

        private void DeleteUnmanaged(string remotename)
        {
            if (m_write_log_info)
                Console.WriteLine("******** " + Strings.SSHBackend.DebugDeleteHeader + " ********");

            using (SharpExpect.SharpExpectProcess p = GetUnmanagedConnection(true))
            {
                p.Sendline("rm \"" + remotename + "\"");
                p.Sendline("exit");

                if (!p.Process.WaitForExit(SSH_TIMEOUT))
                    throw new Exception(Strings.SSHBackend.CloseTimeoutError + "\r\n" + p.LogKillAndDispose());

                if (p.Expect(5000, ".*No such file or directory.*", ".*Couldn't.*") != -1)
                    throw new Exception(string.Format(Strings.SSHBackend.DeleteError, p.LogKillAndDispose()));

                if (m_write_log_info)
                {
                    Console.WriteLine("******** " + Strings.SSHBackend.DebugDeleteFooter + " ********");
                    Console.WriteLine(p.LogKillAndDispose());
                }
            }
        }

        private SharpExpect.SharpExpectProcess GetUnmanagedConnection(bool changeDir)
        {
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

            string server = m_server;
            if (!string.IsNullOrEmpty(m_username))
                server = m_username + "@" + server;

            SharpExpect.SharpExpectProcess proc = null;

            try
            {
                if (m_isLinux)
                {
                    //Since SSH uses direct tty/pty manipulation, and SharpExpect does not simulate this,
                    //we wrap the command with expect, which allows us to use stdin/stdout
                    p.StartInfo.FileName = "expect";

                    p.StartInfo.Arguments = @"-c ""set timeout 30"" -c ""spawn \""" + System.Environment.ExpandEnvironmentVariables(m_sftp) + @"\"" " + server + " " + m_ssh_options + @""" -c ""interact {~~}""";
                }
                else
                {
                    p.StartInfo.FileName = System.Environment.ExpandEnvironmentVariables(m_sftp);
                    p.StartInfo.Arguments = server + " " + m_ssh_options;
                }

                try
                {
                    //Console.WriteLine("Command: " + p.StartInfo.FileName);
                    //Console.WriteLine("Arguments: " + p.StartInfo.Arguments);
                    proc = SharpExpect.SharpExpectProcess.Spawn(p.StartInfo);
                    proc.LogEnabled = m_write_log_info;
                    proc.DefaultTimeout = SSH_TIMEOUT;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    if (m_isLinux)
                        throw new Exception(string.Format(Strings.SSHBackend.LaunchErrorLinux, p.StartInfo.FileName, ex.Message), ex);
                    else
                        throw new Exception(string.Format(Strings.SSHBackend.LaunchErrorWindows, p.StartInfo.FileName, ex.Message), ex);

                }

                bool ready = false;

                while (!ready)
                {
                    switch (proc.Expect(".*timeout.*", ".*denied.*", ".*authenticity.*", ".*Store key in cache\\?.*", ".*login as: .*", ".*(password|passphrase)\\:.*", "sftp>"))
                    {
                        case -1:
                        case 0: //Timeout
                            throw new Exception(string.Format(Strings.SSHBackend.ConnectionTimeoutError, proc.LogKillAndDispose()));
                        case 1: //Access denied
                            throw new Exception(string.Format(Strings.SSHBackend.AuthenticationError, proc.LogKillAndDispose()));
                        case 2: //Host authentication missing
                        case 3:
                            throw new Exception(string.Format(Strings.SSHBackend.HostNotAuthenticatedError, proc.LogKillAndDispose()));
                        case 4: //Send username (does not happen on linux)
                            if (string.IsNullOrEmpty(m_username))
                                throw new Exception(Strings.SSHBackend.UsernameMissingError);
                            proc.Sendline(m_username);
                            continue; //Read next line
                        case 5: //Send password
                            //TODO: Allow the user to enter it with the option --ssh-askpass?
                            if (string.IsNullOrEmpty(m_password))
                                throw new Exception(Strings.SSHBackend.PasswordMissingError);
                            proc.Sendpassword(m_password);
                            continue; //Wait for sftp
                        case 6: //We are ready!
                            ready = true;
                            break;
                        default:
                            throw new Exception(string.Format(Strings.SSHBackend.UnexpectedConnectionError, proc.LogKillAndDispose()));
                    }
                }

                if (!string.IsNullOrEmpty(m_path) && changeDir && !m_noCdCommand)
                {
                    proc.Sendline("cd \"" + m_path + "\"");
                    if (proc.Expect(".*not found.*", ".*No such file or directory.*", "sftp>", "Remote directory is now") < 2)
                        throw new Interface.FolderMissingException(string.Format(Strings.SSHBackend.FolderNotFoundError, m_path, proc.LogKillAndDispose()));

                    string matchpath = m_path;
                    if (matchpath.EndsWith("/"))
                        matchpath = matchpath.Substring(0, matchpath.Length - 1);

                    if (!matchpath.StartsWith("/"))
                        matchpath = "/" + matchpath;

                    proc.Sendline("pwd");
                    if (proc.Expect(".*" + System.Text.RegularExpressions.Regex.Escape(matchpath) + ".*") < 0)
                        throw new Exception(string.Format(Strings.SSHBackend.FolderVerificationError, proc.LogKillAndDispose()));

                    while (proc.GetNextOutputLine(1000) != null)
                    { } //Clean output

                    //Console.WriteLine("Connection is ready!");
                }

            }
            catch (Exception ex)
            {
                if (m_write_log_info)
                    Console.WriteLine(ex.ToString());

                if (proc != null)
                {
                    if (m_write_log_info)
                        Console.WriteLine(proc.LogKillAndDispose());
                    else
                        proc.Dispose();
                }

                throw;
            }

           return proc;
        }

        private void CreateFolderUnmanaged()
        {
            using (SharpExpect.SharpExpectProcess p = GetUnmanagedConnection(false))
            {
                p.Sendline("mkdir \"" + m_path + "\"");
                p.Sendline("exit");

                if (!p.Process.WaitForExit(SSH_TIMEOUT))
                    throw new Exception(Strings.SSHBackend.CloseTimeoutError + "\r\n" + p.LogKillAndDispose());

                if (p.Expect(5000, ".*cannot create.*", ".*Failed.*") != -1)
                    throw new Exception(string.Format(Strings.SSHBackend.DeleteError, p.LogKillAndDispose()));
            }
        }

        #endregion

        #region Managed Implementation

        private SFTPCon CreateManagedConnection(bool changeDir)
        {
            //If the request is for a connection initialized to the right dir, we can use the cached version
            if (changeDir && m_con != null)
                return m_con;

            try
            {
                SFTPCon con;
                string keyfile;
                m_options.TryGetValue(SSH_KEYFILE_OPTION, out keyfile);

                if ((keyfile ?? "").Trim().Length > 0)
                {
                    ValidateKeyFile(m_options[SSH_KEYFILE_OPTION]);

                    con = new SFTPCon(m_server, m_username);
                    con.AddIdentityFile(m_options[SSH_KEYFILE_OPTION], m_password);
                }
                else
                    con = new SFTPCon(m_server, m_username, m_password);

                con.Connect(m_port);

                try
                {
                    if (!m_noCdCommand && !string.IsNullOrEmpty(m_path) && changeDir)
                        con.SetCurrenDir(m_path);
                }
                catch (Exception ex)
                {
                    throw new Interface.FolderMissingException(string.Format(Strings.SSHBackend.FolderNotFoundManagedError, m_path, ex.Message), ex);
                }

                //If the connection is initialized to the right folder, we cache it
                if (changeDir)
                    m_con = con;

                return con;
            }
            catch (Tamir.SharpSsh.jsch.SftpException sx) 
            { 
                throw ReshapeSharpSSHException(sx); 
            }
        }

        private string getFullPath(string path)
        {
            if (m_noCdCommand)
                return m_path + path;
            else
                return path;
        }

        private List<IFileEntry> ListManaged()
        {
            List<IFileEntry> files = new List<IFileEntry>();

            DateTime epochOffset = new DateTime(1970, 1, 1);

            string path = m_noCdCommand ? m_path : ".";


            try
            {
                foreach (Tamir.SharpSsh.jsch.ChannelSftp.LsEntry ls in CreateManagedConnection(true).ListFiles(path))
                    if (ls.getFilename().ToString() != "." && ls.getFilename().ToString() != "..")
                        files.Add(new FileEntry(ls.getFilename().ToString(), ls.getAttrs().getSize(), epochOffset.Add(new TimeSpan(ls.getAttrs().getATime() * TimeSpan.TicksPerSecond)), epochOffset.Add(new TimeSpan(ls.getAttrs().getMTime() * TimeSpan.TicksPerSecond))));
                return files;
            }
            catch (Tamir.SharpSsh.jsch.SftpException sx) 
            { 
                throw ReshapeSharpSSHException(sx); 
            }
        }

        private void PutManaged(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                Put(remotename, fs);
        }

        private void GetManaged(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Open(filename, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                Get(remotename, fs);
        }

        private void DeleteManaged(string remotename)
        {
            try { CreateManagedConnection(true).Delete(remotename); }
            catch (Tamir.SharpSsh.jsch.SftpException sx) { throw ReshapeSharpSSHException(sx); }

        }

        private void CreateFolderManaged()
        {
			string p = m_path;

            try
            {
                using (SFTPCon con = CreateManagedConnection(false))
                {
                    try
                    {
                        //Some SSH implementations fail if the folder name has a trailing slash
                        if (p.EndsWith("/"))
                            p = p.Substring(0, p.Length - 1);

                        con.Mkdir(p);
                    }
                    catch
                    {
                        //For backwards compatibility, we also try to create the folder WITH a trailing slash,
                        // this should never work, but in case there is a SSH implementation that relies
                        // on this we try that too
                        if (p != m_path)
                        {
                            try
                            {
                                //If this succeeds, we continue
                                con.Mkdir(m_path);
                                return;
                            }
                            catch { }
                        }

                        //We report the original error
                        throw;
                    }
                }
            }
            catch (Tamir.SharpSsh.jsch.SftpException sx) 
            { 
                throw ReshapeSharpSSHException(sx); 
            }
        }

        #endregion

        #region IBackendGUI Members

        public string PageTitle
        {
            get { return SSHUI.PageTitle; }
        }

        public string PageDescription
        {
            get { return SSHUI.PageDescription; }
        }

        public System.Windows.Forms.Control GetControl(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
        {
            return new SSHUI(applicationSettings, options);
        }

        public void Leave(System.Windows.Forms.Control control)
        {
            ((SSHUI)control).Save(false);
        }

        public bool Validate(System.Windows.Forms.Control control)
        {
            return ((SSHUI)control).Save(true);
        }

        public string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            return SSHUI.GetConfiguration(applicationSettings, guiOptions, commandlineOptions);
        }
        #endregion

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
        {
            if (!m_useManaged)
                throw new Exception(Strings.SSHBackend.StreamingNotSupportedError);

            try { CreateManagedConnection(true).Put(getFullPath(remotename), stream); }
            catch (Tamir.SharpSsh.jsch.SftpException sx) { throw ReshapeSharpSSHException(sx); }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            if (!m_useManaged)
                throw new Exception(Strings.SSHBackend.StreamingNotSupportedError);

            try { CreateManagedConnection(true).Get(getFullPath(remotename), stream); }
            catch (Tamir.SharpSsh.jsch.SftpException sx) { throw ReshapeSharpSSHException(sx); }
        }

        #endregion

        /// <summary>
        /// Helper function that extracts error messages from SharpSSH style exceptions and converts them into regular .Net exception messages
        /// </summary>
        /// <param name="sx">The exception to convert</param>
        /// <returns>The converted exception</returns>
        private static Exception ReshapeSharpSSHException(Tamir.SharpSsh.jsch.SftpException sx)
        {
            string idstr;
            if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_OK)
                idstr = "OK";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_EOF)
                idstr = "EOF";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_NO_SUCH_FILE)
                idstr = "NO_SUCH_FILE";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_PERMISSION_DENIED)
                idstr = "PERMISSION_DENIED";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_FAILURE)
                idstr = "FAILURE";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_BAD_MESSAGE)
                idstr = "BAD_MESSAGE";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_NO_CONNECTION)
                idstr = "NO_CONNECTION";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_CONNECTION_LOST)
                idstr = "CONNECTION_LOST";
            else if (sx.id == Tamir.SharpSsh.jsch.ChannelSftp.SSH_FX_OP_UNSUPPORTED)
                idstr = "UNSUPPORTED";
            else
                idstr = "UNKNOWN";

            return new Exception(string.Format("{0} - {1}: {2}", sx.id, idstr, sx.message), sx);
        }

        /// <summary>
        /// A helper function for validating SSH key files, as the support for those is limited in the SharpSSH library
        /// </summary>
        /// <param name="filename">The key file to validate</param>
        public static void ValidateKeyFile(string filename)
        {
            if (!System.IO.File.Exists(filename))
                throw new System.IO.FileNotFoundException(string.Format(Strings.SSHBackend.KeyfileNotFoundError, filename));

            string[] lines = System.IO.File.ReadAllLines(filename);

            //Step 1, find the "-----BEGIN RSA PRIVATE KEY-----" header
            System.Text.RegularExpressions.Regex header = new Regex("BEGIN (RSA|DSA|SSH) PRIVATE KEY", RegexOptions.IgnoreCase);

            int firstline = -1;
            for (int i = 0; i < Math.Min(100, lines.Length); i++)
                if (header.Match(lines[i]).Success)
                {
                    firstline = i;
                    break;
                }

            if (firstline == -1)
                throw new System.IO.InvalidDataException(Strings.SSHBackend.IncorrectFileHeaderError);

            //Step 2, read all headers, the first line without a ":" marks the start of the header
            firstline += 1;
            bool encrypted = false;
            string encryptionFormat = null;
            int lastLine;

            for (lastLine = firstline; lastLine < lines.Length; lastLine++)
            {
                if (!lines[lastLine].Contains(":"))
                    break;

                if (lines[lastLine].StartsWith("Proc-Type:") && lines[lastLine].Contains("ENCRYPTED"))
                    encrypted = true;

                if (lines[lastLine].StartsWith("DEK-Info:"))
                    encryptionFormat = lines[lastLine].Substring("DEK-Info:".Length).Split(',')[0].Trim();
            }

            //Step 3, validate the contents
            if ((encrypted && encryptionFormat == null) || (!encrypted && encryptionFormat != null))
                throw new System.IO.InvalidDataException(string.Format(Strings.SSHBackend.KeyfileParseError, string.Join(Environment.NewLine, lines, 0, lastLine)));

            if (encryptionFormat != null && !encryptionFormat.Equals("DES-EDE3-CBC", StringComparison.InvariantCultureIgnoreCase))
                throw new System.IO.InvalidDataException(string.Format(Strings.SSHBackend.UnsupportedKeyfileEncryptionError, string.Join(Environment.NewLine, lines, 0, lastLine)));
        }
    }
}
