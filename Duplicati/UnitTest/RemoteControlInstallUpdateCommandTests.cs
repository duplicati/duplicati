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


#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.RemoteControl;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests that the console can ask the client to install the newest update with the install update control
/// command, and that the client only advertises the capability when it can install updates.
/// </summary>
[TestFixture]
[Category("RemoteControl")]
public class RemoteControlInstallUpdateCommandTests
{
    private string _tempDataFolder = null!;
    private Connection _connection = null!;
    private FakeUpdateInstallService _updates = null!;
    private RemoteControllerHandler _handler = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _tempDataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-rc-installupdate-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDataFolder);

        var databasePath = Path.Combine(_tempDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(dbConnection, true, null, _tempDataFolder, () => { });
        _updates = new FakeUpdateInstallService();

        // The http client, token provider, application settings and queue are only used by other commands
        _handler = new RemoteControllerHandler(_connection, null!, null!, null!, null!, _updates);
    }

    [TearDown]
    public void TearDown()
    {
        _connection?.Dispose();
        try
        {
            if (Directory.Exists(_tempDataFolder))
                Directory.Delete(_tempDataFolder, true);
        }
        catch
        {
            // Best effort cleanup
        }
    }

    private async Task<ControlResponseMessage> SendInstallUpdateAsync()
    {
        ControlResponseMessage? response = null;
        await _handler.OnControlAsync(new KeepRemoteConnection.ControlMessage(
            new ControlRequestMessage(ControlRequestMessage.InstallUpdateCommand, new Dictionary<string, string?>()),
            r => { response = r; return true; },
            null));

        Assert.IsNotNull(response, "The control message should be answered");
        return response!;
    }

    [Test]
    public async Task ConnectAdvertisesUpdateInstallWhenSupported()
    {
        var metadata = await _handler.OnConnectAsync(new Dictionary<string, string?>());
        Assert.AreEqual("true", metadata[RemoteControllerHandler.UpdateInstallFeatureKey]);
    }

    [Test]
    public async Task ConnectDoesNotAdvertiseUpdateInstallWhenUnsupported()
    {
        _updates.Support = new UpdateInstallSupport(false, "Docker images are updated by pulling a new image", "docker");

        var metadata = await _handler.OnConnectAsync(new Dictionary<string, string?>());
        Assert.IsFalse(metadata.ContainsKey(RemoteControllerHandler.UpdateInstallFeatureKey));
    }

    [Test]
    public async Task ConnectDoesNotAdvertiseUpdateInstallWhenConsoleControlIsDisabled()
    {
        _connection.ApplicationSettings.DisableConsoleControl = true;

        var metadata = await _handler.OnConnectAsync(new Dictionary<string, string?>());
        Assert.IsFalse(metadata.ContainsKey(RemoteControllerHandler.UpdateInstallFeatureKey));
    }

    [TestCase(ConsoleUpdateInstallStatus.Installing, "2.3.0.0", "installing")]
    [TestCase(ConsoleUpdateInstallStatus.WaitingForIdle, "2.3.0.0", "waitingforidle")]
    [TestCase(ConsoleUpdateInstallStatus.UpToDate, "2.2.0.1", "uptodate")]
    public async Task ReportsWhatTheUpdateServiceDid(ConsoleUpdateInstallStatus status, string version, string expectedStatus)
    {
        _updates.Result = new ConsoleUpdateInstallResult(status, version);

        var response = await SendInstallUpdateAsync();

        Assert.IsTrue(response.Success, response.ErrorMessage ?? "The control message should succeed");
        Assert.AreEqual(1, _updates.ConsoleRequests);
        Assert.AreEqual(expectedStatus, response.Output![ControlResponseMessage.UpdateStatusKey]);
        Assert.AreEqual(version, response.Output![ControlResponseMessage.UpdateVersionKey]);
    }

    [Test]
    public async Task ReportsFailureWhenTheUpdateCannotBeInstalled()
    {
        _updates.Result = null;
        _updates.RequestError = new InvalidOperationException("The install of 2.3.0.0 failed recently");

        var response = await SendInstallUpdateAsync();

        Assert.IsFalse(response.Success);
        Assert.AreEqual("The install of 2.3.0.0 failed recently", response.ErrorMessage);
    }

    [Test]
    public async Task RefusesWhenConsoleControlIsDisabled()
    {
        _connection.ApplicationSettings.DisableConsoleControl = true;

        var response = await SendInstallUpdateAsync();

        Assert.IsFalse(response.Success);
        Assert.AreEqual(0, _updates.ConsoleRequests);
    }
}
