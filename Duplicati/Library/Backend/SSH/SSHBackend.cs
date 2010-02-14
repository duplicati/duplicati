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
using System.Text.RegularExpressions;


namespace Duplicati.Library.Backend
{
    public class SSH : IStreamingBackend, IBackendGUI
    {
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

        private int m_port = 22;
        
        /// <summary>
        /// A value indicating if the *CLIENT* is a linux client.
        /// Fortunately the sftp program seems to be client dependent, and not server dependant like the ftp program.
        /// </summary>
        private bool m_isLinux = false;

        public SSH()
        {
            m_isLinux = Library.Core.Utility.IsClientLinux;
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

            if (options.ContainsKey("use-sftp-application"))
                m_useManaged = false;
            
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

            if (options.ContainsKey("sftp-command"))
                m_sftp = options["sftp-command"];
            else
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
                m_transfer_timeout = Math.Min(1000 * 60 * 60, Math.Max(1000 * 60, (int)Duplicati.Library.Core.Timeparser.ParseTimeSpan(m_options["transfer-timeout"]).TotalMilliseconds));
            else
                m_transfer_timeout = 1000 * 60 * 15;

            m_write_log_info = options.ContainsKey("debug-to-console");
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

        public List<FileEntry> List()
        {
            if (m_useManaged)
                return ListManaged();
            else
                return ListUnmanaged();
        }

        public void Put(string remotename, string filename)
        {
            if (m_useManaged)
                PutManaged(remotename, filename);
            else
                PutUnmanaged(remotename, filename);
        }

        public void Get(string remotename, string filename)
        {
            if (m_useManaged)
                GetManaged(remotename, filename);
            else
                GetUnmanaged(remotename, filename);
        }

        public void Delete(string remotename)
        {
            if (m_useManaged)
                DeleteManaged(remotename);
            else
                DeleteUnmanaged(remotename);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("sftp-command", CommandLineArgument.ArgumentType.Path, Strings.SSHBackend.DescriptionSFTPCommandShort, Strings.SSHBackend.DescriptionSFTPCommandLong, Library.Core.Utility.IsClientLinux ? "sftp" : "psftp.exe"),
                    new CommandLineArgument("ssh-options", CommandLineArgument.ArgumentType.String, Strings.SSHBackend.DescriptionSSHOptionsShort, Strings.SSHBackend.DescriptionSSHOptionsLong, "-C"),
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, Strings.SSHBackend.DescriptionFTPPasswordShort, Strings.SSHBackend.DescriptionFTPPasswordLong),
                    new CommandLineArgument("ftp-username", CommandLineArgument.ArgumentType.String, Strings.SSHBackend.DescriptionFTPUsernameShort, Strings.SSHBackend.DescriptionFTPUsernameLong),
                    new CommandLineArgument("debug-to-console", CommandLineArgument.ArgumentType.Boolean, Strings.SSHBackend.DescriptionDebugToConsoleShort, Strings.SSHBackend.DescriptionDebugToConsoleLong),
                    new CommandLineArgument("transfer-timeout", CommandLineArgument.ArgumentType.Timespan, Strings.SSHBackend.DescriptionTransferTimeoutShort, Strings.SSHBackend.DescriptionTransferTimeoutLong, "15m"),
                    new CommandLineArgument("use-sftp-application", CommandLineArgument.ArgumentType.Boolean, Strings.SSHBackend.DescriptionUnmanagedShort, Strings.SSHBackend.DescriptionUnmanagedLong, "false"),
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

        public void CreateFolder()
        {
            if (m_useManaged)
                CreateFolderManaged();
            else
                CreateFolderUnmanaged();
        }

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region Unmanaged Implementation
        
        private List<FileEntry> ListUnmanaged()
        {

            if (m_write_log_info)
                Console.WriteLine("******** " + Strings.SSHBackend.DebugListHeader + " *******");
            List<FileEntry> files = new List<FileEntry>();

            using (SharpExpect.SharpExpectProcess p = GetUnmanagedConnection(true))
            {
                if (m_isLinux)
                    p.Sendline("ls -la");
                else
                    p.Sendline("ls");
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

                if (!string.IsNullOrEmpty(m_path) && changeDir)
                {
                    proc.Sendline("cd \"" + m_path + "\"");
                    if (proc.Expect(".*not found.*", ".*No such file or directory.*", "sftp>", "Remote directory is now") < 2)
                        throw new Backend.FolderMissingException(string.Format(Strings.SSHBackend.FolderNotFoundError, m_path, proc.LogKillAndDispose()));

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

        #endregion

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

        #region Managed Implementation

        private SFTPCon CreateManagedConnection(bool changeDir)
        {
            SFTPCon con = new SFTPCon(m_server, m_username, m_password);
            con.Connect(m_port);

            try
            {
                if (!string.IsNullOrEmpty(m_path) && changeDir)
                    con.SetCurrenDir(m_path);
            }
            catch (Exception ex)
            {
                throw new Backend.FolderMissingException(string.Format(Strings.SSHBackend.FolderNotFoundManagedError, m_path, ex.Message), ex);
            }

            return con;
        }

        private List<FileEntry> ListManaged()
        {
            using (SFTPCon con = CreateManagedConnection(true))
            {
                List<FileEntry> files = new List<FileEntry>();

                DateTime epochOffset = new DateTime(1970, 1, 1);

                foreach (Tamir.SharpSsh.jsch.ChannelSftp.LsEntry ls in con.ListFiles("."))
                    if (ls.getFilename().ToString() != "." && ls.getFilename().ToString() != "..")
                        files.Add(new FileEntry(ls.getFilename().ToString(), ls.getAttrs().getSize(), epochOffset.Add(new TimeSpan(ls.getAttrs().getATime() * TimeSpan.TicksPerSecond)), epochOffset.Add(new TimeSpan(ls.getAttrs().getMTime() * TimeSpan.TicksPerSecond))));

                return files;
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
            using (SFTPCon con = CreateManagedConnection(true))
                con.Delete(remotename);
        }

        private void CreateFolderManaged()
        {
            using (SFTPCon con = CreateManagedConnection(false))
                con.Mkdir(m_path);

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

            using (SFTPCon con = CreateManagedConnection(true))
                con.Put(remotename, stream);
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            if (!m_useManaged)
                throw new Exception(Strings.SSHBackend.StreamingNotSupportedError);

            using (SFTPCon con = CreateManagedConnection(true))
                con.Get(remotename, stream);
        }

        #endregion
    }
}
