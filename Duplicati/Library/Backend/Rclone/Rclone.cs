// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class Rclone : IBackend
    {
        private const string OPTION_LOCAL_REPO = "rclone-local-repository";
        private const string OPTION_REMOTE_REPO = "rclone-remote-repository";
        private const string OPTION_REMOTE_PATH = "rclone-remote-path";
        private const string OPTION_RCLONE = "rclone-option";
        private const string OPTION_RCLONE_EXECUTABLE = "rclone-executable";
        private const string RCLONE_ERROR_DIRECTORY_NOT_FOUND = "directory not found";
        private const string RCLONE_ERROR_CONFIG_NOT_FOUND = "didn't find section in config file";

        private readonly string local_repo;
        private readonly string remote_repo;
        private readonly string remote_path;
        private readonly string opt_rclone;
        private readonly string rclone_executable;

        public Rclone()
        {

        }

        public Rclone(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            /*should check here if program is installed */

            local_repo = options.GetValueOrDefault(OPTION_LOCAL_REPO, local_repo);
            remote_repo = options.GetValueOrDefault(OPTION_REMOTE_REPO, remote_repo);
            remote_path = options.GetValueOrDefault(OPTION_REMOTE_PATH, remote_path);
            opt_rclone = options.GetValueOrDefault(OPTION_RCLONE, opt_rclone) ?? "";
            rclone_executable = options.GetValueOrDefault(OPTION_RCLONE_EXECUTABLE, rclone_executable);

            if (string.IsNullOrWhiteSpace(local_repo))
                local_repo = "local";
            if (string.IsNullOrWhiteSpace(remote_repo))
                remote_repo = uri.Host;
            if (string.IsNullOrWhiteSpace(remote_path))
                remote_path = uri.Path;
            if (string.IsNullOrWhiteSpace(rclone_executable))
                rclone_executable = "rclone";

#if DEBUG
            Console.WriteLine("Constructor {0}: {1}:{2} {3}", local_repo, remote_repo, remote_path, opt_rclone);
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

        private async Task<string> RcloneCommandExecuter(String command, String arguments, CancellationToken cancelToken)
        {
            StringBuilder outputBuilder = new StringBuilder();
            StringBuilder errorBuilder = new StringBuilder();
            Process process;

            ProcessStartInfo psi = new ProcessStartInfo
            {
                Arguments = $"{arguments} {opt_rclone}",
                CreateNoWindow = true,
                FileName = command,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };

#if DEBUG
            Console.Error.WriteLine("command executing: {0} {1}", psi.FileName, psi.Arguments);
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
                    }
                }
            );

            process.ErrorDataReceived += new System.Diagnostics.DataReceivedEventHandler
            (
                delegate (object sender, System.Diagnostics.DataReceivedEventArgs e)
                {

                    if (!String.IsNullOrEmpty(e.Data))
                    {
#if DEBUG
                        Console.Error.WriteLine("error {0}", e.Data);
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

            while (!process.HasExited)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (cancelToken.IsCancellationRequested)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }

            process.CancelOutputRead();
            process.CancelErrorRead();

            if (errorBuilder.ToString().Contains(RCLONE_ERROR_DIRECTORY_NOT_FOUND))
            {
                throw new FolderMissingException(errorBuilder.ToString());
            }

            if (errorBuilder.ToString().Contains(RCLONE_ERROR_CONFIG_NOT_FOUND))
            {
                throw new Exception($"Missing config file? {errorBuilder}");
            }

            if (errorBuilder.Length > 0)
            {
                throw new Exception(errorBuilder.ToString());
            }

            return outputBuilder.ToString();
        }


        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            string str_result;

            try
            {

                str_result = await RcloneCommandExecuter(rclone_executable, $"lsjson {remote_repo}:{remote_path}", cancelToken).ConfigureAwait(false);
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
                    Console.Error.WriteLine(item);
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

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            try
            {
                await RcloneCommandExecuter(rclone_executable, $"copyto {local_repo}:{filename} {remote_repo}:{remote_path}/{remotename}", cancelToken).ConfigureAwait(false);
            }
            catch (FolderMissingException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            try
            {
                await RcloneCommandExecuter(rclone_executable, $"copyto {remote_repo}:{Path.Combine(this.remote_path, remotename)} {local_repo}:{filename}", cancelToken).ConfigureAwait(false);
            }
            catch (FolderMissingException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            //this will actually delete the folder if remotename is a folder... 
            // Will give a "directory not found" error if the file does not exist, need to change that to a missing file exception
            try
            {
                await RcloneCommandExecuter(rclone_executable, $"delete {remote_repo}:{Path.Combine(remote_path, remotename)}", cancelToken).ConfigureAwait(false);
            }
            catch (FolderMissingException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>([
                    new CommandLineArgument(OPTION_LOCAL_REPO, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneLocalRepoShort, Strings.Rclone.RcloneLocalRepoLong, "local"),
                    new CommandLineArgument(OPTION_REMOTE_REPO, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneRemoteRepoShort, Strings.Rclone.RcloneRemoteRepoLong, "remote"),
                    new CommandLineArgument(OPTION_REMOTE_PATH, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneRemotePathShort, Strings.Rclone.RcloneRemotePathLong, "backup"),
                    new CommandLineArgument(OPTION_RCLONE, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneOptionRcloneShort, Strings.Rclone.RcloneOptionRcloneLong, ""),
                    new CommandLineArgument(OPTION_RCLONE_EXECUTABLE, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneExecutableShort, Strings.Rclone.RcloneExecutableLong, "rclone")
                ]);

            }
        }

        public string Description
        {
            get
            {
                return Strings.Rclone.Description;
            }
        }

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { remote_repo });

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            return RcloneCommandExecuter(rclone_executable, $"mkdir {remote_repo}:{remote_path}", cancelToken);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion



    }
}
