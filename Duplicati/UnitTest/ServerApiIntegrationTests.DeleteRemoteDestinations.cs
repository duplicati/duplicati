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
using NUnit.Framework;

namespace Duplicati.UnitTest;

public partial class ServerApiIntegrationTests
{
    private const string FirstSecondaryKey = "second-destination";
    private const string SecondSecondaryKey = "third-destination";

    /// <summary>
    /// Choosing one secondary destination deletes the backup files there only: the main
    /// destination and the other secondary destination keep every file, byte for byte.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("DeleteRemoteDestinations")]
    public async Task OnlyTheChosenDestinationLosesItsFiles_Async()
    {
        var (primary, first, second) = PrepareDestinationFolders();

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, primary, first, second).ConfigureAwait(false);
            var primaryBefore = HashFolder(primary);
            var secondBefore = HashFolder(second);
            Assert.That(HashFolder(first), Is.Not.Empty, "The backup should have been copied to the secondary destination");

            var state = await DeleteAndWaitAsync(httpClient, backupId, $"{RemoteDestinationDelete.QueryParameter}={FirstSecondaryKey}").ConfigureAwait(false);
            Assert.That(state.Status, Is.EqualTo("Completed").IgnoreCase, state.ErrorMessage);

            Assert.That(HashFolder(first), Is.Empty, "The chosen destination should have no backup files left");
            Assert.That(HashFolder(primary), Is.EqualTo(primaryBefore), "The main destination should be kept");
            Assert.That(HashFolder(second), Is.EqualTo(secondBefore), "The destination that was not chosen should be kept");

            var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "The job should be deleted");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// When a chosen destination cannot be reached, the other chosen destinations are still
    /// deleted, the task fails and names the destination, and the job and its database are kept.
    /// Started again once the destination is back, the delete goes through, although the files
    /// of the other destination are already gone.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("DeleteRemoteDestinations")]
    public async Task AFailedDestinationKeepsTheJobUntilTheDeleteIsStartedAgain_Async()
    {
        var (primary, first, second) = PrepareDestinationFolders();

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, primary, first, second).ConfigureAwait(false);
            var primaryBefore = HashFolder(primary);
            var dbPath = await GetDatabasePathAsync(httpClient, backupId).ConfigureAwait(false);
            Assert.That(File.Exists(dbPath), Is.True, "The job should have a database");

            // The second secondary destination is unreachable while the delete runs
            var away = second.TrimEnd(Path.DirectorySeparatorChar) + "-away";
            Directory.Move(second, away);

            var query = $"{RemoteDestinationDelete.QueryParameter}={FirstSecondaryKey},{SecondSecondaryKey}&delete-local-db=true";
            var failed = await DeleteAndWaitAsync(httpClient, backupId, query).ConfigureAwait(false);
            Assert.That(failed.Status, Is.EqualTo("Failed").IgnoreCase, "The delete should fail for the unreachable destination");
            Assert.That(failed.FailedDestinations, Is.EqualTo(new[] { SecondSecondaryKey }), failed.ErrorMessage);
            Assert.That(HashFolder(first), Is.Empty, "The reachable destination should be deleted");
            Assert.That(HashFolder(primary), Is.EqualTo(primaryBefore), "The main destination was not chosen");

            var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The job should be kept for another try");
            Assert.That(File.Exists(dbPath), Is.True, "The database should be kept for another try");

            Directory.Move(away, second);
            var secondBefore = HashFolder(second);
            Assert.That(secondBefore, Is.Not.Empty);

            var retried = await DeleteAndWaitAsync(httpClient, backupId, query).ConfigureAwait(false);
            Assert.That(retried.Status, Is.EqualTo("Completed").IgnoreCase, retried.ErrorMessage);
            Assert.That(retried.FailedDestinations, Is.Null);
            Assert.That(HashFolder(second), Is.Empty, "The destination should be deleted on the second try");
            Assert.That(HashFolder(primary), Is.EqualTo(primaryBefore), "The main destination was not chosen");
            Assert.That(File.Exists(dbPath), Is.False, "The database should be deleted once every destination is");

            backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "The job should be deleted");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A destination fails when one of its files cannot be deleted, although it can be listed,
    /// and the job is kept. Here a backup file is replaced by a folder of the same name.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("DeleteRemoteDestinations")]
    public async Task AFileThatCannotBeDeletedFailsItsDestination_Async()
    {
        var (primary, first, second) = PrepareDestinationFolders();

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, primary, first, second).ConfigureAwait(false);
            var stuck = HashFolder(first).Keys.First(x => x.Contains(".dindex.", StringComparison.Ordinal));
            File.Delete(Path.Combine(first, stuck));
            Directory.CreateDirectory(Path.Combine(first, stuck));
            File.WriteAllText(Path.Combine(first, stuck, "inside.txt"), "Keeps the folder from being removed");

            var query = $"{RemoteDestinationDelete.QueryParameter}={FirstSecondaryKey}";
            var failed = await DeleteAndWaitAsync(httpClient, backupId, query).ConfigureAwait(false);
            Assert.That(failed.Status, Is.EqualTo("Failed").IgnoreCase, "The delete should fail for the file that is left");
            Assert.That(failed.FailedDestinations, Is.EqualTo(new[] { FirstSecondaryKey }), failed.ErrorMessage);
            Assert.That(HashFolder(first), Is.Empty, "The other files of the destination should be deleted");

            var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The job should be kept for another try");

            Directory.Delete(Path.Combine(first, stuck), true);
            var retried = await DeleteAndWaitAsync(httpClient, backupId, query).ConfigureAwait(false);
            Assert.That(retried.Status, Is.EqualTo("Completed").IgnoreCase, retried.ErrorMessage);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A request that names no destination, a destination the job does not have, a destination
    /// of another job, the older parameter as well, or a destination that shares its place with
    /// one that is kept, is refused before anything is deleted or queued.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("DeleteRemoteDestinations")]
    public async Task ADestinationChoiceThatCannotBeFollowedIsRefused_Async()
    {
        var (primary, first, second) = PrepareDestinationFolders();
        var other = Path.Combine(BASEFOLDER, "delete-destinations-other");
        var shared = Path.Combine(BASEFOLDER, "delete-destinations-shared");
        foreach (var folder in new[] { other, other + "-secondary", shared })
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
        }

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, primary, first, second).ConfigureAwait(false);
            var otherId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, other, null, null, otherKey: "other-job-destination").ConfigureAwait(false);
            var before = new[] { HashFolder(primary), HashFolder(first), HashFolder(second) };

            var cases = new (string Query, string HelpId)[]
            {
                ($"{RemoteDestinationDelete.QueryParameter}=", "RemoteDestinationsEmpty"),
                ($"{RemoteDestinationDelete.QueryParameter}=no-such-key", "RemoteDestinationsUnknown"),
                ($"{RemoteDestinationDelete.QueryParameter}=other-job-destination", "RemoteDestinationsUnknown"),
                ($"{RemoteDestinationDelete.QueryParameter}={FirstSecondaryKey}&delete-remote-files=false", "RemoteDestinationsConflict"),
                ($"{RemoteDestinationDelete.QueryParameter}={FirstSecondaryKey}&delete-remote-files=true", "RemoteDestinationsConflict"),
            };

            foreach (var (query, helpId) in cases)
            {
                var response = await httpClient.DeleteAsync($"/api/v1/backup/{backupId}?{query}").ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"{query}: {body}");
                using var json = JsonDocument.Parse(body);
                Assert.That(GetStringProperty(json.RootElement, "helpId"), Is.EqualTo(helpId), $"{query}: {body}");
            }

            // A secondary destination at the same place as the main destination
            var sharedId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, shared, shared, null, otherKey: "shared-destination").ConfigureAwait(false);
            var sharedResponse = await httpClient.DeleteAsync($"/api/v1/backup/{sharedId}?{RemoteDestinationDelete.QueryParameter}=shared-destination").ConfigureAwait(false);
            var sharedBody = await sharedResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(sharedResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), sharedBody);
            using (var json = JsonDocument.Parse(sharedBody))
                Assert.That(GetStringProperty(json.RootElement, "helpId"), Is.EqualTo("RemoteDestinationsSharedLocation"), sharedBody);

            Assert.That(new[] { HashFolder(primary), HashFolder(first), HashFolder(second) }, Is.EqualTo(before), "Nothing should be deleted");
            foreach (var id in new[] { backupId, otherId, sharedId })
            {
                var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{id}").ConfigureAwait(false);
                Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"The job {id} should be kept");
            }

            Assert.That(await GetApiExtensionsAsync(httpClient).ConfigureAwait(false), Does.Contain(RemoteDestinationDelete.ApiExtension));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Without the new parameter a delete with the remote files works as before: the files of the
    /// main destination are deleted and the secondary destinations keep theirs.
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("DeleteRemoteDestinations")]
    public async Task TheOlderParameterStillDeletesTheMainDestinationOnly_Async()
    {
        var (primary, first, second) = PrepareDestinationFolders();

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupWithSecondaryDestinationsAsync(httpClient, primary, first, second).ConfigureAwait(false);
            var firstBefore = HashFolder(first);
            var secondBefore = HashFolder(second);

            var state = await DeleteAndWaitAsync(httpClient, backupId, "delete-remote-files=true").ConfigureAwait(false);
            Assert.That(state.Status, Is.EqualTo("Completed").IgnoreCase, state.ErrorMessage);
            Assert.That(HashFolder(primary), Is.Empty, "The main destination should be deleted");
            Assert.That(HashFolder(first), Is.EqualTo(firstBefore));
            Assert.That(HashFolder(second), Is.EqualTo(secondBefore));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A sync job keeps the copied files at the destination, so naming destinations is refused
    /// </summary>
    [Test]
    [Category("Integration")]
    [Category("DeleteRemoteDestinations")]
    public async Task ASyncJobRefusesDestinationsToDelete_Async()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sync sample");
        Directory.CreateDirectory(this.TARGETFOLDER);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var request = new BackupAndScheduleInputDto
            {
                Backup = new BackupAndScheduleInputDto.BackupInputDto
                {
                    Name = $"API integration sync {Guid.NewGuid():N}",
                    Description = "Integration test sync",
                    OperationType = Duplicati.Server.Serialization.OperationType.Sync,
                    TargetURL = BuildFileBackendUrl(this.TARGETFOLDER),
                    Sources = new[] { this.DATAFOLDER },
                    Settings = new[]
                    {
                        new BackupAndScheduleInputDto.SettingInputDto { Name = "--no-encryption", Value = "true" },
                        new BackupAndScheduleInputDto.SettingInputDto { Name = "snapshot-policy", Value = "Off" }
                    },
                    Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                    Metadata = new Dictionary<string, string>()
                }
            };

            var createResponse = await httpClient.PostAsJsonAsync("/api/v1/backups", request, JsonOptions).ConfigureAwait(false);
            createResponse.EnsureSuccessStatusCode();
            var backupId = (await createResponse.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false))?.ID
                ?? throw new InvalidOperationException("Sync job creation did not return an ID");
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var response = await httpClient.DeleteAsync($"/api/v1/backup/{backupId}?{RemoteDestinationDelete.QueryParameter}={RemoteDestinationDelete.PrimaryKey}").ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), body);
            using (var json = JsonDocument.Parse(body))
                Assert.That(GetStringProperty(json.RootElement, "helpId"), Is.EqualTo("RemoteDestinationsSyncJob"), body);
            Assert.That(File.Exists(Path.Combine(this.TARGETFOLDER, "sample.txt")), Is.True, "The synced copy should be kept");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates the folders of the main destination and two secondary destinations, and a file to back up
    /// </summary>
    private (string Primary, string First, string Second) PrepareDestinationFolders()
    {
        Directory.CreateDirectory(this.DATAFOLDER);
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Backup sample " + Guid.NewGuid());
        var folders = new[] { "primary", "first", "second" }
            .Select(x => Path.Combine(BASEFOLDER, "delete-destinations-" + x))
            .ToArray();
        foreach (var folder in folders)
        {
            if (Directory.Exists(folder))
                Directory.Delete(folder, true);
            Directory.CreateDirectory(folder);
        }
        return (folders[0], folders[1], folders[2]);
    }

    /// <summary>
    /// Creates a job with a main destination and up to two secondary destinations that are written
    /// after every backup, and runs a backup
    /// </summary>
    private async Task<string> CreateBackupWithSecondaryDestinationsAsync(HttpClient httpClient, string primary, string? first, string? second, string? otherKey = null)
    {
        var targets = new List<TargetUrlInputDto>();
        if (first != null)
            targets.Add(new TargetUrlInputDto { UrlKey = otherKey ?? FirstSecondaryKey, TargetUrl = BuildFileBackendUrl(first), Mode = "inline" });
        if (second != null)
            targets.Add(new TargetUrlInputDto { UrlKey = SecondSecondaryKey, TargetUrl = BuildFileBackendUrl(second), Mode = "inline" });
        if (first == null && second == null && otherKey != null)
        {
            var otherFolder = primary.TrimEnd(Path.DirectorySeparatorChar) + "-secondary";
            Directory.CreateDirectory(otherFolder);
            targets.Add(new TargetUrlInputDto { UrlKey = otherKey, TargetUrl = BuildFileBackendUrl(otherFolder), Mode = "inline" });
        }

        var request = new BackupAndScheduleInputDto
        {
            Backup = new BackupAndScheduleInputDto.BackupInputDto
            {
                Name = $"API integration destinations {Guid.NewGuid():N}",
                Description = "Integration test backup",
                TargetURL = BuildFileBackendUrl(primary),
                Sources = new[] { this.DATAFOLDER },
                Settings = new[]
                {
                    new BackupAndScheduleInputDto.SettingInputDto { Name = "--no-encryption", Value = "true" },
                    new BackupAndScheduleInputDto.SettingInputDto { Name = "snapshot-policy", Value = "Off" }
                },
                Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                Metadata = new Dictionary<string, string>(),
                AdditionalTargetURLs = targets
            }
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/backups", request, JsonOptions).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.That(response.IsSuccessStatusCode, Is.True, body);
        var backupId = (await response.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false))?.ID
            ?? throw new InvalidOperationException("Backup creation did not return an ID");

        // A secondary destination at the same place as the main one is not written by the backup
        if (first != primary)
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
        return backupId;
    }

    /// <summary>
    /// Asks for the job to be deleted and waits for the delete task to finish
    /// </summary>
    private static async Task<GetTaskStateDto> DeleteAndWaitAsync(HttpClient httpClient, string backupId, string query)
    {
        var response = await httpClient.DeleteAsync($"/api/v1/backup/{backupId}?{query}").ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        var delete = await response.Content.ReadFromJsonAsync<DeleteBackupOutputDto>(JsonOptions).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Delete response was empty");
        Assert.That(delete.ID, Is.Not.Null, $"{delete.Status}: {delete.Reason}");
        return await WaitForTaskToFinishAsync(httpClient, delete.ID!.Value).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the path of the database of a job
    /// </summary>
    private static async Task<string> GetDatabasePathAsync(HttpClient httpClient, string backupId)
    {
        var backup = await httpClient.GetFromJsonAsync<JsonElement>($"/api/v1/backup/{backupId}", JsonOptions).ConfigureAwait(false);
        var job = backup.TryGetProperty("backup", out var camel) ? camel : backup.GetProperty("Backup");
        var path = job.TryGetProperty("dbPath", out var value) || job.TryGetProperty("DBPath", out value) ? value.GetString() : null;
        return path ?? throw new InvalidOperationException("The job has no database path");
    }

    /// <summary>
    /// The backup files in a folder, by name, with a hash of their contents
    /// </summary>
    private static Dictionary<string, string> HashFolder(string folder)
        => Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(x => Path.GetFileName(x).StartsWith("duplicati-", StringComparison.Ordinal))
                .ToDictionary(x => Path.GetFileName(x), x => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(x))))
            : [];
}
