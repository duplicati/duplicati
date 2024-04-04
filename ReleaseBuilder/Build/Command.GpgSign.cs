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
        /// <param name="rtcfg">The runtime configuration</param>
        /// <returns>An awaitable task</returns>
        public static async Task SignReleaseFiles(IEnumerable<string> files, RuntimeConfig rtcfg)
        {
            var (gpgid, passphrase) = GetGpgIdAndPassphrase(rtcfg);

            foreach (var file in files)
                foreach (var armored in new[] { true, false })
                    await ProcessHelper.Execute(
                        [
                            Program.Configuration.Commands.Gpg!,
                            "--pinentry-mode", "loopback",
                            "--passphrase-fd", "0",
                            "--batch", "--yes",
                            armored ? "--armor" : string.Empty,
                            "-u", gpgid,
                            "--output", file + (armored ? "sig.asc" : ".sig"),
                            "--detach-sign", file
                        ],
                        workingDirectory: Path.GetDirectoryName(file),
                        writeStdIn: (stdin) => stdin.WriteLineAsync(passphrase)
                    );
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
