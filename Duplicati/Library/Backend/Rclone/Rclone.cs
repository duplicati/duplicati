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
        private const string OPTION_RCLONE_EXECUTABLE = "rclone-executable";

        private string local_repo;
        private string remote_repo;
        private string remote_path;
        private string opt_rclone;
        private string rclone_executable;

        public Rclone()
        {
            
        }

        public Rclone(string url, Dictionary<string, string> options)
        {

            local_repo = "local";
            remote_repo = "remote";
            remote_path = "C:\\temp\\backup";
            opt_rclone = "";
            rclone_executable = "rclone";
            /*should check here if program is installed */

            if (options.ContainsKey(OPTION_LOCAL_REPO))
                local_repo = options[OPTION_LOCAL_REPO];

            if (options.ContainsKey(OPTION_REMOTE_REPO))
                remote_repo = options[OPTION_REMOTE_REPO];

            if (options.ContainsKey(OPTION_REMOTE_PATH))
                remote_path = options[OPTION_REMOTE_PATH];

            if (options.ContainsKey(OPTION_RCLONE))
                opt_rclone = options[OPTION_RCLONE];

            if (options.ContainsKey(OPTION_RCLONE_EXECUTABLE))
                rclone_executable = options[OPTION_RCLONE_EXECUTABLE];

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

        private string RcloneCommandExecuter(String command, String arguments)
        {
            System.Diagnostics.ProcessStartInfo psi = new System.Diagnostics.ProcessStartInfo();
            psi.Arguments = arguments;
            psi.CreateNoWindow = true;
            psi.FileName = command;
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
            try
            {
#if DEBUG
                Console.Error.WriteLine(String.Format("command executing: {0} {1}", psi.FileName, psi.Arguments));
#endif
                p = System.Diagnostics.Process.Start(psi);
            }

            catch (System.ComponentModel.Win32Exception ex)
            {
                throw new Exception(String.Format("Program \"{0}\" does not exist", command), ex);
            }

            string error = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (error.Length > 0) {
                if (error.Contains("directory not found"))
                    throw new FolderMissingException(error);
                if (error.Contains("didn't find section in config file"))
                    throw new Exception(String.Format("Missing config file? {0}", error));
            }
            return p.StandardOutput.ReadToEnd();
        }


        public IEnumerable<IFileEntry> List()
        {
            //Console.Error.WriteLine(string.Format("Listing contents: rclone lsjson {0}:{1}",remote_repo, remote_path));

            JArray files = JArray.Parse(RcloneCommandExecuter(rclone_executable, String.Format("lsjson {0}:{1}", remote_repo, remote_path)));
            // this will give an error if the executable does not exist.

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
            try
            {
                RcloneCommandExecuter(rclone_executable, String.Format("copyto {0}:{1} {2}:{3}/{4}", local_repo, filename, remote_repo, remote_path, remotename));
            }
            catch (FolderMissingException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        public void Get(string remotename, string filename)
        {
            try
            {
                RcloneCommandExecuter(rclone_executable, String.Format("copyto {2}:{3}/{4} {0}:{1}", local_repo, filename, remote_repo, remote_path, remotename));
            }
            catch (FolderMissingException ex) {
                throw new FileMissingException(ex);
            }
        }

        public void Delete(string remotename)
        {
            //this will actually delete the folder if remotename is a folder... 
            // Will give a "directory not found" error if the file does not exist, need to change that to a missing file exception
            try
            {
                RcloneCommandExecuter(rclone_executable, String.Format("delete {0}:{1}/{2}", remote_repo, remote_path, remotename));
            }
            catch (FolderMissingException ex) {
                throw new FileMissingException(ex);
            }
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
            
                    RcloneCommandExecuter(rclone_executable, String.Format("mkdir {0}:{1}", remote_repo, remote_path));
              
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion



    }
}
