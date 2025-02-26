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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Duplicati.Library.Interface;
using System.Linq;
using System.Globalization;
using System.Threading;
using Duplicati.Library.Utility;
using Duplicati.StreamUtil;
using System.Threading.Tasks;

namespace Duplicati.CommandLine.BackendTester
{
    public class Program
    {
        class TempFile
        {
            public readonly string remotefilename;
            public readonly string localfilename;
            public readonly byte[] hash;
            public readonly long length;
            public bool found = false;

            public TempFile(string remotefilename, string localfilename, byte[] hash, long length)
            {
                this.remotefilename = remotefilename;
                this.localfilename = localfilename;
                this.hash = hash;
                this.length = length;
            }
        }

        private const string ValidFilenameChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ123456789";
        private const string ExtendedChars = "-_',=)(&%$#@! +";

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        public static int Main(string[] _args)
        {
            try
            {
                Library.AutoUpdater.PreloadSettingsLoader.ConfigurePreloadSettings(ref _args, Library.AutoUpdater.PackageHelper.NamedExecutable.BackendTester);

                if (_args.Length == 1)
                {
                    try
                    {
                        var p = Environment.ExpandEnvironmentVariables(_args[0]);
                        if (System.IO.File.Exists(p))
                            _args = (from x in System.IO.File.ReadLines(p)
                                     where !string.IsNullOrWhiteSpace(x) && !x.Trim().StartsWith("#", StringComparison.Ordinal)
                                     select x.Trim()
                            ).ToArray();
                    }
                    catch
                    {
                    }
                }

                List<string> args = new List<string>(_args);
                Dictionary<string, string> options = Library.Utility.CommandLineParser.ExtractOptions(args);

                if (args.Count != 1 || HelpOptionExtensions.IsArgumentAnyHelpString(args))
                {
                    Console.WriteLine("Usage: <protocol>://<username>:<password>@<path>");
                    Console.WriteLine("Example: ftp://user:pass@server/folder");
                    Console.WriteLine();
                    Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.DynamicLoader.BackendLoader.Keys));

                    Console.WriteLine();
                    List<string> lines = new List<string>();
                    foreach (Library.Interface.ICommandLineArgument arg in SupportedCommands)
                        Library.Interface.CommandLineArgument.PrintArgument(lines, arg);

                    foreach (string s in lines)
                        Console.WriteLine(s);

                    return 0;
                }

                if (options.ContainsKey("tempdir") && !string.IsNullOrEmpty(options["tempdir"]))
                    Library.Utility.SystemContextSettings.DefaultTempPath = options["tempdir"];

                using var SystemSettings = Duplicati.Library.Utility.SystemContextSettings.StartSession();

                if (!options.ContainsKey("auth_password") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_PASSWORD")))
                    options["auth_password"] = System.Environment.GetEnvironmentVariable("AUTH_PASSWORD");

                if (!options.ContainsKey("auth_username") && !string.IsNullOrEmpty(System.Environment.GetEnvironmentVariable("AUTH_USERNAME")))
                    options["auth_username"] = System.Environment.GetEnvironmentVariable("AUTH_USERNAME");

                int reruns = 5;
                if (options.ContainsKey("reruns"))
                    reruns = int.Parse(options["reruns"]);

                for (int i = 0; i < reruns; i++)
                {
                    Console.WriteLine(DateTime.Now.ToShortDateString() + " " + DateTime.Now.ToLongTimeString());
                    Console.WriteLine(LogTimeStamp + "Starting run no {0}", i);
                    if (!Run(args, options, i == 0))
                        return 1;
                }
                Console.WriteLine(LogTimeStamp + "Unittest complete!");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine(LogTimeStamp + "Unittest failed: " + ex);
            }

            return 1;
        }

        static bool Run(List<string> args, Dictionary<string, string> options, bool first)
        {
            bool failAfterFinished = false;
            var backend = Library.DynamicLoader.BackendLoader.GetBackend(args[0], options);
            if (backend == null)
            {
                Console.WriteLine("Unsupported backend");
                Console.WriteLine();
                Console.WriteLine("Supported backends: " + string.Join(",", Duplicati.Library.DynamicLoader.BackendLoader.Keys));
                return false;
            }

            var allowedChars = ValidFilenameChars;
            if (options.ContainsKey("extended-chars"))
            {
                allowedChars += String.IsNullOrEmpty(options["extended-chars"]) ? ExtendedChars : options["extended-chars"];
            }

            var autoCreateFolders = Library.Utility.Utility.ParseBoolOption(options, "auto-create-folder");

            options.TryGetValue("enable-module", out var enabledModulesValue);
            options.TryGetValue("disable-module", out var disabledModulesValue);
            var enabledModules = enabledModulesValue == null ? new string[0] : enabledModulesValue.Trim().ToLower(CultureInfo.InvariantCulture).Split(',');
            var disabledModules = disabledModulesValue == null ? new string[0] : disabledModulesValue.Trim().ToLower(CultureInfo.InvariantCulture).Split(',');

            var loadedModules = new List<IGenericModule>();
            foreach (var m in Library.DynamicLoader.GenericLoader.Modules)
                if (!disabledModules.Contains(m.Key, StringComparer.OrdinalIgnoreCase) && (m.LoadAsDefault || enabledModules.Contains(m.Key, StringComparer.OrdinalIgnoreCase)))
                {
                    m.Configure(options);
                    loadedModules.Add(m);
                }

            try
            {
                IEnumerable<Library.Interface.IFileEntry> curlist = null;
                try
                {
                    backend.TestAsync(CancellationToken.None).Await();
                    curlist = backend.ListAsync(CancellationToken.None).ToBlockingEnumerable().ToList();
                }
                catch (FolderMissingException)
                {
                    if (autoCreateFolders)
                    {
                        try
                        {
                            backend.CreateFolderAsync(CancellationToken.None).Await();
                            curlist = backend.ListAsync(CancellationToken.None).ToBlockingEnumerable().ToList();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(LogTimeStamp + "Autocreate folder failed with message: " + ex.Message);
                        }
                    }

                    if (curlist == null)
                        throw;
                }

                foreach (Library.Interface.IFileEntry fe in curlist)
                    if (!fe.IsFolder)
                    {
                        if (Library.Utility.Utility.ParseBoolOption(options, "auto-clean") && first)
                            if (Library.Utility.Utility.ParseBoolOption(options, "force"))
                            {
                                Console.WriteLine(LogTimeStamp + "Auto clean, removing file: {0}", fe.Name);
                                backend.DeleteAsync(fe.Name, CancellationToken.None).Await();
                                continue;
                            }
                            else
                                Console.WriteLine("Specify the --force flag to actually delete files");

                        var fileCount = curlist.Where(x => !x.IsFolder).Count();
                        var filenames = curlist.Where(x => !x.IsFolder).Select(x => x.Name).Take(10).ToList();
                        Console.WriteLine(LogTimeStamp + $"*** Remote folder contains {fileCount} file(s), aborting");
                        Console.WriteLine(LogTimeStamp + $"*** First {filenames.Count} file(s): {Environment.NewLine}{string.Join(Environment.NewLine, filenames)}");
                        if (fileCount > filenames.Count)
                            Console.WriteLine(LogTimeStamp + $"*** ... and {fileCount - filenames.Count} more file(s)");
                        return false;
                    }


                var number_of_files = 10;
                var min_file_size = 1024;
                var max_file_size = 1024 * 1024 * 50;
                var min_filename_size = 5;
                var max_filename_size = 80;
                var disableStreaming = Library.Utility.Utility.ParseBoolOption(options, "disable-streaming-transfers");
                var skipOverwriteTest = Library.Utility.Utility.ParseBoolOption(options, "skip-overwrite-test");
                var trimFilenameSpaces = Library.Utility.Utility.ParseBoolOption(options, "trim-filename-spaces");
                var waitAfterUpload = TimeSpan.Zero;
                var waitAfterDelete = TimeSpan.Zero;

                var throttleUpload = 0L;
                if (options.TryGetValue("throttle-upload", out var throttleUploadString))
                {
                    if (!(backend is IStreamingBackend) || disableStreaming)
                    {
                        Console.WriteLine(LogTimeStamp + "Warning: Throttling is only supported in this tool on streaming backends");
                    }

                    throttleUpload = Duplicati.Library.Utility.Sizeparser.ParseSize(throttleUploadString, "kb");
                }

                var throttleDownload = 0L;
                if (options.TryGetValue("throttle-download", out var throttleDownloadString))
                {
                    if (!(backend is IStreamingBackend) || disableStreaming)
                    {
                        Console.WriteLine(LogTimeStamp + "Warning: Throttling is only supported in this tool on streaming backends");
                    }

                    throttleDownload = Duplicati.Library.Utility.Sizeparser.ParseSize(throttleDownloadString, "kb");
                }

                int readWriteTimeout = backend is ITimeoutExemptBackend ||
                                       (Environment.GetEnvironmentVariable("READ_WRITE_TIMEOUT_SECONDS") is { } timeout
                                        && int.TryParse(timeout, out var seconds)
                                        && seconds == -1)
                    ? Timeout.Infinite
                    : Environment.GetEnvironmentVariable("READ_WRITE_TIMEOUT_SECONDS") is { } timeoutRetry
                      && int.TryParse(timeoutRetry, out var secondsRetry)
                        ? (int)TimeSpan.FromSeconds(secondsRetry).TotalMilliseconds
                        : (int)TimeSpan.FromSeconds(60).TotalMilliseconds;

                Console.WriteLine(LogTimeStamp + "Read Write Timeout set to {0}{1}",
                    readWriteTimeout == Timeout.Infinite ? "infinite" : readWriteTimeout + " ms",
                    backend is ITimeoutExemptBackend ? " (because implements ITimeoutExemptBackend)" : "");

                // Allow overriding the timeout for the backend here, even if timeouts are disabled
                if (options.TryGetValue("read-write-timeout", out var readWriteTimeoutString))
                    readWriteTimeout = (int)Timeparser.ParseTimeSpan(readWriteTimeoutString).TotalMilliseconds;

                if (readWriteTimeout <= 0)
                    readWriteTimeout = Timeout.Infinite;

                if (options.ContainsKey("number-of-files"))
                    number_of_files = int.Parse(options["number-of-files"]);
                if (options.ContainsKey("min-file-size"))
                    min_file_size = (int)Duplicati.Library.Utility.Sizeparser.ParseSize(options["min-file-size"], "mb");
                if (options.ContainsKey("max-file-size"))
                    max_file_size = (int)Duplicati.Library.Utility.Sizeparser.ParseSize(options["max-file-size"], "mb");

                if (options.ContainsKey("min-filename-length"))
                    min_filename_size = int.Parse(options["min-filename-length"]);
                if (options.ContainsKey("max-filename-length"))
                    max_filename_size = int.Parse(options["max-filename-length"]);

                if (options.ContainsKey("wait-after-upload"))
                    waitAfterUpload = Timeparser.ParseTimeSpan(options["wait-after-upload"]);
                if (options.ContainsKey("wait-after-delete"))
                    waitAfterDelete = Timeparser.ParseTimeSpan(options["wait-after-delete"]);

                var rnd = new Random();
                var sha = System.Security.Cryptography.SHA256.Create();

                //Create random files
                using (var tf = new Duplicati.Library.Utility.TempFolder())
                {
                    var files = new List<TempFile>();
                    for (int i = 0; i < number_of_files; i++)
                    {
                        var filename = CreateRandomRemoteFileName(min_filename_size, max_filename_size, allowedChars, trimFilenameSpaces, rnd);

                        var localfilename = CreateRandomFile(tf, i, min_file_size, max_file_size, rnd);

                        //Calculate local hash and length
                        using (var fs = new System.IO.FileStream(localfilename, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                            files.Add(new TempFile(filename, localfilename, sha.ComputeHash(fs), fs.Length));
                    }

                    byte[] dummyFileHash = null;
                    if (!skipOverwriteTest)
                    {
                        Console.WriteLine(LogTimeStamp + "Uploading wrong files ...");
                        using (var dummy = Library.Utility.TempFile.WrapExistingFile(CreateRandomFile(tf, files.Count, 1024, 2048, rnd)))
                        {
                            using (var fs = new System.IO.FileStream(dummy, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                                dummyFileHash = sha.ComputeHash(fs);

                            //Upload a dummy file for entry 0 and the last one, they will be replaced by the real files afterwards
                            //We upload entry 0 twice just to try to freak any internal cache list
                            Uploadfile(dummy, 0, files[0].remotefilename, backend, disableStreaming, throttleUpload, readWriteTimeout);
                            Uploadfile(dummy, 0, files[0].remotefilename, backend, disableStreaming, throttleUpload, readWriteTimeout);
                            Uploadfile(dummy, files.Count - 1, files[files.Count - 1].remotefilename, backend, disableStreaming, throttleUpload, readWriteTimeout);
                        }

                    }

                    Console.WriteLine(LogTimeStamp + "Uploading files ...");

                    for (int i = 0; i < files.Count; i++)
                        Uploadfile(files[i].localfilename, i, files[i].remotefilename, backend, disableStreaming, throttleUpload, readWriteTimeout);

                    TempFile originalRenamedFile = null;
                    string renamedFileNewName = null;
                    if (backend is IRenameEnabledBackend renameEnabledBackend)
                    {
                        // Rename the second file in the list, if there are more than one. If not, just do the first one.
                        int renameIndex = files.Count > 1 ? 1 : 0;
                        originalRenamedFile = files[renameIndex];

                        renamedFileNewName = CreateRandomRemoteFileName(min_filename_size, max_filename_size, allowedChars, trimFilenameSpaces, rnd);

                        Console.WriteLine(LogTimeStamp + "Renaming file {0} from {1} to {2}", renameIndex, originalRenamedFile.remotefilename, renamedFileNewName);

                        renameEnabledBackend.RenameAsync(originalRenamedFile.remotefilename, renamedFileNewName, CancellationToken.None).Await();
                        files[renameIndex] = new TempFile(renamedFileNewName, originalRenamedFile.localfilename, originalRenamedFile.hash, originalRenamedFile.length);
                    }

                    if (waitAfterUpload > TimeSpan.Zero)
                    {
                        Console.WriteLine(LogTimeStamp + "Waiting {0} after upload", waitAfterUpload);
                        Thread.Sleep(waitAfterUpload);
                    }

                    Console.WriteLine(LogTimeStamp + "Verifying file list ...");

                    curlist = backend.ListAsync(CancellationToken.None).ToBlockingEnumerable().ToList();
                    foreach (var fe in curlist)
                        if (!fe.IsFolder)
                        {
                            bool found = false;
                            foreach (var tx in files)
                                if (tx.remotefilename == fe.Name)
                                {
                                    if (tx.found)
                                        Console.WriteLine(LogTimeStamp + "*** File with name {0} was found more than once", tx.remotefilename);
                                    found = true;
                                    tx.found = true;

                                    if (fe.Size > 0 && tx.length != fe.Size)
                                        Console.WriteLine(LogTimeStamp + "*** File with name {0} has size {1} but the size was reported as {2}", tx.remotefilename, tx.length, fe.Size);

                                    break;
                                }

                            if (!found)
                                if (originalRenamedFile != null && renamedFileNewName != null && originalRenamedFile.remotefilename == fe.Name)
                                {
                                    Console.WriteLine(LogTimeStamp + "*** File with name {0} was found on server but was supposed to have been renamed to {1}!", fe.Name, renamedFileNewName);
                                }
                                else
                                {
                                    Console.WriteLine(LogTimeStamp + "*** File with name {0} was found on server but not uploaded!", fe.Name);
                                }
                        }

                    foreach (var tx in files)
                        if (!tx.found)
                            Console.WriteLine(LogTimeStamp + "*** File with name {0} was uploaded but not found afterwards", tx.remotefilename);

                    Console.WriteLine(LogTimeStamp + "Downloading files");

                    for (int i = 0; i < files.Count; i++)
                    {
                        using (var cf = new Duplicati.Library.Utility.TempFile())
                        {
                            Exception e = null;
                            Console.Write(LogTimeStamp + "Downloading file {0} ... ", i);

                            var sw = Stopwatch.StartNew();

                            try
                            {
                                if (backend is IStreamingBackend streamingBackend && !disableStreaming)
                                {
                                    using (var fs = new System.IO.FileStream(cf, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                                    using (var timeoutStream = new TimeoutObservingStream(fs) { WriteTimeout = readWriteTimeout })
                                    using (var ts = new Library.Utility.ThrottledStream(timeoutStream, throttleDownload, throttleDownload))
                                    using (var nss = new NonSeekableStream(ts))
                                        streamingBackend.GetAsync(files[i].remotefilename, nss, timeoutStream.TimeoutToken).Await();
                                }
                                else
                                    backend.GetAsync(files[i].remotefilename, cf, CancellationToken.None).Await();

                                e = null;
                            }
                            catch (Exception ex)
                            {
                                e = ex;
                            }

                            if (e != null)
                            {
                                failAfterFinished = true;
                                Console.WriteLine(LogTimeStamp + $"failed\n*** Error: {e} after {sw.ElapsedMilliseconds} ms");
                            }
                            else
                                Console.WriteLine($" done in {sw.ElapsedMilliseconds} ms");

                            Console.Write(LogTimeStamp + "Checking hash ... ");

                            using (var fs = new System.IO.FileStream(cf, System.IO.FileMode.Open, System.IO.FileAccess.Read))
                                if (Convert.ToBase64String(sha.ComputeHash(fs)) != Convert.ToBase64String(files[i].hash))
                                {
                                    if (dummyFileHash != null && Convert.ToBase64String(sha.ComputeHash(fs)) == Convert.ToBase64String(dummyFileHash))
                                        Console.WriteLine(LogTimeStamp + "failed\n*** Downloaded file was the dummy file"); // Should this be failed?
                                    else
                                    {
                                        failAfterFinished = true;
                                        Console.WriteLine(LogTimeStamp + "failed\n*** Downloaded file was corrupt");
                                    }
                                }
                                else
                                    Console.WriteLine("done");
                        }
                    }

                    Console.WriteLine(LogTimeStamp + "Deleting files...");

                    foreach (var tx in files)
                        try { backend.DeleteAsync(tx.remotefilename, CancellationToken.None).Await(); }
                        catch (Exception ex)
                        {
                            failAfterFinished = true;
                            Console.WriteLine(LogTimeStamp + "*** Failed to delete file {0}, message: {1}", tx.remotefilename, ex);
                        }

                    if (waitAfterDelete > TimeSpan.Zero)
                    {
                        Console.WriteLine(LogTimeStamp + "Waiting {0} after delete", waitAfterDelete);
                        Thread.Sleep(waitAfterDelete);
                    }

                    curlist = backend.ListAsync(CancellationToken.None).ToBlockingEnumerable().ToList();
                    foreach (var fe in curlist)
                        if (!fe.IsFolder)
                        {
                            Console.WriteLine(LogTimeStamp + "*** Remote folder contains {0} after cleanup", fe.Name);
                        }

                    // Test some error cases
                    Console.WriteLine(LogTimeStamp + "Checking retrieval of non-existent file...");
                    var caughtExpectedException = false;
                    try
                    {
                        using (var tempFile = new Duplicati.Library.Utility.TempFile())
                        {
                            backend.GetAsync(string.Format("NonExistentFile-{0}", Guid.NewGuid()), tempFile.Name, CancellationToken.None).Await();
                        }
                    }
                    catch (FileMissingException)
                    {
                        Console.WriteLine(LogTimeStamp + "Caught expected FileMissingException");
                        caughtExpectedException = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(LogTimeStamp + "*** Retrieval of non-existent file failed: {0}", ex);
                    }

                    if (!caughtExpectedException)
                    {
                        failAfterFinished = true;
                        Console.WriteLine(LogTimeStamp + "*** Retrieval of non-existent file should have failed with FileMissingException");
                    }
                }

                // Test quota retrieval
                if (backend is IQuotaEnabledBackend quotaEnabledBackend)
                {
                    Console.WriteLine(LogTimeStamp + "Checking quota...");
                    IQuotaInfo quota = null;
                    bool noException;
                    try
                    {
                        quota = quotaEnabledBackend.GetQuotaInfoAsync(CancellationToken.None).Await();
                        noException = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(LogTimeStamp + "*** Checking quota information failed: {0}", ex);
                        noException = false;
                    }

                    if (noException)
                    {
                        if (quota != null)
                        {
                            Console.WriteLine(LogTimeStamp + "Free Space:  {0}", Library.Utility.Utility.FormatSizeString(quota.FreeQuotaSpace));
                            Console.WriteLine(LogTimeStamp + "Total Space: {0}", Library.Utility.Utility.FormatSizeString(quota.TotalQuotaSpace));
                        }
                        else
                        {
                            Console.WriteLine(LogTimeStamp + "Unable to retrieve quota information");
                        }
                    }
                }

                // Test DNSName lookup
                Console.WriteLine(LogTimeStamp + "Checking DNS names used by this backend...");
                try
                {
                    var dnsNames = backend.GetDNSNamesAsync(CancellationToken.None).Await();
                    if (dnsNames != null)
                    {
                        foreach (string dnsName in dnsNames)
                        {
                            Console.WriteLine(dnsName);
                        }
                    }
                    else
                    {
                        Console.WriteLine(LogTimeStamp + "No DNS names reported");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(LogTimeStamp + "*** Checking DNSName failed: {0}", ex);
                }
            }
            finally
            {
                foreach (var m in loadedModules)
                    if (m is IDisposable disposable)
                        disposable.Dispose();
            }

            return !failAfterFinished;
        }

        private static void Uploadfile(string localfilename, int i, string remotefilename, IBackend backend, bool disableStreaming, long throttle, int readWriteTimeout)
        {
            Console.Write(LogTimeStamp + "Uploading file {0}, {1} ... ", i, Duplicati.Library.Utility.Utility.FormatSizeString(new System.IO.FileInfo(localfilename).Length));
            Exception e = null;

            var sw = Stopwatch.StartNew();

            try
            {
                if (backend is IStreamingBackend streamingBackend && !disableStreaming)
                {
                    using (var fs = new System.IO.FileStream(localfilename, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read))
                    using (var timeoutStream = new TimeoutObservingStream(fs) { ReadTimeout = readWriteTimeout })
                    using (var ts = new Library.Utility.ThrottledStream(timeoutStream, throttle, throttle))
                    using (var nss = new NonSeekableStream(ts))
                        streamingBackend.PutAsync(remotefilename, nss, timeoutStream.TimeoutToken).Await();
                }
                else
                    backend.PutAsync(remotefilename, localfilename, CancellationToken.None).Await();

                e = null;
            }
            catch (Exception ex)
            {
                e = ex;
            }

            if (e != null)
            {
                Console.WriteLine(LogTimeStamp + $"Failed to upload file {i}, error message: {e}, remote name: {remotefilename} after {sw.ElapsedMilliseconds} ms");
                while (e.InnerException != null)
                {
                    e = e.InnerException;
                    Console.WriteLine(LogTimeStamp + "  Inner exception: {0}", e);
                }
            }
            else
            {
                Console.WriteLine($" done! in {sw.ElapsedMilliseconds} ms");
            }
        }

        private static string CreateRandomRemoteFileName(int min_filename_size, int max_filename_size, string allowedChars, bool trimFilenameSpaces, Random rnd)
        {
            StringBuilder filenameBuilder = new StringBuilder();
            int filenamelen = rnd.Next(min_filename_size, max_filename_size);
            for (int j = 0; j < filenamelen; j++)
                filenameBuilder.Append(allowedChars[rnd.Next(0, allowedChars.Length)]);

            string filename = filenameBuilder.ToString();
            if (trimFilenameSpaces)
                filename = filename.Trim();

            return filename;
        }

        private static string CreateRandomFile(Library.Utility.TempFolder tf, int i, int min_file_size, int max_file_size, Random rnd)
        {
            Console.Write(LogTimeStamp + "Generating file {0}", i);
            string filename = System.IO.Path.Combine(tf, i.ToString());
            using (System.IO.FileStream fs = new System.IO.FileStream(filename, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write))
            {
                //Random size
                byte[] buf = new byte[1024];
                int size = rnd.Next(min_file_size, max_file_size);

                Console.WriteLine(" ({0})", Duplicati.Library.Utility.Utility.FormatSizeString(size));

                while (size > 0)
                {
                    rnd.NextBytes(buf);
                    fs.Write(buf, 0, Math.Min(buf.Length, size));
                    size -= buf.Length;
                }
            }

            return filename;
        }

        private static string LogTimeStamp => $"[{DateTime.Now:HH:mm:ss fff}] ";

        public static IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("reruns", CommandLineArgument.ArgumentType.Integer, "The number of test runs to perform", "A number that describes how many times the test is performed", "5"),
                    new CommandLineArgument("tempdir", CommandLineArgument.ArgumentType.Path, "The path used to store temporary files", "The backend tester will use the system default temp path. You can set this option to choose another path."),
                    new CommandLineArgument("extended-chars", CommandLineArgument.ArgumentType.String, "A list of allowed extended filename chars", "A list of characters besides {a-z, A-Z, 0-9} to use when generating filenames", ExtendedChars),
                    new CommandLineArgument("number-of-files", CommandLineArgument.ArgumentType.Integer, "The number of files to test with", "An integer describing how many files to upload during a test run", "10"),
                    new CommandLineArgument("min-file-size", CommandLineArgument.ArgumentType.Size, "The minimum allowed file size", "File sizes are chosen at random, this value is the lower bound", "1kb"),
                    new CommandLineArgument("max-file-size", CommandLineArgument.ArgumentType.Size, "The maximum allowed file size", "File sizes are chosen at random, this value is the upper bound", "50mb"),
                    new CommandLineArgument("min-filename-length", CommandLineArgument.ArgumentType.Integer, "The minimum allowed filename length", "File name lengths are chosen at random, this value is the lower bound", "5"),
                    new CommandLineArgument("max-filename-length", CommandLineArgument.ArgumentType.Integer, "The minimum allowed filename length", "File name lengths are chosen at random, this value is the upper bound", "80"),
                    new CommandLineArgument("trim-filename-spaces", CommandLineArgument.ArgumentType.Boolean, "Trim whitespace from filenames", "A value that indicates if whitespace should be trimmed from the ends of randomly generated filenames", "false"),
                    new CommandLineArgument("auto-create-folder", CommandLineArgument.ArgumentType.Boolean, "Allow automatic folder creation", "A value that indicates if missing folders are created automatically", "false"),
                    new CommandLineArgument("skip-overwrite-test", CommandLineArgument.ArgumentType.Boolean, "Bypass the overwrite test", "A value that indicates if dummy files should be uploaded prior to uploading the real files", "false"),
                    new CommandLineArgument("auto-clean", CommandLineArgument.ArgumentType.Boolean, "Remove any files found in target folder", "A value that indicates if all files in the target folder should be deleted before starting the first test", "false"),
                    new CommandLineArgument("force", CommandLineArgument.ArgumentType.Boolean, "Activate file deletion", "A value that indicates if existing files should really be deleted when using auto-clean", "false"),
                    new CommandLineArgument("wait-after-upload", CommandLineArgument.ArgumentType.Timespan, "Wait after all uploads", "A value that indicates how long to wait after all files are uploaded, to account for the backends eventual consistency", "0s"),
                    new CommandLineArgument("wait-after-delete", CommandLineArgument.ArgumentType.Timespan, "Wait after all deletes", "A value that indicates how long to wait after each delete operation, to account for the backends eventual consistency", "0s"),
                });
            }
        }
    }
}
