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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Duplicati.Server;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Mcp;
using Duplicati.WebserverCore.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests of the optional MCP endpoint, driven with the MCP client over HTTP
/// </summary>
public partial class ServerApiIntegrationTests
{
    /// <summary>
    /// The server argument that turns the MCP endpoint on
    /// </summary>
    private static readonly string[] EnableMcpArgs = [$"--{WebServerLoader.OPTION_WEBSERVICE_ENABLE_MCP_SERVER}=true"];

    /// <summary>
    /// A minimal MCP request, enough to get past the checks in front of the endpoint
    /// </summary>
    private const string McpPingRequest = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}";

    /// <summary>
    /// A server that has not turned the MCP endpoint on answers as if it did not exist, also for a logged in caller
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpEndpointIsNotFoundUnlessEnabled_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            using var response = await PostMcpAsync(httpClient, true, null).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            // No token can be issued for an endpoint that is off, as it would start to work when the endpoint is turned on
            using (var refused = await httpClient.PostAsJsonAsync("/api/v2/mcp/tokens", new CreateMcpTokenInputDto("agent", McpTokenLevel.Read, null, null, null), JsonOptions).ConfigureAwait(false))
                Assert.That(await ReadV2StatusCodeAsync(refused).ConfigureAwait(false), Is.EqualTo("HTTP-404"));
            Assert.That(await ListMcpTokensAsync(httpClient).ConfigureAwait(false), Is.Empty);

            // The capability is reported also while the endpoint is off, so a client can offer the switch
            var extensions = await GetApiExtensionsAsync(httpClient).ConfigureAwait(false);
            Assert.That(extensions, Does.Contain(SystemInfoProvider.McpServerExtension),
                "System info should advertise the MCP endpoint");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The enabled MCP endpoint needs the credentials of the API, and rejects requests a web page on another site makes
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpEndpointRequiresCredentialsAndAKnownOrigin_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            using (var response = await PostMcpAsync(httpClient, false, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "A request without credentials must be refused");

            using (var response = await PostMcpAsync(httpClient, true, "https://evil.example.com").ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "A request from a foreign origin must be refused");

            using (var response = await PostMcpAsync(httpClient, true, $"http://127.0.0.1:{httpClient.BaseAddress!.Port}").ConfigureAwait(false))
                Assert.That(response.IsSuccessStatusCode, Is.True, $"A request from the server's own origin must be served, got {(int)response.StatusCode}");

            using (var response = await PostMcpAsync(httpClient, true, null).ConfigureAwait(false))
                Assert.That(response.IsSuccessStatusCode, Is.True, $"A request with credentials must be served, got {(int)response.StatusCode}");
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// A tool call that is refused answers with the reason alone, a call without a required argument names it,
    /// and neither is written to the server log as a failure
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpToolRefusalGivesTheReasonAndIsNotLoggedAsAFailure_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            using var captured = new CapturingLoggerProvider();
            ((ILoggerFactory)Duplicati.Server.Program.DuplicatiWebserver.Provider.GetService(typeof(ILoggerFactory))!).AddProvider(captured);

            await using var mcp = await ConnectMcpAsync(httpClient).ConfigureAwait(false);
            await CallMcpAsync(mcp, "list_backups", []).ConfigureAwait(false);

            Assert.That(await CallMcpForErrorAsync(mcp, "run_backup", new() { ["backup"] = "missing" }).ConfigureAwait(false),
                Is.EqualTo("No backup has the ID or name \"missing\". Use list_backups to find the ID."));
            Assert.That(await CallMcpForErrorAsync(mcp, "get_task", new() { ["taskId"] = 999999 }).ConfigureAwait(false),
                Is.EqualTo("No such task found"));

            Assert.That(await CallMcpForErrorAsync(mcp, "run_backup", []).ConfigureAwait(false),
                Is.EqualTo("The tool run_backup needs the argument \"backup\"."));
            Assert.That(await CallMcpForErrorAsync(mcp, "restore", []).ConfigureAwait(false),
                Is.EqualTo("The tool restore needs the arguments \"backup\", \"overwrite\"."));

            Assert.That(captured.Entries.Where(x => x.Contains("ModelContextProtocol")), Is.Empty,
                "Calls that are served or refused as intended should leave nothing in the server log");
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// Collects what the web server logs
    /// </summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        /// <summary>
        /// The log entries, each with its level and category
        /// </summary>
        public ConcurrentQueue<string> Entries { get; } = new();

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, Entries);

        /// <inheritdoc />
        public void Dispose() { }

        private sealed class CapturingLogger(string category, ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
        }
    }

    /// <summary>
    /// A backup that has not run has no versions, which is an answer and not an error:
    /// asking leaves no error on the backup for the user to find
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpBackupThatHasNotRunHasNoVersions_Async()
    {
        await File.WriteAllTextAsync(Path.Combine(this.DATAFOLDER, "sample.txt"), "sample").ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, "mcp-secret-passphrase").ConfigureAwait(false);
            await using var mcp = await ConnectMcpAsync(httpClient).ConfigureAwait(false);

            var (versions, _) = await CallMcpAsync(mcp, "list_versions", new() { ["backup"] = backupId }).ConfigureAwait(false);
            Assert.That(versions.GetProperty("versions").GetArrayLength(), Is.EqualTo(0));

            Assert.That(await CallMcpForErrorAsync(mcp, "list_files", new() { ["backup"] = backupId }).ConfigureAwait(false),
                Does.Contain("has no versions yet. Run it with run_backup first."));
            Assert.That(await CallMcpForErrorAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*" }).ConfigureAwait(false),
                Does.Contain("has no versions yet. Run it with run_backup first."));

            var (backups, _) = await CallMcpAsync(mcp, "list_backups", []).ConfigureAwait(false);
            var listed = backups.GetProperty("backups").EnumerateArray().Single(x => x.GetProperty("id").GetString() == backupId);
            Assert.That(listed.TryGetProperty("lastErrorMessage", out var lastError), Is.False, $"Asking for the versions was recorded as an error: {lastError}");
            Assert.That(listed.TryGetProperty("lastErrorDate", out _), Is.False);
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// What the tools report reads the same way everywhere: a backup is a backup while it waits and when it has finished,
    /// times have one format, and a list says how much there is on its first page and ends with a page that is not full
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpReportsTimesOperationsAndPagesConsistently_Async()
    {
        foreach (var name in new[] { "a.txt", "b.txt", "c.txt" })
            await File.WriteAllTextAsync(Path.Combine(this.DATAFOLDER, name), name).ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, "mcp-secret-passphrase").ConfigureAwait(false);
            await using var mcp = await ConnectMcpAsync(httpClient).ConfigureAwait(false);

            // A paused server keeps the task in the queue, where it is named by its operation
            (await httpClient.PostAsync("/api/v1/serverstate/pause", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
            var (started, _) = await CallMcpAsync(mcp, "run_backup", new() { ["backup"] = backupId }).ConfigureAwait(false);
            var taskId = started.GetProperty("taskId").GetInt64();
            Assert.That(started.GetProperty("operation").GetString(), Is.EqualTo("Backup"));

            var (status, _) = await CallMcpAsync(mcp, "get_server_status", []).ConfigureAwait(false);
            Assert.That(status.GetProperty("queue").EnumerateArray().Single(x => x.GetProperty("taskId").GetInt64() == taskId).GetProperty("operation").GetString(), Is.EqualTo("Backup"));

            (await httpClient.PostAsync("/api/v1/serverstate/resume", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
            var finished = await WaitForMcpTaskAsync(mcp, taskId).ConfigureAwait(false);
            Assert.That(finished.GetProperty("status").GetString(), Is.EqualTo("Completed"), finished.ToString());
            Assert.That(finished.GetProperty("operation").GetString(), Is.EqualTo("Backup"));
            var restorePoint = finished.GetProperty("restorePoint").GetProperty("time").GetString();

            // The times of the backup are written like the time of the restore point
            var (backups, _) = await CallMcpAsync(mcp, "list_backups", []).ConfigureAwait(false);
            var listed = backups.GetProperty("backups").EnumerateArray().Single(x => x.GetProperty("id").GetString() == backupId);
            Assert.That(listed.GetProperty("lastBackupStarted").GetString(), Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
            Assert.That(listed.GetProperty("lastBackupFinished").GetString(), Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
            Assert.That(restorePoint, Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
            Assert.That(listed.GetProperty("versionCount").GetInt64(), Is.EqualTo(1));

            // The top level is a single page, whatever page size is asked for
            var (top, _) = await CallMcpAsync(mcp, "list_files", new() { ["backup"] = backupId, ["pageSize"] = 50 }).ConfigureAwait(false);
            var folder = top.GetProperty("entries").EnumerateArray().Single().GetProperty("path").GetString();
            Assert.That(top.GetProperty("page").GetInt32(), Is.EqualTo(0));
            Assert.That(top.GetProperty("pageSize").GetInt32(), Is.EqualTo(50));
            Assert.That(top.GetProperty("totalCount").GetInt64(), Is.EqualTo(1));

            var (afterTop, _) = await CallMcpAsync(mcp, "list_files", new() { ["backup"] = backupId, ["page"] = 1 }).ConfigureAwait(false);
            Assert.That(afterTop.GetProperty("entries").GetArrayLength(), Is.EqualTo(0), "Reading the top level page by page must come to an end");
            Assert.That(afterTop.GetProperty("page").GetInt32(), Is.EqualTo(1));

            // The three files are two pages of two: the total comes with the first, and the second is not full
            var (first, _) = await CallMcpAsync(mcp, "list_files", new() { ["backup"] = backupId, ["path"] = folder, ["pageSize"] = 2 }).ConfigureAwait(false);
            Assert.That(first.GetProperty("entries").GetArrayLength(), Is.EqualTo(2));
            Assert.That(first.GetProperty("pageSize").GetInt32(), Is.EqualTo(2));
            Assert.That(first.GetProperty("totalCount").GetInt64(), Is.EqualTo(3));

            var (second, _) = await CallMcpAsync(mcp, "list_files", new() { ["backup"] = backupId, ["path"] = folder, ["pageSize"] = 2, ["page"] = 1 }).ConfigureAwait(false);
            Assert.That(second.GetProperty("entries").GetArrayLength(), Is.EqualTo(1));
            Assert.That(second.GetProperty("page").GetInt32(), Is.EqualTo(1));
            Assert.That(second.TryGetProperty("totalCount", out var uncounted), Is.False, $"A total that was not counted must be left out, got {uncounted}");

            var (found, _) = await CallMcpAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*.txt", ["pageSize"] = 2 }).ConfigureAwait(false);
            Assert.That(found.GetProperty("totalCount").GetInt64(), Is.EqualTo(3));
            var (foundMore, _) = await CallMcpAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*.txt", ["pageSize"] = 2, ["page"] = 1 }).ConfigureAwait(false);
            Assert.That(foundMore.GetProperty("entries").GetArrayLength(), Is.EqualTo(1));
            Assert.That(foundMore.TryGetProperty("totalCount", out _), Is.False);
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// The scenario the endpoint exists for: run a backup, wait for it, change files, restore what the backup captured
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpBackupWaitChangeRestore_Async()
    {
        var passphrase = "mcp-secret-passphrase";
        var keptFile = Path.Combine(this.DATAFOLDER, "kept.txt");
        var deletedFile = Path.Combine(this.DATAFOLDER, "sub", "deleted.txt");
        var addedFile = Path.Combine(this.DATAFOLDER, "added-later.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(deletedFile)!);
        await File.WriteAllTextAsync(keptFile, "original").ConfigureAwait(false);
        await File.WriteAllTextAsync(deletedFile, "to be deleted").ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupName = $"MCP backup {Guid.NewGuid():N}";
            var backupId = await CreateBackupAsync(httpClient, passphrase, backupName).ConfigureAwait(false);

            await using var mcp = await ConnectMcpAsync(httpClient).ConfigureAwait(false);

            var tools = (await mcp.ListToolsAsync().ConfigureAwait(false)).Select(x => x.Name).ToList();
            Assert.That(tools, Is.EquivalentTo(new[] { "get_server_status", "list_backups", "run_backup", "get_task", "wait_for_task", "stop_task", "list_versions", "list_files", "search_files", "restore" }));

            // The backup is listed, without anything that gives access to its data
            var (backups, backupsText) = await CallMcpAsync(mcp, "list_backups", []).ConfigureAwait(false);
            var listed = backups.GetProperty("backups").EnumerateArray().Single(x => x.GetProperty("id").GetString() == backupId);
            Assert.That(listed.GetProperty("name").GetString(), Is.EqualTo(backupName));
            Assert.That(listed.GetProperty("sources").EnumerateArray().Select(x => x.GetString()), Does.Contain(this.DATAFOLDER));
            Assert.That(listed.GetProperty("destinationType").GetString(), Is.EqualTo("file"));
            Assert.That(backupsText, Does.Not.Contain(passphrase));
            Assert.That(backupsText, Does.Not.Contain(this.TARGETFOLDER));

            // Run the backup by name and wait for it
            var (started, _) = await CallMcpAsync(mcp, "run_backup", new() { ["backup"] = backupName }).ConfigureAwait(false);
            Assert.That(started.GetProperty("alreadyQueued").GetBoolean(), Is.False);
            var backupTask = await WaitForMcpTaskAsync(mcp, started.GetProperty("taskId").GetInt64()).ConfigureAwait(false);
            Assert.That(backupTask.GetProperty("status").GetString(), Is.EqualTo("Completed"), backupTask.ToString());
            Assert.That(backupTask.GetProperty("result").GetProperty("parsedResult").ToString(), Is.EqualTo("Success").Or.EqualTo("Warning"), backupTask.ToString());
            Assert.That(backupTask.GetProperty("result").GetProperty("backup").GetProperty("addedFiles").GetInt64(), Is.EqualTo(2));
            var restoreTime = backupTask.GetProperty("restorePoint").GetProperty("time").GetString()!;

            // The version and its content can be found
            var (versions, _) = await CallMcpAsync(mcp, "list_versions", new() { ["backup"] = backupId }).ConfigureAwait(false);
            Assert.That(versions.GetProperty("versions").EnumerateArray().Select(x => x.GetProperty("time").GetString()), Is.EqualTo(new[] { restoreTime }));
            var (found, _) = await CallMcpAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*deleted.txt" }).ConfigureAwait(false);
            Assert.That(found.GetProperty("entries").EnumerateArray().Select(x => x.GetProperty("path").GetString()), Is.EqualTo(new[] { deletedFile }));
            var (listing, _) = await CallMcpAsync(mcp, "list_files", new() { ["backup"] = backupId, ["path"] = Library.Common.IO.Util.AppendDirSeparator(this.DATAFOLDER) }).ConfigureAwait(false);
            Assert.That(listing.GetProperty("entries").EnumerateArray().Select(x => x.GetProperty("path").GetString()), Does.Contain(keptFile));

            // The risky work: change a file, delete one, add one
            await File.WriteAllTextAsync(keptFile, "changed").ConfigureAwait(false);
            File.Delete(deletedFile);
            await File.WriteAllTextAsync(addedFile, "added").ConfigureAwait(false);

            // A second backup captures the changes, and the first task keeps reporting the version it made
            var (second, _) = await CallMcpAsync(mcp, "run_backup", new() { ["backup"] = backupId }).ConfigureAwait(false);
            var secondTask = await WaitForMcpTaskAsync(mcp, second.GetProperty("taskId").GetInt64()).ConfigureAwait(false);
            Assert.That(secondTask.GetProperty("status").GetString(), Is.EqualTo("Completed"), secondTask.ToString());
            Assert.That(secondTask.GetProperty("restorePoint").GetProperty("time").GetString(), Is.Not.EqualTo(restoreTime));
            Assert.That(secondTask.GetProperty("restorePoint").GetProperty("version").GetInt64(), Is.EqualTo(0));
            var (firstTask, _) = await CallMcpAsync(mcp, "get_task", new() { ["taskId"] = started.GetProperty("taskId").GetInt64() }).ConfigureAwait(false);
            Assert.That(firstTask.GetProperty("restorePoint").GetProperty("time").GetString(), Is.EqualTo(restoreTime), "A later backup must not change the restore point of a task");
            Assert.That(firstTask.GetProperty("restorePoint").GetProperty("version").GetInt64(), Is.EqualTo(1));

            // A search looks in one version: the newest, or the one the time selects
            var (newestKept, _) = await CallMcpAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*kept.txt" }).ConfigureAwait(false);
            Assert.That(newestKept.GetProperty("entries").EnumerateArray().Select(x => x.GetProperty("version").GetInt64()), Is.EqualTo(new[] { 0L }));
            var (newestDeleted, _) = await CallMcpAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*deleted.txt" }).ConfigureAwait(false);
            Assert.That(newestDeleted.GetProperty("entries").GetArrayLength(), Is.EqualTo(0), "A file deleted before the newest version must not be found in it");
            var (olderDeleted, _) = await CallMcpAsync(mcp, "search_files", new() { ["backup"] = backupId, ["filter"] = "*deleted.txt", ["time"] = restoreTime }).ConfigureAwait(false);
            Assert.That(olderDeleted.GetProperty("entries").EnumerateArray().Select(x => x.GetProperty("version").GetInt64()), Is.EqualTo(new[] { 1L }));

            // A time after the newest version selects that version, and a time that is not a time is explained
            var (later, _) = await CallMcpAsync(mcp, "list_files", new() { ["backup"] = backupId, ["path"] = Library.Common.IO.Util.AppendDirSeparator(this.DATAFOLDER), ["time"] = "2100-01-01T00:00:00Z" }).ConfigureAwait(false);
            Assert.That(later.GetProperty("entries").EnumerateArray().Select(x => x.GetProperty("path").GetString()), Does.Contain(addedFile));
            Assert.That(await CallMcpForErrorAsync(mcp, "list_files", new() { ["backup"] = backupId, ["time"] = "2000-01-01T00:00:00Z" }).ConfigureAwait(false), Does.Contain("no version"));
            Assert.That(await CallMcpForErrorAsync(mcp, "list_files", new() { ["backup"] = backupId, ["time"] = "not-a-time" }).ConfigureAwait(false), Does.Contain("not understood"));
            Assert.That(await CallMcpForErrorAsync(mcp, "restore", new() { ["backup"] = backupId, ["overwrite"] = true, ["time"] = "not-a-time" }).ConfigureAwait(false), Does.Contain("not understood"));

            // A restore from before the first version is refused, as it would otherwise restore the newest version
            Assert.That(await CallMcpForErrorAsync(mcp, "restore", new() { ["backup"] = backupId, ["overwrite"] = true, ["time"] = "2000-01-01T00:00:00Z" }).ConfigureAwait(false), Does.Contain("no version"));

            // Restore to the original location, from the version the backup reported
            var (restore, _) = await CallMcpAsync(mcp, "restore", new() { ["backup"] = backupId, ["overwrite"] = true, ["time"] = restoreTime }).ConfigureAwait(false);
            var restoreTask = await WaitForMcpTaskAsync(mcp, restore.GetProperty("taskId").GetInt64()).ConfigureAwait(false);
            Assert.That(restoreTask.GetProperty("status").GetString(), Is.EqualTo("Completed"), restoreTask.ToString());
            Assert.That(restoreTask.GetProperty("result").GetProperty("restore").GetProperty("restoredFiles").GetInt64(), Is.EqualTo(2));

            Assert.That(await File.ReadAllTextAsync(keptFile).ConfigureAwait(false), Is.EqualTo("original"));
            Assert.That(await File.ReadAllTextAsync(deletedFile).ConfigureAwait(false), Is.EqualTo("to be deleted"));
            Assert.That(await File.ReadAllTextAsync(addedFile).ConfigureAwait(false), Is.EqualTo("added"), "A restore must leave files created after the backup in place");

            // Errors meant for the agent arrive as tool errors with a readable message
            var unknown = await mcp.CallToolAsync("run_backup", new Dictionary<string, object?> { ["backup"] = "no-such-backup" }).ConfigureAwait(false);
            Assert.That(unknown.IsError, Is.True);
            Assert.That(string.Join("\n", unknown.Content.OfType<TextContentBlock>().Select(x => x.Text)), Does.Contain("list_backups"));
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// A wait that times out is a normal answer saying the task is not finished, and a second
    /// request to run a queued backup returns the task that is already there
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpWaitReturnsUnfinishedTaskAtTimeout_Async()
    {
        await File.WriteAllTextAsync(Path.Combine(this.DATAFOLDER, "sample.txt"), "sample").ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, "mcp-secret-passphrase").ConfigureAwait(false);
            await using var mcp = await ConnectMcpAsync(httpClient).ConfigureAwait(false);

            // A paused server keeps the task in the queue
            (await httpClient.PostAsync("/api/v1/serverstate/pause", null).ConfigureAwait(false)).EnsureSuccessStatusCode();

            var (started, _) = await CallMcpAsync(mcp, "run_backup", new() { ["backup"] = backupId }).ConfigureAwait(false);
            var taskId = started.GetProperty("taskId").GetInt64();

            var (again, _) = await CallMcpAsync(mcp, "run_backup", new() { ["backup"] = backupId }).ConfigureAwait(false);
            Assert.That(again.GetProperty("taskId").GetInt64(), Is.EqualTo(taskId));
            Assert.That(again.GetProperty("alreadyQueued").GetBoolean(), Is.True);

            var (waited, _) = await CallMcpAsync(mcp, "wait_for_task", new() { ["taskId"] = taskId, ["timeoutSeconds"] = 1 }).ConfigureAwait(false);
            Assert.That(waited.GetProperty("finished").GetBoolean(), Is.False);
            Assert.That(waited.GetProperty("status").GetString(), Is.EqualTo("Waiting"));
            Assert.That(waited.GetProperty("serverPaused").GetBoolean(), Is.True);

            var (status, _) = await CallMcpAsync(mcp, "get_server_status", []).ConfigureAwait(false);
            Assert.That(status.GetProperty("paused").GetBoolean(), Is.True);
            Assert.That(status.GetProperty("queue").EnumerateArray().Select(x => x.GetProperty("taskId").GetInt64()), Does.Contain(taskId));

            (await httpClient.PostAsync("/api/v1/serverstate/resume", null).ConfigureAwait(false)).EnsureSuccessStatusCode();

            var finished = await WaitForMcpTaskAsync(mcp, taskId).ConfigureAwait(false);
            Assert.That(finished.GetProperty("status").GetString(), Is.EqualTo("Completed"), finished.ToString());
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// An MCP token can be issued, listed and deleted through the API. It opens the MCP endpoint and
    /// nothing else, and stops working when it is deleted.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpTokenIsIssuedListedAndRevokedThroughTheApi_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var created = await CreateMcpTokenAsync(httpClient, new CreateMcpTokenInputDto("test agent", McpTokenLevel.Read, null, null, "1h")).ConfigureAwait(false);
            Assert.That(created.Token.Name, Is.EqualTo("test agent"));
            Assert.That(created.Token.Level, Is.EqualTo(McpTokenLevel.Read));
            Assert.That(created.Token.Expires, Is.EqualTo(DateTime.UtcNow.AddHours(1)).Within(TimeSpan.FromMinutes(1)));

            var listed = await ListMcpTokensAsync(httpClient).ConfigureAwait(false);
            Assert.That(listed.Select(x => x.Id), Is.EqualTo(new[] { created.Token.Id }));

            using var tokenClient = CreateBearerClient(httpClient, created.AccessToken);

            // The token is refused by the API, also by the endpoints that issue tokens
            using (var response = await tokenClient.GetAsync("/api/v1/backups").ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            using (var response = await tokenClient.GetAsync("/api/v2/mcp/tokens").ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            // It is accepted by the MCP endpoint
            await using (var mcp = await ConnectMcpAsync(tokenClient).ConfigureAwait(false))
                await CallMcpAsync(mcp, "get_server_status", []).ConfigureAwait(false);

            // Invalid requests are refused
            foreach (var invalid in new[]
            {
                new CreateMcpTokenInputDto("", McpTokenLevel.Read, null, null, null),
                new CreateMcpTokenInputDto("agent", McpTokenLevel.Read, ["no-such-backup"], null, null),
                new CreateMcpTokenInputDto("agent", McpTokenLevel.Backup, null, [BASEFOLDER], null),
                new CreateMcpTokenInputDto("agent", McpTokenLevel.Restore, null, ["relative/path"], null),
                new CreateMcpTokenInputDto("agent", McpTokenLevel.Read, null, null, "not-a-time")
            })
                using (var response = await httpClient.PostAsJsonAsync("/api/v2/mcp/tokens", invalid, JsonOptions).ConfigureAwait(false))
                    Assert.That(await ReadV2StatusCodeAsync(response).ConfigureAwait(false), Is.EqualTo("HTTP-400"), $"Expected {invalid} to be refused");

            // A deleted token stops working at once
            using (var response = await httpClient.DeleteAsync($"/api/v2/mcp/token/{created.Token.Id}").ConfigureAwait(false))
                Assert.That(await ReadV2StatusCodeAsync(response).ConfigureAwait(false), Is.EqualTo("OK"));
            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            using (var response = await httpClient.DeleteAsync($"/api/v2/mcp/token/{created.Token.Id}").ConfigureAwait(false))
                Assert.That(await ReadV2StatusCodeAsync(response).ConfigureAwait(false), Is.EqualTo("HTTP-404"));
            Assert.That(await ListMcpTokensAsync(httpClient).ConfigureAwait(false), Is.Empty);
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// Turning the MCP endpoint off deletes the tokens, so turning it on again does not give
    /// the agents that had access their access back, and no token can be issued while it is off
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task TurningTheMcpServerOffRemovesTheTokens_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var request = new CreateMcpTokenInputDto("test agent", McpTokenLevel.Read, null, null, null);
            var created = await CreateMcpTokenAsync(httpClient, request).ConfigureAwait(false);
            using var tokenClient = CreateBearerClient(httpClient, created.AccessToken);

            // Saving the settings with the endpoint still on keeps the tokens
            await SetMcpServerEnabledAsync(httpClient, true).ConfigureAwait(false);
            Assert.That((await ListMcpTokensAsync(httpClient).ConfigureAwait(false)).Select(x => x.Id), Is.EqualTo(new[] { created.Token.Id }));
            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            await SetMcpServerEnabledAsync(httpClient, false).ConfigureAwait(false);
            Assert.That(await ListMcpTokensAsync(httpClient).ConfigureAwait(false), Is.Empty);
            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            // Nothing can be issued or deleted while the endpoint is off
            using (var response = await httpClient.PostAsJsonAsync("/api/v2/mcp/tokens", request, JsonOptions).ConfigureAwait(false))
                Assert.That(await ReadV2StatusCodeAsync(response).ConfigureAwait(false), Is.EqualTo("HTTP-404"));
            using (var response = await httpClient.DeleteAsync($"/api/v2/mcp/token/{created.Token.Id}").ConfigureAwait(false))
                Assert.That(await ReadV2StatusCodeAsync(response).ConfigureAwait(false), Is.EqualTo("HTTP-404"));
            Assert.That(await ListMcpTokensAsync(httpClient).ConfigureAwait(false), Is.Empty);

            // Turning the endpoint on again does not bring the token back
            await SetMcpServerEnabledAsync(httpClient, true).ConfigureAwait(false);
            Assert.That(await ListMcpTokensAsync(httpClient).ConfigureAwait(false), Is.Empty);
            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            // A new token can be issued, and works
            var again = await CreateMcpTokenAsync(httpClient, request).ConfigureAwait(false);
            using var againClient = CreateBearerClient(httpClient, again.AccessToken);
            using (var response = await PostMcpAsync(againClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// A token stops working at the time it expires, and stays listed, marked as expired, until it is deleted
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpTokenStopsWorkingWhenItExpires_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            // The API does not issue a token that lives for less than a minute, so this one comes from the store
            var store = (IMcpTokenStore?)Duplicati.Server.Program.DuplicatiWebserver.Provider.GetService(typeof(IMcpTokenStore))
                ?? throw new InvalidOperationException("The server has no MCP token store");
            var lifetime = TimeSpan.FromSeconds(3);
            var (entry, token) = store.Create("short-lived", McpTokenLevel.Read, null, [], lifetime);

            using var tokenClient = CreateBearerClient(httpClient, token);
            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await ListMcpTokensAsync(httpClient).ConfigureAwait(false)).Single().Expired, Is.False);

            var untilExpired = entry.Expires - DateTime.UtcNow + TimeSpan.FromMilliseconds(250);
            if (untilExpired > TimeSpan.Zero)
                await Task.Delay(untilExpired).ConfigureAwait(false);

            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            var listed = (await ListMcpTokensAsync(httpClient).ConfigureAwait(false)).Single();
            Assert.That(listed.Id, Is.EqualTo(entry.Id));
            Assert.That(listed.Expired, Is.True);
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// Changing the password replaces the key the MCP tokens are signed with, so the tokens
    /// stop working and are no longer listed
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpTokensAreRemovedWhenThePasswordChanges_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var created = await CreateMcpTokenAsync(httpClient, new CreateMcpTokenInputDto("test agent", McpTokenLevel.Read, null, null, null)).ConfigureAwait(false);
            using var tokenClient = CreateBearerClient(httpClient, created.AccessToken);
            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var change = await httpClient.PatchAsJsonAsync("/api/v1/serversettings", new Dictionary<string, string> { ["server-passphrase"] = "new-integration-test-password" }).ConfigureAwait(false);
            change.EnsureSuccessStatusCode();
            await AuthenticateAsync(httpClient, "new-integration-test-password").ConfigureAwait(false);

            using (var response = await PostMcpAsync(tokenClient, true, null).ConfigureAwait(false))
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(await ListMcpTokensAsync(httpClient).ConfigureAwait(false), Is.Empty, "A token that no longer works must not be listed");
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// An MCP token only sees the backups it was issued for, cannot use tools above its level,
    /// and can only restore to the original location or below its restore folders
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpTokenIsLimitedToItsScope_Async()
    {
        await File.WriteAllTextAsync(Path.Combine(this.DATAFOLDER, "sample.txt"), "sample").ConfigureAwait(false);
        var allowedRoot = Path.Combine(BASEFOLDER, "mcp-restore-root");
        var outside = Path.Combine(BASEFOLDER, "mcp-outside");
        var otherTarget = Path.Combine(BASEFOLDER, "other-target");

        // The fixture does not remove these folders, and what an earlier run left in them would fail this one
        void RemoveFolders()
        {
            foreach (var folder in new[] { allowedRoot, outside, otherTarget })
                SafeDeleteDirectory(folder);
        }

        RemoveFolders();
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(outside);

        try
        {
            await RunScopeTestAsync(allowedRoot, outside, otherTarget).ConfigureAwait(false);
        }
        finally
        {
            RemoveFolders();
        }
    }

    /// <summary>
    /// The body of <see cref="McpTokenIsLimitedToItsScope_Async"/>
    /// </summary>
    /// <param name="allowedRoot">The folder the restore token can restore to</param>
    /// <param name="outside">A folder next to it, which the token cannot restore to</param>
    /// <param name="otherTarget">The destination of the backup the tokens cannot use</param>
    /// <returns>An awaitable task</returns>
    private async Task RunScopeTestAsync(string allowedRoot, string outside, string otherTarget)
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var allowedId = await CreateBackupAsync(httpClient, "mcp-secret-passphrase", "Allowed backup").ConfigureAwait(false);
            var otherId = await CreateBackupAsync(httpClient, "mcp-secret-passphrase", "Other backup", otherTarget).ConfigureAwait(false);

            // A read token for one backup
            var read = await CreateMcpTokenAsync(httpClient, new CreateMcpTokenInputDto("reader", McpTokenLevel.Read, [allowedId], null, null)).ConfigureAwait(false);
            using (var readClient = CreateBearerClient(httpClient, read.AccessToken))
            await using (var mcp = await ConnectMcpAsync(readClient).ConfigureAwait(false))
            {
                var (backups, _) = await CallMcpAsync(mcp, "list_backups", []).ConfigureAwait(false);
                Assert.That(backups.GetProperty("backups").EnumerateArray().Select(x => x.GetProperty("id").GetString()), Is.EqualTo(new[] { allowedId }));

                Assert.That(await CallMcpForErrorAsync(mcp, "list_versions", new() { ["backup"] = otherId }).ConfigureAwait(false), Does.Contain("No backup"));
                Assert.That(await CallMcpForErrorAsync(mcp, "list_versions", new() { ["backup"] = "Other backup" }).ConfigureAwait(false), Does.Contain("No backup"));
                Assert.That(await CallMcpForErrorAsync(mcp, "run_backup", new() { ["backup"] = allowedId }).ConfigureAwait(false), Does.Contain("permission level"));
                Assert.That(await CallMcpForErrorAsync(mcp, "restore", new() { ["backup"] = allowedId, ["overwrite"] = true }).ConfigureAwait(false), Does.Contain("permission level"));
            }

            // Keep tasks in the queue, so restores that are accepted do not run
            (await httpClient.PostAsync("/api/v1/serverstate/pause", null).ConfigureAwait(false)).EnsureSuccessStatusCode();

            // A task of another backup is not visible to the token
            var otherTask = await httpClient.PostAsync($"/api/v1/backup/{otherId}/run", null).ConfigureAwait(false);
            otherTask.EnsureSuccessStatusCode();
            var otherTaskId = (await otherTask.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false))!.ID;

            // A restore token with one restore folder
            var restore = await CreateMcpTokenAsync(httpClient, new CreateMcpTokenInputDto("restorer", McpTokenLevel.Restore, [allowedId], [allowedRoot], null)).ConfigureAwait(false);
            using (var restoreClient = CreateBearerClient(httpClient, restore.AccessToken))
            await using (var mcp = await ConnectMcpAsync(restoreClient).ConfigureAwait(false))
            {
                Assert.That(await CallMcpForErrorAsync(mcp, "get_task", new() { ["taskId"] = otherTaskId }).ConfigureAwait(false), Does.Contain("No such task"));
                Assert.That(await CallMcpForErrorAsync(mcp, "stop_task", new() { ["taskId"] = otherTaskId }).ConfigureAwait(false), Does.Contain("No such task"));

                // A task that does not exist yet is refused the same way, so it cannot be stopped as it is queued
                Assert.That(await CallMcpForErrorAsync(mcp, "stop_task", new() { ["taskId"] = otherTaskId + 1000 }).ConfigureAwait(false), Does.Contain("No such task"));

                var (status, statusText) = await CallMcpAsync(mcp, "get_server_status", []).ConfigureAwait(false);
                Assert.That(status.GetProperty("queue").EnumerateArray().Select(x => x.GetProperty("taskId").GetInt64()), Does.Contain(otherTaskId));
                Assert.That(statusText, Does.Not.Contain("Other backup"));

                foreach (var refused in new[] { BASEFOLDER, allowedRoot + "-sibling", Path.Combine(allowedRoot, "..", "escaped") })
                    Assert.That(await CallMcpForErrorAsync(mcp, "restore", new() { ["backup"] = allowedId, ["overwrite"] = true, ["restorePath"] = refused }).ConfigureAwait(false),
                        Does.Contain("can only restore"), $"A restore to {refused} must be refused");

                // A link below the restore folder does not lead out of it. Creating a link needs a privilege on Windows.
                if (!OperatingSystem.IsWindows())
                {
                    Directory.CreateSymbolicLink(Path.Combine(allowedRoot, "link"), outside);
                    Directory.CreateSymbolicLink(Path.Combine(allowedRoot, "relative-link"), Path.Combine("..", "mcp-outside"));

                    // The file system goes to the parent of what a link points to, which is not the folder the link is in
                    Directory.CreateSymbolicLink(Path.Combine(allowedRoot, "parent-link"), Path.Combine("link", "..", "mcp-outside"));
                    foreach (var refused in new[] { Path.Combine(allowedRoot, "link"), Path.Combine(allowedRoot, "link", "sub"), Path.Combine(allowedRoot, "relative-link", "sub"), Path.Combine(allowedRoot, "parent-link") })
                        Assert.That(await CallMcpForErrorAsync(mcp, "restore", new() { ["backup"] = allowedId, ["overwrite"] = true, ["restorePath"] = refused }).ConfigureAwait(false),
                            Does.Contain("can only restore"), $"A restore to {refused} must be refused");
                }

                // A link that stays below the restore folder is followed
                if (!OperatingSystem.IsWindows())
                {
                    Directory.CreateDirectory(Path.Combine(allowedRoot, "real"));
                    Directory.CreateSymbolicLink(Path.Combine(allowedRoot, "inside-link"), "real");
                    var (linked, _) = await CallMcpAsync(mcp, "restore", new() { ["backup"] = allowedId, ["overwrite"] = false, ["restorePath"] = Path.Combine(allowedRoot, "inside-link", "sub") }).ConfigureAwait(false);
                    Assert.That(linked.GetProperty("status").GetString(), Is.EqualTo("Waiting"));
                }

                // A restore that has not started cannot be stopped, and the tool says so
                var (queued, _) = await CallMcpAsync(mcp, "restore", new() { ["backup"] = allowedId, ["overwrite"] = false, ["restorePath"] = Path.Combine(allowedRoot, "sub") }).ConfigureAwait(false);
                Assert.That(queued.GetProperty("status").GetString(), Is.EqualTo("Waiting"));
                Assert.That(await CallMcpForErrorAsync(mcp, "stop_task", new() { ["taskId"] = queued.GetProperty("taskId").GetInt64() }).ConfigureAwait(false), Does.Contain("cannot be stopped"));
                await CallMcpAsync(mcp, "restore", new() { ["backup"] = allowedId, ["overwrite"] = false }).ConfigureAwait(false);
            }

            // Stop the queued tasks, so the server can shut down
            foreach (var task in (await httpClient.GetFromJsonAsync<GetTaskStateDto[]>("/api/v1/tasks", JsonOptions).ConfigureAwait(false))!)
                (await httpClient.PostAsync($"/api/v1/task/{task.ID}/abort", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
            (await httpClient.PostAsync("/api/v1/serverstate/resume", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// The bridge of the server utility lets a client that talks over standard input and output use the
    /// MCP endpoint, and answers with an error when the server refuses the token
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task McpBridgeForwardsStandardInputAndOutputToTheEndpoint_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, "mcp-secret-passphrase", "Bridged backup").ConfigureAwait(false);
            var created = await CreateMcpTokenAsync(httpClient, new CreateMcpTokenInputDto("bridge", McpTokenLevel.Read, null, null, null)).ConfigureAwait(false);
            var endpoint = new Uri(httpClient.BaseAddress!, McpServerSetup.EndpointPath);

            await RunWithMcpBridgeAsync(endpoint, created.AccessToken, async transport =>
            {
                await using var mcp = await McpClient.CreateAsync(transport).ConfigureAwait(false);

                Assert.That((await mcp.ListToolsAsync().ConfigureAwait(false)).Select(x => x.Name), Does.Contain("wait_for_task"));

                var (backups, _) = await CallMcpAsync(mcp, "list_backups", []).ConfigureAwait(false);
                Assert.That(backups.GetProperty("backups").EnumerateArray().Select(x => x.GetProperty("id").GetString()), Is.EqualTo(new[] { backupId }));

                // An error from a tool passes through as a tool error
                Assert.That(await CallMcpForErrorAsync(mcp, "run_backup", new() { ["backup"] = backupId }).ConfigureAwait(false), Does.Contain("permission level"));
            }).ConfigureAwait(false);

            // A refused token gives the client an error to show, not a request that never completes
            await RunWithMcpBridgeAsync(endpoint, created.AccessToken + "x", transport =>
            {
                var ex = Assert.CatchAsync(async () => await (await McpClient.CreateAsync(transport).ConfigureAwait(false)).DisposeAsync().ConfigureAwait(false));
                Assert.That(ex!.ToString(), Does.Contain("refused the credentials"));
                return Task.CompletedTask;
            }).ConfigureAwait(false);
        }, null, "127.0.0.1", EnableMcpArgs).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the MCP bridge of the server utility over in-memory pipes
    /// </summary>
    /// <param name="endpoint">The address of the MCP endpoint</param>
    /// <param name="token">The token the bridge sends</param>
    /// <param name="testBody">The test, given the transport of a client that is connected to the bridge</param>
    /// <returns>An awaitable task</returns>
    private static async Task RunWithMcpBridgeAsync(Uri endpoint, string token, Func<StreamClientTransport, Task> testBody)
    {
        var clientToBridge = new System.IO.Pipelines.Pipe();
        var bridgeToClient = new System.IO.Pipelines.Pipe();
        using var bridgeClient = new HttpClient
        {
            DefaultRequestHeaders = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token) }
        };
        using var log = new StringWriter();

        var bridge = new Duplicati.CommandLine.ServerUtil.McpBridge(
            endpoint,
            () => bridgeClient,
            _ => Task.FromResult(false),
            clientToBridge.Reader.AsStream(),
            bridgeToClient.Writer.AsStream(),
            log);
        var bridgeTask = bridge.RunAsync(default);

        try
        {
            await testBody(new StreamClientTransport(clientToBridge.Writer.AsStream(), bridgeToClient.Reader.AsStream(), null)).ConfigureAwait(false);
        }
        finally
        {
            // Closing the input is how a client stops the bridge
            await clientToBridge.Writer.CompleteAsync().ConfigureAwait(false);
            await bridgeTask.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            await bridgeToClient.Writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Turns the MCP endpoint on or off the way the settings page does
    /// </summary>
    /// <param name="httpClient">The logged in client for the server</param>
    /// <param name="enabled">True to turn the endpoint on</param>
    /// <returns>An awaitable task</returns>
    private static async Task SetMcpServerEnabledAsync(HttpClient httpClient, bool enabled)
    {
        using var response = await httpClient.PatchAsJsonAsync("/api/v1/serversettings", new Dictionary<string, string> { ["enable-mcp-server"] = enabled ? "True" : "False" }).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Issues an MCP token through the API
    /// </summary>
    /// <param name="httpClient">The logged in client for the server</param>
    /// <param name="input">The name and scope of the token</param>
    /// <returns>The issued token</returns>
    private static async Task<CreateMcpTokenOutputDto> CreateMcpTokenAsync(HttpClient httpClient, CreateMcpTokenInputDto input)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v2/mcp/tokens", input, JsonOptions).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var envelope = await response.Content.ReadFromJsonAsync<ResponseEnvelope<CreateMcpTokenOutputDto>>(JsonOptions).ConfigureAwait(false);
        Assert.That(envelope?.Success, Is.True, envelope?.Error);
        return envelope!.Data ?? throw new InvalidOperationException("Token response was empty");
    }

    /// <summary>
    /// Lists the MCP tokens through the API
    /// </summary>
    /// <param name="httpClient">The logged in client for the server</param>
    /// <returns>The issued tokens</returns>
    private static async Task<McpTokenDto[]> ListMcpTokensAsync(HttpClient httpClient)
    {
        var envelope = await httpClient.GetFromJsonAsync<ResponseEnvelope<McpTokenDto[]>>("/api/v2/mcp/tokens", JsonOptions).ConfigureAwait(false);
        Assert.That(envelope?.Success, Is.True, envelope?.Error);
        return envelope!.Data ?? [];
    }

    /// <summary>
    /// Reads the status code in the envelope of a V2 API response, which is where a V2 endpoint reports failures
    /// </summary>
    /// <param name="response">The response</param>
    /// <returns>The status code, which is OK for a success</returns>
    private static async Task<string?> ReadV2StatusCodeAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ResponseEnvelope>(JsonOptions).ConfigureAwait(false))?.StatusCode;
    }

    /// <summary>
    /// Creates a client for the server that sends a bearer token
    /// </summary>
    /// <param name="httpClient">A client for the server</param>
    /// <param name="token">The token to send</param>
    /// <returns>The client</returns>
    private static HttpClient CreateBearerClient(HttpClient httpClient, string token)
        => new()
        {
            BaseAddress = httpClient.BaseAddress,
            DefaultRequestHeaders = { Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token) }
        };

    /// <summary>
    /// Calls an MCP tool that is expected to fail with an error for the agent
    /// </summary>
    /// <param name="mcp">The MCP client</param>
    /// <param name="tool">The name of the tool</param>
    /// <param name="arguments">The arguments of the call</param>
    /// <returns>The error message</returns>
    private static async Task<string> CallMcpForErrorAsync(McpClient mcp, string tool, Dictionary<string, object?> arguments)
    {
        var result = await mcp.CallToolAsync(tool, arguments).ConfigureAwait(false);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(x => x.Text));
        Assert.That(result.IsError, Is.True, $"The tool {tool} was expected to fail, but returned: {text}");
        return text;
    }

    /// <summary>
    /// Posts a minimal request to the MCP endpoint
    /// </summary>
    /// <param name="httpClient">The logged in client for the server</param>
    /// <param name="authenticated">False to send the request without credentials</param>
    /// <param name="origin">The origin header to send, if any</param>
    /// <returns>The response</returns>
    private static async Task<HttpResponseMessage> PostMcpAsync(HttpClient httpClient, bool authenticated, string? origin)
    {
        using var client = new HttpClient { BaseAddress = httpClient.BaseAddress };
        using var request = new HttpRequestMessage(HttpMethod.Post, McpServerSetup.EndpointPath)
        {
            Content = new StringContent(McpPingRequest, Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (authenticated)
            request.Headers.Authorization = httpClient.DefaultRequestHeaders.Authorization;
        if (origin != null)
            request.Headers.Add("Origin", origin);

        return await client.SendAsync(request).ConfigureAwait(false);
    }

    /// <summary>
    /// Connects an MCP client to the server, with the credentials of the given client
    /// </summary>
    /// <param name="httpClient">The logged in client for the server</param>
    /// <returns>The MCP client</returns>
    private static Task<McpClient> ConnectMcpAsync(HttpClient httpClient)
        => McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = new Uri(httpClient.BaseAddress!, McpServerSetup.EndpointPath),
                TransportMode = HttpTransportMode.StreamableHttp
            },
            httpClient,
            null,
            false));

    /// <summary>
    /// Calls an MCP tool that is expected to succeed
    /// </summary>
    /// <param name="mcp">The MCP client</param>
    /// <param name="tool">The name of the tool</param>
    /// <param name="arguments">The arguments of the call</param>
    /// <returns>The structured result, and everything the tool returned as text</returns>
    private static async Task<(JsonElement Result, string Text)> CallMcpAsync(McpClient mcp, string tool, Dictionary<string, object?> arguments)
    {
        var result = await mcp.CallToolAsync(tool, arguments).ConfigureAwait(false);
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(x => x.Text));
        Assert.That(result.IsError, Is.Not.True, $"The tool {tool} failed: {text}");
        Assert.That(result.StructuredContent, Is.Not.Null, $"The tool {tool} returned no structured content");

        var structured = JsonSerializer.SerializeToElement(result.StructuredContent);
        return (structured, text + "\n" + structured.GetRawText());
    }

    /// <summary>
    /// Waits for a task the way an agent does, with repeated bounded waits
    /// </summary>
    /// <param name="mcp">The MCP client</param>
    /// <param name="taskId">The ID of the task</param>
    /// <returns>The final state of the task</returns>
    private static async Task<JsonElement> WaitForMcpTaskAsync(McpClient mcp, long taskId)
    {
        for (var i = 0; i < 12; i++)
        {
            var (state, _) = await CallMcpAsync(mcp, "wait_for_task", new() { ["taskId"] = taskId, ["timeoutSeconds"] = 10 }).ConfigureAwait(false);
            if (state.GetProperty("finished").GetBoolean())
                return state;
        }

        throw new TimeoutException($"Task {taskId} did not finish in the allotted time");
    }
}
