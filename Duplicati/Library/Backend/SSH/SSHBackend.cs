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
        private string m_scp;
        private string m_ssh_options;

        public SSH()
        {
        }

        public SSH(string url, Dictionary<string, string> options)
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

            if (options.ContainsKey("scp-command"))
                m_scp = options["scp-command"];
            else
            {
                if (System.Environment.OSVersion.Platform == PlatformID.Unix || System.Environment.OSVersion.Platform == PlatformID.MacOSX)
                    m_scp = "scp";
                else
                    m_scp = "pscp.exe";
            }

            if (options.ContainsKey("ssh-options"))
                m_ssh_options = options["ssh-options"];
            else
                m_ssh_options = "-C";

            if (!u.IsDefaultPort)
                m_ssh_options += " -P " + u.Port;
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
            List<FileEntry> files = new List<FileEntry>();

            using (System.Diagnostics.Process p = GetConnection(true, null))
            {
                p.StandardInput.WriteLine("ls");
                p.StandardInput.WriteLine("exit");

                string s;
                while ((s = p.StandardOutput.ReadLine()) != null)
                {
                    FileEntry fe = FTP.ParseLine(s);
                    if (fe != null && fe.Name != "." && fe.Name != "..")
                        files.Add(fe);
                }

                if (!p.WaitForExit(5000))
                {
                    p.Close();
                    throw new Exception("Timeout while closing session");
                }
                
                return files;
            }

        }


        public void Put(string remotename, string filename)
        {
            using (System.Diagnostics.Process p = GetConnection(false, "\"" + filename + "\" \"" + BuildEscapedPath(remotename) + "\""))
            {
                if (!p.WaitForExit(5 * 60 * 1000))
                {
                    p.Close();
                    throw new Exception("Timeout while uploading file");
                }

                if (p.StandardError.Peek() != -1)
                    throw new Exception(p.StandardError.ReadToEnd());
            }
        }

        public void Get(string remotename, string filename)
        {
            using (System.Diagnostics.Process p = GetConnection(false, "\"" + BuildEscapedPath(remotename) + "\" \"" + filename + "\""))
            {
                if (!p.WaitForExit(5 * 60 * 1000))
                {
                    p.Close();
                    throw new Exception("Timeout while uploading file");
                }

                if (p.StandardError.Peek() != -1)
                    throw new Exception(p.StandardError.ReadToEnd());
            }
        }

        public void Delete(string remotename)
        {
            using (System.Diagnostics.Process p = GetConnection(true, null))
            {
                p.StandardInput.WriteLine("rm \"" + remotename + "\"");
                p.StandardInput.WriteLine("exit");
                if (!p.WaitForExit(5000))
                {
                    p.Close();
                    throw new Exception("Timeout while closing session");
                }
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("sftp-command", CommandLineArgument.ArgumentType.Path, "The path to the \"sftp\" program", "The full path to the \"sftp\" application.", (System.Environment.OSVersion.Platform == PlatformID.Unix || System.Environment.OSVersion.Platform == PlatformID.MacOSX) ? "sftp" : "psftp.exe"),
                    new CommandLineArgument("scp-command", CommandLineArgument.ArgumentType.Path, "The path to the \"scp\" program", "The full path to the \"scp\" application.", (System.Environment.OSVersion.Platform == PlatformID.Unix || System.Environment.OSVersion.Platform == PlatformID.MacOSX) ? "scp" : "pscp.exe"),
                    new CommandLineArgument("ssh-options", CommandLineArgument.ArgumentType.String, "Extra options to the ssh commands", "Supply any extra commandline arguments, which are passed unaltered to the ssh application", "-C"),
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, "Supplies the password used to connect to the server", "The password used to connect to the server. This may also be supplied as the environment variable \"FTP_PASSWORD\"."),
                });

            }
        }

        public string Description
        {
            get
            {
                return "This backend can read and write data to an SSH based backend, using SCP and SFTP.\nAllowed formats are \"ssh://hostname/folder\" or \"ssh://username:password@hostname/folder\"\nNOTE: This backend does not support throttling uploads or downloads.";
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

        private System.Diagnostics.Process GetConnection(bool sftp, string args)
        {
            System.Diagnostics.Process p = new System.Diagnostics.Process();
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardInput = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            p.StartInfo.FileName = sftp ? m_sftp : m_scp;
            p.StartInfo.Arguments = m_ssh_options;

            if (sftp)
                p.StartInfo.Arguments += " " + m_server;

            if (args != null)
                p.StartInfo.Arguments += " " + args;

            try
            {
                p.Start();
            }
            catch(Exception ex)
            {
                throw new Exception("Failed to start the SSH application (" + p.StartInfo.FileName + ").\r\nError message: " + ex.Message, ex);
            }

            //TODO: This is not the most robust way of dealing with
            // the psftp and pscp commands. It is likely not very portable either

            System.Text.StringBuilder prompts = new StringBuilder();

            char[] tmp = new char[1000];
            int t = p.StandardOutput.Read(tmp, 0, tmp.Length);
            string greeting = new string(tmp, 0, t);
            prompts.Append(greeting);

            if (!string.IsNullOrEmpty(m_username) && greeting.Trim() == "login as:")
            {
                p.StandardInput.WriteLine(m_username);
                t = p.StandardOutput.Read(tmp, 0, tmp.Length);
                greeting = new string(tmp, 0, t);
                prompts.Append(greeting);
            }

            if (!string.IsNullOrEmpty(m_password) && greeting.Trim().ToLower().EndsWith(" password:"))
            {
                p.StandardInput.WriteLine(m_password);

                t = p.StandardOutput.Read(tmp, 0, tmp.Length);
                greeting = new string(tmp, 0, t);
                prompts.Append(greeting);
            }

            if (sftp && !greeting.Trim().ToLower().StartsWith("remote working directory"))
            {
                //Sometime the message comes a little delayed later
                do
                {
                    t = p.StandardOutput.Read(tmp, 0, tmp.Length);
                    greeting = new string(tmp, 0, t);
                    prompts.Append(greeting);
                } while (greeting.Length > 0 && greeting.Length < 4);

                if (!greeting.Trim().ToLower().StartsWith("remote working directory"))
                    throw new Exception("Failed to login, prompts were: " + prompts.ToString());
            }

            if (sftp)
                if (!string.IsNullOrEmpty(m_path))
                    p.StandardInput.WriteLine("cd \"" + m_path + "\"");

            return p;
        }
    }
}
