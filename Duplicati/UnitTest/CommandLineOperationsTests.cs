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
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest
{
    public class CommandLineOperationsTests : BasicSetupHelper
    {
        public const string S3_URL = $"https://testfiles.duplicati.com/";

        /// <summary>
        /// The log tag
        /// </summary>
        private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<CommandLineOperationsTests>();

        /// <summary>
        /// The folder that contains all the source data which the test is based on
        /// </summary>
        protected readonly string SOURCEFOLDER = Path.Combine(BASEFOLDER, "data");

        private readonly string zipFilename = "data.zip";
        private string zipFilepath => Path.Combine(BASEFOLDER, this.zipFilename);

        private readonly string zipAlternativeFilename = "data-alternative.zip";
        private string zipAlternativeFilepath => Path.Combine(BASEFOLDER, this.zipAlternativeFilename);

        protected virtual IEnumerable<string> SourceDataFolders
        {
            get
            {
                return
                    from x in systemIO.EnumerateDirectories(SOURCEFOLDER)
                    orderby x
                    select x;
            }
        }

        [SetUp]
        public void SetUp()
        {
            if (!systemIO.FileExists(zipAlternativeFilepath))
            {
                var url = $"{S3_URL}{this.zipFilename}";
                DownloadS3FileIfNewer(zipFilepath, url);
                ZipFileExtractToDirectory(this.zipFilepath, BASEFOLDER);
            }
            else
            {
                ZipFileExtractToDirectory(this.zipAlternativeFilepath, BASEFOLDER);
            }
        }

        private void DownloadS3FileIfNewer(string destinationFilePath, string url, int retries = 5)
            => DownloadS3FileIfNewerAsync(destinationFilePath, url, retries).Await();

        public static async Task DownloadS3FileIfNewerAsync(string destinationFilePath, string url, int retries = 5)
        {
            do
            {
                try
                {
                    using var httpClient = new HttpClient();
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);

                    if (systemIO.FileExists(destinationFilePath))
                        request.Headers.IfModifiedSince = systemIO.FileGetLastWriteTimeUtc(destinationFilePath);

                    using var response = await httpClient.SendAsync(request);

                    if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
                    {
                        Console.WriteLine("File has not been modified since last download.");
                        return;
                    }
                    else
                    {
                        using var tmpFile = new TempFile();
                        Console.WriteLine($"Downloading file from {url} to: {tmpFile}");
                        var contentStream = await response.Content.ReadAsStreamAsync();
                        var fileInfo = new FileInfo(tmpFile);
                        using (var fileStream = fileInfo.OpenWrite())
                            await contentStream.CopyToAsync(fileStream);

                        // After download, check if the file length matches the response length
                        long responseLength = response.Content.Headers.ContentLength ?? 0;
                        long fileLength = new FileInfo(tmpFile).Length;
                        if (responseLength != fileLength)
                            throw new Exception($"Downloaded file length {fileLength} does not match response length {responseLength}");

                        Console.WriteLine($"Download completed, moving to {destinationFilePath}");
                        File.Move(tmpFile, destinationFilePath, true);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    retries--;
                    Console.WriteLine($"Download failed: {ex.Message}");
                    if (retries <= 0)
                        throw;

                    await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                    try
                    {
                        System.Net.Dns.GetHostEntry(new System.Uri(url).Host);
                    }
                    catch (Exception)
                    {
                    }
                    Console.WriteLine($"Retrying download, {retries} retries left.");
                }
            } while (retries > 0);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (systemIO.DirectoryExists(this.SOURCEFOLDER))
            {
                systemIO.DirectoryDelete(this.SOURCEFOLDER, true);
            }
        }

        [Test]
        [Category("BulkData")]
        [Category("BulkNormal")]
        public void RunCommands()
        {
            DoRunCommands(TARGETFOLDER);
        }

        [Test]
        [Category("BulkData")]
        [Category("BulkNoSize")]
        public void RunCommandsWithoutSize()
        {
            Duplicati.Library.DynamicLoader.BackendLoader.AddBackend(new SizeOmittingBackend());
            DoRunCommands(new SizeOmittingBackend().ProtocolKey + "://" + TARGETFOLDER);
        }

        private void DoRunCommands(string target)
        {
            var opts = from n in TestOptions select string.Format("--{0}=\"{1}\"", n.Key, n.Value);
            var backupargs = (new string[] { "backup", target, DATAFOLDER }.Union(opts)).ToArray();

            if (SourceDataFolders == null || SourceDataFolders.Count() < 3)
            {
                ProgressWriteLine($"ERROR: A minimum of 3 data folders are required in {SOURCEFOLDER}.");
                throw new Exception("Failed during initial minimum data folder check");
            }

            foreach (var n in SourceDataFolders)
            {
                var foldername = Path.GetFileName(n);
                var targetfolder = Path.Combine(DATAFOLDER, foldername);
                ProgressWriteLine("Adding folder {0} to source", foldername);

                systemIO.DirectoryMove(n, targetfolder);

                var size = systemIO.EnumerateFiles(targetfolder, "*", SearchOption.AllDirectories).Select(systemIO.FileLength).Sum();

                ProgressWriteLine("Running backup with {0} data added ...", Duplicati.Library.Utility.Utility.FormatSizeString(size));
                using (new Library.Logging.Timer(LOGTAG, "BackupWithDataAdded", string.Format("Backup with {0} data added", Duplicati.Library.Utility.Utility.FormatSizeString(size))))
                    Duplicati.CommandLine.Program.Main(backupargs);

                ProgressWriteLine("Testing data ...");
                using (new Library.Logging.Timer(LOGTAG, "TestRemoteData", "Test remote data"))
                    if (Duplicati.CommandLine.Program.Main((new string[] { "test", target, "all" }.Union(opts)).ToArray()) != 0)
                        throw new Exception("Failed during remote verification");
            }

            ProgressWriteLine("Running unchanged backup ...");
            using (new Library.Logging.Timer(LOGTAG, "UnchangedBackup", "Unchanged backup"))
                Duplicati.CommandLine.Program.Main(backupargs);

            var datafolders = systemIO.EnumerateDirectories(DATAFOLDER);

            var f = datafolders.Skip(datafolders.Count() / 2).First();

            ProgressWriteLine("Renaming folder {0}", Path.GetFileName(f));
            systemIO.DirectoryMove(f, Path.Combine(Path.GetDirectoryName(f), Path.GetFileName(f) + "-renamed"));

            ProgressWriteLine("Running backup with renamed folder...");
            using (new Library.Logging.Timer(LOGTAG, "BackupWithRenamedFolder", "Backup with renamed folder"))
                Duplicati.CommandLine.Program.Main(backupargs);

            datafolders = systemIO.EnumerateDirectories(DATAFOLDER);

            ProgressWriteLine("Deleting data");
            var rm1 = datafolders.First();
            var rm2 = datafolders.Skip(1).First();
            var rm3 = datafolders.Skip(2).First();

            systemIO.DirectoryDelete(rm1, true);
            systemIO.DirectoryDelete(rm2, true);
            var rmfiles = systemIO.EnumerateFiles(rm3, "*", SearchOption.AllDirectories);
            foreach (var n in rmfiles.Take(rmfiles.Count() / 2))
                systemIO.FileDelete(n);

            ProgressWriteLine("Running backup with deleted data...");
            using (new Library.Logging.Timer(LOGTAG, "BackupWithDeletedData", "Backup with deleted data"))
                Duplicati.CommandLine.Program.Main(backupargs);

            ProgressWriteLine("Testing the compare method ...");
            using (new Library.Logging.Timer(LOGTAG, "CompareMethod", "Compare method"))
                Duplicati.CommandLine.Program.Main((new string[] { "compare", target, "0", "1" }.Union(opts)).ToArray());

            for (var i = 0; i < 5; i++)
            {
                ProgressWriteLine("Running backup with changed logfile {0} of {1} ...", i + 1, 5);
                systemIO.FileCopy(LOGFILE, Path.Combine(SOURCEFOLDER, Path.GetFileName(LOGFILE)), true);

                using (new Library.Logging.Timer(LOGTAG, "BackupWithLogfileChange", string.Format("Backup with logfilechange {0}", i + 1)))
                    Duplicati.CommandLine.Program.Main(backupargs);
            }

            ProgressWriteLine("Compacting data ...");
            using (new Library.Logging.Timer(LOGTAG, "Compacting", "Compacting"))
                Duplicati.CommandLine.Program.Main((new string[] { "compact", target, "--small-file-max-count=2" }.Union(opts)).ToArray());


            datafolders = systemIO.EnumerateDirectories(DATAFOLDER);
            var rf = datafolders.Skip(datafolders.Count() - 2).First();

            ProgressWriteLine("Partial restore of {0} ...", Path.GetFileName(rf));
            using (new Library.Logging.Timer(LOGTAG, "PartialRestore", "Partial restore"))
                Duplicati.CommandLine.Program.Main((new string[] { "restore", target, rf + "*", "--restore-path=\"" + RESTOREFOLDER + "\"" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying partial restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfPartialRestore", "Verification of partial restored files"))
                TestUtils.AssertDirectoryTreesAreEquivalent(rf, RESTOREFOLDER, true, "VerificationOfPartialRestore");

            systemIO.DirectoryDelete(RESTOREFOLDER, true);

            ProgressWriteLine("Partial restore of {0} without local db...", Path.GetFileName(rf));
            using (new Library.Logging.Timer(LOGTAG, "PartialRestoreWithoutLocalDb", "Partial restore without local db"))
                Duplicati.CommandLine.Program.Main((new string[] { "restore", target, rf + "*", "--restore-path=\"" + RESTOREFOLDER + "\"", "--no-local-db" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying partial restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfPartialRestore", "Verification of partial restored files"))
                TestUtils.AssertDirectoryTreesAreEquivalent(rf, RESTOREFOLDER, true, "VerificationOfPartialRestore");

            systemIO.DirectoryDelete(RESTOREFOLDER, true);

            ProgressWriteLine("Full restore ...");
            using (new Library.Logging.Timer(LOGTAG, "FullRestore", "Full restore"))
                Duplicati.CommandLine.Program.Main((new string[] { "restore", target, "*", "--restore-path=\"" + RESTOREFOLDER + "\"" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying full restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfFullRestore", "Verification of restored files"))
                foreach (var s in systemIO.EnumerateDirectories(DATAFOLDER))
                    TestUtils.AssertDirectoryTreesAreEquivalent(s, Path.Combine(RESTOREFOLDER, Path.GetFileName(s)), true, "VerificationOfFullRestore");

            systemIO.DirectoryDelete(RESTOREFOLDER, true);

            ProgressWriteLine("Full restore without local db...");
            using (new Library.Logging.Timer(LOGTAG, "FullRestoreWithoutDb", "Full restore without local db"))
                Duplicati.CommandLine.Program.Main((new string[] { "restore", target, "*", "--restore-path=\"" + RESTOREFOLDER + "\"", "--no-local-db" }.Union(opts)).ToArray());

            ProgressWriteLine("Verifying full restore ...");
            using (new Library.Logging.Timer(LOGTAG, "VerificationOfFullRestoreWithoutDb", "Verification of restored files"))
                foreach (var s in systemIO.EnumerateDirectories(DATAFOLDER))
                    TestUtils.AssertDirectoryTreesAreEquivalent(s, Path.Combine(RESTOREFOLDER, Path.GetFileName(s)), true, "VerificationOfFullRestoreWithoutDb");

            ProgressWriteLine("Testing data ...");
            using (new Library.Logging.Timer(LOGTAG, "TestRemoteData", "Test remote data"))
                if (Duplicati.CommandLine.Program.Main((new string[] { "test", target, "all" }.Union(opts)).ToArray()) != 0)
                    throw new Exception("Failed during final remote verification");
        }
    }
}

