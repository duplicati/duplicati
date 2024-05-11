using System.CommandLine;
using System.Security.Cryptography;

namespace ReleaseBuilder.CreateKey;

public static class Command
{
    public static System.CommandLine.Command Create()
    {
        var passwordOption = SharedOptions.passwordOption;

        var keyfileArgument = new Argument<FileInfo>(
            name: "keyfile",
            description: "Path to keyfile to use for signing release manifests",
            getDefaultValue: () => new FileInfo(Program.Configuration.ConfigFiles.UpdaterKeyfile.FirstOrDefault() ?? "./signkey.key")
        );

        var command = new System.CommandLine.Command("create-key", "Creates a new key for signing releases")
        {
            passwordOption,
            keyfileArgument
        };

        command.SetHandler((password, keyfile) =>
        {
            if (keyfile.Exists)
            {
                Console.WriteLine($"Keyfile already exists at {keyfile.FullName}");
                Program.ReturnCode = 1;
                return;
            }

            var keyfilePassword = string.IsNullOrEmpty(password)
                ? ConsoleHelper.ReadPassword("Enter keyfile password")
                : password;

            var newkey = RSA.Create(2048).ToXmlString(true);
            using (var fs = File.OpenWrite(keyfile.FullName))
            using (var ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(newkey)))
                SharpAESCrypt.SharpAESCrypt.Encrypt(keyfilePassword, ms, fs);

            Console.WriteLine($"Keyfile created at {keyfile.FullName}");
        }, passwordOption, keyfileArgument);

        return command;
    }
}
