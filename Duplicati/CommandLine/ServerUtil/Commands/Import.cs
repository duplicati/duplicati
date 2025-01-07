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

public static class Import
{
    public static Command Create() =>
        new Command("import", "Import a backup configuration")
        {
            new Argument<FileInfo>("file", "The file to import, may be encrypted") {
                Arity = ArgumentArity.ExactlyOne
            },
            new Argument<string>("passphrase", "The passphrase to use for decryption") {
                Arity = ArgumentArity.ZeroOrOne
            },
            new Option<bool>(name: "--import-metadata", description: "Import metadata from the backup", getDefaultValue: () => false)
        }
        .WithHandler(CommandHandler.Create<Settings, FileInfo, string, bool>(async (settings, file, passphrase, importMetadata) =>
        {
            if (!file.Exists)
                throw new UserReportedException($"File {file.FullName} does not exist");

            Console.WriteLine($"Importing backup configuration from {file.FullName}");
            if (IsEncrypted(file))
            {
                if (string.IsNullOrWhiteSpace(passphrase))
                    passphrase = HelperMethods.ReadPasswordFromConsole("The file is encrypted. Please provide the encryption password: ");

                if (string.IsNullOrWhiteSpace(passphrase))
                    throw new UserReportedException("No password provided");

                if (settings.SecretProvider != null)
                {
                    var opts = new Dictionary<string, string?>() { { "password", passphrase } };
                    await settings.ReplaceSecrets(opts).ConfigureAwait(false);
                    passphrase = opts["password"]!;
                }
            }

            var connection = await settings.GetConnection();
            var result = await connection.ImportBackup(file.FullName, passphrase, importMetadata);

            Console.WriteLine($"Imported \"{result.Name}\" with ID {result.ID}");
        }));

    private static bool IsEncrypted(FileInfo file)
    {
        using var fs = file.OpenRead();
        var header = new byte[3].AsSpan();
        if (fs.Read(header) != 3)
            return false;
        return header.SequenceEqual("AES"u8);
    }

}
