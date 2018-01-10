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
using System.Text;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Duplicati.Library.Backend
{
    public class Rclone : IBackend
    {
        private const string OPTION_LOCAL_REPO = "rclone-local-repository";
        private const string OPTION_REMOTE_REPO = "rclone-remote-repository";
        private const string OPTION_REMOTE_PATH = "rclone-remote-path";
        private const string OPTION_RCLONE = "rclone-option";

        private string local_repo;
        private string remote_repo;
        private string remote_path;
        private string opt_rclone;

        public Rclone()
        {
            
        }

        public Rclone(string url, Dictionary<string, string> options)
        {

            local_repo = "local";
            remote_repo = "remote";
            remote_path = "backup";
            opt_rclone = "";
            /*should check here if program is installed */

            if (options.ContainsKey(OPTION_LOCAL_REPO))
                local_repo = options[OPTION_LOCAL_REPO];

            if (options.ContainsKey(OPTION_REMOTE_REPO))
                remote_repo = options[OPTION_REMOTE_REPO];

            if (options.ContainsKey(OPTION_REMOTE_PATH))
                remote_path = options[OPTION_REMOTE_PATH];

            if (options.ContainsKey(OPTION_RCLONE))
                opt_rclone = options[OPTION_RCLONE];


            Console.Error.WriteLine(string.Format("Constructor {0}: {1}:{2} {3}", local_repo, remote_repo, remote_path, opt_rclone));

        }

        #region IBackendInterface Members

        public string DisplayName
        {
            get { return Strings.Rclone.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "rclone"; }
        }

        public bool SupportsStreaming
        {
            get { return false; }
        }

        public IEnumerable<IFileEntry> List()
        {
            //Console.Error.WriteLine(string.Format("Listing contents: rclone lsjson {0}:{1}",remote_repo, remote_path));

            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = String.Format("lsjson {0}:{1}", remote_repo, remote_path);
            psi.CreateNoWindow = true;
            psi.FileName = "rclone";
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
#endif
            System.Diagnostics.Process p = null;
            Console.Error.WriteLine("before try");
            try
            {
                Console.Error.WriteLine("process listing starting: {0} {1}", psi.FileName, psi.Arguments);
                p = System.Diagnostics.Process.Start(psi);
                // this will give an error if eg. the program does not exist
                Console.Error.WriteLine("process listing started");
            }
            
            catch (System.ComponentModel.Win32Exception ex)
            {
                Console.Error.WriteLine("Inside catch");
                throw new Exception("Program does not exist", ex);
            }

            String result = p.StandardOutput.ReadToEnd();
            JArray files = JArray.Parse(result);
            // this will give an error if the path or config does not exist.

            //if (!System.IO.Directory.Exists(m_path))
            //  throw new FolderMissingException(Strings.FileBackend.FolderMissingError(m_path));
            foreach (JObject item in files)
            {
                FileEntry fe = new FileEntry(
                    item.GetValue("Name").Value<string>(),
                    item.GetValue("Size").Value<long>(),
                    DateTime.Parse(item.GetValue("ModTime").Value<string>()),
                    DateTime.Parse(item.GetValue("ModTime").Value<string>())
                );
                fe.IsFolder = item.GetValue("IsDir").Value<bool>();
                yield return fe;


            }
        }

        public void Put(string remotename, string filename)
        {

            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = String.Format("copyto {0}:{1} {2}:{3}/{4}", local_repo, filename, remote_repo, remote_path, remotename);
            psi.CreateNoWindow = true;
            psi.FileName = "rclone";
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
#endif

            System.Diagnostics.Process p;
            p = System.Diagnostics.Process.Start(psi);

            String result_out = p.StandardOutput.ReadToEnd();
            String result_err = p.StandardError.ReadToEnd();
            Console.Error.WriteLine(result_out);
            Console.Error.WriteLine(result_err);
        }

        public void Get(string remotename, string filename)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = String.Format("copyto {2}:{3}/{4} {0}:{1}", local_repo, filename, remote_repo, remote_path, remotename);
            psi.CreateNoWindow = true;
            psi.FileName = "rclone";
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
#endif

            System.Diagnostics.Process p;
            p = System.Diagnostics.Process.Start(psi);

            String result_out = p.StandardOutput.ReadToEnd();
            String result_err = p.StandardError.ReadToEnd();
            Console.Error.WriteLine(result_out);
            Console.Error.WriteLine(result_err);
        }

        public void Delete(string remotename)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = String.Format("delete {0}:{1}/{2}", remote_repo, remote_path, remotename);
            psi.CreateNoWindow = true;
            psi.FileName = "rclone";
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;

#if DEBUG
            psi.CreateNoWindow = false;
            psi.WindowStyle = System.Diagnostics.ProcessWindowStyle.Normal;
#endif

            System.Diagnostics.Process p;
            p = System.Diagnostics.Process.Start(psi);

            String result_out = p.StandardOutput.ReadToEnd();
            String result_err = p.StandardError.ReadToEnd();
            Console.Error.WriteLine(result_out);
            Console.Error.WriteLine(result_err);
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(OPTION_LOCAL_REPO, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneLocalRepoShort, Strings.Rclone.RcloneLocalRepoLong, "local"),
                    new CommandLineArgument(OPTION_REMOTE_REPO, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneRemoteRepoShort, Strings.Rclone.RcloneRemoteRepoLong, "remote"),
                    new CommandLineArgument(OPTION_REMOTE_PATH, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneRemotePathShort, Strings.Rclone.RcloneRemotePathLong, "backup"),
                    new CommandLineArgument(OPTION_RCLONE, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneOptionRcloneShort, Strings.Rclone.RcloneOptionRcloneLong, ""),
                });

            }
        }

        public string Description
        {
            get
            {
                return Strings.Rclone.Description;
            }
        }

        public void Test()
        {
            this.TestList();
        }

        public void CreateFolder()
        {
           /* if (System.IO.Directory.Exists(m_path))
                throw new FolderAreadyExistedException();

            System.IO.Directory.CreateDirectory(m_path);*/
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion



    }
}
