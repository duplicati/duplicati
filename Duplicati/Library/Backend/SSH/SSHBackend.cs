#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
    public class SSH : IBackend
    {
        private string m_server;
        private string m_path;
        private string m_username;
        private string m_password;
        Dictionary<string, string> m_options;

        private string m_sftp;
        private string m_ssh_options;

        private const int SSH_TIMEOUT = 30 * 1000;

        private bool m_write_log_info = false;
        
        /// <summary>
        /// A value indicating if the *CLIENT* is a linux client.
        /// Fortunately the sftp program seems to be client dependent, and not server dependant like the ftp program.
        /// </summary>
        private bool m_isLinux = false;

        public SSH()
        {
            m_isLinux = (System.Environment.OSVersion.Platform == PlatformID.MacOSX || System.Environment.OSVersion.Platform == PlatformID.Unix);
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

            m_path = u.AbsolutePath.Substring(1);
            if (!m_path.EndsWith("/"))
                m_path += "/";

            m_server = u.Host;

            if (options.ContainsKey("sftp-command"))
                m_sftp = options["sftp-command"];
            else
            {
                if (System.Environment.OSVersion.Platform == PlatformID.Unix || System.Environment.OSVersion.Platform == PlatformID.MacOSX)
                    m_sftp = "sftp";
                else
                    m_sftp = "psftp.exe";
            }

            if (options.ContainsKey("ssh-options"))
                m_ssh_options = options["ssh-options"];
            else
                m_ssh_options = "-C";

            if (!u.IsDefaultPort)
                m_ssh_options += " -P " + u.Port;

            m_write_log_info = options.ContainsKey("debug-to-console");
        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return "SSH based"; }
        }

        public string ProtocolKey
        {
            get { return "ssh"; }
        }

        public List<FileEntry> List()
        {
            if (m_write_log_info)
                Console.WriteLine("******** List *******");
            List<FileEntry> files = new List<FileEntry>();

            using (SharpExpect.SharpExpectProcess p = GetConnection())
            {
                if (m_isLinux)
                    p.Sendline("ls -la");
                else
                    p.Sendline("ls");
                p.Sendline("exit");

                string s;

                while ((s = p.GetNextOutputLine(1000)) != null)
                {
                    FileEntry fe = FTP.ParseLine(s.Trim());
                    if (fe != null && fe.Name != "." && fe.Name != "..")
                        files.Add(fe);
                    else if (m_write_log_info)
                        Console.WriteLine("Failed to parse line: " + s);
                }

                if (!p.Process.WaitForExit(5000))
                    throw new Exception("Timeout while closing session");

                if (m_write_log_info)
                {
                    Console.WriteLine("******** List Complete *******");
                    Console.WriteLine(p.LogKillAndDispose());
                }

                return files;
            }

        }


        public void Put(string remotename, string filename)
        {
            if (m_write_log_info)
                Console.WriteLine("******** Put ********");
            using (SharpExpect.SharpExpectProcess p = GetConnection())
            {
                string cmd = "put \"" + filename + "\" \"" + remotename + "\"";

                p.Sendline(cmd);

                if (p.Expect(SSH_TIMEOUT, "local\\:.+", "Uploading .*") < 0)
                    throw new Exception("Failed to get expected response to command: " + cmd);

                p.Sendline("exit");

                if (!p.Process.WaitForExit(5 * 60 * 1000))
                    throw new Exception("Timeout while uploading file");

                if ((m_isLinux ? p.Expect(1000, "exit", "sftp> exit") : p.Expect(1000, "Using username .*")) < 0)
                    throw new Exception("Got unexpected exit response");

                if (m_write_log_info)
                {
                    Console.WriteLine("******** Put Completed ********");
                    Console.WriteLine(p.LogKillAndDispose());
                }
            }
        }

        public void Get(string remotename, string filename)
        {
            if (m_write_log_info)
                Console.WriteLine("******** Get ********");

            using (SharpExpect.SharpExpectProcess p = GetConnection())
            {
                string cmd = "get \"" + remotename + "\" \"" + filename + "\"";
                p.Sendline(cmd);

                if (p.Expect(SSH_TIMEOUT, "remote\\:.+", "Downloading .*", "Fetching .*") < 0)
                    throw new Exception("Failed to get expected response to command: " + cmd);

                p.Sendline("exit");

                if (!p.Process.WaitForExit(5 * 60 * 1000))
                    throw new Exception("Timeout while uploading file");

                if ((m_isLinux ? p.Expect(1000, "exit", "sftp> exit") : p.Expect(1000, "Using username .*")) < 0)
                    throw new Exception("Got unexpected exit response");

                if (m_write_log_info)
                {
                    Console.WriteLine("******** Get Completed ********");
                    Console.WriteLine(p.LogKillAndDispose());
                }
            }
        }

        public void Delete(string remotename)
        {
            if (m_write_log_info)
                Console.WriteLine("******** Delete ********");

            using (SharpExpect.SharpExpectProcess p = GetConnection())
            {
                p.Sendline("rm \"" + remotename + "\"");
                p.Sendline("exit");

                if (p.Expect(1000, ".*No such file or directory.*", ".*Couldn't.*") != -1)
                    throw new Exception("Failed to delete file: " + p.LogKillAndDispose());

                if (!p.Process.WaitForExit(5000))
                    throw new Exception("Timeout while closing session");

                if (m_write_log_info)
                {
                    Console.WriteLine("******** Delete completed ********");
                    Console.WriteLine(p.LogKillAndDispose());
                }
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("sftp-command", CommandLineArgument.ArgumentType.Path, "The path to the \"sftp\" program", "The full path to the \"sftp\" application.", (System.Environment.OSVersion.Platform == PlatformID.Unix || System.Environment.OSVersion.Platform == PlatformID.MacOSX) ? "sftp" : "psftp.exe"),
                    new CommandLineArgument("ssh-options", CommandLineArgument.ArgumentType.String, "Extra options to the ssh commands", "Supply any extra commandline arguments, which are passed unaltered to the ssh application", "-C"),
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, "Supplies the password used to connect to the server", "The password used to connect to the server. This may also be supplied as the environment variable \"FTP_PASSWORD\"."),
                    new CommandLineArgument("debug-to-console", CommandLineArgument.ArgumentType.Boolean, "Prints debug info to the console", "The SSH backend relies on an external program (sftp) to work. Since the external program may change at any time, this may break the backend. Enable this option to get debug information about the ssh connection written to the console."),
                });

            }
        }

        public string Description
        {
            get
            {
                return "This backend can read and write data to an SSH based backend, using SCP and SFTP. Allowed formats are \"ssh://hostname/folder\" or \"ssh://username:password@hostname/folder\". NOTE: This backend does not support throttling uploads or downloads, and requires that sftp and scp are installed (using putty for windows).";
            }
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

        private string BuildEscapedPath(string remotename)
        {
            string path = m_path + remotename;

            path = m_server + ":" + path;
            if (!string.IsNullOrEmpty(m_username))
                path = m_username + "@" + path;

            return path;
        }

        private SharpExpect.SharpExpectProcess GetConnection()
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

                    p.StartInfo.Arguments = @"-c ""set timeout 30"" -c ""spawn \""" + m_sftp + @"\"" " + server + " " + m_ssh_options + @""" -c ""interact {~~}""";
                }
                else
                {
                    p.StartInfo.FileName = m_sftp;
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
                        throw new Exception("Failed to start the SSH application (" + p.StartInfo.FileName + ").\r\nMake sure that \"expect\" is installed\r\nError message: " + ex.Message, ex);
                    else
                        throw new Exception("Failed to start the SSH application (" + p.StartInfo.FileName + ").\r\nMake sure that \"putty\" is installed, and you have set the correct path.\r\nError message: " + ex.Message, ex);

                }

                bool ready = false;

                while (!ready)
                {
                    switch (proc.Expect(".*timeout.*", ".*denied.*", ".*authenticity.*", ".*Store key in cache\\?.*", ".*login as: .*", ".*(password|passphrase)\\:.*", "sftp>"))
                    {
                        case -1:
                        case 0: //Timeout
                            throw new Exception("Timeout occured while connection, log: " + proc.LogKillAndDispose());
                        case 1: //Access denied
                            throw new Exception("Login failed due to bad credentials, log: " + proc.LogKillAndDispose());
                        case 2: //Host authentication missing
                        case 3:
                            throw new Exception("The host is not authenticated, please connect to the host using SSH, and then re-rerun Duplicati, log: " + proc.LogKillAndDispose());
                        case 4: //Send username (does not happen on linux)
                            if (string.IsNullOrEmpty(m_username))
                                throw new Exception("A username was expected, but none was supplied");
                            proc.Sendline(m_username);
                            continue; //Read next line
                        case 5: //Send password
                            //TODO: Allow the user to enter it with the option --ssh-askpass?
                            if (string.IsNullOrEmpty(m_password))
                                throw new Exception("A password was expected, but passwordless login was specified");
                            proc.Sendpassword(m_password);
                            continue; //Wait for sftp
                        case 6: //We are ready!
                            ready = true;
                            break;
                        default:
                            throw new Exception("Unexpected error: " + proc.LogKillAndDispose());
                    }
                }

                if (!string.IsNullOrEmpty(m_path))
                {
                    proc.Sendline("cd \"" + m_path + "\"");
                    if (proc.Expect(".*not found.*", ".*No such file or directory.*", "sftp>", "Remote directory is now") < 2)
                        throw new Exception("Folder not found: " + m_path + ", log: " + proc.LogKillAndDispose());

                    string matchpath = m_path;
                    if (matchpath.EndsWith("/"))
                        matchpath = matchpath.Substring(0, matchpath.Length - 1);

                    if (!matchpath.StartsWith("/"))
                        matchpath = "/" + matchpath;

                    proc.Sendline("pwd");
                    if (proc.Expect(".*" + System.Text.RegularExpressions.Regex.Escape(matchpath) + ".*") < 0)
                        throw new Exception("Failed to validate the remote directory: " + proc.LogKillAndDispose());

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

    }
}
