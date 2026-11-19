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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Duplicati.Server;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests of restoring files as a download in the browser
/// </summary>
public partial class ServerApiIntegrationTests
{
    private const string RestoreDownloadPassphrase = "restore-download-passphrase";

    [Test]
    [Category("Integration")]
    [Category("RestoreDownload")]
    public async Task ARestoredFileIsDownloadedWithItsName_Async()
    {
        await File.WriteAllTextAsync(Path.Combine(DATAFOLDER, "report.txt"), "single file").ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            Assert.That(await GetApiExtensionsAsync(httpClient).ConfigureAwait(false), Does.Contain(SystemInfoProvider.RestoreDownloadExtension));

            var backupId = await CreateBackupAsync(httpClient, RestoreDownloadPassphrase).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var (response, _) = await RestoreForDownloadAsync(httpClient, backupId).ConfigureAwait(false);
            Assert.That(response.Content.Headers.ContentDisposition?.DispositionType, Is.EqualTo("attachment"));
            Assert.That(DownloadName(response), Is.EqualTo("report.txt"));
            Assert.That(await response.Content.ReadAsStringAsync().ConfigureAwait(false), Is.EqualTo("single file"));
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    [Category("RestoreDownload")]
    public async Task SeveralRestoredFilesAreDownloadedAsAZipFile_Async()
    {
        await File.WriteAllTextAsync(Path.Combine(DATAFOLDER, "a.txt"), "first").ConfigureAwait(false);
        Directory.CreateDirectory(Path.Combine(DATAFOLDER, "sub"));
        await File.WriteAllTextAsync(Path.Combine(DATAFOLDER, "sub", "b.txt"), "second").ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, RestoreDownloadPassphrase).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var (response, tempId) = await RestoreForDownloadAsync(httpClient, backupId).ConfigureAwait(false);
            Assert.That(DownloadName(response), Is.EqualTo(RestoreDownload.ZipName));

            using var zip = new ZipArchive(await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
            var files = zip.Entries
                .Where(e => !string.IsNullOrEmpty(e.Name))
                .ToDictionary(e => e.FullName.Replace('\\', '/'), e => new StreamReader(e.Open()).ReadToEnd());
            Assert.That(files.Values, Is.EquivalentTo(new[] { "first", "second" }));
            Assert.That(files.Keys.Any(k => k.EndsWith("sub/b.txt", StringComparison.Ordinal)), Is.True, string.Join(", ", files.Keys));

            // The download can be fetched again until it expires
            Assert.That((await DownloadAsync(httpClient, tempId).ConfigureAwait(false)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    [Category("RestoreDownload")]
    public async Task OnlyARestoreDownloadCanBeFetched_Async()
    {
        await File.WriteAllTextAsync(Path.Combine(DATAFOLDER, "report.txt"), "single file").ConfigureAwait(false);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, RestoreDownloadPassphrase).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            // A temp file of another kind, here a bug report, is not served
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/createreport").ConfigureAwait(false);
            var bugreportId = await FindNotificationIdAsync(httpClient, "bug-report:created:").ConfigureAwait(false);
            Assert.That((await DownloadAsync(httpClient, bugreportId).ConfigureAwait(false)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));

            // A token for another operation is refused
            var bugreportToken = await IssueTokenAsync(httpClient, "bugreport").ConfigureAwait(false);
            var wrongToken = await httpClient.GetAsync($"/api/v1/restore-download/{bugreportId}?token={bugreportToken}").ConfigureAwait(false);
            Assert.That(wrongToken.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

            // A restore path and a download do not go together
            var both = await httpClient.PostAsJsonAsync($"/api/v1/backup/{backupId}/restore",
                new RestoreInputDto(null, RestoreDownloadPassphrase, "now", Path.Combine(BASEFOLDER, "elsewhere"), true, false, false, null, null, null, true), JsonOptions).ConfigureAwait(false);
            Assert.That(both.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    [Category("RestoreDownload")]
    public async Task TheOptionTurnsRestoreDownloadsOff_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            Assert.That(await GetApiExtensionsAsync(httpClient).ConfigureAwait(false), Does.Not.Contain(SystemInfoProvider.RestoreDownloadExtension));

            var backupId = await CreateBackupAsync(httpClient, RestoreDownloadPassphrase).ConfigureAwait(false);
            var response = await httpClient.PostAsJsonAsync($"/api/v1/backup/{backupId}/restore",
                new RestoreInputDto(null, RestoreDownloadPassphrase, "now", null, true, false, false, null, null, null, true), JsonOptions).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        }, null, "127.0.0.1", [$"--{WebServerLoader.OPTION_WEBSERVICE_DISABLE_RESTORE_DOWNLOAD}=true"]).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a restore for download, waits for the notification that it is ready, and downloads it
    /// </summary>
    private static async Task<(HttpResponseMessage Response, long TempId)> RestoreForDownloadAsync(HttpClient httpClient, string backupId)
    {
        var restore = await httpClient.PostAsJsonAsync($"/api/v1/backup/{backupId}/restore",
            new RestoreInputDto(null, RestoreDownloadPassphrase, "now", null, true, false, false, null, null, null, true), JsonOptions).ConfigureAwait(false);
        restore.EnsureSuccessStatusCode();
        var task = await restore.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Restore start response was empty");
        await WaitForTaskCompletionAsync(httpClient, task.ID).ConfigureAwait(false);
        await AssertTaskCompletedAsync(httpClient, task.ID).ConfigureAwait(false);

        var tempId = await FindNotificationIdAsync(httpClient, RestoreDownload.NotificationActionPrefix).ConfigureAwait(false);
        var response = await DownloadAsync(httpClient, tempId).ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync().ConfigureAwait(false));
        return (response, tempId);
    }

    /// <summary>
    /// Finds the id at the end of the action of the latest notification whose action has the prefix
    /// </summary>
    private static async Task<long> FindNotificationIdAsync(HttpClient httpClient, string actionPrefix)
    {
        var notifications = await httpClient.GetFromJsonAsync<JsonElement>("/api/v1/notifications", JsonOptions).ConfigureAwait(false);
        var action = notifications.EnumerateArray()
            .Select(n => n.TryGetProperty("Action", out var value) ? value.GetString() : null)
            .LastOrDefault(a => a != null && a.StartsWith(actionPrefix, StringComparison.Ordinal));
        Assert.That(action, Is.Not.Null, $"A notification with the action {actionPrefix} should be shown");
        return long.Parse(action!.Substring(actionPrefix.Length));
    }

    private static string? DownloadName(HttpResponseMessage response)
        => response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');

    private static async Task<HttpResponseMessage> DownloadAsync(HttpClient httpClient, long tempId)
    {
        var token = await IssueTokenAsync(httpClient, RestoreDownload.TokenOperation).ConfigureAwait(false);
        return await httpClient.GetAsync($"/api/v1/restore-download/{tempId}?token={Uri.EscapeDataString(token)}").ConfigureAwait(false);
    }

    private static async Task<string> IssueTokenAsync(HttpClient httpClient, string operation)
    {
        var response = await httpClient.PostAsync($"/api/v1/auth/issuetoken/{operation}", null).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var token = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(false);
        return token.GetProperty("Token").GetString()!;
    }
}
