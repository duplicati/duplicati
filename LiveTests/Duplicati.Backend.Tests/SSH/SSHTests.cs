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

namespace Duplicati.Backend.Tests.SSH;

/// <summary>
/// SSH Tests
/// </summary>
[TestClass]
public sealed class SshTests : BaseSftpgoTest
{
    
    /// <summary>
    /// Tests connecting to SSH using password authentication
    /// </summary>
    [TestMethod]
    public async Task TestSSHWithPassword()
    {

        var outputConsumer = new OutputConsumer();
        var filePermissions = UnixFileModes.UserRead | UnixFileModes.UserWrite | UnixFileModes.UserExecute |
                                        UnixFileModes.GroupRead | UnixFileModes.GroupWrite | UnixFileModes.GroupExecute |
                                        UnixFileModes.OtherRead | UnixFileModes.OtherWrite | UnixFileModes.OtherExecute;

        var temporaryKeysDir = CreateHttpsCertificates();

        CreateUsersFile(temporaryKeysDir);
        
        var container = new ContainerBuilder()
            .WithImage("drakkan/sftpgo")
            .WithEnvironment("SFTPGO_LOG_LEVEL", "debug")
            .WithEnvironment("SFTPGO_LOADDATA_FROM", "/var/lib/sftpgo/users.json")
            .WithEnvironment("SFTPGO_LOADDATA_CLEAN", "0")
            .WithEnvironment("SFTPGO_SFTPD__BINDINGS__0__PORT", "3333")
            .WithPortBinding(3000, 3333)
            .WithResourceMapping(temporaryKeysDir, "/var/lib/sftpgo/", filePermissions)
            .WithOutputConsumer(outputConsumer)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(3333))
            .Build();

        Console.WriteLine("Starting container");
        await container.StartAsync();

        // Once started we will already cleanup temporary directory
        temporaryKeysDir.Delete(true);

        Console.WriteLine("Waiting X seconds");
        await Task.Delay(TimeSpan.FromSeconds(1));
        
       // Console.WriteLine($"TCP Connect testing, @::1 is open = { await IsPortOpenAsync("::1",3000) }");
       // Console.WriteLine($"TCP Connect testing, @localhost is open = { await IsPortOpenAsync("localhost",3000) }");

        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                $"ssh://{TEST_USER_NAME}:{TEST_USER_PASSWORD}@127.0.0.1:3000",
                "--ssh-accept-any-fingerprints"
            }.Concat(Parameters.GlobalTestParameters).ToArray());
        
        if (exitCode != 0)
        {
            Console.WriteLine(await outputConsumer.GetStreamsOutput());
            Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");
        }

        await container.StopAsync();
    }
}