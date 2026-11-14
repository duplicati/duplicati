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

public static class Import
{
    public static Command Create()
    {
        var fileArgument = new Argument<FileInfo>("file")
        {
            Description = "The file to import, may be encrypted",
            Arity = ArgumentArity.ExactlyOne
        };
        var passphraseArgument = new Argument<string?>("passphrase")
        {
            Description = "The passphrase to use for decryption of the configuration file, if encrypted",
            Arity = ArgumentArity.ZeroOrOne
        };
        var importMetadataOption = new Option<bool>("--import-metadata")
        {
            Description = "Import metadata from the backup"
        };
        var backupPassphraseOption = new Option<string>("--backup-passphrase")
        {
            Description = "The passphrase to inject into the backup configuration after import. Use this option if the configuration was exported without secrets.",
            DefaultValueFactory = _ => string.Empty
        };
        var backupUrlOption = new Option<string>("--backup-url")
        {
            Description = "The url to inject into the backup configuration after import. Use this option to replace the storage URL.",
            DefaultValueFactory = _ => string.Empty
        };
        var expandEnvVarsOption = new Option<bool>("--expand-env-vars")
        {
            Description = "Expand %VAR% environment variables in the configuration with the values of the server, so an exported configuration can be used as a template. A variable that is not defined is left as it is."
        };

        var cmd = new Command("import", "Import a backup configuration")
        {
            fileArgument,
            passphraseArgument,
            importMetadataOption,
            backupPassphraseOption,
            backupUrlOption,
            expandEnvVarsOption
        };
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var settings = SettingsBinder.GetSettings(parseResult);
            var output = OutputInterceptorBinder.GetConsoleInterceptor(parseResult);
            var file = parseResult.GetValue(fileArgument)!;
            var passphrase = parseResult.GetValue(passphraseArgument);
            var importMetadata = parseResult.GetValue(importMetadataOption);
            var backupPassphrase = parseResult.GetValue(backupPassphraseOption)!;
            var backupUrl = parseResult.GetValue(backupUrlOption)!;
            var expandEnvVars = parseResult.GetValue(expandEnvVarsOption);

            if (!file.Exists)
                throw new UserReportedException($"File {file.FullName} does not exist");

            output.AppendConsoleMessage($"Importing backup configuration from {file.FullName}");
            if (IsEncrypted(file))
            {
                if (output.JsonOutputMode)
                    throw new UserReportedException("No password provided with json mode.");

                if (string.IsNullOrWhiteSpace(passphrase))
                    passphrase = Library.Utility.Utility.ReadSecretFromConsole("The file is encrypted. Please provide the encryption password: ");

                if (string.IsNullOrWhiteSpace(passphrase))
                    throw new UserReportedException("No password provided");

                if (settings.SecretProvider != null)
                {
                    var opts = new Dictionary<string, string?> { { "password", passphrase } };
                    await settings.ReplaceSecretsAsync(opts).ConfigureAwait(false);
                    passphrase = opts["password"]!;
                }
            }

            var extraSettings = new Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(backupPassphrase))
                extraSettings.Add("settings.passphrase", backupPassphrase);
            if (!string.IsNullOrWhiteSpace(backupUrl))
                extraSettings.Add("targeturl", backupUrl);

            var connection = await settings.GetConnectionAsync(output);
            var result = await connection.ImportBackupAsync(file.FullName, passphrase, importMetadata, extraSettings, expandEnvVars);

            output.AppendConsoleMessage($"Imported \"{result.Name}\" with ID {result.ID}");
            output.AppendCustomObject("Imported", new { Id = result.ID, Name = result.Name });
            output.SetResult(true);
        });
        return cmd;
    }

    private static bool IsEncrypted(FileInfo file)
    {
        using var fs = file.OpenRead();
        var header = new byte[3].AsSpan();
        if (fs.Read(header) != 3)
            return false;
        return header.SequenceEqual("AES"u8);
    }
}
