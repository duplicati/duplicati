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
