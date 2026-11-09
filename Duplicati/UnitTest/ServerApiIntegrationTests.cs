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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.Common.IO;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Utility;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Dto.V2;
using Duplicati.WebserverCore.Endpoints.V1.Backup;
using Duplicati.WebserverCore.Abstractions;
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
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Test]
    [Category("Integration")]
    public async Task ServerBackupLifecycle_Async()
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
    public async Task ImportSupportsProvidingPassphraseOverride_Async()
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

            var importedBackup = await AssertBackupListedAsync(httpClient, result.Id!).ConfigureAwait(false);
            Assert.That(importedBackup.IsUnencryptedOrPassphraseStored, Is.True, "Imported backup should be ready to run without prompting for a passphrase");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task HttpClientHelperStillCreatesClientsAfterTheServerStops_Async()
    {
        await WithAuthenticatedServerAsync(_ => Task.CompletedTask).ConfigureAwait(false);

        // Once the webserver has terminated, its service provider and client factory are disposed
        await ServerProgram.DuplicatiWebserver.TerminationTask.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);

        Assert.DoesNotThrow(() => HttpClientHelper.CreateClient().Dispose(),
            "The helper must not keep using the client factory of a stopped server");
    }

    [Test]
    [Category("Integration")]
    public async Task SystemInfoReportsActiveSecretProvider_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var systemInfo = await httpClient.GetFromJsonAsync<JsonElement>("/api/v1/systeminfo", JsonOptions).ConfigureAwait(false);

            var hasActiveSecretProvider = systemInfo.TryGetProperty("activeSecretProvider", out var provider) || systemInfo.TryGetProperty("ActiveSecretProvider", out provider);
            Assert.That(hasActiveSecretProvider, Is.True, "System information should include the active secret provider field");
            Assert.That(provider.ValueKind, Is.EqualTo(JsonValueKind.Object), "The active secret provider should be reported as an object");

            Assert.That(GetStringProperty(provider, "key"), Is.EqualTo("env"), "The provider key should be the configured URL scheme");
            Assert.That(GetStringProperty(provider, "displayName"), Is.Not.Null.And.Not.Empty, "The provider display name should be populated");
            Assert.That(GetStringProperty(provider, "cachingLevel"), Is.EqualTo("InMemory"), "The caching level should reflect the server argument");
            Assert.That(GetStringProperty(provider, "pattern"), Is.EqualTo("$"), "The pattern should be the default when not configured");

            var hasIsSetSupported = provider.TryGetProperty("isSetSupported", out var isSetSupported) || provider.TryGetProperty("IsSetSupported", out isSetSupported);
            Assert.That(hasIsSetSupported && isSetSupported.ValueKind == JsonValueKind.False, Is.True, "The environment provider does not support storing secrets");

            var hasDefaultSecretProvider = systemInfo.TryGetProperty("defaultSecretProvider", out var defaultProvider) || systemInfo.TryGetProperty("DefaultSecretProvider", out defaultProvider);
            Assert.That(hasDefaultSecretProvider, Is.True, "System information should include the default secret provider field");
            Assert.That(defaultProvider.ValueKind, Is.EqualTo(JsonValueKind.Null), "No default secret provider should be reported when it is disabled");
        }, null, "127.0.0.1", ["--secret-provider=env://", "--secret-provider-cache=InMemory", "--disable-default-secret-provider=true"]).ConfigureAwait(false);
    }

    private static string? GetStringProperty(JsonElement element, string camelCaseName)
    {
        var pascalCaseName = char.ToUpperInvariant(camelCaseName[0]) + camelCaseName[1..];
        if (element.TryGetProperty(camelCaseName, out var value) || element.TryGetProperty(pascalCaseName, out value))
            return value.GetString();

        Assert.Fail($"Property {camelCaseName} was not found");
        return null;
    }

    [Test]
    [Category("Integration")]
    public async Task ServerMetadataEndpointsReturnData_Async()
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

                var hasActiveSecretProvider = systemInfo.TryGetProperty("activeSecretProvider", out var activeSecretProviderElement) || systemInfo.TryGetProperty("ActiveSecretProvider", out activeSecretProviderElement);
                Assert.That(hasActiveSecretProvider, Is.True, "System information should include the active secret provider field");
                Assert.That(activeSecretProviderElement.ValueKind, Is.EqualTo(JsonValueKind.Null), "No secret provider should be reported when the server runs without one");

                // The default provider is the OS credential store, which is always present on Windows and macOS
                // but depends on libsecret being available on Linux
                var hasDefaultSecretProvider = systemInfo.TryGetProperty("defaultSecretProvider", out var defaultSecretProviderElement) || systemInfo.TryGetProperty("DefaultSecretProvider", out defaultSecretProviderElement);
                Assert.That(hasDefaultSecretProvider, Is.True, "System information should include the default secret provider field");
                if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                {
                    Assert.That(defaultSecretProviderElement.ValueKind, Is.EqualTo(JsonValueKind.Object), "The default secret provider should be reported on Windows and macOS");
                    Assert.That(GetStringProperty(defaultSecretProviderElement, "key"), Is.EqualTo(OperatingSystem.IsWindows() ? "wincred" : "keychain"), "The default provider should be the OS credential store");
                    Assert.That(GetStringProperty(defaultSecretProviderElement, "cachingLevel"), Is.Null, "The default provider does not resolve option secrets, so it has no caching level");
                    Assert.That(GetStringProperty(defaultSecretProviderElement, "pattern"), Is.Null, "The default provider does not resolve option secrets, so it has no pattern");
                }
                else
                {
                    Assert.That(defaultSecretProviderElement.ValueKind, Is.AnyOf(JsonValueKind.Object, JsonValueKind.Null), "The default secret provider should be an object or null");
                }

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
    public async Task ServerBackupListMasksTargetUrl_Async()
    {
        var secret = $"secret-{Guid.NewGuid():N}";
        var backupName = $"API masking backup {Guid.NewGuid():N}";
        Directory.CreateDirectory(this.TARGETFOLDER);
        var targetUrl = BuildFileBackendUrl(this.TARGETFOLDER) + "?auth-username=user&auth-password=" + secret;

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var request = new BackupAndScheduleInputDto
            {
                Backup = new BackupAndScheduleInputDto.BackupInputDto
                {
                    Name = backupName,
                    Description = "Masking test backup",
                    TargetURL = targetUrl,
                    Sources = new[] { this.DATAFOLDER },
                    Settings = new[] { new BackupAndScheduleInputDto.SettingInputDto { Name = "passphrase", Value = "integration-passphrase" } },
                    Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                    Metadata = new Dictionary<string, string>()
                }
            };

            var response = await httpClient.PostAsJsonAsync("/api/v1/backups", request, JsonOptions).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var created = await response.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false)
                          ?? throw new InvalidOperationException("Backup creation response was empty");

            var listedBackup = await AssertBackupListedAsync(httpClient, created.ID!, backupName).ConfigureAwait(false);
            Assert.That(listedBackup.TargetURL, Does.Not.Contain(secret), "Backup list should not expose the target URL password");
            Assert.That(listedBackup.TargetURL, Does.Contain(Duplicati.Server.Database.Connection.PASSWORD_PLACEHOLDER), "Backup list should mask the target URL password");
            Assert.That(listedBackup.TargetURL, Does.Contain("auth-username=user"), "Backup list should keep non-secret query parameters");

            var getResponse = await httpClient.GetAsync($"/api/v1/backup/{created.ID}").ConfigureAwait(false);
            getResponse.EnsureSuccessStatusCode();
            var single = await getResponse.Content.ReadFromJsonAsync<BackupGet.GetBackupResultDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Backup get response was empty");
            Assert.That(single.Backup.TargetURL, Is.EqualTo(listedBackup.TargetURL), "List and single-backup GET should mask the target URL the same way");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task BackupCanBeDisabledAndEnabledThroughTheApi_Async()
    {
        const string passphrase = "integration-passphrase";
        var name = $"Enabled flag backup {Guid.NewGuid():N}";

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            async Task<bool> IsEnabledAsync(string id)
            {
                var listed = await AssertBackupListedAsync(httpClient, id).ConfigureAwait(false);
                var getResponse = await httpClient.GetAsync($"/api/v1/backup/{id}").ConfigureAwait(false);
                getResponse.EnsureSuccessStatusCode();
                var single = await getResponse.Content.ReadFromJsonAsync<BackupGet.GetBackupResultDto>(JsonOptions).ConfigureAwait(false)
                             ?? throw new InvalidOperationException("Backup get response was empty");
                Assert.That(single.Backup.Enabled, Is.EqualTo(listed.Enabled), "List and single-backup GET should agree on the enabled flag");
                return listed.Enabled;
            }

            Assert.That(await GetApiExtensionsAsync(httpClient).ConfigureAwait(false), Does.Contain(SystemInfoProvider.BackupEnableDisableExtension),
                "System info should advertise that backups can be enabled and disabled");

            var backupId = await CreateBackupAsync(httpClient, passphrase, name).ConfigureAwait(false);
            Assert.That(await IsEnabledAsync(backupId).ConfigureAwait(false), Is.True, "A new backup should be enabled");

            (await httpClient.PostAsync($"/api/v1/backup/{backupId}/disable", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
            Assert.That(await IsEnabledAsync(backupId).ConfigureAwait(false), Is.False, "Disabling should be reflected by the API");

            // A client that does not know about the flag must not re-enable the backup when it saves it
            var update = new BackupAndScheduleInputDto
            {
                Backup = new BackupAndScheduleInputDto.BackupInputDto
                {
                    Name = name,
                    Description = "Updated without the enabled flag",
                    TargetURL = BuildFileBackendUrl(this.TARGETFOLDER),
                    Sources = new[] { this.DATAFOLDER },
                    Settings = new[] { new BackupAndScheduleInputDto.SettingInputDto { Name = "passphrase", Value = passphrase } },
                    Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                    Metadata = new Dictionary<string, string>()
                }
            };
            (await httpClient.PutAsJsonAsync($"/api/v1/backup/{backupId}", update, JsonOptions).ConfigureAwait(false)).EnsureSuccessStatusCode();
            Assert.That(await IsEnabledAsync(backupId).ConfigureAwait(false), Is.False, "Saving without the flag should keep the backup disabled");

            // An exported disabled backup is imported as disabled
            var exportBytes = await ExportConfigurationAsync(httpClient, backupId).ConfigureAwait(false);
            var importedId = await ImportConfigurationAsync(httpClient, exportBytes).ConfigureAwait(false);
            Assert.That(await IsEnabledAsync(importedId).ConfigureAwait(false), Is.False, "Import should keep the disabled flag");

            (await httpClient.PostAsync($"/api/v1/backup/{backupId}/enable", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
            Assert.That(await IsEnabledAsync(backupId).ConfigureAwait(false), Is.True, "Enabling should be reflected by the API");

            var missing = await httpClient.PostAsync("/api/v1/backup/999999/disable", null).ConfigureAwait(false);
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "Disabling an unknown backup should return 404");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task BackupEnableDisableExtensionCanBeHidden_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var extensions = await GetApiExtensionsAsync(httpClient).ConfigureAwait(false);
            Assert.That(extensions, Does.Not.Contain(SystemInfoProvider.BackupEnableDisableExtension),
                "A disabled API extension should not be reported by system info");
            Assert.That(extensions, Is.Not.Empty, "Hiding one extension should keep the others");
        }, null, "127.0.0.1", [$"--webservice-disable-api-extensions={SystemInfoProvider.BackupEnableDisableExtension}"]).ConfigureAwait(false);
    }

    private static async Task<string[]> GetApiExtensionsAsync(HttpClient httpClient)
    {
        var systemInfo = await httpClient.GetFromJsonAsync<JsonElement>("/api/v1/systeminfo", JsonOptions).ConfigureAwait(false);
        var found = systemInfo.TryGetProperty("apiExtensions", out var extensions) || systemInfo.TryGetProperty("APIExtensions", out extensions);
        Assert.That(found, Is.True, "System information should include the API extensions");
        return extensions.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
    }

    // The trailing semicolon is the value from issue #7425; the second value has an interval
    // (48 months) bigger than the timeframe it is in (2 years), which the issue ran into next.
    [TestCase("2W:U,2M:60D,4M:16W,2Y:48M;")]
    [TestCase("2W:U,2M:60D,4M:16W,2Y:48M")]
    [Category("Integration")]
    public async Task SavingABackupWithAnInvalidRetentionPolicyIsRejected_Async(string retentionPolicy)
    {
        const string validRetentionPolicy = "1W:1D,4W:1W,12M:1M";
        Directory.CreateDirectory(this.TARGETFOLDER);

        BackupAndScheduleInputDto CreateRequest(string name, string policy) => new()
        {
            Backup = new BackupAndScheduleInputDto.BackupInputDto
            {
                Name = name,
                Description = "Retention policy validation test",
                TargetURL = BuildFileBackendUrl(this.TARGETFOLDER),
                Sources = new[] { this.DATAFOLDER },
                Settings = new[]
                {
                    new BackupAndScheduleInputDto.SettingInputDto { Name = "passphrase", Value = "integration-passphrase" },
                    new BackupAndScheduleInputDto.SettingInputDto { Name = "retention-policy", Value = policy }
                },
                Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                Metadata = new Dictionary<string, string>()
            }
        };

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var createInvalid = await httpClient.PostAsJsonAsync("/api/v1/backups", CreateRequest($"Invalid retention {Guid.NewGuid():N}", retentionPolicy), JsonOptions).ConfigureAwait(false);
            var createInvalidBody = await createInvalid.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(createInvalid.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
                $"Creating a backup with the retention policy '{retentionPolicy}' should be rejected; got {(int)createInvalid.StatusCode}: {createInvalidBody}");
            Assert.That(createInvalidBody, Does.Contain("retention-policy"), "The error should name the option");

            // The issue edits an existing backup, so the update must be rejected as well, and leave the stored value alone
            var name = $"Valid retention {Guid.NewGuid():N}";
            var createValid = await httpClient.PostAsJsonAsync("/api/v1/backups", CreateRequest(name, validRetentionPolicy), JsonOptions).ConfigureAwait(false);
            var createValidBody = await createValid.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(createValid.IsSuccessStatusCode, Is.True,
                $"Creating a backup with the retention policy '{validRetentionPolicy}' should succeed; got {(int)createValid.StatusCode}: {createValidBody}");
            var created = JsonSerializer.Deserialize<CreateBackupDto>(createValidBody, JsonOptions)
                          ?? throw new InvalidOperationException("Backup creation response was empty");

            var update = await httpClient.PutAsJsonAsync($"/api/v1/backup/{created.ID}", CreateRequest(name, retentionPolicy), JsonOptions).ConfigureAwait(false);
            var updateBody = await update.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
                $"Changing the retention policy to '{retentionPolicy}' should be rejected; got {(int)update.StatusCode}: {updateBody}");
            Assert.That(updateBody, Does.Contain("retention-policy"), "The error should name the option");

            var getResponse = await httpClient.GetAsync($"/api/v1/backup/{created.ID}").ConfigureAwait(false);
            getResponse.EnsureSuccessStatusCode();
            var stored = await getResponse.Content.ReadFromJsonAsync<BackupGet.GetBackupResultDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Backup get response was empty");
            Assert.That(stored.Backup.Settings?.Single(x => x.Name == "retention-policy").Value, Is.EqualTo(validRetentionPolicy),
                "A rejected update should not change the stored retention policy");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task ServerRepairUpdateListsRootPaths_Async()
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
                PageSize = 0,
                ReturnExtended = false
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

    /// <summary>
    /// A restore that could not restore a file finishes without throwing and reports the
    /// failure as an error in its results. The task state has to carry an error message then,
    /// as that is what the web UI uses to tell a failed restore from a successful one
    /// (#4644). The status stays "Completed", which clients use to see that the task is done.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task RestoreWithErrorsReportsAnErrorMessage_Async()
    {
        var backupPassphrase = "integration-passphrase";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample content");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            // A file in the way that the restore cannot read, so checking it fails
            await DirectoryDeleteSafeAsync(this.RESTOREFOLDER).ConfigureAwait(false);
            Directory.CreateDirectory(this.RESTOREFOLDER);
            var blocked = Path.Combine(this.RESTOREFOLDER, "sample.txt");
            File.WriteAllText(blocked, "Other content");

            FileStream? holder = null;
            if (OperatingSystem.IsWindows())
                holder = new FileStream(blocked, FileMode.Open, FileAccess.Read, FileShare.None);
            else
                File.SetUnixFileMode(blocked, UnixFileMode.None);

            try
            {
                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        using (File.OpenRead(blocked)) { }
                        Assert.Ignore("The file can still be read (running as root?), so the restore would not fail");
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                var restoreResponse = await httpClient.PostAsJsonAsync(
                    $"/api/v1/backup/{backupId}/restore",
                    new RestoreInputDto(null, backupPassphrase, "now", this.RESTOREFOLDER, true, false, false, null, null),
                    JsonOptions).ConfigureAwait(false);
                restoreResponse.EnsureSuccessStatusCode();
                var task = await restoreResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("Restore start response was empty");

                var state = await WaitForTaskToFinishAsync(httpClient, task.ID).ConfigureAwait(false);
                Assert.That(state.Status, Is.EqualTo("Completed").IgnoreCase, "The restore ran to the end");
                Assert.That(state.ErrorMessage, Is.Not.Null.And.Not.Empty, "The restore that could not restore the file was reported without an error");
            }
            finally
            {
                holder?.Dispose();
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(blocked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The restore described in #4644 and #4051: the destination is not there, for instance
    /// because the drive holding it is not connected. The restore fails, and the task is
    /// reported as "Failed" with the reason rather than as a success.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task RestoreFromAMissingDestinationReportsFailure_Async()
    {
        var backupPassphrase = "integration-passphrase";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample content");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            // No retries, so the missing destination is reported without waiting for them
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase,
                extraSettings: [new BackupAndScheduleInputDto.SettingInputDto { Name = "number-of-retries", Value = "0" }]).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            await DirectoryDeleteSafeAsync(this.RESTOREFOLDER).ConfigureAwait(false);
            Directory.CreateDirectory(this.RESTOREFOLDER);

            var moved = this.TARGETFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-moved";
            Directory.Move(this.TARGETFOLDER, moved);
            try
            {
                var restoreResponse = await httpClient.PostAsJsonAsync(
                    $"/api/v1/backup/{backupId}/restore",
                    new RestoreInputDto(null, backupPassphrase, "now", this.RESTOREFOLDER, true, false, false, null, null),
                    JsonOptions).ConfigureAwait(false);
                restoreResponse.EnsureSuccessStatusCode();
                var task = await restoreResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("Restore start response was empty");

                var state = await WaitForTaskToFinishAsync(httpClient, task.ID).ConfigureAwait(false);
                Assert.That(state.Status, Is.EqualTo("Failed").IgnoreCase, "The restore from a missing destination was not reported as failed");
                Assert.That(state.ErrorMessage, Is.Not.Null.And.Not.Empty, "The failed restore was reported without a reason");
            }
            finally
            {
                Directory.Move(moved, this.TARGETFOLDER);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The runner stamps a task as finished before the task's failure is stored. A client that
    /// asked for the state of the task in between was told it had completed, and the restore
    /// from a missing destination above was then reported as a success. The state is read when
    /// the runner sends the progress update that follows the stamp.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task FailedTaskIsNotReportedAsCompletedBeforeItsFailureIsStored_Async()
    {
        var backupPassphrase = "integration-passphrase";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample content");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase,
                extraSettings: [new BackupAndScheduleInputDto.SettingInputDto { Name = "number-of-retries", Value = "0" }]).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var provider = ServerProgram.DuplicatiWebserver.Provider;
            var eventPollNotify = (EventPollNotify)provider.GetService(typeof(EventPollNotify))!;
            var queueRunner = (IQueueRunnerService)provider.GetService(typeof(IQueueRunnerService))!;
            var taskQueue = (ITaskQueueService)provider.GetService(typeof(ITaskQueueService))!;

            // The state of each task that is current and stamped as finished, as a client
            // asking at that moment would get it
            var seen = new ConcurrentQueue<(long TaskID, string Status)>();
            void OnProgressUpdate(object? sender, Func<IProgressEventData>? progress)
            {
                var current = queueRunner.GetCurrentTask();
                if (current?.TaskFinished != null)
                    seen.Enqueue((current.TaskID, taskQueue.GetTaskInfo(current.TaskID).Status));
            }

            var moved = this.TARGETFOLDER.TrimEnd(Path.DirectorySeparatorChar) + "-moved";
            Directory.Move(this.TARGETFOLDER, moved);
            eventPollNotify.ProgressUpdate += OnProgressUpdate;
            try
            {
                var restoreResponse = await httpClient.PostAsJsonAsync(
                    $"/api/v1/backup/{backupId}/restore",
                    new RestoreInputDto(null, backupPassphrase, "now", this.RESTOREFOLDER, true, false, false, null, null),
                    JsonOptions).ConfigureAwait(false);
                restoreResponse.EnsureSuccessStatusCode();
                var task = await restoreResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                           ?? throw new InvalidOperationException("Restore start response was empty");

                var state = await WaitForTaskToFinishAsync(httpClient, task.ID).ConfigureAwait(false);
                await WaitForTaskResultAsync(task.ID).ConfigureAwait(false);

                var states = seen.Where(x => x.TaskID == task.ID).Select(x => x.Status).ToList();
                TestContext.Progress.WriteLine($"Restore task {task.ID}: polled {state.Status}, seen while finishing: {string.Join(", ", states)}");
                Assert.That(states, Is.Not.Empty, "The restore was not seen while it was finishing");
                Assert.That(states, Has.None.EqualTo("Completed").IgnoreCase, "The failed restore was reported as completed while it was finishing");
            }
            finally
            {
                eventPollNotify.ProgressUpdate -= OnProgressUpdate;
                Directory.Move(moved, this.TARGETFOLDER);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The transferred totals in the progress count every completed transfer. A local destination
    /// copies a volume in milliseconds, so its transfers are rarely seen as active; counting only
    /// the active transfers showed no backend activity at all.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task ProgressCountsTransfersThatCompleteBetweenReads_Async()
    {
        var rnd = new Random(3);
        for (var i = 0; i < 4; i++)
        {
            var data = new byte[1024 * 1024];
            rnd.NextBytes(data);
            File.WriteAllBytes(Path.Combine(this.DATAFOLDER, $"file{i}.bin"), data);
        }

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, "integration-passphrase").ConfigureAwait(false);

            var eventPollNotify = (EventPollNotify)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(EventPollNotify))!;
            IProgressEventData? last = null;
            void OnProgressUpdate(object? sender, Func<IProgressEventData>? progress)
            {
                var value = progress?.Invoke();
                if (value?.BackupID == backupId)
                    last = value;
            }

            eventPollNotify.ProgressUpdate += OnProgressUpdate;
            try
            {
                await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
            }
            finally
            {
                eventPollNotify.ProgressUpdate -= OnProgressUpdate;
            }

            var uploadedFiles = Directory.GetFiles(this.TARGETFOLDER).Sum(x => new FileInfo(x).Length);
            Assert.That(last, Is.Not.Null, "no progress was reported for the backup");
            Assert.That(last!.BackendBytesUploaded, Is.EqualTo(uploadedFiles), "every uploaded volume is counted");
            Assert.That(last.BackendBytesDownloaded, Is.GreaterThanOrEqualTo(0));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A backup that warns on every run keeps one notification, for its latest run, rather than
    /// one more for each run (#6420). Before, the notifications piled up into the thousands,
    /// which made dismissing them all hang the browser (#4719).
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task BackupWarningReplacesTheNotificationOfTheEarlierRun_Async()
    {
        var backupPassphrase = "integration-passphrase";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample content");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            // An option the backup does not know makes each run end with a warning
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase,
                extraSettings: [new BackupAndScheduleInputDto.SettingInputDto { Name = "not-a-real-option", Value = "true" }]).ConfigureAwait(false);

            var connection = (Duplicati.Server.Database.Connection)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(Duplicati.Server.Database.Connection))!;
            var otherNotifications = connection.GetNotifications().Count(n => n.BackupID != backupId);

            for (var i = 0; i < 3; i++)
                await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var notifications = connection.GetNotifications();
            var forBackup = notifications.Where(n => n.BackupID == backupId).ToList();
            Assert.That(forBackup.Select(n => n.Type), Is.EqualTo(new[] { Duplicati.Server.Serialization.NotificationType.Warning }),
                $"Expected one warning notification for the backup, got: {string.Join(" | ", forBackup.Select(n => $"{n.Type} {n.Title}: {n.Message}"))}");
            Assert.That(notifications.Count(n => n.BackupID != backupId), Is.EqualTo(otherNotifications),
                "The notifications of others were changed");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The server stores the task configuration in the backup when --store-task-config is set,
    /// and takes the option out before running the backup. It was left in for the other
    /// operations, so a restore of such a backup warned that the option is not supported
    /// (forum topic 22816).
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task StoreTaskConfigIsNotPassedToOtherOperations_Async()
    {
        var backupPassphrase = "integration-passphrase";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample content");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase,
                extraSettings: [new BackupAndScheduleInputDto.SettingInputDto { Name = "store-task-config", Value = "Self" }]).ConfigureAwait(false);
            var connection = (Duplicati.Server.Database.Connection)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(Duplicati.Server.Database.Connection))!;

            void AssertNoWarningForTheOption(string operation)
            {
                var notifications = connection.GetNotifications().Where(n => n.BackupID == backupId).ToList();
                Assert.That(notifications.Where(n => n.Message?.Contains("store-task-config") ?? false), Is.Empty,
                    $"The {operation} warned about the option: {string.Join(" | ", notifications.Select(n => $"{n.Type} {n.Title}: {n.Message}"))}");
            }

            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
            AssertNoWarningForTheOption("backup");

            await DirectoryDeleteSafeAsync(this.RESTOREFOLDER).ConfigureAwait(false);
            Directory.CreateDirectory(this.RESTOREFOLDER);
            await RestoreAndVerifyAsync(httpClient, backupId, backupPassphrase, this.RESTOREFOLDER, "Sample content").ConfigureAwait(false);
            AssertNoWarningForTheOption("restore");

            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/verify").ConfigureAwait(false);
            AssertNoWarningForTheOption("verify");

            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/compact").ConfigureAwait(false);
            AssertNoWarningForTheOption("compact");

            // The backup still stores the task configuration
            var configResponse = await httpClient.PostAsync($"/api/v1/backup/{backupId}/restore-task-config", null).ConfigureAwait(false);
            configResponse.EnsureSuccessStatusCode();
            var storedConfig = await configResponse.Content.ReadFromJsonAsync<JsonElement>(JsonOptions).ConfigureAwait(false);
            Assert.That(storedConfig.ValueKind == JsonValueKind.Array ? storedConfig.GetArrayLength() : -1, Is.EqualTo(1),
                $"The backup did not store its task configuration: {storedConfig}");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The old UI lists the files of one version to choose what to restore. The all-versions option
    /// of the backup or the server must not turn that into a list of every version, or it shows
    /// files that the restore of the version then leaves out (#5055).
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task FileListOfOneVersionIgnoresTheAllVersionsSetting_Async()
    {
        var backupPassphrase = "integration-passphrase";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "kept.txt"), "kept");
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "deleted.txt"), "deleted");

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var backupId = await CreateBackupAsync(httpClient, backupPassphrase,
                extraSettings: [new BackupAndScheduleInputDto.SettingInputDto { Name = "all-versions", Value = "true" }]).ConfigureAwait(false);

            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
            File.Delete(Path.Combine(this.DATAFOLDER, "deleted.txt"));
            await Task.Delay(TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            var listFilesetsResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-filesets", new ListFilesetsRequestDto { BackupId = backupId }, JsonOptions).ConfigureAwait(false);
            listFilesetsResponse.EnsureSuccessStatusCode();
            var listFilesets = await listFilesetsResponse.Content.ReadFromJsonAsync<ListFilesetsResponseDto>(JsonOptions).ConfigureAwait(false)
                               ?? throw new InvalidOperationException("list-filesets response was empty");
            Assert.That(listFilesets.Data, Has.Count.EqualTo(2));
            var latest = listFilesets.Data!.OrderByDescending(fileset => fileset.Time).First().Time.ToUniversalTime().ToString("o", System.Globalization.CultureInfo.InvariantCulture);
            var folderFilter = Uri.EscapeDataString("@" + Util.AppendDirSeparator(this.DATAFOLDER));

            async Task<string[]> ListFileNamesAsync(string query)
            {
                var response = await httpClient.GetAsync($"/api/v1/backup/{backupId}/files?prefix-only=false&folder-contents=true&filter={folderFilter}&{query}").ConfigureAwait(false);
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
                var files = JsonDocument.Parse(body).RootElement.EnumerateObject()
                    .Single(p => string.Equals(p.Name, "Files", StringComparison.OrdinalIgnoreCase)).Value;
                return files.EnumerateArray()
                    .Select(f => f.EnumerateObject().Single(p => string.Equals(p.Name, "Path", StringComparison.OrdinalIgnoreCase)).Value.GetString()!)
                    .Select(Path.GetFileName)
                    .Where(n => !string.IsNullOrEmpty(n))
                    .Select(n => n!)
                    .ToArray();
            }

            // The latest version, which is what the restore of that version gives back
            Assert.That(await ListFileNamesAsync($"time={Uri.EscapeDataString(latest)}").ConfigureAwait(false), Is.EquivalentTo(new[] { "kept.txt" }));

            // Asking for every version still lists the deleted file
            Assert.That(await ListFileNamesAsync("all-versions=true").ConfigureAwait(false), Does.Contain("deleted.txt"));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A scheduled run that is due while the server is paused, as it is for a while after a start,
    /// waits in the queue. If the schedule is removed in the meantime, the run is no longer wanted
    /// and must not start when the server resumes (#5045). A run whose schedule is kept still starts.
    /// </summary>
    [Test]
    [Category("Integration")]
    [TestCase(true)]
    [TestCase(false)]
    public async Task QueuedScheduledRunStartsOnlyWhileItIsScheduled_Async(bool removeSchedule)
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sample content");
        Directory.CreateDirectory(this.TARGETFOLDER);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var queueRunner = (IQueueRunnerService)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(IQueueRunnerService))!;
            (await httpClient.PostAsync("/api/v1/serverstate/pause", null).ConfigureAwait(false)).EnsureSuccessStatusCode();

            var backup = new BackupAndScheduleInputDto.BackupInputDto
            {
                Name = $"Scheduled backup {Guid.NewGuid():N}",
                Description = "Integration test backup",
                TargetURL = BuildFileBackendUrl(this.TARGETFOLDER),
                Sources = new[] { this.DATAFOLDER },
                Settings = new[] { new BackupAndScheduleInputDto.SettingInputDto { Name = "passphrase", Value = "integration-passphrase" } },
                Filters = Array.Empty<BackupAndScheduleInputDto.FilterInputDto>(),
                Metadata = new Dictionary<string, string>()
            };
            var createResponse = await httpClient.PostAsJsonAsync("/api/v1/backups", new BackupAndScheduleInputDto
            {
                Backup = backup,
                Schedule = new BackupAndScheduleInputDto.ScheduleInputDto { Time = DateTime.UtcNow.AddHours(-1), Repeat = "1D" }
            }, JsonOptions).ConfigureAwait(false);
            createResponse.EnsureSuccessStatusCode();
            var backupId = (await createResponse.Content.ReadFromJsonAsync<CreateBackupDto>(JsonOptions).ConfigureAwait(false))!.ID;

            // The run is overdue, so the scheduler queues it while the server is paused
            var stopwatch = Stopwatch.StartNew();
            while (!queueRunner.GetCurrentTasks().Any(t => t.BackupID == backupId))
            {
                if (stopwatch.Elapsed > TimeSpan.FromSeconds(30))
                    Assert.Fail("The scheduler did not queue the overdue run");
                await Task.Delay(100).ConfigureAwait(false);
            }

            if (removeSchedule)
            {
                var updateResponse = await httpClient.PutAsJsonAsync($"/api/v1/backup/{backupId}", new BackupAndScheduleInputDto { Backup = backup }, JsonOptions).ConfigureAwait(false);
                updateResponse.EnsureSuccessStatusCode();
            }

            (await httpClient.PostAsync("/api/v1/serverstate/resume", null).ConfigureAwait(false)).EnsureSuccessStatusCode();

            stopwatch.Restart();
            while (queueRunner.GetCurrentTasks().Count > 0 || queueRunner.GetCurrentTask() != null)
            {
                if (stopwatch.Elapsed > TimeSpan.FromMinutes(1))
                    Assert.Fail("The queue did not empty");
                await Task.Delay(100).ConfigureAwait(false);
            }

            if (removeSchedule)
                Assert.That(Directory.GetFiles(this.TARGETFOLDER), Is.Empty, "The run of the removed schedule backed up");
            else
                Assert.That(Directory.GetFiles(this.TARGETFOLDER, "*.dlist.*"), Has.Length.EqualTo(1), "The scheduled run did not back up");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Waits until the task is no longer current, so its result has been stored.
    /// </summary>
    private static async Task WaitForTaskResultAsync(long taskId)
    {
        var queueRunner = (IQueueRunnerService)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(IQueueRunnerService))!;
        var stopwatch = Stopwatch.StartNew();
        while (queueRunner.GetCurrentTask()?.TaskID == taskId)
        {
            if (stopwatch.Elapsed > TimeSpan.FromMinutes(1))
                throw new TimeoutException($"Task {taskId} is still current");
            await Task.Delay(50).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The server tries the ports it is given in turn when one cannot be used. A port that the
    /// system refuses (Windows reserves ranges of ports for Hyper-V, WSL and Docker) made the
    /// default localhost binding fail with an error that was not recognized, so the server
    /// stopped instead of trying the next port (#7330).
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task ServerMovesOnFromAPortItIsNotAllowedToUse_Async()
    {
        int refused;
        if (OperatingSystem.IsWindows())
        {
            // A port in a range Windows has excluded, as in the issue. Listing the ranges does not
            // need administrator rights, but adding one does, so the test uses one that is there.
            var reserved = GetExcludedPort();
            if (reserved == null)
                Assert.Ignore("Windows has no excluded port range for both IPv4 and IPv6 here");
            refused = reserved!.Value;
        }
        else
        {
            // A privileged port is refused to a user that is not root
            refused = 1;
            try
            {
                using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                probe.Bind(new IPEndPoint(IPAddress.Loopback, refused));
                Assert.Ignore("Port 1 can be used here (running as root?), so it is not refused");
            }
            catch (SocketException)
            {
            }
        }

        var next = GetFreeTcpPort();

        // The default interface binds localhost on both IPv4 and IPv6, as the tray icon does
        await WithAuthenticatedServerAsync(httpClient =>
        {
            Assert.That(ServerProgram.DuplicatiWebserver.Port, Is.EqualTo(next), "The server did not move on to the next port");
            return Task.CompletedTask;
        }, ports: $"{refused},{next}", listenInterface: "loopback").ConfigureAwait(false);
    }

    /// <summary>
    /// Finds a TCP port that Windows has excluded for both IPv4 and IPv6, as listed by
    /// <c>netsh interface ipvN show excludedportrange</c>.
    /// </summary>
    /// <returns>The port, or <c>null</c> if there is none.</returns>
    private static int? GetExcludedPort()
    {
        static List<(int Start, int End)> Ranges(string family)
        {
            using var process = Process.Start(new ProcessStartInfo("netsh", $"interface {family} show excludedportrange protocol=tcp")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();

            // The headers are localized, the rows are two numbers
            return output
                .Split('\n')
                .Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
                .Select(parts => (int.Parse(parts[0]), int.Parse(parts[1])))
                .ToList();
        }

        var v6 = Ranges("ipv6");
        return Ranges("ipv4")
            .SelectMany(r => Enumerable.Range(r.Start, r.End - r.Start + 1))
            .Where(port => port > 1024 && v6.Any(r => port >= r.Start && port <= r.End))
            .Select(port => (int?)port)
            .FirstOrDefault();
    }

    /// <summary>
    /// A destination folder that is already there is the desired outcome of an
    /// automatic creation, the way the backend manager treats it. These tests use a
    /// backend that reports the folder as missing and then reports it as already
    /// existing, which is what happens when the folder appears between the test and
    /// the create.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task DestinationAutoCreateAcceptsExistingFolder_Async()
    {
        FolderExistsBackend.Reset();
        BackendLoader.AddBackend(new FolderExistsBackend());

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var response = await httpClient.PostAsJsonAsync(
                "/api/v1/remoteoperation/test?autocreate=true",
                new { path = "folderexists://existing" },
                JsonOptions).ConfigureAwait(false);

            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(response.IsSuccessStatusCode, Is.True,
                $"Testing a destination that is already there should succeed; got {(int)response.StatusCode}: {body}");
            Assert.That(FolderExistsBackend.CreateFolderCalls, Is.EqualTo(1), "The folder should have been created once");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task DestinationAutoCreateAcceptsExistingFolderV2_Async()
    {
        FolderExistsBackend.Reset();
        BackendLoader.AddBackend(new FolderExistsBackend());

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var request = new DestinationTestRequestDto
            {
                DestinationUrl = "folderexists://existing",
                Options = new Dictionary<string, string>(),
                AutoCreate = true
            };

            var response = await httpClient.PostAsJsonAsync("/api/v2/destination/test", request, JsonOptions).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<DestinationTestResponseDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("destination/test response was empty");

            Assert.That(result.Success, Is.True, $"Testing a destination that is already there should succeed; got {result.StatusCode}: {result.Error}");
            Assert.That(result.StatusCode, Is.EqualTo("OK"));
            // The client reads the folder state from the payload, so the test has to
            // continue after the folder was reported as existing
            Assert.That(result.Data, Is.Not.Null, "The folder state should be reported");
            Assert.That(result.Data!.FolderExists, Is.True);
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task DestinationTestDoesNotCreateWhenReadOnly_Async()
    {
        FolderExistsBackend.Reset();
        BackendLoader.AddBackend(new FolderExistsBackend());

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var response = await httpClient.PostAsJsonAsync(
                "/api/v1/remoteoperation/test?autocreate=true&readOnlyTest=true",
                new { path = "folderexists://existing" },
                JsonOptions).ConfigureAwait(false);

            Assert.That(response.IsSuccessStatusCode, Is.False, "A read-only test of a missing folder should fail");
            Assert.That(FolderExistsBackend.CreateFolderCalls, Is.EqualTo(0), "A read-only test should not create anything");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A sync job has its own database schema and result type, but is shown in the same
    /// places as a backup. The last run must be reported through the sync metadata keys,
    /// and the result of the run must be readable from the job log.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task SyncJobReportsLastRunAndLogs_Async()
    {
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sync sample");
        File.WriteAllBytes(Path.Combine(this.DATAFOLDER, "empty.md"), Array.Empty<byte>());
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
                        // The server validation looks for the setting with the leading dashes
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

            Assert.That(File.Exists(Path.Combine(this.TARGETFOLDER, "sample.txt")), Is.True, "The sync should have copied the file");
            Assert.That(File.Exists(Path.Combine(this.TARGETFOLDER, "empty.md")), Is.True, "The sync should have copied the empty file");

            var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            backupResponse.EnsureSuccessStatusCode();
            var backup = await backupResponse.Content.ReadFromJsonAsync<BackupGet.GetBackupResultDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Backup retrieval response was empty");

            var metadata = backup.Backup.Metadata ?? new Dictionary<string, string>();
            foreach (var key in new[] { "LastSyncStarted", "LastSyncFinished", "LastSyncDuration", "SourceFilesCount", "SourceFilesSize", "SourceSizeString" })
                Assert.That(metadata.ContainsKey(key), Is.True, $"The key {key} should be written for a sync job");
            foreach (var key in new[] { "LastBackupStarted", "LastBackupFinished", "LastBackupDuration" })
                Assert.That(metadata.ContainsKey(key), Is.False, $"The backup key {key} should not be written for a sync job");
            Assert.That(metadata["SourceFilesCount"], Is.EqualTo("2"));

            var logResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}/log?pagesize=25").ConfigureAwait(false);
            Assert.That(logResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The log of a sync job should be readable");
            var log = await logResponse.Content.ReadFromJsonAsync<List<Dictionary<string, JsonElement>>>(JsonOptions).ConfigureAwait(false);
            Assert.That(log, Is.Not.Null.And.Count.EqualTo(1), "A sync run should write one result entry to the log");
            Assert.That(log![0]["Type"].GetString(), Is.EqualTo("Result"));
            using (var resultJson = JsonDocument.Parse(log[0]["Message"].GetString()!))
            {
                Assert.That(resultJson.RootElement.GetProperty("MainOperation").GetString(), Is.EqualTo("Sync"));
                Assert.That(resultJson.RootElement.GetProperty("FilesUploaded").GetInt64(), Is.EqualTo(2));
            }

            var remoteLogResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}/remotelog?pagesize=25").ConfigureAwait(false);
            Assert.That(remoteLogResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), "The remote log of a sync job should be readable");
            var remoteLog = await remoteLogResponse.Content.ReadFromJsonAsync<List<Dictionary<string, JsonElement>>>(JsonOptions).ConfigureAwait(false);
            // The sync handler does not currently record its remote operations in the
            // database, so only the request succeeding is verified, not the contents
            Assert.That(remoteLog, Is.Not.Null, "The remote log of a sync job should be a list");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// A sync job with snapshot folders lists one version per run, browses a version under
    /// the original source path and restores an older version through the same endpoints
    /// the restore of a backup uses. Operations that need a backup database are still
    /// refused (forum topic 22782), and the sync database still works afterwards.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task SyncJobListsAndRestoresSnapshots_Async()
    {
        var samplePath = Path.Combine(this.DATAFOLDER, "sample.txt");
        File.WriteAllText(samplePath, "First version");
        Directory.CreateDirectory(this.TARGETFOLDER);

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var extensions = await GetApiExtensionsAsync(httpClient).ConfigureAwait(false);
            Assert.That(extensions, Does.Contain(SystemInfoProvider.SyncSnapshotsExtension),
                "System info should advertise snapshot folders for Sync jobs");
            Assert.That(extensions, Does.Contain(SystemInfoProvider.SyncRestoreExtension),
                "System info should advertise that Sync jobs can be restored");

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
                        new BackupAndScheduleInputDto.SettingInputDto { Name = "snapshot-policy", Value = "Off" },
                        new BackupAndScheduleInputDto.SettingInputDto { Name = "--" + Duplicati.Library.Main.Options.SYNC_SNAPSHOT_FOLDERS_OPTION, Value = "true" }
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
            File.WriteAllText(samplePath, "Second version");
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);

            // The restore page starts by listing the versions: one per snapshot, newest first
            var listFilesetsResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-filesets", new ListFilesetsRequestDto { BackupId = backupId }, JsonOptions).ConfigureAwait(false);
            var listFilesetsBody = await listFilesetsResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(listFilesetsResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), listFilesetsBody);
            string olderTime;
            using (var listFilesets = JsonDocument.Parse(listFilesetsBody))
            {
                Assert.That(listFilesets.RootElement.GetProperty("Success").GetBoolean(), Is.True, listFilesetsBody);
                var versions = listFilesets.RootElement.GetProperty("Data").EnumerateArray().ToArray();
                Assert.That(versions, Has.Length.EqualTo(2), listFilesetsBody);
                Assert.That(versions[0].GetProperty("Version").GetInt64(), Is.EqualTo(0), listFilesetsBody);
                olderTime = versions[1].GetProperty("Time").GetDateTime().ToUniversalTime().ToString("o");
            }

            // The file tree starts at the source folder, as it does for a backup
            var sourceFolder = Duplicati.Library.Common.IO.Util.AppendDirSeparator(this.DATAFOLDER);
            var rootResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-folder", new ListFolderContentRequestDto { BackupId = backupId, Paths = null, Time = olderTime, PageSize = 1000, Page = 0 }, JsonOptions).ConfigureAwait(false);
            var rootBody = await rootResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(rootResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), rootBody);
            using (var root = JsonDocument.Parse(rootBody))
                Assert.That(root.RootElement.GetProperty("Data").EnumerateArray().Select(x => x.GetProperty("Path").GetString()), Is.EqualTo(new[] { sourceFolder }), rootBody);

            var folderResponse = await httpClient.PostAsJsonAsync("/api/v2/backup/list-folder", new ListFolderContentRequestDto { BackupId = backupId, Paths = new[] { sourceFolder }, Time = olderTime, PageSize = 1000, Page = 0 }, JsonOptions).ConfigureAwait(false);
            var folderBody = await folderResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            using (var folder = JsonDocument.Parse(folderBody))
                Assert.That(folder.RootElement.GetProperty("Data").EnumerateArray().Select(x => x.GetProperty("Path").GetString()), Does.Contain(samplePath), folderBody);

            // Restore the older version of the folder, the way the restore page asks for it
            if (Directory.Exists(this.RESTOREFOLDER))
                Directory.Delete(this.RESTOREFOLDER, true);
            var restoreResponse = await httpClient.PostAsJsonAsync(
                $"/api/v1/backup/{backupId}/restore",
                new RestoreInputDto(new[] { sourceFolder + "*" }, null, olderTime, this.RESTOREFOLDER, true, false, false, null, null),
                JsonOptions).ConfigureAwait(false);
            var restoreBody = await restoreResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(restoreResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), restoreBody);
            var restoreTask = await restoreResponse.Content.ReadFromJsonAsync<TaskStartedDto>(JsonOptions).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Restore did not return a task");
            await WaitForTaskCompletionAsync(httpClient, restoreTask.ID).ConfigureAwait(false);
            await AssertTaskCompletedAsync(httpClient, restoreTask.ID).ConfigureAwait(false);
            Assert.That(File.ReadAllText(Path.Combine(this.RESTOREFOLDER, "sample.txt")), Is.EqualTo("First version"));

            // Operations that need a backup database are still refused
            var verifyResponse = await httpClient.PostAsync($"/api/v1/backup/{backupId}/verify", null).ConfigureAwait(false);
            var verifyBody = await verifyResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            Assert.That(verifyResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), verifyBody);
            using (var verify = JsonDocument.Parse(verifyBody))
                Assert.That(GetStringProperty(verify.RootElement, "helpId"), Is.EqualTo("SyncJobNotSupported"), verifyBody);

            // Listing and restoring did not damage the sync database
            await RunTaskAndWaitAsync(httpClient, $"/api/v1/backup/{backupId}/run").ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Deleting the remote files of a job removes the backup volumes at the destination. A
    /// sync job has no volumes there, only the copied files, so the option is ignored: the
    /// job is deleted and the copy is kept, including files that look like backup volumes,
    /// as when a backup folder is synced (forum topic 22782).
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task SyncJobCanBeDeletedWithItsRemoteFiles_Async()
    {
        const string volumeLikeName = "duplicati-20260101T000000Z.dlist.zip";
        File.WriteAllText(Path.Combine(this.DATAFOLDER, "sample.txt"), "Sync sample");
        File.WriteAllText(Path.Combine(this.DATAFOLDER, volumeLikeName), "Synced backup volume");
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
            Assert.That(File.Exists(Path.Combine(this.TARGETFOLDER, volumeLikeName)), Is.True, "The sync should have copied the file");

            var deleteResponse = await httpClient.DeleteAsync($"/api/v1/backup/{backupId}?delete-remote-files=true&delete-local-db=true").ConfigureAwait(false);
            deleteResponse.EnsureSuccessStatusCode();
            var delete = await deleteResponse.Content.ReadFromJsonAsync<DeleteBackupOutputDto>(JsonOptions).ConfigureAwait(false)
                         ?? throw new InvalidOperationException("Delete response was empty");
            Assert.That(delete.ID, Is.Not.Null, $"{delete.Status}: {delete.Reason}");

            var state = await WaitForTaskToFinishAsync(httpClient, delete.ID!.Value).ConfigureAwait(false);
            Assert.That(state.Status, Is.EqualTo("Completed").IgnoreCase, state.ErrorMessage);

            var backupResponse = await httpClient.GetAsync($"/api/v1/backup/{backupId}").ConfigureAwait(false);
            Assert.That(backupResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "The sync job should be deleted");
            Assert.That(File.Exists(Path.Combine(this.TARGETFOLDER, "sample.txt")), Is.True, "The synced copy should be kept");
            Assert.That(File.Exists(Path.Combine(this.TARGETFOLDER, volumeLikeName)), Is.True, "The synced copy should be kept");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task DeleteAllNotificationsRemovesEveryNotificationInOneRequest_Async()
    {
        // Repeated warnings can leave thousands of notifications, and dismissing them one request
        // at a time floods the browser (#4719)
        const int count = 200;

        await WithAuthenticatedServerAsync(async httpClient =>
        {
            // The server may have registered notifications of its own when it started
            var existing = await httpClient.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications", JsonOptions).ConfigureAwait(false);
            RegisterNotifications(count);

            var before = await httpClient.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications", JsonOptions).ConfigureAwait(false);
            Assert.That(before, Has.Count.EqualTo(existing!.Count + count));
            var stateBefore = await GetServerStateAsync(httpClient).ConfigureAwait(false);
            Assert.That(stateBefore.GetProperty("HasWarning").GetBoolean(), Is.True);

            var response = await httpClient.DeleteAsync("/api/v1/notifications").ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Deleting all notifications should succeed");

            var after = await httpClient.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications", JsonOptions).ConfigureAwait(false);
            Assert.That(after, Is.Empty, "All notifications should be deleted");

            var stateAfter = await GetServerStateAsync(httpClient).ConfigureAwait(false);
            Assert.That(stateAfter.GetProperty("HasWarning").GetBoolean(), Is.False, "No unacknowledged warning should be left");
            Assert.That(stateAfter.GetProperty("LastNotificationUpdateID").GetInt64() - stateBefore.GetProperty("LastNotificationUpdateID").GetInt64(),
                Is.EqualTo(1), "Deleting all notifications should signal one notification update");
        }).ConfigureAwait(false);
    }

    [Test]
    [Category("Integration")]
    public async Task DeleteNotificationRemovesOnlyThatNotification_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            RegisterNotifications(3);

            var notifications = (await httpClient.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications", JsonOptions).ConfigureAwait(false))!;
            var removed = notifications.Single(x => x.Title == "Warning 1").ID;

            var response = await httpClient.DeleteAsync($"/api/v1/notification/{removed}").ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

            var after = await httpClient.GetFromJsonAsync<List<NotificationDto>>("/api/v1/notifications", JsonOptions).ConfigureAwait(false);
            Assert.That(after!.Select(x => x.ID), Is.EquivalentTo(notifications.Where(x => x.ID != removed).Select(x => x.ID)));

            var missing = await httpClient.DeleteAsync($"/api/v1/notification/{removed}").ConfigureAwait(false);
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "Deleting a notification that is gone should report it as not found");
        }).ConfigureAwait(false);
    }

    private static void RegisterNotifications(int count)
    {
        var connection = (Duplicati.Server.Database.Connection?)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(Duplicati.Server.Database.Connection))
            ?? throw new InvalidOperationException("The server has no database connection");
        for (var i = 0; i < count; i++)
            connection.RegisterNotification(Duplicati.Server.Serialization.NotificationType.Warning, $"Warning {i}", $"Message {i}",
                null, null, "backup:show-log", null, null, null, (self, all) => self);
    }

    private static async Task<JsonElement> GetServerStateAsync(HttpClient httpClient)
    {
        using var doc = JsonDocument.Parse(await httpClient.GetStringAsync("/api/v1/serverstate").ConfigureAwait(false));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Changing the password resets the JWT signing key and drops all token families,
    /// so tokens issued before the change stop working right away, without a restart.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task ChangingThePasswordInvalidatesExistingTokens_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = httpClient.BaseAddress };
            var connection = (Duplicati.Server.Database.Connection?)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(Duplicati.Server.Database.Connection))
                ?? throw new InvalidOperationException("The server has no database connection");

            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = "integration-test-password", rememberMe = true }, JsonOptions).ConfigureAwait(false);
            login.EnsureSuccessStatusCode();
            var cookie = login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("RefreshToken_", StringComparison.Ordinal) && !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase)).Split(';')[0];
            var tokens = await login.Content.ReadFromJsonAsync<AccessTokenOutputDto>(JsonOptions).ConfigureAwait(false);
            var oldJwtConfig = connection.ApplicationSettings.JWTConfig;

            using var oldSocket = await ConnectAuthenticatedWebsocketAsync(httpClient.BaseAddress!, tokens!.AccessToken!).ConfigureAwait(false);

            var change = await httpClient.PatchAsJsonAsync("/api/v1/serversettings", new Dictionary<string, string> { ["server-passphrase"] = "new-integration-test-password" }).ConfigureAwait(false);
            change.EnsureSuccessStatusCode();

            Assert.That(connection.ApplicationSettings.JWTConfig, Is.Not.Null.And.Not.EqualTo(oldJwtConfig), "The JWT config should be replaced");
            Assert.That(await ReadUntilClosedAsync(oldSocket).ConfigureAwait(false), Is.EqualTo((System.Net.WebSockets.WebSocketCloseStatus)4401), "A websocket authenticated before the change should be closed");

            using var oldAccess = new HttpRequestMessage(HttpMethod.Get, "/api/v1/serverstate");
            oldAccess.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);
            Assert.That((await client.SendAsync(oldAccess).ConfigureAwait(false)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "An access token from before the change should be rejected");

            var callerAccess = await httpClient.GetAsync("/api/v1/serverstate").ConfigureAwait(false);
            Assert.That(callerAccess.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "The token used to change the password should be rejected too");

            using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh") { Content = JsonContent.Create(new { nonce = tokens.RefreshNonce }) };
            refresh.Headers.Add("Cookie", cookie);
            Assert.That((await client.SendAsync(refresh).ConfigureAwait(false)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "A refresh token from before the change should be rejected");

            var oldPassword = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = "integration-test-password", rememberMe = true }, JsonOptions).ConfigureAwait(false);
            Assert.That(oldPassword.IsSuccessStatusCode, Is.False, "The old password should no longer log in");

            await AuthenticateAsync(httpClient, "new-integration-test-password").ConfigureAwait(false);
            var afterLogin = await httpClient.GetAsync("/api/v1/serverstate").ConfigureAwait(false);
            Assert.That(afterLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK), "A login with the new password should work");

            // The websocket authenticator is long-lived, so it must also validate with the new key
            using var newSocket = await ConnectAuthenticatedWebsocketAsync(httpClient.BaseAddress!, httpClient.DefaultRequestHeaders.Authorization!.Parameter!).ConfigureAwait(false);
            await newSocket.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, CancellationToken.None).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens a notifications websocket and authenticates it with the token, failing the test if it is rejected
    /// </summary>
    private static async Task<System.Net.WebSockets.ClientWebSocket> ConnectAuthenticatedWebsocketAsync(Uri baseAddress, string token)
    {
        var socket = new System.Net.WebSockets.ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://{baseAddress.Authority}/notifications"), CancellationToken.None).ConfigureAwait(false);
        await socket.SendAsync(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { Version = 1, Token = token })), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);

        var buffer = new byte[4096];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
        Assert.That(result.MessageType, Is.EqualTo(System.Net.WebSockets.WebSocketMessageType.Text), $"The websocket was closed: {result.CloseStatus} {result.CloseStatusDescription}");
        using var reply = JsonDocument.Parse(buffer.AsMemory(0, result.Count));
        Assert.That(reply.RootElement.GetProperty("Success").GetBoolean(), Is.True, $"The websocket rejected the token: {reply.RootElement}");
        return socket;
    }

    /// <summary>
    /// Reads from the websocket until the server closes it, returning the close status
    /// </summary>
    private static async Task<System.Net.WebSockets.WebSocketCloseStatus?> ReadUntilClosedAsync(System.Net.WebSockets.ClientWebSocket socket)
    {
        var buffer = new byte[16 * 1024];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, timeout.Token).ConfigureAwait(false);
            if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                return result.CloseStatus;
        }
    }

    /// <summary>
    /// The refresh token cookie was set for the domain the request named in its Host header.
    /// Behind a reverse proxy that does not pass the Host header on, that is the address of
    /// the server, not the name the browser uses, so the browser rejected the cookie, every
    /// refresh failed, and the login looped (issue #6042). A cookie without a domain is for
    /// the host the browser asked, whatever the server sees.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task TheRefreshCookieHasNoDomain_Async()
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            // Handle the cookies here, to see what the server sets
            using var client = new HttpClient(new HttpClientHandler { UseCookies = false }) { BaseAddress = httpClient.BaseAddress };

            static string RefreshCookie(HttpResponseMessage response)
            {
                var cookies = response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];
                var set = cookies
                    .Where(c => c.StartsWith("RefreshToken_", StringComparison.Ordinal))
                    .Where(c => !c.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                Assert.That(set, Has.Count.EqualTo(1), $"one refresh cookie should be set: {string.Join(" | ", cookies)}");
                Assert.That(set[0], Does.Not.Contain("domain=").IgnoreCase, "the refresh cookie should have no domain");
                return set[0].Split(';')[0];
            }

            var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { password = "integration-test-password", rememberMe = true }, JsonOptions).ConfigureAwait(false);
            login.EnsureSuccessStatusCode();
            var cookie = RefreshCookie(login);
            var tokens = await login.Content.ReadFromJsonAsync<AccessTokenOutputDto>(JsonOptions).ConfigureAwait(false);

            // The cookie works for a refresh, which sets a new one the same way
            using var refresh = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh") { Content = JsonContent.Create(new { nonce = tokens!.RefreshNonce }) };
            refresh.Headers.Add("Cookie", cookie);
            var refreshed = await client.SendAsync(refresh).ConfigureAwait(false);
            refreshed.EnsureSuccessStatusCode();
            RefreshCookie(refreshed);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// The server opened, created or upgraded its database before it checked that no other
    /// server was running. Two servers started at the same time, such as from two startup
    /// entries, then upgraded the same database at once, and the one that failed copied its
    /// copy of the old database back over it (forum 22694: "no such column: OperationType").
    /// A second server must stop before it touches the database.
    /// </summary>
    [Test]
    [Category("Integration")]
    public async Task ASecondServerDoesNotTouchTheDatabaseAsync()
    {
        // The test holds the instance lock and starts the second server in its own process.
        // On Linux and macOS the lock is a POSIX record lock (lockf), which belongs to the
        // process, so a second lock from the same process does not conflict and the server
        // would start. On Windows the lock is a file opened with FileShare.None, which
        // conflicts within one process as well. The order checked here is the same everywhere.
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The instance lock does not conflict within one process outside Windows.");

        var serverDataFolder = Path.Combine(BASEFOLDER, $"server-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(serverDataFolder);
        var databasePath = Path.Combine(serverDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME);

        var previousDataFolderEnv = Environment.GetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME);
        Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, serverDataFolder);
        var portableModeEnvName = $"{AutoUpdateSettings.AppName}__{DataFolderManager.PORTABLE_MODE_OPTION.Replace('-', '_')}".ToUpperInvariant();
        var previousPortableModeEnv = Environment.GetEnvironmentVariable(portableModeEnvName);
        Environment.SetEnvironmentVariable(portableModeEnvName, "false");

        try
        {
            // The server that is already running
            using var running = new SingleInstance(serverDataFolder);
            Assert.That(running.IsFirstInstance, Is.True);

            // Any origin but the server itself, so that the second server throws instead of exiting the process
            var applicationSettings = new ApplicationSettings { Origin = "Test" };
            string[] serverArgs =
            [
                $"--{WebServerLoader.OPTION_PORT}={GetFreeTcpPort()}",
                $"--{WebServerLoader.OPTION_INTERFACE}=127.0.0.1",
                $"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}={serverDataFolder}",
                "--webservice-api-only=true"
            ];

            Assert.ThrowsAsync<SingleInstance.MultipleInstanceException>(
                () => RunServerInBackgroundAsync(applicationSettings, serverArgs).WaitAsync(TimeSpan.FromSeconds(60)));
            Assert.That(File.Exists(databasePath), Is.False, "The second server created or opened the database before it found the running one");
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, previousDataFolderEnv);
            Environment.SetEnvironmentVariable(portableModeEnvName, previousPortableModeEnv);
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            SafeDeleteDirectory(serverDataFolder);
        }
    }

    private Task WithAuthenticatedServerAsync(Func<HttpClient, Task> testBody, string? ports = null, string listenInterface = "127.0.0.1")
        => WithAuthenticatedServerAsync(testBody, ports, listenInterface, []);

    private async Task WithAuthenticatedServerAsync(Func<HttpClient, Task> testBody, string? ports, string listenInterface, string[] extraServerArgs)
    {
        var serverPassword = "integration-test-password";
        var serverDataFolder = Path.Combine(BASEFOLDER, $"server-data-{Guid.NewGuid():N}");
        Directory.CreateDirectory(serverDataFolder);

        var previousDataFolderEnv = Environment.GetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME);
        Environment.SetEnvironmentVariable(DataFolderManager.DATAFOLDER_ENV_NAME, serverDataFolder);

        // Debug builds default to portable mode, which keeps the data next to the test assembly
        // and wins over the data folder variable. The data folder option in the server
        // arguments does not help either, as the data folder is read from the process command
        // line, so every test would share one server database.
        var portableModeEnvName = $"{AutoUpdateSettings.AppName}__{DataFolderManager.PORTABLE_MODE_OPTION.Replace('-', '_')}".ToUpperInvariant();
        var previousPortableModeEnv = Environment.GetEnvironmentVariable(portableModeEnvName);
        Environment.SetEnvironmentVariable(portableModeEnvName, "false");

        ApplicationSettings? applicationSettings = null;
        Task<int>? serverTask = null;
        Duplicati.Server.Database.Connection? serverConnection = null;

        try
        {
            applicationSettings = new ApplicationSettings();
            string[] serverArgs =
            [
                $"--{WebServerLoader.OPTION_PORT}={ports ?? GetFreeTcpPort().ToString()}",
                $"--{WebServerLoader.OPTION_INTERFACE}={listenInterface}",
                $"--{WebServerLoader.OPTION_WEBSERVICE_PASSWORD}={serverPassword}",
                $"--{DataFolderManager.SERVER_DATAFOLDER_OPTION}={serverDataFolder}",
                "--webservice-api-only=true",
                .. extraServerArgs
            ];

            ServerProgram.ServerStartedEvent.Reset();
            serverTask = RunServerInBackgroundAsync(applicationSettings, serverArgs);

            if (!ServerProgram.ServerStartedEvent.WaitOne(TimeSpan.FromSeconds(60)))
                Assert.Fail("Server did not start within the allotted time");

            // Each test gets a server database of its own, not one shared with the other tests
            Assert.That(File.Exists(Path.Combine(serverDataFolder, DataFolderManager.SERVER_DATABASE_FILENAME)), Is.True,
                $"The server did not keep its database in the test data folder, but in {applicationSettings.DataFolder}");

            // The server does not close its database when it stops, as a process that stops exits,
            // so the test closes it to be able to remove the data folder
            serverConnection = (Duplicati.Server.Database.Connection?)ServerProgram.DuplicatiWebserver.Provider.GetService(typeof(Duplicati.Server.Database.Connection));

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
            Environment.SetEnvironmentVariable(portableModeEnvName, previousPortableModeEnv);

            // Closing the connection returns it to the pool, which keeps the file open until cleared
            serverConnection?.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
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

    /// <summary>
    /// The purge-files endpoint passes SuppressCompact on to the task as no-auto-compact, only when it is set,
    /// and only for this operation. The value given for the operation overrides the setting of the backup,
    /// and the stored settings of the backup are not changed (duplicati/ngclient#216).
    /// </summary>
    /// <param name="requestValue">The JSON value of SuppressCompact in the request, or an empty string to leave it out</param>
    /// <param name="jobNoAutoCompact">The no-auto-compact setting stored on the backup, if any</param>
    /// <param name="expectedEffective">The no-auto-compact value the purge runs with, or <c>null</c> if it is not set</param>
    [Test]
    [Category("Integration")]
    [TestCase("", null, null)]
    [TestCase("null", null, null)]
    [TestCase("true", null, true)]
    [TestCase("false", null, false)]
    [TestCase("false", "true", false)]
    [TestCase("", "true", true)]
    public async Task PurgeFilesPassesSuppressCompactToItsTaskOnly_Async(string requestValue, string? jobNoAutoCompact, bool? expectedEffective)
    {
        await WithAuthenticatedServerAsync(async httpClient =>
        {
            var provider = ServerProgram.DuplicatiWebserver.Provider;
            var queueRunner = (IQueueRunnerService)provider.GetService(typeof(IQueueRunnerService))!;
            var connection = (Duplicati.Server.Database.Connection)provider.GetService(typeof(Duplicati.Server.Database.Connection))!;

            var backupId = await CreateBackupAsync(httpClient, "integration-passphrase",
                extraSettings: jobNoAutoCompact == null
                    ? null
                    : [new BackupAndScheduleInputDto.SettingInputDto { Name = "no-auto-compact", Value = jobNoAutoCompact }]).ConfigureAwait(false);

            // Keep the task in the queue, so its options can be read before it runs
            (await httpClient.PostAsync("/api/v1/serverstate/pause", null).ConfigureAwait(false)).EnsureSuccessStatusCode();

            var path = Path.Combine(this.DATAFOLDER, "purged.txt");
            var suppressCompact = requestValue == "" ? "" : $",\"SuppressCompact\":{requestValue}";
            var body = $"{{\"BackupId\":{JsonSerializer.Serialize(backupId)},\"Filters\":[{JsonSerializer.Serialize(path)}],\"Versions\":[0,2]{suppressCompact}}}";
            var response = await httpClient.PostAsync("/api/v2/backup/purge-files",
                new StringContent(body, System.Text.Encoding.UTF8, "application/json")).ConfigureAwait(false);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync().ConfigureAwait(false));

            var task = queueRunner.GetCurrentTasks().OfType<Runner.IRunnerData>()
                .Single(t => t.BackupID == backupId && t.Operation == Duplicati.Server.Serialization.DuplicatiOperation.PurgeFiles);

            Assert.That(task.FilterStrings, Is.EqualTo(new[] { path }));
            Assert.That(task.ExtraOptions?["version"], Is.EqualTo("0,2"));

            if (requestValue is "" or "null")
                Assert.That(task.ExtraOptions!.ContainsKey("no-auto-compact"), Is.False, "An unset SuppressCompact must not add an option");
            else
                Assert.That(Utility.ParseBool(task.ExtraOptions!["no-auto-compact"], !bool.Parse(requestValue)), Is.EqualTo(bool.Parse(requestValue)));

            // The options the task runs with: the backup's settings, then the task's own options, as Runner does
            var backup = connection.GetBackup(backupId)!;
            var options = Runner.ApplyOptions(connection, backup, Runner.GetCommonOptions(connection), out _);
            foreach (var option in task.ExtraOptions!)
                options[option.Key] = option.Value;

            if (expectedEffective == null)
                Assert.That(options.ContainsKey("no-auto-compact"), Is.False);
            else
                Assert.That(Utility.ParseBool(options["no-auto-compact"], !expectedEffective.Value), Is.EqualTo(expectedEffective.Value));

            // The stored settings of the backup stay as they were
            var stored = backup.Settings.Where(s => s.Name == "no-auto-compact").Select(s => s.Value).ToArray();
            Assert.That(stored, Is.EqualTo(jobNoAutoCompact == null ? Array.Empty<string>() : new[] { jobNoAutoCompact }));

            (await httpClient.PostAsync("/api/v1/serverstate/resume", null).ConfigureAwait(false)).EnsureSuccessStatusCode();
            var stopwatch = Stopwatch.StartNew();
            while (queueRunner.GetCurrentTasks().Count > 0 || queueRunner.GetCurrentTask() != null)
            {
                if (stopwatch.Elapsed > TimeSpan.FromMinutes(1))
                    Assert.Fail("The queue did not empty");
                await Task.Delay(100).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private string BuildFileBackendUrl(string folder)
    {
        var normalized = Util.AppendDirSeparator(Path.GetFullPath(folder));
        return new Uri(normalized).AbsoluteUri;
    }

    private async Task<string> CreateBackupAsync(HttpClient httpClient, string passphrase, string? backupName = null, string? targetFolder = null, IEnumerable<BackupAndScheduleInputDto.SettingInputDto>? extraSettings = null)
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
        }
        .Concat(extraSettings ?? [])
        .ToArray();

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
            new RestoreInputDto(null, passphrase, "now", restoreFolder, true, false, false, null, null),
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

    /// <summary>
    /// Waits for a task to finish, whether it completed or failed, and returns its final state.
    /// </summary>
    private static async Task<GetTaskStateDto> WaitForTaskToFinishAsync(HttpClient httpClient, long taskId)
    {
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            var state = await GetTaskStateAsync(httpClient, taskId).ConfigureAwait(false);

            if (string.Equals(state.Status, "Completed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(state.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                return state;

            if (stopwatch.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException($"Task {taskId} did not finish in the allotted time");

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

    private static Task<int> RunServerInBackgroundAsync(ApplicationSettings applicationSettings, string[] args)
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
