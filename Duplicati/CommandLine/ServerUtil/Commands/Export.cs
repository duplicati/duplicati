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
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Export
{
    public static Command Create() =>
        new Command("export", "Export a backup configuration")
        {
            new Argument<string[]>("backups", "The backup id or name to export, use 'all' to export all backups") {
                Arity = ArgumentArity.OneOrMore
            },

            new Option<string?>(name: "--encryption-passphrase", description: "The passphrase to use for encrypting the backup configuration", getDefaultValue: () => null),
            new Option<bool?>(name: "--export-passwords", description: "Flag toggling the inclusion of sensitive values, such as passwords, defaults to true if a passphrase is supplied", getDefaultValue: () => null),
            new Option<bool>(name: "--overwrite", description: "Flag toggling the overwriting of existing files", getDefaultValue: () => false),
            new Option<bool>(name: "--unencrypted", description: "Flag toggling unencrypted export of configurations", getDefaultValue: () => false),
            new Option<DirectoryInfo>(name: "--destination", description: "The folder where the backup configuration should be exported to", getDefaultValue: () => new DirectoryInfo(Directory.GetCurrentDirectory())),
        }
        .WithHandler(CommandHandler.Create<Settings, string[], string?, bool?, bool, bool, DirectoryInfo>(async (settings, backups, encryptionPassphrase, exportPasswords, overwrite, unencrypted, destination) =>
        {
            if (!destination.Exists)
            {
                Console.WriteLine($"Creating destination folder {destination.FullName}");
                destination.Create();
            }

            var connection = await settings.GetConnection();
            var serverbackups = await connection.ListBackups();
            var includeAllBackups = backups.Any(x => string.Equals(x, "all", StringComparison.OrdinalIgnoreCase));
            var targetbackups = serverbackups.Where(b => includeAllBackups || backups.Any(x => b.Name.Contains(x, StringComparison.OrdinalIgnoreCase)) || backups.Contains(b.ID.ToString())).ToArray();
            if (targetbackups.Length == 0)
                throw new UserReportedException($"No backups found matching: {string.Join(", ", backups)}");

            if (unencrypted)
            {
                if (!string.IsNullOrWhiteSpace(encryptionPassphrase))
                    throw new UserReportedException("The --unencrypted flag cannot be used with a passphrase");

                if (!exportPasswords.HasValue)
                {
                    Console.WriteLine("The --export-passwords flag is not set, sensitive keys will not be included in the exported file");
                    exportPasswords = false;
                }
                else if (exportPasswords.Value)
                {
                    Console.WriteLine("Warning: Exporting unencrypted configurations with sensitive keys included");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(encryptionPassphrase))
                {
                    encryptionPassphrase = HelperMethods.ReadPasswordFromConsole("Please provide a passphrase to encrypt the backup configuration: ");
                    if (string.IsNullOrWhiteSpace(encryptionPassphrase))
                        throw new UserReportedException("No passphrase provided, use --unencrypted to export unencrypted configurations");
                }

                if (settings.SecretProvider != null)
                {
                    var opts = new Dictionary<string, string?>() { { "password", encryptionPassphrase } };
                    await settings.ReplaceSecrets(opts).ConfigureAwait(false);
                    encryptionPassphrase = opts["password"]!;
                }

                if (!exportPasswords.HasValue)
                    exportPasswords = true;
            }

            Console.WriteLine($"Exporting {targetbackups.Length} backup{(targetbackups.Length == 1 ? "" : "s")} to {destination.FullName}");

            foreach (var backup in targetbackups)
            {
                var name = backup.Name;
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');

                var file = new FileInfo(Path.Combine(destination.FullName, $"{backup.ID}-{backup.Name}.json{(unencrypted ? "" : ".aes")}"));
                if (file.Exists && !overwrite)
                {
                    Console.WriteLine($"Skipping existing file {file.FullName}, use --overwrite to force");
                    continue;
                }

                using (var s = await connection.ExportBackup(backup.ID, encryptionPassphrase, exportPasswords.Value))
                using (var fs = file.Create())
                    await s.CopyToAsync(fs);

                Console.WriteLine($"- Exported to {file.Name}");
            }
        }));
}
