// Copyright (C) 2026, The Duplicati Team
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

namespace Duplicati.CommandLine.ServerUtil.Commands;

public static class Export
{
    public static Command Create()
    {
        var backupsArgument = new Argument<string[]>("backups")
        {
            Description = "The backup id or name to export, use 'all' to export all backups",
            Arity = ArgumentArity.OneOrMore
        };
        var encryptionPassphraseOption = new Option<string?>("--encryption-passphrase")
        {
            Description = "The passphrase to use for encrypting the backup configuration",
            DefaultValueFactory = _ => null
        };
        var exportPasswordsOption = new Option<bool?>("--export-passwords")
        {
            Description = "Flag toggling the inclusion of sensitive values, such as passwords, defaults to true if a passphrase is supplied",
            DefaultValueFactory = _ => null
        };
        var overwriteOption = new Option<bool>("--overwrite")
        {
            Description = "Flag toggling the overwriting of existing files"
        };
        var unencryptedOption = new Option<bool>("--unencrypted")
        {
            Description = "Flag toggling unencrypted export of configurations"
        };
        var destinationOption = new Option<DirectoryInfo>("--destination")
        {
            Description = "The folder where the backup configuration should be exported to",
            DefaultValueFactory = _ => new DirectoryInfo(Directory.GetCurrentDirectory())
        };

        var cmd = new Command("export", "Export a backup configuration")
        {
            backupsArgument,
            encryptionPassphraseOption,
            exportPasswordsOption,
            overwriteOption,
            unencryptedOption,
            destinationOption
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);
            var backups = parseResult.GetValue(backupsArgument) ?? [];
            var encryptionPassphrase = parseResult.GetValue(encryptionPassphraseOption);
            var exportPasswords = parseResult.GetValue(exportPasswordsOption);
            var overwrite = parseResult.GetValue(overwriteOption);
            var unencrypted = parseResult.GetValue(unencryptedOption);
            var destination = parseResult.GetValue(destinationOption)!;

            if (!destination.Exists)
            {
                output.AppendConsoleMessage($"Creating destination folder {destination.FullName}");
                destination.Create();
            }

            var connection = await settings.GetConnectionAsync(output);
            var serverbackups = await connection.ListBackupsAsync();
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
                    output.AppendConsoleMessage("The --export-passwords flag is not set, sensitive keys will not be included in the exported file");
                    exportPasswords = false;
                }
                else if (exportPasswords.Value)
                {
                    output.AppendConsoleMessage("Warning: Exporting unencrypted configurations with sensitive keys included");
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(encryptionPassphrase))
                {
                    if (output.JsonOutputMode)
                        throw new UserReportedException("No passphrase provided in json mode, cannot proceed");
                    encryptionPassphrase = Library.Utility.Utility.ReadSecretFromConsole("Please provide a passphrase to encrypt the backup configuration: ");
                    if (string.IsNullOrWhiteSpace(encryptionPassphrase))
                        throw new UserReportedException("No passphrase provided, use --unencrypted to export unencrypted configurations");
                }

                if (settings.SecretProvider != null)
                {
                    var opts = new Dictionary<string, string?> { { "password", encryptionPassphrase } };
                    await settings.ReplaceSecretsAsync(opts).ConfigureAwait(false);
                    encryptionPassphrase = opts["password"]!;
                }

                exportPasswords ??= true;
            }

            output.AppendConsoleMessage($"Exporting {targetbackups.Length} backup{(targetbackups.Length == 1 ? "" : "s")} to {destination.FullName}");

            List<dynamic> exportedBackups = [];

            foreach (var backup in targetbackups)
            {
                var name = backup.Name;
                foreach (var c in Path.GetInvalidFileNameChars())
                    name = name.Replace(c, '_');

                var file = new FileInfo(Path.Combine(destination.FullName, $"{backup.ID}-{backup.Name}.json{(unencrypted ? "" : ".aes")}"));
                if (file.Exists && !overwrite)
                {
                    output.AppendConsoleMessage($"Skipping existing file {file.FullName}, use --overwrite to force");
                    continue;
                }

                await using (var s = await connection.ExportBackupAsync(backup.ID, encryptionPassphrase, exportPasswords.Value))
                await using (var fs = file.Create())
                    await s.CopyToAsync(fs);
                exportedBackups.Add(new { Id = backup.ID, Name = backup.Name, File = file.FullName });
                output.AppendConsoleMessage($"- Exported to {file.Name}");
            }
            output.AppendCustomObject("ExportedBackups", exportedBackups);
            output.SetResult(true);
        });
        return cmd;
    }
}
