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

namespace Duplicati.Backend.Tests.FTP;

/// <summary>
/// FTP Tests
/// </summary>
[TestClass]
public sealed class FtpTests : BaseSftpgoTest
{

    /// <summary>
    /// Test a simple FTP connection, no SSL
    /// </summary>
    /// <returns></returns>
    [TestMethod]
    public async Task TestSimpleFtp()
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
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__PORT", "2121")
            .WithEnvironment("SFTPGO_FTPD__PASSIVE_PORT_RANGE__END", "20999")
            .WithEnvironment("SFTPGO_FTPD__PASSIVE_PORT_RANGE__START", "20000")
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__ACTIVE_CONNECTIONS_SECURITY", "1")
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__CERTIFICATE_FILE", CERTIFICATE_FILE)
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__CERTIFICATE_KEY_FILE", CERTIFICATE_PRIVATE_KEY_FILE)
            .WithResourceMapping(temporaryKeysDir, "/var/lib/sftpgo/", filePermissions)
            .WithPortBinding(2121, 2121)

            .WithOutputConsumer(outputConsumer);

        container = Enumerable.Range(20000, 1001)
            .Aggregate(container, (current, port) => current.WithPortBinding(port, port));

        var builtContainer = container
            .WithOutputConsumer(outputConsumer)
            .Build();

        Console.WriteLine("Starting container");
        await builtContainer.StartAsync();

        // Once started we will already cleanup temporary directory
        temporaryKeysDir.Delete(true);

        Console.WriteLine("Waiting X seconds");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Running this ignoring the certificate
        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                $"ftp://{TEST_USER_NAME}:{TEST_USER_PASSWORD}@localhost:2121/",
            }.Concat(Parameters.GlobalTestParameters).ToArray());

        if (exitCode != 0)
        {
            Console.WriteLine(await outputConsumer.GetStreamsOutput());
            Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");
        }

        await builtContainer.StopAsync();
    }

    /// <summary>
    /// Test FTP connection with SSL/TLS
    /// </summary>
    /// <returns></returns>
    [TestMethod]
    public async Task TestFtpSsl()
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
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__PORT", "2121")
            .WithEnvironment("SFTPGO_FTPD__PASSIVE_PORT_RANGE__END", "22999")
            .WithEnvironment("SFTPGO_FTPD__PASSIVE_PORT_RANGE__START", "22000")
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__ACTIVE_CONNECTIONS_SECURITY", "1")
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__CERTIFICATE_FILE", CERTIFICATE_FILE)
            .WithEnvironment("SFTPGO_FTPD__BINDINGS__0__CERTIFICATE_KEY_FILE", CERTIFICATE_PRIVATE_KEY_FILE)
            .WithResourceMapping(temporaryKeysDir, "/var/lib/sftpgo/", filePermissions)
            .WithPortBinding(2122, 2121)

            .WithOutputConsumer(outputConsumer);

        container = Enumerable.Range(22000, 1000)
            .Aggregate(container, (current, port) => current.WithPortBinding(port, port));

        var builtContainer = container
            .WithOutputConsumer(outputConsumer)
            .Build();

        Console.WriteLine("Starting container");
        await builtContainer.StartAsync();

        // Once started we will already cleanup temporary directory
        temporaryKeysDir.Delete(true);

        Console.WriteLine("Waiting X seconds");
        await Task.Delay(TimeSpan.FromSeconds(5));

        // Running this ignoring the certificate
        var exitCode = CommandLine.BackendTester.Program.Main(
            ["ftp://testuser:testpassword@localhost:2122/", "use-ssl=true"]);

        if (exitCode != 0)
        {
            Console.WriteLine(await outputConsumer.GetStreamsOutput());
            Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");
        }

        await builtContainer.StopAsync();
    }

}