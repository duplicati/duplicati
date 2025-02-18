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

using DotNet.Testcontainers.Images;

namespace Duplicati.Backend.Tests.CIFS;

/// <summary>
/// CIFS Tests
/// </summary>
[TestClass]
public sealed class CIFSTests : BaseSftpgoTest
{

    /// <summary>
    /// Test CIFS with TestContainers creating a Samba Server with TestContainers.
    ///
    /// This test has no requirement of environment variables.
    /// </summary>
    [TestMethod]
    public async Task TestCIFS()
    {
        var outputConsumer = new OutputConsumer();
        var randomPassword = GeneratePassword();
        var testFilePath = Path.Combine(Path.GetTempPath(), "samba-test");
        Directory.CreateDirectory(testFilePath);
        // Create smb.conf
        var smbConfig = @"[global]
workgroup = WORKGROUP
server string = Samba Server
log file = /var/log/samba/log.%m
max log size = 50
security = user
passdb backend = tdbsam

[testshare1]
path = /shares/testshare1
valid users = smbuser1
writable = yes
browseable = yes";

        await File.WriteAllTextAsync(Path.Combine(testFilePath, "smb.conf"), smbConfig);

        // Create entrypoint script
        var entrypoint = $@"#!/bin/bash
useradd -M -s /sbin/nologin smbuser1
mkdir -p /shares/testshare1
chown smbuser1:smbuser1 /shares/testshare1
chmod 700 /shares/testshare1
(echo {randomPassword}; echo {randomPassword}) | smbpasswd -a smbuser1 -s
smbpasswd -e smbuser1
smbd --foreground --no-process-group --debug-stdout";

        await File.WriteAllTextAsync(Path.Combine(testFilePath, "entrypoint.sh"), entrypoint);

        var container = new ContainerBuilder()
            .WithImage("ubuntu:22.04")
            .WithImagePullPolicy(PullPolicy.Missing) 
            .WithCommand("/bin/bash", "-c", "apt-get update && " +
                "DEBIAN_FRONTEND=noninteractive apt-get install -y samba && " +
                "bash /etc/samba/entrypoint.sh")
            .WithResourceMapping(testFilePath, "/etc/samba", UnixFileModes.UserRead |
                UnixFileModes.UserWrite | UnixFileModes.GroupRead | UnixFileModes.OtherRead)
            .WithPortBinding(139, 139)
            .WithPortBinding(445, 445)
            .WithOutputConsumer(outputConsumer)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(139))
            .Build();

        Console.WriteLine("Starting container with wait strategy for port 139");
        await container.StartAsync();
        Console.WriteLine("Samba has started and its ready to accept connections");

        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                $"cifs://localhost/testshare1/new/?transport=directtcp&auth-domain&auth-username=smbuser1&auth-password={randomPassword}",
            }.Concat(Parameters.GlobalTestParameters).ToArray());

        Console.WriteLine(await outputConsumer.GetStreamsOutput());
        if (exitCode != 0)
        {
            Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");
        }

        await container.StopAsync();
    }

    /// <summary>
    ///  Simple helper to generate a random password for the test.
    ///
    /// Only alphanumeric characters are used.
    /// </summary>
    /// <param name="length">Lenght of the password defaulting to 32</param>
    private string GeneratePassword(int length = 32)
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var random = new Random();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[random.Next(s.Length)]).ToArray());
    }
}