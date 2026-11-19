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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Duplicati.Library.RestAPI;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Dto.V2;
using NUnit.Framework;

namespace Duplicati.UnitTest;

public partial class ServerApiIntegrationTests
{
    private const string LaggingDestinationKey = "lagging-destination";

    /// <summary>
    /// A secondary destination that was last written after the first backup holds the first
    /// version only. Copied to a temporary job for that destination, the job lists that version
    /// alone and restores it, while the main destination cannot be reached. The saved job and
    /// its database are not changed, and deleting the temporary job removes its database.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("CopyToTempDestination")]
    public async Task ASecondaryDestinationListsAndRestoresItsOwnVersions_Async()
    {
        var (primary, secondary) = PrepareCopyToTempFolders();
        var sample = Path.Combine(this.DATAFOLDER, "sample.txt");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            File.WriteAllText(sample, "Version on both destinations");
            var backupId = await CreateBackupWithLaggingDestinationAsync(httpClient, primary, secondary).ConfigureAwait(false);
            var olderTime = (await ListFilesetTimesAsync(httpClient, backupId).ConfigureAwait(false)).Single();

            // The second version is written to the main destination only: the secondary
            // destination is put back the way it was after the first backup
            var lagging = secondary + "-first";
            CopyFolder(secondary, lagging);
            File.WriteAllText(sample, "Version on the main destination only");
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
            Assert.That(await ListFilesetTimesAsync(httpClient, backupId).ConfigureAwait(false), Has.Length.EqualTo(2));
            Directory.Delete(secondary, true);
            Directory.Move(lagging, secondary);

            var jobBefore = await httpClient.GetStringAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            var dbPath = await GetJobDatabasePathAsync(httpClient, backupId).ConfigureAwait(false);
            var dbBefore = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dbPath)));

            // The main destination cannot be reached, so nothing can come from it
            var away = primary + "-away";
            Directory.Move(primary, away);

            var temporaryId = await CopyToTempAsync(httpClient, backupId, LaggingDestinationKey).ConfigureAwait(false);
            var temporary = await httpClient.GetFromJsonAsync<JsonElement>($"/api/v1/backup/{temporaryId}", JsonOptions).ConfigureAwait(false);
            var temporaryDb = await GetJobDatabasePathAsync(httpClient, temporaryId).ConfigureAwait(false);
            Assert.That(temporaryDb, Is.Not.EqualTo(dbPath), "The temporary job should have a database of its own");
            Assert.That(temporary.GetProperty("Backup").GetProperty("TargetURL").GetString(), Does.Contain("copy-to-temp-secondary"));

            var times = await ListFilesetTimesAsync(httpClient, temporaryId).ConfigureAwait(false);
            Assert.That(times, Is.EqualTo(new[] { olderTime }), "Only the version at the secondary destination should be listed");

            // The restore page builds the database of the version, lists it and restores from a fresh copy
            await RunTaskWithBodyAndWaitAsync(httpClient, $"/api/v1/backup/{temporaryId}/repairupdate", new RepairInputDto(true, olderTime, null, null, null)).ConfigureAwait(false);
            var sourceFolder = Duplicati.Library.Common.IO.Util.AppendDirSeparator(this.DATAFOLDER);
            var folderResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-folder", new ListFolderContentRequestDto { BackupId = temporaryId, Paths = new[] { sourceFolder }, Time = olderTime, PageSize = 1000, Page = 0 }, JsonOptions).ConfigureAwait(false);
            var folderBody = await folderResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(folderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), folderBody);
            using (var folder = JsonDocument.Parse(folderBody))
                Assert.That(folder.RootElement.GetProperty("Data").EnumerateArray().Select(x => x.GetProperty("Path").GetString()), Does.Contain(sample), folderBody);

            var restoreId = await CopyToTempAsync(httpClient, temporaryId, null).ConfigureAwait(false);
            if (Directory.Exists(this.RESTOREFOLDER))
                Directory.Delete(this.RESTOREFOLDER, true);
            var restoreResponse = await httpClient.PostAsJsonAsync($"/api/v1/backup/{restoreId}/restore", new RestoreInputDto(new[] { sample }, null, olderTime, this.RESTOREFOLDER, true, false, false, null, null), JsonOptions).ConfigureAwait(false);
            var restoreBody = await restoreResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(restoreResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), restoreBody);
            var restoreTask = await restoreResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Restore did not return a task");
            await WaitForTaskCompletionAsync(httpClient, restoreTask.ID).ConfigureAwait(false);
            await AssertTaskCompletedAsync(httpClient, restoreTask.ID).ConfigureAwait(false);
            Assert.That(File.ReadAllText(Path.Combine(this.RESTOREFOLDER, "sample.txt")), Is.EqualTo("Version on both destinations"));

            Assert.That(await httpClient.GetStringAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false), Is.EqualTo(jobBefore), "The saved job should not change");
            Assert.That(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dbPath))), Is.EqualTo(dbBefore), "The database of the saved job should not change");

            foreach (var id in new[] { temporaryId, restoreId })
            {
                var deleted = await httpClient.DeleteAsync($"/api/v1/backup/{id}?delete-local-db=true").ConfigureAwait(false);
                deleted.EnsureSuccessStatusCode();
                var deleteTask = await deleted.Content.ReadFromJsonAsync<DeleteBackupOutputDto>(JsonOptions).ConfigureAwait(false);
                await WaitForTaskCompletionAsync(httpClient, deleteTask?.ID ?? throw new InvalidOperationException("Delete did not start a task")).ConfigureAwait(false);
            }
            Assert.That(File.Exists(temporaryDb), Is.False, "Deleting the temporary job should remove its database");
            var listed = await httpClient.GetFromJsonAsync<TemporaryBackupDto[]>("/api/v1/backups/temporary", JsonOptions).ConfigureAwait(false);
            Assert.That(listed!.Select(x => x.ID), Does.Not.Contain(temporaryId).And.Not.Contain(restoreId));

            Directory.Move(away, primary);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A key the job does not have, a key of another job, or an empty key is refused before a
    /// temporary job is made. A secondary destination that cannot be reached fails the listing
    /// and does not fall back to the main destination.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("CopyToTempDestination")]
    public async Task AnUnknownOrUnreachableSecondaryDestinationIsReported_Async()
    {
        var (primary, secondary) = PrepareCopyToTempFolders();
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            Assert.That(await GetApiExtensionsAsync(httpClient).ConfigureAwait(false), Does.Contain(CopyToTempDestination.ApiExtension));
            var backupId = await CreateBackupWithLaggingDestinationAsync(httpClient, primary, secondary).ConfigureAwait(false);
            await CreateBackupWithLaggingDestinationAsync(httpClient, Path.Combine(BASEFOLDER, "copy-to-temp-other"), Path.Combine(BASEFOLDER, "copy-to-temp-other-secondary"), "copy-to-temp-other-key").ConfigureAwait(false);
            var before = (await httpClient.GetFromJsonAsync<TemporaryBackupDto[]>("/api/v1/backups/temporary", JsonOptions).ConfigureAwait(false))!.Length;

            foreach (var (key, helpId) in new[] { ("no-such-key", "CopyToTempDestinationUnknown"), ("copy-to-temp-other-key", "CopyToTempDestinationUnknown"), ("", "CopyToTempDestinationEmpty") })
            {
                var response = await httpClient.PostAsync($"/api/v1/backup/{backupId}/copytotemp?{CopyToTempDestination.QueryParameter}={Uri.EscapeDataString(key)}", null).ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{key}: {body}");
                using var json = JsonDocument.Parse(body);
                Assert.That(GetStringProperty(json.RootElement, "helpId"), Is.EqualTo(helpId), $"{key}: {body}");
            }
            var after = (await httpClient.GetFromJsonAsync<TemporaryBackupDto[]>("/api/v1/backups/temporary", JsonOptions).ConfigureAwait(false))!.Length;
            Assert.That(after, Is.EqualTo(before), "No temporary job should be made for a refused key");

            Directory.Move(secondary, secondary + "-away");
            var temporaryId = await CopyToTempAsync(httpClient, backupId, LaggingDestinationKey).ConfigureAwait(false);
            var listResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-filesets", new ListFilesetsRequestDto { BackupId = temporaryId }, JsonOptions).ConfigureAwait(false);
            var listBody = await listResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(listBody, Does.Contain("FolderMissing"), "The listing should report the missing destination, not the versions of the main destination");
            Assert.That(listBody, Does.Not.Contain("\"Version\""), listBody);

            var deleted = await httpClient.DeleteAsync($"/api/v1/backup/{temporaryId}?delete-local-db=true").ConfigureAwait(false);
            deleted.EnsureSuccessStatusCode();
            var listed = await httpClient.GetFromJsonAsync<TemporaryBackupDto[]>("/api/v1/backups/temporary", JsonOptions).ConfigureAwait(false);
            Assert.That(listed!.Select(x => x.ID), Does.Not.Contain(temporaryId));
            Directory.Move(secondary + "-away", secondary);
        }).ConfigureAwait(false);
    }

    private (string Primary, string Secondary) PrepareCopyToTempFolders()
    {
        Directory.CreateDirectory(this.DATAFOLDER);
        var folders = new[] { "copy-to-temp-primary", "copy-to-temp-secondary", "copy-to-temp-other", "copy-to-temp-other-secondary" }
            .Select(x => Path.Combine(BASEFOLDER, x))
            .ToArray();
        foreach (var folder in folders.Concat(folders.Select(x => x + "-away")).Concat(folders.Select(x => x + "-first")))
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
        foreach (var folder in folders)
            Directory.CreateDirectory(folder);
        return (folders[0], folders[1]);
    }

    /// <summary>
    /// Creates an encrypted job with a secondary destination that is written after every backup, and runs a backup
    /// </summary>
    private async Task<string> CreateBackupWithLaggingDestinationAsync(HttpClient httpClient, string primary, string secondary, string key = LaggingDestinationKey)
    {
        var request = new BackupAndScheduleInputDto
        {
            Backup = new BackupAndScheduleInputDto.BackupInputDto
            {
                Name = $"API integration copy to temp {Guid.NewGuid():N}",
                Description = "Integration test backup",
                TargetURL = BuildFileBackendUrl(primary),
                Sources = new[] { this.DATAFOLDER },
                Settings = new[]
                {
                    new BackupAndScheduleInputDto.SettingInputDto { Name = "passphrase", Value = "copy-to-temp-passphrase" },
                    new BackupAndScheduleInputDto.SettingInputDto { Name = "snapshot-policy", Value = "Off" }
                },
                Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                Metadata = new Dictionary<string, string>(),
                AdditionalTargetURLs = [new TargetUrlInputDto { UrlKey = key, TargetUrl = BuildFileBackendUrl(secondary), Mode = "inline" }]
            }
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/backups", request, JsonOptions).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.That(response.IsSuccessStatusCode, Is.True, body);
        var backupId = (await response.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false))?.ID
            ?? throw new InvalidOperationException("Backup creation did not return an ID");
        await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
        Assert.That(Directory.EnumerateFiles(secondary, "duplicati-*").Any(), Is.True, "The backup should have been copied to the secondary destination");
        return backupId;
    }

    private static async Task<string> CopyToTempAsync(HttpClient httpClient, string backupId, string? destination)
    {
        var query = destination == null ? "" : $"?{CopyToTempDestination.QueryParameter}={Uri.EscapeDataString(destination)}";
        var response = await httpClient.PostAsync($"/api/v1/backup/{backupId}/copytotemp{query}", null).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        return (await response.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false))?.ID
            ?? throw new InvalidOperationException("The copy did not return an ID");
    }

    /// <summary>
    /// The times of the versions of a job, newest first, as the restore page lists them
    /// </summary>
    private static async Task<string[]> ListFilesetTimesAsync(HttpClient httpClient, string backupId)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v2/backup/list-filesets", new ListFilesetsRequestDto { BackupId = backupId }, JsonOptions).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        using var json = JsonDocument.Parse(body);
        Assert.That(json.RootElement.GetProperty("Success").GetBoolean(), Is.True, body);
        return json.RootElement.GetProperty("Data").EnumerateArray()
            .Select(x => x.GetProperty("Time").GetDateTime().ToUniversalTime().ToString("o"))
            .ToArray();
    }

    private static async Task RunTaskWithBodyAndWaitAsync(HttpClient httpClient, string relativeUrl, object body)
    {
        var response = await httpClient.PostAsJsonAsync(relativeUrl, body, JsonOptions).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        var task = await response.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Task response was empty");
        await WaitForTaskCompletionAsync(httpClient, task.ID).ConfigureAwait(false);
        await AssertTaskCompletedAsync(httpClient, task.ID).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the path of the database of a job
    /// </summary>
    private static async Task<string> GetJobDatabasePathAsync(HttpClient httpClient, string backupId)
    {
        var backup = await httpClient.GetFromJsonAsync<JsonElement>($"/api/v1/backup/{backupId}", JsonOptions).ConfigureAwait(false);
        var job = backup.TryGetProperty("backup", out var camel) ? camel : backup.GetProperty("Backup");
        var path = job.TryGetProperty("dbPath", out var value) || job.TryGetProperty("DBPath", out value) ? value.GetString() : null;
        return path ?? throw new InvalidOperationException("The job has no database path");
    }

    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
    }
}
