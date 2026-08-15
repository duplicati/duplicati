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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SecretsManager;
using Amazon.SecretsManager.Model;
using Duplicati.Library.SecretProvider;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.SecretManager.V1;
using Grpc.Core;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Duplicati.Library.Interface;
using Docker.DotNet;

namespace Duplicati.UnitTest;

#nullable enable

[TestFixture]
public class SecretProviderSetSecretTests
{
    [Test]
    public async Task FileProvider_SetSecret_WritesToFile_Async()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"duplicati-secrets-{Guid.NewGuid():N}.json");
        var passphrase = "test1234";
        await SharpAESCrypt.AESCrypt.EncryptAsync(passphrase, new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")), File.Create(tempFile));

        var provider = new FileSecretProvider();
        await provider.InitializeAsync(new Uri($"file://{tempFile}?passphrase={passphrase}"), CancellationToken.None);

        try
        {
            await provider.SetSecretAsync("alpha", "bravo", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { "alpha" }, CancellationToken.None);
            Assert.AreEqual("bravo", secrets["alpha"]);

            NUnit.Framework.Assert.ThrowsAsync<UserInformationException>(() => provider.SetSecretAsync("alpha", "charlie", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync("alpha", "charlie", overwrite: true, CancellationToken.None);
            var updated = await provider.ResolveSecretsAsync(new[] { "alpha" }, CancellationToken.None);
            Assert.AreEqual("charlie", updated["alpha"]);

            using var ms = new MemoryStream();
            using var fs = File.OpenRead(tempFile);
            await SharpAESCrypt.AESCrypt.DecryptAsync(passphrase, fs, ms, SharpAESCrypt.DecryptionOptions.Default with { LeaveOpen = true }, CancellationToken.None);
            ms.Position = 0;
            using var document = JsonDocument.Parse(ms);
            Assert.AreEqual("charlie", document.RootElement.GetProperty("alpha").GetString());
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task AwsProvider_SetSecret_Works_Async()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AWSSM_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("AWS secret provider tests require DUPLICATI_TEST_AWSSM_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var accessKey = queryParams["access-id"];
        var secretKey = queryParams["secret-key"];
        var region = queryParams["region"];
        var secretName = queryParams["secrets"];

        AmazonSecretsManagerClient? cleanupClient = null;
        // The keys written through the provider are stored inside the configured
        // secret, while the direct lookup test creates a secret of its own, so both
        // have to be cleaned up separately
        var createdSecretIds = new List<string>();
        string? containerKey = null;

        try
        {
            cleanupClient = new AmazonSecretsManagerClient(accessKey, secretKey, Amazon.RegionEndpoint.GetBySystemName(region));

            var provider = new AWSSecretProvider();
            await provider.InitializeAsync(uri, CancellationToken.None);

            var key = $"duplicati-aws-{Guid.NewGuid():N}";
            containerKey = key;

            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
            var secrets = await ResolveSecretWithRetryAsync(provider, key, "value1");
            Assert.AreEqual("value1", secrets[key]);

            await AssertSetSecretRejectsExistingKeyAsync(provider, key, "value2");

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await ResolveSecretWithRetryAsync(provider, key, "value3");
            Assert.AreEqual("value3", updated[key]);

            // Verify direct-secret lookup path and missing-key behavior in AWSSecretProvider.ResolveSecretsAsync.
            var directSecretId = $"duplicati-aws-direct-{Guid.NewGuid():N}";
            createdSecretIds.Add(directSecretId);

            await cleanupClient.CreateSecretAsync(new Amazon.SecretsManager.Model.CreateSecretRequest
            {
                Name = directSecretId,
                SecretString = "direct-value"
            }).ConfigureAwait(false);

            var directSecrets = await ResolveSecretWithRetryAsync(provider, directSecretId, "direct-value");
            Assert.AreEqual("direct-value", directSecrets[directSecretId]);

            NUnit.Framework.Assert.ThrowsAsync<KeyNotFoundException>(() =>
                provider.ResolveSecretsAsync(new[] { "duplicati-aws-missing-" + Guid.NewGuid().ToString("N") }, CancellationToken.None));
        }
        finally
        {
            if (cleanupClient != null)
            {
                foreach (var secretId in createdSecretIds)
                {
                    try
                    {
                        await cleanupClient.DeleteSecretAsync(new Amazon.SecretsManager.Model.DeleteSecretRequest
                        {
                            SecretId = secretId,
                            ForceDeleteWithoutRecovery = true
                        }).ConfigureAwait(false);
                    }
                    catch (ResourceNotFoundException)
                    {
                    }
                }

                if (containerKey != null)
                    await RemoveKeyFromContainerSecretAsync(cleanupClient, secretName, containerKey).ConfigureAwait(false);

                cleanupClient.Dispose();
            }
        }
    }

    /// <summary>
    /// Removes a single key from the secret that the provider stores its keys in, so
    /// the test keys do not pile up in it
    /// </summary>
    /// <param name="client">The client to use</param>
    /// <param name="secrets">The configured secrets, of which the first one is used as the store</param>
    /// <param name="key">The key to remove</param>
    private static async Task RemoveKeyFromContainerSecretAsync(AmazonSecretsManagerClient client, string secrets, string key)
    {
        var containerSecretId = secrets.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(containerSecretId))
            return;

        try
        {
            var current = await client.GetSecretValueAsync(new GetSecretValueRequest { SecretId = containerSecretId }).ConfigureAwait(false);
            var values = string.IsNullOrWhiteSpace(current.SecretString)
                ? null
                : JsonSerializer.Deserialize<Dictionary<string, string>>(current.SecretString);

            if (values == null || !values.Remove(key))
                return;

            await client.PutSecretValueAsync(new PutSecretValueRequest
            {
                SecretId = containerSecretId,
                SecretString = JsonSerializer.Serialize(values)
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Cleaning up must not turn a passing test into a failing one
            Console.WriteLine($"Failed to remove the test key from the secret store: {ex.Message}");
        }
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task AzureProvider_SetSecret_Works_Async()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AZKV_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("Azure secret provider tests require DUPLICATI_TEST_AZKV_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => System.Uri.UnescapeDataString(p[1]));
        var tenantId = queryParams["tenant-id"];
        var clientId = queryParams["client-id"];
        var clientSecret = queryParams["client-secret"];
        var keyVaultName = queryParams["keyvault-name"];
        var vaultUri = queryParams.TryGetValue("vault-uri", out var vu) ? vu : $"https://{keyVaultName}.vault.azure.net";

        SecretClient? cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            cleanupClient = new SecretClient(new System.Uri(vaultUri), credential);

            var provider = new AzureSecretProvider();
            await provider.InitializeAsync(uri, CancellationToken.None);

            var key = $"duplicati-az-{Guid.NewGuid():N}";
            createdSecretId = key;
            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
            Assert.AreEqual("value1", secrets[key]);

            NUnit.Framework.Assert.ThrowsAsync<UserInformationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await ResolveSecretWithRetryAsync(provider, key, "value3");
            Assert.AreEqual("value3", updated[key]);
        }
        finally
        {
            if (cleanupClient != null)
            {
                if (!string.IsNullOrEmpty(createdSecretId))
                {
                    try
                    {
                        var operation = await cleanupClient.StartDeleteSecretAsync(createdSecretId).ConfigureAwait(false);
                        await operation.WaitForCompletionAsync().ConfigureAwait(false);
                    }
                    catch (Azure.RequestFailedException ex) when (ex.Status == 404)
                    {
                    }
                }
            }
        }
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task GcsProvider_SetSecret_Works_Async()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_GCS_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("GCS secret provider tests require DUPLICATI_TEST_GCS_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var projectId = queryParams["project-id"];
        var serviceAccountJson = queryParams["service-account-json"];

        SecretManagerServiceClient? cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            var builder = new SecretManagerServiceClientBuilder();
            builder.Credential = GoogleCredential.FromJson(serviceAccountJson);
            cleanupClient = builder.Build();

            var provider = new GCSSecretProvider();
            await provider.InitializeAsync(uri, CancellationToken.None);

            var key = $"duplicati-gcs-{Guid.NewGuid():N}";
            createdSecretId = key;
            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
            Assert.AreEqual("value1", secrets[key]);

            NUnit.Framework.Assert.ThrowsAsync<UserInformationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await ResolveSecretWithRetryAsync(provider, key, "value3");
            Assert.AreEqual("value3", updated[key]);
        }
        finally
        {
            if (cleanupClient != null)
            {
                if (!string.IsNullOrEmpty(createdSecretId))
                {
                    try
                    {
                        await cleanupClient.DeleteSecretAsync(new SecretName(projectId, createdSecretId)).ConfigureAwait(false);
                    }
                    catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
                    {
                    }
                }
            }
        }
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task HcVaultProvider_SetSecret_Works_Async()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_HCV_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("HashiCorp Vault provider tests require DUPLICATI_TEST_HCV_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var host = uri.GetLeftPart(UriPartial.Authority);
        var token = queryParams["token"];
        var mount = queryParams.TryGetValue("mount", out var m) ? m : "secret";

        VaultClient? cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            var provider = new HCVaultSecretProvider();
            await provider.InitializeAsync(uri, CancellationToken.None);

            cleanupClient = new VaultClient(new VaultClientSettings(host, new TokenAuthMethodInfo(token)));

            var key = $"duplicati-hcv-{Guid.NewGuid():N}";
            createdSecretId = key;

            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
            Assert.AreEqual("value1", secrets[key]);

            NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await ResolveSecretWithRetryAsync(provider, key, "value3");
            Assert.AreEqual("value3", updated[key]);
        }
        finally
        {
            if (cleanupClient != null)
            {
                if (!string.IsNullOrEmpty(createdSecretId))
                {
                    try
                    {
                        await cleanupClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(createdSecretId, mountPoint: mount).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }

    /// <summary>
    /// A provider that reports a written secret as missing for the first reads, the
    /// way a service that is not immediately consistent behaves
    /// </summary>
    private sealed class DelayedVisibilitySecretProvider : ISecretProvider
    {
        /// <summary>
        /// The number of attempts that report a stored secret as still missing
        /// </summary>
        private readonly int _staleAttempts;
        private readonly Dictionary<string, string> _stored = new();
        private int _staleReads;
        private int _staleChecks;

        public DelayedVisibilitySecretProvider(int staleAttempts)
            => _staleAttempts = staleAttempts;

        public Task<Dictionary<string, string>> ResolveSecretsAsync(IEnumerable<string> keys, CancellationToken cancellationToken)
        {
            var result = new Dictionary<string, string>();
            var missing = new List<string>();

            foreach (var key in keys)
            {
                if (_stored.TryGetValue(key, out var value) && _staleReads++ >= _staleAttempts)
                    result[key] = value;
                else
                    missing.Add(key);
            }

            if (missing.Count > 0)
                throw new KeyNotFoundException("The following keys were not found: " + string.Join(", ", missing));

            return Task.FromResult(result);
        }

        public Task SetSecretAsync(string key, string value, bool overwrite, CancellationToken cancellationToken)
        {
            // The existence check reads the stored values, so it can miss a secret
            // that was just written, the same way the read does
            if (!overwrite && _stored.ContainsKey(key) && _staleChecks++ >= _staleAttempts)
                throw new UserInformationException($"The key '{key}' already exists", "KeyAlreadyExists");

            _stored[key] = value;
            return Task.CompletedTask;
        }

        public Task InitializeAsync(Uri config, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<bool> IsSupported(CancellationToken cancellationToken) => Task.FromResult(true);
        public bool IsSetSupported => true;
        public string Key => "delayed";
        public string DisplayName => "Delayed visibility provider";
        public string Description => "A provider that becomes readable only after a number of reads";
        public IList<ICommandLineArgument> SupportedCommands => new List<ICommandLineArgument>();
    }

    [Test]
    public async Task ResolveSecretWithRetry_WaitsForADelayedSecret_Async()
    {
        // A write is not guaranteed to be visible to the next read, and the read
        // reports a missing key by throwing
        var provider = new DelayedVisibilitySecretProvider(staleAttempts: 1);
        await provider.SetSecretAsync("alpha", "bravo", overwrite: false, CancellationToken.None);

        var secrets = await ResolveSecretWithRetryAsync(provider, "alpha", "bravo");

        Assert.AreEqual("bravo", secrets["alpha"]);
    }

    [Test]
    public async Task ResolveSecretWithRetry_ReportsASecretThatNeverAppears_Async()
    {
        // A key that is genuinely absent must still fail, not be reported as empty
        var provider = new DelayedVisibilitySecretProvider(staleAttempts: int.MaxValue);
        var noDelays = new[] { TimeSpan.Zero, TimeSpan.Zero };

        Assert.ThrowsAsync<KeyNotFoundException>(() => ResolveSecretWithRetryAsync(provider, "alpha", "bravo", noDelays));
        await Task.CompletedTask;
    }

    [Test]
    public async Task SetSecretRejectsExistingKey_WaitsForTheWriteToBeVisible_Async()
    {
        // The duplicate check reads the stored values, so it can miss a key that
        // was just written
        var provider = new DelayedVisibilitySecretProvider(staleAttempts: 1);
        await provider.SetSecretAsync("alpha", "bravo", overwrite: false, CancellationToken.None);

        await AssertSetSecretRejectsExistingKeyAsync(provider, "alpha", "charlie");
    }

    /// <summary>
    /// The delays to use while waiting for a written secret to become readable
    /// </summary>
    private static readonly TimeSpan[] ConsistencyDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    /// <summary>
    /// Checks that writing to an existing key is rejected. The existence check reads
    /// the stored secrets, so it can miss a key that was just written; a write that
    /// slips through is harmless here, as the callers overwrite the value afterwards.
    /// </summary>
    /// <param name="provider">The provider to write to</param>
    /// <param name="key">The key that already exists</param>
    /// <param name="value">The value to attempt to write</param>
    /// <param name="delays">The delays to use, or null to use the defaults</param>
    private static async Task AssertSetSecretRejectsExistingKeyAsync(ISecretProvider provider, string key, string value, TimeSpan[]? delays = null)
    {
        foreach (var delay in delays ?? ConsistencyDelays)
        {
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay).ConfigureAwait(false);

            try
            {
                await provider.SetSecretAsync(key, value, overwrite: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (UserInformationException)
            {
                return;
            }
        }

        NUnit.Framework.Assert.Fail($"Writing to the existing key '{key}' should have been rejected");
    }

    /// <summary>
    /// Reads a secret, waiting for it to become readable, as a write is not
    /// guaranteed to be visible to the next read
    /// </summary>
    /// <param name="provider">The provider to read from</param>
    /// <param name="key">The key to read</param>
    /// <param name="expected">The value to wait for</param>
    /// <param name="delays">The delays to use, or null to use the defaults</param>
    /// <returns>The resolved secrets</returns>
    private static async Task<IDictionary<string, string>> ResolveSecretWithRetryAsync(ISecretProvider provider, string key, string expected, TimeSpan[]? delays = null)
    {
        var schedule = delays ?? ConsistencyDelays;
        IDictionary<string, string>? secrets = null;

        for (var i = 0; i < schedule.Length; i++)
        {
            if (schedule[i] > TimeSpan.Zero)
                await Task.Delay(schedule[i]).ConfigureAwait(false);

            try
            {
                secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None).ConfigureAwait(false);
            }
            // A key that is not readable yet is reported as missing; on the last
            // attempt the error is passed on, so a key that is really absent fails
            catch (KeyNotFoundException) when (i < schedule.Length - 1)
            {
                continue;
            }

            if (secrets.TryGetValue(key, out var actual) && actual == expected)
                break;
        }

        return secrets ?? new Dictionary<string, string>();
    }

    private static async Task<bool> IsDockerAvailable_Async()
    {
        try
        {
            // Uses default env/OS docker endpoint resolution.
            using var client = new DockerClientConfiguration().CreateClient();
            await client.System.PingAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task HcVaultProvider_SetSecret_Works_WithTestcontainers_Async()
    {
        if (!await IsDockerAvailable_Async())
            Assert.Ignore("Testcontainers not available");

        const string rootToken = "duplicati-root-token";
        const string mount = "kv";
        const string probeSecret = "probe";

        IContainer? container = null;
        VaultClient? cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            container = new ContainerBuilder()
                .WithImage("hashicorp/vault:1.17")
                .WithImagePullPolicy(PullPolicy.Missing)
                .WithPortBinding(8200, 8200)
                .WithCommand("vault", "server", "-dev", $"-dev-root-token-id={rootToken}", "-dev-listen-address=0.0.0.0:8200")
                .WithWaitStrategy(Wait.ForUnixContainer().UntilPortIsAvailable(8200))
                .Build();

            try
            {
                await container.StartAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                NUnit.Framework.Assert.Ignore($"HashiCorp Vault Testcontainers setup failed: {ex.Message}");
                return;
            }

            var hostPort = container.GetMappedPublicPort(8200);
            var host = $"http://localhost:{hostPort}";

            cleanupClient = new VaultClient(new VaultClientSettings(host, new TokenAuthMethodInfo(rootToken)));

            // Ensure the KV v2 secrets engine is available on the configured mount.
            using (var httpClient = new HttpClient { BaseAddress = new Uri(host) })
            {
                httpClient.DefaultRequestHeaders.Add("X-Vault-Token", rootToken);

                var mountConfig = new
                {
                    type = "kv",
                    options = new Dictionary<string, string>
                    {
                        ["version"] = "2"
                    }
                };

                var json = System.Text.Json.JsonSerializer.Serialize(mountConfig);
                using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

                var response = await httpClient.PostAsync($"/v1/sys/mounts/{mount}", content).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode &&
                    response.StatusCode != System.Net.HttpStatusCode.BadRequest)
                {
                    NUnit.Framework.Assert.Ignore($"Failed to configure KV v2 secrets engine on mount '{mount}': {response.StatusCode}");
                    return;
                }
            }

            // Ensure the probe secret exists so that HCVaultSecretProvider.InitializeAsync connectivity check succeeds.
            var probePayload = new Dictionary<string, object>
            {
                ["dummy"] = "value"
            };

            await cleanupClient.V1.Secrets.KeyValue.V2.WriteSecretAsync(
                probeSecret,
                probePayload,
                null,
                mount).ConfigureAwait(false);

            var providerUri = new Uri(
                $"hcv://localhost:{hostPort}/?token={Uri.EscapeDataString(rootToken)}&connection-type=http&mount={mount}&secrets={probeSecret}");

            var provider = new HCVaultSecretProvider();
            await provider.InitializeAsync(providerUri, CancellationToken.None);

            var key = $"duplicati-hcv-{Guid.NewGuid():N}";
            createdSecretId = key;

            // Write the secret using the provider under test.
            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);

            // Verify the secret directly via Vault using the same mount, to avoid relying on ResolveSecretsAsync
            // semantics in this Testcontainers-based test (those are covered by the environment-based test).
            var secret1 = await cleanupClient.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(key, mountPoint: mount)
                .ConfigureAwait(false);

            var data1 = secret1?.Data?.Data;
            Assert.IsNotNull(data1, "Vault returned no data for the created secret");

            // Verify that attempting to set without overwrite fails.
            NUnit.Framework.Assert.ThrowsAsync<Duplicati.Library.Interface.UserInformationException>(() =>
                provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            // Overwrite and verify that Vault still returns data for the secret.
            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);

            var secret2 = await cleanupClient.V1.Secrets.KeyValue.V2
                .ReadSecretAsync(key, mountPoint: mount)
                .ConfigureAwait(false);

            var data2 = secret2?.Data?.Data;
            Assert.IsNotNull(data2, "Vault returned no data for the updated secret");

            // Verify ResolveSecretsAsync behavior for missing keys.
            NUnit.Framework.Assert.ThrowsAsync<KeyNotFoundException>(() =>
                provider.ResolveSecretsAsync(new[] { "duplicati-hcv-missing-" + Guid.NewGuid().ToString("N") }, CancellationToken.None));
        }
        finally
        {
            if (cleanupClient != null)
            {
                if (!string.IsNullOrEmpty(createdSecretId))
                {
                    try
                    {
                        await cleanupClient.V1.Secrets.KeyValue.V2.DeleteSecretAsync(createdSecretId, mountPoint: mount)
                            .ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                }
            }

            if (container != null)
            {
                try
                {
                    await container.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                }

                await container.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Exercises the libsecret provider against whatever Secret Service is on the session
    /// bus. Unlike the other providers here there is no way to point it at a container: the
    /// service has to be on the same bus as this process, so the test runs where one exists
    /// and skips where it does not.
    /// </summary>
    [Test]
    [Category("SecretProviders.Remote")]
    public async Task LibSecretProvider_SetSecret_Works_Async()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Ignore("The libsecret provider is only supported on Linux");
            return;
        }

        var provider = new LibSecretLinuxProvider();
        if (!await provider.IsSupported(CancellationToken.None).ConfigureAwait(false))
        {
            Assert.Ignore("No Secret Service is available on the session bus");
            return;
        }

        // The default collection, not one of the test's own making: creating a collection
        // asks the secret service for a new keyring, which it answers with a prompt for the
        // keyring password, and a prompt needs a prompter to display it. Where there is none
        // the service hands back "/" for both the collection and the prompt, so a test that
        // created its own collection could only ever run in front of a desktop session.
        //
        // The keys below are named for this test and carry a fresh guid, so nothing that is
        // already in the default collection is read, written or replaced.
        await provider.InitializeAsync(new Uri("libsecret://"), CancellationToken.None).ConfigureAwait(false);
        Assert.IsTrue(await provider.DoesCollectionExist(CancellationToken.None).ConfigureAwait(false),
            "The default collection should be resolvable");

        var key = $"duplicati-test-key-{Guid.NewGuid():N}";

        await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None).ConfigureAwait(false);
        var resolved = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual("value1", resolved[key], "The stored secret should come back unchanged");

        // Writing the same key again without overwrite must not silently replace it.
        // Caught by hand rather than with Assert.ThrowsAsync: the platform guard above does
        // not reach inside a lambda, which CA1416 reports.
        var refused = false;
        try
        {
            await provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
            refused = true;
        }
        Assert.IsTrue(refused, "Storing over an existing key without overwrite should be refused");

        var unchanged = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual("value1", unchanged[key], "The refused write must leave the original value in place");

        await provider.SetSecretAsync(key, "value2", overwrite: true, CancellationToken.None).ConfigureAwait(false);
        var overwritten = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None).ConfigureAwait(false);
        Assert.AreEqual("value2", overwritten[key], "Overwriting should replace the stored value");

        // A key that was never stored must not come back as a value.
        var missingKey = $"duplicati-test-missing-{Guid.NewGuid():N}";
        var reportedMissing = false;
        try
        {
            var absent = await provider.ResolveSecretsAsync(new[] { missingKey }, CancellationToken.None).ConfigureAwait(false);
            reportedMissing = !absent.ContainsKey(missingKey);
        }
        catch (Exception)
        {
            reportedMissing = true;
        }
        Assert.IsTrue(reportedMissing, "Resolving a key that does not exist must not yield a value");
    }
}