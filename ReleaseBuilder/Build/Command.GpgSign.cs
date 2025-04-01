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
using System.IO.Compression;

namespace ReleaseBuilder.Build;

public static partial class Command
{
    /// <summary>
    /// Implementation of the gpg sign command
    /// </summary>
    private static class GpgSign
    {
        /// <summary>
        /// Performs a GPG sign operation on the files
        /// </summary>
        /// <param name="files">The files to sign</param>
        /// <param name="signaturefile">The signature file to create</param>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static async Task SignReleaseFiles(IEnumerable<string> files, string signaturefile, RuntimeConfig rtcfg)
        {
            var tmpfile = signaturefile + ".tmp";
            if (File.Exists(tmpfile))
                File.Delete(tmpfile);

            var (gpgid, passphrase) = GetGpgIdAndPassphrase(rtcfg);
            using (var zip = ZipFile.Open(tmpfile, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                    foreach (var armored in new[] { true, false })
                    {
                        var outputfile = file + (armored ? ".sig.asc" : ".sig");
                        var outputpath = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, outputfile);

                        await ProcessHelper.Execute(
                            [
                                rtcfg.Configuration.Commands.Gpg!,
                                "--pinentry-mode", "loopback",
                                "--passphrase-fd", "0",
                                "--batch", "--yes",
                                armored ? "--armor" : "--no-armor",
                                "-u", gpgid,
                                "--output", outputfile,
                                "--detach-sign", file
                            ],
                            workingDirectory: Path.GetDirectoryName(file),
                            writeStdIn: (stdin) => stdin.WriteLineAsync(passphrase)
                        );

                        zip.CreateEntryFromFile(outputpath, Path.GetFileName(outputfile));
                        File.Delete(outputpath);
                    }

                // Add information about the signing key
                using (var stream = zip.CreateEntry("sign-key.txt", CompressionLevel.Optimal).Open())
                    stream.Write(System.Text.Encoding.UTF8.GetBytes($"{gpgid}\nhttps://keys.openpgp.org/search?q={gpgid}\nhttps://pgp.mit.edu/pks/lookup?op=get&search={gpgid}\n"));
            }

            File.Move(tmpfile, signaturefile, true);
        }

        /// <summary>
        /// Gets the GPG ID and passphrase from the keyfile
        /// </summary>
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>The GPG ID and passphrase</returns>
        static (string GpgId, string GpgPassphrase) GetGpgIdAndPassphrase(RuntimeConfig rtcfg)
        {
            using var ms = new MemoryStream();
            using var fs = File.OpenRead(rtcfg.Configuration.ConfigFiles.GpgKeyfile);
            SharpAESCrypt.SharpAESCrypt.Decrypt(rtcfg.KeyfilePassword, fs, ms);
            var parts = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Split('\n', 2, StringSplitOptions.RemoveEmptyEntries);
            return (parts[0], parts[1]);
        }
    }
}
