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
using Duplicati.Library.Utility.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

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
        private readonly TimeoutOptionsHelper.Timeouts timeouts;

        public Rclone()
        {
            local_repo = null!;
            remote_repo = null!;
            remote_path = null!;
            opt_rclone = null!;
            rclone_executable = null!;
            timeouts = null!;
        }

        public Rclone(string url, Dictionary<string, string?> options)
        {
            var uri = new Utility.Uri(url);
            /*should check here if program is installed */

            local_repo = options.GetValueOrDefault(OPTION_LOCAL_REPO) ?? "";
            remote_repo = options.GetValueOrDefault(OPTION_REMOTE_REPO) ?? "";
            remote_path = options.GetValueOrDefault(OPTION_REMOTE_PATH) ?? "";
            opt_rclone = options.GetValueOrDefault(OPTION_RCLONE) ?? "";
            rclone_executable = options.GetValueOrDefault(OPTION_RCLONE_EXECUTABLE) ?? "";

            if (string.IsNullOrWhiteSpace(local_repo))
                local_repo = "local";
            if (string.IsNullOrWhiteSpace(remote_repo))
                remote_repo = uri.Host;
            if (string.IsNullOrWhiteSpace(remote_path))
                remote_path = uri.Path;
            if (string.IsNullOrWhiteSpace(rclone_executable))
                rclone_executable = "rclone";

            timeouts = TimeoutOptionsHelper.Parse(options);

#if DEBUG
            Console.WriteLine("Constructor {0}: {1}:{2} {3}", local_repo, remote_repo, remote_path, opt_rclone);
#endif
        }

        #region IBackendInterface Members

        public string DisplayName => Strings.Rclone.DisplayName;

        public string ProtocolKey => "rclone";

        private async Task<string> RcloneCommandExecuter(string command, string arguments, TimeSpan timeout, CancellationToken cancelToken)
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
            process.OutputDataReceived += new DataReceivedEventHandler
            (
                delegate (object sender, DataReceivedEventArgs e)
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
#if DEBUG
                        //  Console.Error.WriteLine(String.Format("output {0}", e.Data));
#endif
                        // append the new data to the data already read-in
                        outputBuilder.Append(e.Data);
                    }
                }
            );

            process.ErrorDataReceived += new DataReceivedEventHandler
            (
                delegate (object sender, DataReceivedEventArgs e)
                {

                    if (!string.IsNullOrEmpty(e.Data))
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

            using var timer = timeout == Timeout.InfiniteTimeSpan
                ? new TaskCompletionSource<bool>().Task
                : Task.Delay(timeout);

            var killed = false;
            while (!process.HasExited)
            {
                await Task.Delay(500).ConfigureAwait(false);
                if (cancelToken.IsCancellationRequested || timer.IsCompleted)
                {
                    killed = true;
                    process.Kill();
                    process.WaitForExit();
                }
            }

            process.CancelOutputRead();
            process.CancelErrorRead();

            if (errorBuilder.ToString().Contains(RCLONE_ERROR_DIRECTORY_NOT_FOUND))
                throw new FolderMissingException(errorBuilder.ToString());

            if (errorBuilder.ToString().Contains(RCLONE_ERROR_CONFIG_NOT_FOUND))
                throw new Exception($"Missing config file? {errorBuilder}");

            if (errorBuilder.Length > 0)
                throw new Exception(errorBuilder.ToString());

            if (killed)
            {
                if (timer.IsCompleted)
                    throw new TimeoutException();

                throw new TaskCanceledException();
            }

            return outputBuilder.ToString();
        }


        /// <inheritdoc />
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            string str_result;

            try
            {

                str_result = await RcloneCommandExecuter(rclone_executable, $"lsjson {remote_repo}:{remote_path}", timeouts.ListTimeout, cancelToken).ConfigureAwait(false);
                // this will give an error if the executable does not exist.
            }
            catch (FolderMissingException ex)
            {
                throw new FolderMissingException(ex);
            }

            using (var jsonReader = new JsonTextReader(new StringReader(str_result)))
            {
                //no date parsing by JArray needed, will be parsed later
                jsonReader.DateParseHandling = DateParseHandling.None;
                var array = JArray.Load(jsonReader);

                foreach (JObject item in array)
                {
#if DEBUG
                    Console.Error.WriteLine(item);
#endif
                    var modTimeString = item.GetValue("ModTime")?.Value<string>();
                    var modTime = string.IsNullOrWhiteSpace(modTimeString)
                        ? new DateTime(0)
                        : DateTime.Parse(modTimeString);

                    var fe = new FileEntry(
                        item.GetValue("Name")?.Value<string>(),
                        item.GetValue("Size")?.Value<long>() ?? -1,
                        modTime,
                        modTime
                    )
                    {
                        IsFolder = item.GetValue("IsDir")?.Value<bool>() ?? false
                    };
                    yield return fe;
                }
            }
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            try
            {
                await RcloneCommandExecuter(rclone_executable, $"copyto {local_repo}:{filename} {remote_repo}:{remote_path}/{remotename}", Timeout.InfiniteTimeSpan, cancelToken).ConfigureAwait(false);
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
                await RcloneCommandExecuter(rclone_executable, $"copyto {remote_repo}:{Path.Combine(this.remote_path, remotename)} {local_repo}:{filename}", Timeout.InfiniteTimeSpan, cancelToken).ConfigureAwait(false);
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
                await RcloneCommandExecuter(rclone_executable, $"delete {remote_repo}:{Path.Combine(remote_path, remotename)}", timeouts.ShortTimeout, cancelToken).ConfigureAwait(false);
            }
            catch (FolderMissingException ex)
            {
                throw new FileMissingException(ex);
            }
        }

        public IList<ICommandLineArgument> SupportedCommands => [
            new CommandLineArgument(OPTION_LOCAL_REPO, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneLocalRepoShort, Strings.Rclone.RcloneLocalRepoLong, "local"),
            new CommandLineArgument(OPTION_REMOTE_REPO, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneRemoteRepoShort, Strings.Rclone.RcloneRemoteRepoLong, "remote"),
            new CommandLineArgument(OPTION_REMOTE_PATH, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneRemotePathShort, Strings.Rclone.RcloneRemotePathLong, "backup"),
            new CommandLineArgument(OPTION_RCLONE, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneOptionRcloneShort, Strings.Rclone.RcloneOptionRcloneLong, ""),
            new CommandLineArgument(OPTION_RCLONE_EXECUTABLE, CommandLineArgument.ArgumentType.String, Strings.Rclone.RcloneExecutableShort, Strings.Rclone.RcloneExecutableLong, "rclone"),
            .. TimeoutOptionsHelper.GetOptions()
                .Where(x => x.Name != TimeoutOptionsHelper.READ_WRITE_TIMEOUT_OPTION)
        ];

        public string Description => Strings.Rclone.Description;

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { remote_repo });

        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            return RcloneCommandExecuter(rclone_executable, $"mkdir {remote_repo}:{remote_path}", timeouts.ShortTimeout, cancelToken);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion



    }
}
