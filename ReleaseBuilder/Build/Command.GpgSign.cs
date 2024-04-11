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
                                Program.Configuration.Commands.Gpg!,
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

                        zip.CreateEntryFromFile(outputpath, outputfile);
                        File.Delete(outputpath);
                    }

                // Add information about the signing key
                using (var stream = zip.CreateEntry("sign-key.txt", CompressionLevel.Optimal).Open())
                    stream.Write(System.Text.Encoding.UTF8.GetBytes($"{gpgid}\nhttps://pgp.mit.edu/pks/lookup?op=get&search={gpgid}\n"));
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
            using var fs = File.OpenRead(Program.Configuration.ConfigFiles.GpgKeyfile);
            SharpAESCrypt.SharpAESCrypt.Decrypt(rtcfg.KeyfilePassword, fs, ms);
            var parts = System.Text.Encoding.UTF8.GetString(ms.ToArray()).Split('\n', 2, StringSplitOptions.RemoveEmptyEntries);
            return (parts[0], parts[1]);
        }
    }
}
