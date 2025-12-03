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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Common.IO;
using Duplicati.Server;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Endpoints.V1.Backup;
using Duplicati.WebserverCore.Dto;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;
using ServerProgram = Duplicati.Server.Program;

namespace Duplicati.UnitTest;

[NonParallelizable]
public class ServerApiIntegrationTests : BasicSetupHelper
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Test]
    [Category("Integration")]
    public async Task ServerBackupLifecycle()
    {
        var backupPassphrase = "integration-passphrase";
        var backupName = $"API integration backup {Guid.NewGuid():N}";
        var importRestoreFolder = Path.Combine(BASEFOLDER, "restored-from-import");
        Directory.CreateDirectory(importRestoreFolder);

        var sampleFilePath = Path.Combine(this.DATAFOLDER, "sample.txt");
        var expectedContents = $"Sample content {Guid.NewGuid():N}";
        File.WriteAllText(sampleFilePath, expectedContents);

        try
        {
            await WithAuthenticatedServerAsync(async httpClient =>
            {
                var backupId = await CreateBackupAsync(httpClient, backupPassphrase, backupName).ConfigureAwait(false);
                var listedBackup = await AssertBackupListedAsync(httpClient, backupId, backupName).ConfigureAwait(false);
                var expectedTargetUrl = BuildFileBackendUrl(this.TARGETFOLDER);
                Assert.That(listedBackup.TargetURL, Is.EqualTo(expectedTargetUrl), "Backup list should report the configured target");

                var runTask = await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
                Assert.That(runTask.ID, Is.GreaterThan(0), "Running the backup should return a task identifier");

                await DirectoryDeleteSafeAsync(this.RESTOREFOLDER).ConfigureAwait(false);
                Directory.CreateDirectory(this.RESTOREFOLDER);
                await RestoreAndVerifyAsync(httpClient, backupId, backupPassphrase, this.RESTOREFOLDER, expectedContents).ConfigureAwait(false);

                var exportBytes = await ExportConfigurationAsync(httpClient, backupId).ConfigureAwait(false);
                Assert.That(exportBytes.Length, Is.GreaterThan(0), "Export configuration should produce a payload");
                var importedBackupId = await ImportConfigurationAsync(httpClient, exportBytes).ConfigureAwait(false);
                Assert.That(importedBackupId, Is.Not.Empty.And.Not.EqualTo(backupId), "Import should register a distinct backup");

                var backupsAfterImport = await GetBackupsAsync(httpClient).ConfigureAwait(false);
                Assert.That(backupsAfterImport.Select(entry => entry.Backup.ID), Does.Contain(backupId), "Original backup should remain listed after import");
                Assert.That(backupsAfterImport.Select(entry => entry.Backup.ID), Does.Contain(importedBackupId), "Imported backup should be listed");

                await DirectoryDeleteSafeAsync(importRestoreFolder).ConfigureAwait(false);
                Directory.CreateDirectory(importRestoreFolder);
                await RestoreAndVerifyAsync(httpClient, importedBackupId, backupPassphrase, importRestoreFolder, expectedContents).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            SafeDeleteDirectory(importRestoreFolder);
        }
    }

    [Test]
    [Category("Integration")]
    public async Task ImportSupportsProvidingPassphraseOverride()
    {
        var backupPassphrase = "integration-passphrase";

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase).ConfigureAwait(false);
            var exportBytes = await ExportConfigurationAsync(httpClient, backupId, exportPasswords: false).ConfigureAwait(false);
            Assert.That(exportBytes.Length, Is.GreaterThan(0), "Export configuration should produce a payload");

            var importRequest = new ImportBackupInputDto(
                Convert.ToBase64String(exportBytes),
                cmdline: false,
                import_metadata: true,
                direct: true,
                temporary: false,
                passphrase: null,
                replace_settings: new Dictionary<string, string>() { { "settings.passphrase", backupPassphrase } }
            );

            var response = await httpClient.PostAsJsonAsync("/api/v1/backups/import", importRequest, JsonOptions).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ImportBackupOutputDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Import response was empty");

            Assert.That(result.Id, Is.Not.Null.And.Not.Empty, "Import should return a backup ID");

            var importedBackup = await AssertBackupListedAsync(httpClient, result.Id).ConfigureAwait(false);
            Assert.That(importedBackup.IsUnencryptedOrPassphraseStored, Is.True, "Imported backup should be ready to run without prompting for a passphrase");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task ServerMetadataEndpointsReturnData()
    {
        var entryAssemblyLocation = Duplicati.Library.Utility.Utility.getEntryAssembly().Location;
        var installationRoot = Path.GetDirectoryName(entryAssemblyLocation) ?? ".";
        var licensesRoot = Path.Combine(installationRoot, "licenses");
        var integrationLicenseFolder = Path.Combine(licensesRoot, $"integration-{Guid.NewGuid():N}");
        var licenseTitle = Path.GetFileName(integrationLicenseFolder);
        var licensesRootAlreadyExists = Directory.Exists(licensesRoot);

        Directory.CreateDirectory(integrationLicenseFolder);
        await File.WriteAllTextAsync(Path.Combine(integrationLicenseFolder, "license.txt"), "Integration test license").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(integrationLicenseFolder, "homepage.txt"), "https://duplicati.com/integration-test").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(integrationLicenseFolder, "licensedata.json"), "{\"license\":\"integration\"}").ConfigureAwait(false);

        try
        {
            await WithAuthenticatedServerAsync(async httpClient =>
            {
                var systemInfo = await httpClient.GetFromJsonAsync<JsonElement>("/api/v1/systeminfo", JsonOptions).ConfigureAwait(false);
                Assert.That(systemInfo.ValueKind, Is.EqualTo(JsonValueKind.Object), "System info should be returned as a JSON object");

                if (!systemInfo.TryGetProperty("apiVersion", out var apiVersionElement) && !systemInfo.TryGetProperty("APIVersion", out apiVersionElement))
                    Assert.Fail("System info payload did not contain an API version");
                Assert.That(apiVersionElement.GetInt32(), Is.GreaterThan(0), "API version should be a positive integer");

                if (!systemInfo.TryGetProperty("serverVersionName", out var serverVersionNameElement) && !systemInfo.TryGetProperty("ServerVersionName", out serverVersionNameElement))
                    Assert.Fail("System info payload did not contain a server version name");
                Assert.That(serverVersionNameElement.GetString(), Is.Not.Null.And.Not.Empty, "Server version name should be populated");

                var hasOptionsProperty = systemInfo.TryGetProperty("options", out var optionsElement) || systemInfo.TryGetProperty("Options", out optionsElement);
                Assert.That(hasOptionsProperty && optionsElement.ValueKind == JsonValueKind.Array, Is.True, "System information should include option metadata");

                var logPollResponse = await httpClient.GetAsync("/api/v1/logdata/poll?level=Warning&id=0&pagesize=25").ConfigureAwait(false);
                logPollResponse.EnsureSuccessStatusCode();
                var logPoll = await logPollResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(false);
                Assert.That(logPoll.ValueKind, Is.EqualTo(JsonValueKind.Array), "Log poll should return an array result");

                var logResponse = await httpClient.GetAsync("/api/v1/logdata/log?pagesize=25").ConfigureAwait(false);
                logResponse.EnsureSuccessStatusCode();
                var logRecords = await logResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(false);
                Assert.That(logRecords.ValueKind, Is.EqualTo(JsonValueKind.Array), "Log history should be returned as a JSON array");
                if (logRecords.GetArrayLength() > 0)
                {
                    var firstRecord = logRecords.EnumerateArray().First();
                    Assert.That(firstRecord.ValueKind, Is.EqualTo(JsonValueKind.Object), "Log entries should be JSON objects");
                    Assert.That(firstRecord.EnumerateObject().Any(), Is.True, "Log entry objects should expose columns");
                }

                var licenseResponse = await httpClient.GetAsync("/api/v1/licenses").ConfigureAwait(false);
                licenseResponse.EnsureSuccessStatusCode();
                var licenses = await licenseResponse.Content.ReadFromJsonAsync<LicenseDto[]>(JsonOptions).ConfigureAwait(false)
                                ?? throw new InvalidOperationException("License response was empty");
                Assert.That(licenses.Select(license => license.Title), Does.Contain(licenseTitle), "Licenses endpoint should include the integration license entry");
            }).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(integrationLicenseFolder))
                    Directory.Delete(integrationLicenseFolder, true);
            }
            catch
            {
                // Ignore cleanup errors
            }

            if (!licensesRootAlreadyExists)
            {
                try
                {
                    if (Directory.Exists(licensesRoot) && !Directory.EnumerateFileSystemEntries(licensesRoot).Any())
                        Directory.Delete(licensesRoot, true);
                }
                catch
                {
                    // Ignore cleanup errors
                }
            }
        }
    }

    [Test]
    [Category("Integration")]
    public async Task ServerRepairUpdateListsRootPaths()
    {
        var backupPassphrase = "repair-update-passphrase";
        var sampleFilePath = Path.Combine(this.DATAFOLDER, "sample.txt");
        File.WriteAllText(sampleFilePath, "Repair update sample");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            backupResponse.EnsureSuccessStatusCode();
            var backup = await backupResponse.Content.ReadFromJsonAsync<BackupGet.GetBackupResultDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Backup retrieval response was empty");
            Assert.That(backup.Backup.ID, Is.EqualTo(backupId), "Backup GET should return the created backup");

            var listFilesetsRequest = new ListFilesetsRequestDto { BackupId = backupId };
            var listFilesetsResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-filesets", listFilesetsRequest, JsonOptions).ConfigureAwait(false);
            listFilesetsResponse.EnsureSuccessStatusCode();
            var listFilesets = await listFilesetsResponse.Content.ReadFromJsonAsync<ListFilesetsResponseDto>(JsonOptions).ConfigureAwait(false)
                                ?? throw new InvalidOperationException("list-filesets response was empty");
            Assert.That(listFilesets.Data, Is.Not.Null.And.Not.Empty, "list-filesets should return at least one fileset");
            var latestFileset = listFilesets.Data!.OrderByDescending(fileset => fileset.Time).First();
            var timestamp = latestFileset.Time.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);

            var repairPayload = new Dictionary<string, object?>
            {
                ["only_paths"] = true,
                ["time"] = timestamp,
            };
            var repairResponse = await httpClient.PostAsync(
                $"/api/v1/backup/{backupId}/repairupdate",
                JsonContent.Create(repairPayload, options: JsonOptions)).ConfigureAwait(false);
            repairResponse.EnsureSuccessStatusCode();
            var repairTask = await repairResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                            ?? throw new InvalidOperationException("Repair update response was empty");
            await WaitForTaskCompletionAsync(httpClient, repairTask.ID).ConfigureAwait(false);

            var listFolderRequest = new ListFolderContentRequestDto
            {
                BackupId = backupId,
                Time = timestamp,
                Paths = null,
                Page = 0,
                PageSize = 0
            };
            var listFolderResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-folder", listFolderRequest, JsonOptions).ConfigureAwait(false);
            listFolderResponse.EnsureSuccessStatusCode();
            var listFolder = await listFolderResponse.Content.ReadFromJsonAsync<ListFolderContentResponseDto>(JsonOptions).ConfigureAwait(false)
                             ?? throw new InvalidOperationException("list-folder response was empty");
            Assert.That(listFolder.Success, Is.True, "list-folder should succeed");
            Assert.That(listFolder.Data, Is.Not.Null.And.Not.Empty, "list-folder should return folder entries");

            var expectedRoot = Util.AppendDirSeparator(Path.GetFullPath(this.DATAFOLDER));
            bool rootFound = listFolder.Data!
                .Where(entry => entry.IsDirectory)
                .Select(entry => Util.AppendDirSeparator(Path.GetFullPath(entry.Path)))
                .Any(entryPath => string.Equals(entryPath, expectedRoot, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));

            Assert.That(rootFound, Is.True, "list-folder root listing should include the original data folder");
        }).ConfigureAwait(false);
    }

    private async Task WithAuthenticatedServerAsync(Func<HttpClient, Task> testBody)
    {
        var serverPassword = "integration-test-password";
        var serverDataFolder = Path.Combine(BASEFOLDER, $"server-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(serverDataFolder);

        var previousDataFolderEnv = Environment.GetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME);
        Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, serverDataFolder);

        ApplicationSettings? applicationSettings = null;
        Task<int>? serverTask = null;

        try
        {
            applicationSettings = new ApplicationSettings();
            var serverArgs = new[]
            {
                $"--{WebServerLoader.OPTION_PORT}={GetFreeTcpPort()}",
                $"--{WebServerLoader.OPTION_INTERFACE}=127.0.0.1",
                $"--{WebServerLoader.OPTION_WEBSERVICE_PASSWORD}={serverPassword}",
                $"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}={serverDataFolder}",
                "--webservice-api-only=true"
            };

            ServerProgram.ServerStartedEvent.Reset();
            serverTask = RunServerInBackground(applicationSettings, serverArgs);

            if (!ServerProgram.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(60)))
                Assert.Fail("Server did not start within the allotted time");

            var port = ServerProgram.DuplicatiWebserver.Port;
            var baseUri = new Uri($"http://127.0.0.1:{port}");

            using var httpClient = new HttpClient { BaseAddress = baseUri };
            await AuthenticateAsync(httpClient, serverPassword).ConfigureAwait(false);

            await testBody(httpClient).ConfigureAwait(false);

            applicationSettings.SignalApplicationExit();
            if (serverTask != null)
                Assert.That(await serverTask.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false), Is.EqualTo(0));
        }
        finally
        {
            if (serverTask != null && !serverTask.IsCompleted)
            {
                applicationSettings?.SignalApplicationExit();
                try
                {
                    await serverTask.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
                }
                catch
                {
                    // Ignore shutdown errors during cleanup
                }
            }

            Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, previousDataFolderEnv);
            SafeDeleteDirectory(serverDataFolder);
            ServerProgram.ServerStartedEvent.Reset();
        }
    }

    private static async Task AuthenticateAsync(HttpClient httpClient, string password)
    {
        var response = await httpClient.PostAsJsonAsync("/api/v1/auth/login", new { password, rememberMe = true }, JsonOptions).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<AccessTokenOutputDto>(JsonOptions).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Authentication response was empty");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);
    }

    private string BuildFileBackendUrl(string folder)
    {
        var normalized = Util.AppendDirSeparator(Path.GetFullPath(folder));
        return new Uri(normalized).AbsoluteUri;
    }

    private async Task<string> CreateBackupAsync(HttpClient httpClient, string passphrase, string? backupName = null, string? targetFolder = null)
    {
        var backupTarget = targetFolder ?? this.TARGETFOLDER;
        Directory.CreateDirectory(backupTarget);

        var settings = new[]
        {
            new BackupAndScheduleInputDto.SettingInputDto { Name = "passphrase", Value = passphrase },
            new BackupAndScheduleInputDto.SettingInputDto { Name = "dblock-size", Value = "1mb" },
            new BackupAndScheduleInputDto.SettingInputDto { Name = "blocksize", Value = "50kb" },
            new BackupAndScheduleInputDto.SettingInputDto { Name = "compression-module", Value = "zip" },
            new BackupAndScheduleInputDto.SettingInputDto { Name = "snapshot-policy", Value = "Off" }
        };

        var request = new BackupAndScheduleInputDto
        {
            Backup = new BackupAndScheduleInputDto.BackupInputDto
            {
                Name = backupName ?? $"API integration backup {Guid.NewGuid():N}",
                Description = "Integration test backup",
                TargetURL = BuildFileBackendUrl(backupTarget),
                Sources = new[] { this.DATAFOLDER },
                Settings = settings,
                Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                Metadata = new Dictionary<string, string>()
            }
        };

        var response = await httpClient.PostAsJsonAsync("/api/v1/backups", request, JsonOptions).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Backup creation response was empty");
        if (string.IsNullOrWhiteSpace(result.ID))
            throw new InvalidOperationException("Backup creation did not return an ID");
        return result.ID;
    }

    private async Task RestoreAndVerifyAsync(HttpClient httpClient, string backupId, string passphrase, string restoreFolder, string expectedContents)
    {
        var restoreResponse = await httpClient.PostAsJsonAsync(
            $"/api/v1/backup/{backupId}/restore",
            new RestoreInputDto(null, passphrase, "now", restoreFolder, true, false, false),
            JsonOptions).ConfigureAwait(false);
        restoreResponse.EnsureSuccessStatusCode();
        var task = await restoreResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Restore start response was empty");

        await WaitForTaskCompletionAsync(httpClient, task.ID).ConfigureAwait(false);
        await AssertTaskCompletedAsync(httpClient, task.ID).ConfigureAwait(false);

        var restoredFiles = Directory.GetFiles(restoreFolder, "*", SearchOption.AllDirectories);
        Assert.That(restoredFiles.Length, Is.EqualTo(1), "Expected a single restored file");
        var restoredContent = await File.ReadAllTextAsync(restoredFiles[0]).ConfigureAwait(false);
        Assert.That(restoredContent, Is.EqualTo(expectedContents));
    }

    private async Task<TaskStartedDto> RunTaskAndWaitAsync(HttpClient httpClient, string relativeUrl)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, relativeUrl);
        var response = await httpClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var task = await response.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                   ?? throw new InvalidOperationException("Task response was empty");
        await WaitForTaskCompletionAsync(httpClient, task.ID).ConfigureAwait(false);
        await AssertTaskCompletedAsync(httpClient, task.ID).ConfigureAwait(false);
        return task;
    }

    private static async Task WaitForTaskCompletionAsync(HttpClient httpClient, long taskId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var state = await GetTaskStateAsync(httpClient, taskId).ConfigureAwait(false);

            if (string.Equals(state.Status, "Completed", StringComparison.OrdinalIgnoreCase))
                return;
            if (string.Equals(state.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(state.ErrorMessage ?? state.Exception ?? "Task failed");

            if (stopwatch.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException($"Task {taskId} did not complete in the allotted time");

            await Task.Delay(250).ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ExportConfigurationAsync(HttpClient httpClient, string backupId, bool exportPasswords = true)
    {
        var tokenResponse = await httpClient.PostAsync("/api/v1/auth/issuetoken/export", null).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        var token = await tokenResponse.Content.ReadFromJsonAsync<SingleOperationTokenOutputDto>(JsonOptions).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("Token response was empty");

        var exportResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}/export?token={Uri.EscapeDataString(token.Token)}&export-passwords={exportPasswords.ToString().ToLowerInvariant()}").ConfigureAwait(false);
        exportResponse.EnsureSuccessStatusCode();
        return await exportResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private static async Task<string> ImportConfigurationAsync(HttpClient httpClient, byte[] configBytes)
    {
        var importRequest = new ImportBackupInputDto(
            Convert.ToBase64String(configBytes),
            cmdline: false,
            import_metadata: true,
            direct: true,
            temporary: false,
            passphrase: null,
            replace_settings: null);

        var response = await httpClient.PostAsJsonAsync("/api/v1/backups/import", importRequest, JsonOptions).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ImportBackupOutputDto>(JsonOptions).ConfigureAwait(false)
                     ?? throw new InvalidOperationException("Import response was empty");
        if (string.IsNullOrWhiteSpace(result.Id))
            throw new InvalidOperationException("Import did not return a backup ID");
        return result.Id;
    }

    private static async Task<BackupAndScheduleOutputDto[]> GetBackupsAsync(HttpClient httpClient)
    {
        var response = await httpClient.GetAsync("/api/v1/backups").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<BackupAndScheduleOutputDto[]>(JsonOptions).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Backups list response was empty");
    }

    private static async Task<BackupDto> AssertBackupListedAsync(HttpClient httpClient, string backupId, string? expectedName = null)
    {
        var backups = await GetBackupsAsync(httpClient).ConfigureAwait(false);
        Assert.That(backups, Is.Not.Null.And.Not.Empty, "Backup list should not be empty");
        var match = backups
            .Select(entry => entry.Backup)
            .FirstOrDefault(backup => string.Equals(backup.ID, backupId, StringComparison.Ordinal));

        Assert.That(match, Is.Not.Null, $"Backup list should contain backup '{backupId}'");
        if (expectedName != null)
            Assert.That(match!.Name, Is.EqualTo(expectedName), "Backup list should report the expected name");

        return match!;
    }

    private static async Task AssertTaskCompletedAsync(HttpClient httpClient, long taskId)
    {
        var state = await GetTaskStateAsync(httpClient, taskId).ConfigureAwait(false);
        Assert.That(state.Status, Is.EqualTo("Completed").IgnoreCase, $"Task {taskId} should be completed");
        Assert.That(state.TaskFinished, Is.Not.Null, $"Task {taskId} should report a completion time");
        Assert.That(state.ErrorMessage, Is.Null, $"Task {taskId} should not report an error message");
        Assert.That(state.Exception, Is.Null, $"Task {taskId} should not report an exception");
    }

    private static async Task<GetTaskStateDto> GetTaskStateAsync(HttpClient httpClient, long taskId)
    {
        var response = await httpClient.GetAsync($"/api/v1/task/{taskId}").ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GetTaskStateDto>(JsonOptions).ConfigureAwait(false)
               ?? throw new InvalidOperationException("Task state response was empty");
    }

    private static Task<int> RunServerInBackground(ApplicationSettings applicationSettings, string[] args)
    {
        var tcs = new TaskCompletionSource<int>();
        var thread = new Thread(() =>
        {
            try
            {
                var exitCode = ServerProgram.Main(applicationSettings, args);
                tcs.TrySetResult(exitCode);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true
        };
        if (OperatingSystem.IsWindows())
            thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task DirectoryDeleteSafeAsync(string path)
    {
        if (!Directory.Exists(path))
            return;

        var retries = 5;
        while (true)
        {
            try
            {
                Directory.Delete(path, true);
                return;
            }
            catch (IOException) when (--retries >= 0)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (--retries >= 0)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
