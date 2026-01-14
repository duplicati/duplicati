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

using System.IO;
using System.Linq;
using NUnit.Framework;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Library.Main.Database;
using System.Threading.Tasks;
using System.Net.Http;
using System;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest;

public class Issue6705 : BasicSetupHelper
{
    // Copied from CommandLineOperationsTests.cs
    public static async Task DownloadS3FileIfNewerAsync(string destinationFilePath, string url, int retries = 5)
    {
        do
        {
            try
            {
                using var httpClient = new HttpClient(); // Because it's a test unit, will use a new instance created via default constructor.
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

    // The backups were created using the following "generic" script on each OS:
    /*
    git clone git@github.com:duplicati/documentation.git duplicati-docs
    dotnet build
    mkdir cross-os-backup
    Executables/Duplicati.CommandLine/bin/Debug/net10.0/Duplicati.CommandLine backup "file://cross-os-backup/backup" ./duplicati-docs --passphrase=123456 --blocksize=10kb --dblock-size=10mb --dbpath="cross-os-backup/db.sqlite"
    mv duplicati-docs cross-os-backup/original
    zip -r {OS}.zip cross-os-backup
    */

    [Test]
    [Category("Targeted")]
    public async Task RestoreAcrossOperatingSystems(
        [Values("Windows", "Linux", "MacOS")] string original_os,
        [Values(true, false)] bool use_packed_db,
        [Values(true, false)] bool use_legacy_restore,
        [Values(true, false)] bool skip_metadata
    )
    {
        // Download the backup made on different OS
        var url = $"https://testfiles.duplicati.com/issue6705/{original_os}.zip";
        var zipFilepath = Path.Combine(BASEFOLDER, $"{original_os}.zip");
        //await DownloadS3FileIfNewerAsync(zipFilepath, url);
        File.Copy($"~/git/duplicati-carl/{original_os}_large.zip", zipFilepath, true);
        ZipFileExtractToDirectory(zipFilepath, TARGETFOLDER);
        var extractedPath = Path.Combine(TARGETFOLDER, "cross-os-backup");

        var testopts = TestOptions.Expand(new
        {
            // Everything is local at this point, so no need for retries. If there's an issue, fail fast.
            retry_delay = "0",
            number_of_retries = "0",

            restore_path = RESTOREFOLDER,
            restore_legacy = use_legacy_restore,
            skip_metadata = skip_metadata,
        });
        if (use_packed_db)
            testopts["dbpath"] = Path.Combine(extractedPath, "db.sqlite");

        using var c = new Library.Main.Controller($"file://{extractedPath}/backup", testopts, null);
        TestUtils.AssertResults(c.Restore(null));

        // Verify restored files
        TestUtils.AssertDirectoryTreesAreEquivalent(Path.Combine(extractedPath, "original"), RESTOREFOLDER, false, "Restored files do not match original files");
    }

}
