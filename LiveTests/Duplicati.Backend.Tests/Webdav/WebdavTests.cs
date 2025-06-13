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

namespace Duplicati.Backend.Tests.Webdav;

[TestClass]
public sealed class WebDavTests : BaseSftpgoTest
{

    [TestMethod]
    public async Task TestWebdavHttps()
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
            .WithEnvironment("SFTPGO_WEBDAVD__BINDINGS__0__ENABLE_HTTPS", "1")
            .WithEnvironment("SFTPGO_WEBDAVD__BINDINGS__0__PORT", "8090")
            .WithEnvironment("SFTPGO_WEBDAVD__BINDINGS__0__CERTIFICATE_FILE", "fullchain.pem")
            .WithEnvironment("SFTPGO_WEBDAVD__BINDINGS__0__CERTIFICATE_KEY_FILE", "privkey.pem")
            .WithEnvironment("SFTPGO_LOADDATA_FROM", "/var/lib/sftpgo/users.json")
            .WithEnvironment("SFTPGO_LOADDATA_CLEAN", "0")
            .WithResourceMapping(temporaryKeysDir, "/var/lib/sftpgo/", filePermissions)
            .WithPortBinding(8090, 8090)
            .WithOutputConsumer(outputConsumer)
            .Build();

        Console.WriteLine("Starting container");
        await container.StartAsync();

        // Once started we will already cleanup temporary directory
        temporaryKeysDir.Delete(true);

        Console.WriteLine("Waiting X seconds");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Print logs
        Console.WriteLine("Fetching logs...");
        var containerStartupLogs = await outputConsumer.GetStreamsOutput();
        Console.WriteLine(containerStartupLogs);

        // Running this ignoring the certificate
        var exitCode = CommandLine.BackendTester.Program.Main(
            new[] { $"webdavs://{TEST_USER_NAME}:{TEST_USER_PASSWORD}@localhost:8090/", "--accept-any-ssl-certificate=true" }.Concat(Parameters.GlobalTestParameters).ToArray());

        if (exitCode != 0)
        {
            Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");
        }

        // Running not ignoring the certificate, should return non-zero exit code
        exitCode = CommandLine.BackendTester.Program.Main(
            new[] { $"webdavs://{TEST_USER_NAME}:{TEST_USER_PASSWORD}@localhost:8090/", "--accept-any-ssl-certificate=false" }.Concat(Parameters.GlobalTestParameters).ToArray());

        if (exitCode == 0)
        {
            Assert.Fail("BackendTester is returning zero when it should return non-zero exit code, check logs for details");
        }

        Console.WriteLine("Stopping container");
        await container.StopAsync();

    }
    
    [TestMethod]
    public async Task TestWebdavHttp()
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
            .WithEnvironment("SFTPGO_WEBDAVD__BINDINGS__0__PORT", "8090")
            .WithEnvironment("SFTPGO_LOADDATA_FROM", "/var/lib/sftpgo/users.json")
            .WithEnvironment("SFTPGO_LOADDATA_CLEAN", "0")
            .WithResourceMapping(temporaryKeysDir, "/var/lib/sftpgo/", filePermissions)
            .WithPortBinding(8091, 8090)
            .WithOutputConsumer(outputConsumer)
            .Build();

        Console.WriteLine("Starting container");
        await container.StartAsync();

        // Once started we will already cleanup temporary directory
        temporaryKeysDir.Delete(true);

        Console.WriteLine("Waiting X seconds");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Print logs
        Console.WriteLine("Fetching logs...");
        var containerStartupLogs = await outputConsumer.GetStreamsOutput();
        Console.WriteLine(containerStartupLogs);

        // Running this ignoring the certificate
        var exitCode = CommandLine.BackendTester.Program.Main(
            ["webdav://testuser:testpassword@localhost:8091/"]);

        if (exitCode != 0)
        {
            Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");
        }

        Console.WriteLine("Stopping container");
        await container.StopAsync();

    }
}