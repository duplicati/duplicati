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
using System.Diagnostics;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.IO;

namespace Duplicati.Library.Backend
{
    public class Rclone : IBackend
    {
        private const string OPTION_LOCAL_REPO = "rclone-local-repository";
        private const string OPTION_REMOTE_REPO = "rclone-remote-repository";
        private const string OPTION_REMOTE_PATH = "rclone-remote-path";
        private const string OPTION_RCLONE = "rclone-option";
        private const string OPTION_RCLONE_EXECUTABLE = "rclone-executable";
        private const string RCLONE_ERROR_DIRECTORY_NOT_FOUND = "directory not found";
        private const string RCLONE_ERROR_CONFIG_NOT_FOUND = "didn't find section in config file";

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
            var uri = new Utility.Uri(url);

            remote_repo = uri.Host;
            remote_path = uri.Path;


            local_repo = "local";
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
#if DEBUG
            Console.WriteLine(string.Format("Constructor {0}: {1}:{2} {3}", local_repo, remote_repo, remote_path, opt_rclone));
#endif
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
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            Process process;

            ProcessStartInfo psi = new ProcessStartInfo
            {
                Arguments = String.Format("{0} {1}", arguments, opt_rclone),
                CreateNoWindow = true,
                FileName = command,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

#if DEBUG
            Console.Error.WriteLine(String.Format("command executing: {0} {1}", psi.FileName, psi.Arguments));
#endif
            process = new Process
            {
                StartInfo = psi,
                // enable raising events because Process does not raise events by default
                EnableRaisingEvents = true
            };
            // attach the event handler for OutputDataReceived before starting the process
            process.OutputDataReceived += new System.Diagnostics.DataReceivedEventHandler
            (
                delegate (object sender, System.Diagnostics.DataReceivedEventArgs e)
                {
                    if (!String.IsNullOrEmpty(e.Data))
                    {
#if DEBUG
                      //  Console.Error.WriteLine(String.Format("output {0}", e.Data));
#endif
                        // append the new data to the data already read-in
                        outputBuilder.Append(e.Data);
                    };
                }
            );  

            process.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler
            (
                delegate (object sender, System.Diagnostics.DataReceivedEventArgs e)
                {

                    if (!String.IsNullOrEmpty(e.Data))
                    {
#if DEBUG
                        Console.Error.WriteLine(String.Format("error {0}", e.Data));
#endif
                        errorBuilder.Append(e.Data);
                    }
                }
            );

            // start the process
            // then begin asynchronously reading the output
            // then wait for the process to exit
            // then cancel asynchronously reading the output
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();
            process.CancelOutputRead();
            process.CancelErrorRead();

            if (errorBuilder.ToString().Contains(RCLONE_ERROR_DIRECTORY_NOT_FOUND))
            {
                throw new FolderMissingException(errorBuilder.ToString());
            }

            if (errorBuilder.ToString().Contains(RCLONE_ERROR_CONFIG_NOT_FOUND))
            {
                throw new Exception(String.Format("Missing config file? {0}", errorBuilder.ToString()));
            }

            if (errorBuilder.Length > 0) {
                throw new Exception(errorBuilder.ToString());
            }
            Console.Error.WriteLine(errorBuilder.ToString());
            return outputBuilder.ToString();
        }


        public IEnumerable<IFileEntry> List()
        {
            JArray files;
            String str_result;

            try
            {

                str_result = RcloneCommandExecuter(rclone_executable, String.Format("lsjson {0}:{1}", remote_repo, remote_path));
                // this will give an error if the executable does not exist.
            }

            catch (FolderMissingException ex)
            {
                throw new FolderMissingException(ex);
            }

            using (JsonReader jsonReader = new JsonTextReader(new StringReader(str_result)))
            {
                //no date parsing by JArray needed, will be parsed later
                jsonReader.DateParseHandling = DateParseHandling.None;
                var array = JArray.Load(jsonReader);

                foreach (JObject item in array)
                {
#if DEBUG
                    Console.Error.WriteLine(item.ToString());
#endif
                    FileEntry fe = new FileEntry(
                        item.GetValue("Name").Value<string>(),
                        item.GetValue("Size").Value<long>(),
                        DateTime.Parse(item.GetValue("ModTime").Value<string>()),
                        DateTime.Parse(item.GetValue("ModTime").Value<string>())
                    )
                    {
                        IsFolder = item.GetValue("IsDir").Value<bool>()
                    };
                    yield return fe;
                }
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
                    new CommandLineArgument(OPTION_RCLONE_EXECUTABLE, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneExecutableShort, Strings.Rclone.RcloneExecutableLong, "rclone")
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

        public string[] DNSName
        {
            get { return new string[] { remote_repo }; }
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
