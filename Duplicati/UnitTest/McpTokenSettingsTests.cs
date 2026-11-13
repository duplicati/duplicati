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

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Duplicati.WebserverCore.Middlewares;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The rule that MCP tokens only exist while the MCP server is turned on: turning it off
/// deletes them, and none can be issued until it is on again
/// </summary>
[TestFixture]
[Category("Mcp")]
public class McpTokenSettingsTests
{
    private string _dataFolder = null!;
    private Connection _connection = null!;
    private McpTokenStore _store = null!;

    [SetUp]
    public async Task SetUpAsync()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), $"duplicati-mcp-token-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dataFolder);

        var databasePath = Path.Combine(_dataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var database = await SQLiteLoader.LoadConnectionAsync(databasePath).ConfigureAwait(false);
        DatabaseUpgrader.UpgradeDatabase(database, databasePath, typeof(DatabaseSchemaMarker));
        _connection = new Connection(database, true, null, _dataFolder, () => { });

        var jwtConfig = JWTConfig.Create();
        _store = new McpTokenStore(_connection, new JWTTokenProvider(() => jwtConfig));
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        try
        {
            Directory.Delete(_dataFolder, true);
        }
        catch (IOException)
        {
            // The folder is in the temporary folder, where it is removed later
        }
    }

    /// <summary>
    /// Issues a token that can read all backups
    /// </summary>
    /// <param name="name">The name of the token</param>
    /// <returns>The description of the token</returns>
    private McpTokenEntry Issue(string name)
        => _store.Create(name, McpTokenLevel.Read, null, [], TimeSpan.FromDays(1)).Entry;

    [Test]
    public void ServerIsOffUntilItIsTurnedOn()
    {
        Assert.That(_connection.ApplicationSettings.EnableMcpServer, Is.False);
        Assert.That(_store.List(), Is.Empty);
    }

    [Test]
    public void NoTokenIsIssuedWhileTheServerIsOff()
    {
        Assert.That(() => Issue("agent"), Throws.InstanceOf<NotFoundException>().With.Message.EqualTo(McpTokenStore.DisabledMessage));
        Assert.That(_store.List(), Is.Empty);
        Assert.That(_connection.ApplicationSettings.McpTokens, Is.Null.Or.Empty);
    }

    [Test]
    public void TheListIsNotChangedWhileTheServerIsOff()
    {
        var called = false;
        var changed = _connection.ApplicationSettings.UpdateMcpTokens(_ =>
        {
            called = true;
            return "[]";
        });

        Assert.That(changed, Is.False);
        Assert.That(called, Is.False, "The list is not even read while the server is off");
        Assert.That(_connection.ApplicationSettings.McpTokens, Is.Null.Or.Empty);
    }

    [Test]
    public void TokensAreIssuedListedAndDeletedWhileTheServerIsOn()
    {
        _connection.ApplicationSettings.EnableMcpServer = true;

        var first = Issue("first");
        var second = Issue("second");
        Assert.That(_store.List().Select(x => x.Name), Is.EqualTo(new[] { "first", "second" }));
        Assert.That(_store.Get(second.Id)?.Name, Is.EqualTo("second"));

        Assert.That(_store.Delete(first.Id), Is.True);
        Assert.That(_store.Delete(first.Id), Is.False, "A token can only be deleted once");
        Assert.That(_store.List().Select(x => x.Id), Is.EqualTo(new[] { second.Id }));
        Assert.That(_store.Get(first.Id), Is.Null);
    }

    [Test]
    public void TurningTheServerOffDeletesTheTokens()
    {
        _connection.ApplicationSettings.EnableMcpServer = true;
        var token = Issue("agent");

        _connection.ApplicationSettings.EnableMcpServer = false;

        Assert.That(_store.List(), Is.Empty);
        Assert.That(_store.Get(token.Id), Is.Null);
        Assert.That(_store.Delete(token.Id), Is.False);

        // Turning the server on again does not give access back
        _connection.ApplicationSettings.EnableMcpServer = true;
        Assert.That(_store.List(), Is.Empty);
        Assert.That(Issue("new agent").Name, Is.EqualTo("new agent"));
    }

    /// <summary>
    /// The settings page and the server utility change the setting through the list of settings
    /// </summary>
    [TestCase("False")]
    [TestCase("false")]
    [TestCase("")]
    [TestCase(null)]
    public void TurningTheServerOffThroughTheSettingsDeletesTheTokens(string? value)
    {
        _connection.ApplicationSettings.EnableMcpServer = true;
        Issue("agent");

        _connection.ApplicationSettings.UpdateSettings(new() { [Duplicati.Server.Database.ServerSettings.CONST.ENABLE_MCP_SERVER] = value }, false);

        Assert.That(_connection.ApplicationSettings.EnableMcpServer, Is.False);
        Assert.That(_store.List(), Is.Empty);
    }

    [Test]
    public void ChangingOtherSettingsKeepsTheTokens()
    {
        _connection.ApplicationSettings.EnableMcpServer = true;
        var token = Issue("agent");

        _connection.ApplicationSettings.UpdateSettings(new() { [Duplicati.Server.Database.ServerSettings.CONST.STARTUP_DELAY] = "5s" }, false);
        _connection.ApplicationSettings.UpdateSettings(new() { [Duplicati.Server.Database.ServerSettings.CONST.ENABLE_MCP_SERVER] = "True" }, false);
        _connection.ApplicationSettings.EnableMcpServer = true;

        Assert.That(_store.List().Select(x => x.Id), Is.EqualTo(new[] { token.Id }));
    }

    [Test]
    public void TheTokensAreDeletedInTheDatabaseToo()
    {
        _connection.ApplicationSettings.EnableMcpServer = true;
        Issue("agent");
        _connection.ApplicationSettings.ReloadSettings();
        Assert.That(_store.List(), Has.Count.EqualTo(1), "The token is stored, not only kept in memory");

        _connection.ApplicationSettings.EnableMcpServer = false;
        _connection.ApplicationSettings.ReloadSettings();

        Assert.That(_connection.ApplicationSettings.EnableMcpServer, Is.False);
        Assert.That(_store.List(), Is.Empty);
    }
}
