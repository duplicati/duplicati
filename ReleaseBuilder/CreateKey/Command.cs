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
            getDefaultValue: () => new FileInfo(Configuration.Create(ReleaseChannel.Debug).ConfigFiles.UpdaterKeyfile.FirstOrDefault() ?? "./signkey.key")
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
