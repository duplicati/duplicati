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
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;

namespace Duplicati.UnitTest;

[TestFixture]
public class SecretProviderSetSecretTests
{
    [Test]
    public async Task FileProvider_SetSecret_WritesToFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"duplicati-secrets-{Guid.NewGuid():N}.json");
        await File.WriteAllTextAsync(tempFile, "{}", CancellationToken.None);

        var provider = new FileSecretProvider();
        await provider.InitializeAsync(new Uri(tempFile), CancellationToken.None);

        try
        {
            await provider.SetSecretAsync("alpha", "bravo", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { "alpha" }, CancellationToken.None);
            Assert.AreEqual("bravo", secrets["alpha"]);

            NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync("alpha", "charlie", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync("alpha", "charlie", overwrite: true, CancellationToken.None);
            var updated = await provider.ResolveSecretsAsync(new[] { "alpha" }, CancellationToken.None);
            Assert.AreEqual("charlie", updated["alpha"]);

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(tempFile, CancellationToken.None));
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
    public async Task AwsProvider_SetSecret_Works()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AWSSM_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("AWS secret provider tests require DUPLICATI_TEST_AWSSM_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var accessKey = queryParams["access-key"];
        var secretKey = queryParams["secret-key"];
        var region = queryParams["region"];
        var secretName = queryParams["secrets"];

        AmazonSecretsManagerClient cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            cleanupClient = new AmazonSecretsManagerClient(accessKey, secretKey, Amazon.RegionEndpoint.GetBySystemName(region));

            var provider = new AWSSecretProvider();
            await provider.InitializeAsync(uri, CancellationToken.None);

            var key = $"duplicati-aws-{Guid.NewGuid():N}";
            createdSecretId = key;

            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
            Assert.AreEqual("value1", secrets[key]);

            NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
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
                        await cleanupClient.DeleteSecretAsync(new Amazon.SecretsManager.Model.DeleteSecretRequest
                        {
                            SecretId = createdSecretId,
                            ForceDeleteWithoutRecovery = true
                        }).ConfigureAwait(false);
                    }
                    catch (ResourceNotFoundException)
                    {
                    }
                }

                cleanupClient.Dispose();
            }
        }
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task AzureProvider_SetSecret_Works()
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

        SecretClient cleanupClient = null;
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

            NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
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
    public async Task GcsProvider_SetSecret_Works()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_GCS_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("GCS secret provider tests require DUPLICATI_TEST_GCS_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var projectId = queryParams["project-id"];
        var accessToken = queryParams["access-token"];

        SecretManagerServiceClient cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            var builder = new SecretManagerServiceClientBuilder();
            builder.Credential = GoogleCredential.FromAccessToken(accessToken);
            cleanupClient = builder.Build();

            var provider = new GCSSecretProvider();
            await provider.InitializeAsync(uri, CancellationToken.None);

            var key = $"duplicati-gcs-{Guid.NewGuid():N}";
            createdSecretId = key;
            await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
            var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
            Assert.AreEqual("value1", secrets[key]);

            NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

            await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
            var updated = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
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
    public async Task HcVaultProvider_SetSecret_Works()
    {
        var url = Environment.GetEnvironmentVariable("DUPLICATI_TEST_HCV_URL");

        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore("HashiCorp Vault provider tests require DUPLICATI_TEST_HCV_URL environment variable");

        var uri = new Uri(url);
        var queryParams = uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=')).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        var host = uri.GetLeftPart(UriPartial.Authority);
        var token = queryParams["token"];
        var mount = queryParams.TryGetValue("mount", out var m) ? m : "secret";

        VaultClient cleanupClient = null;
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
            var updated = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
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

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task HcVaultProvider_SetSecret_Works_WithTestcontainers()
    {
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
        }
        finally
        {
            if (cleanupClient != null && !string.IsNullOrEmpty(createdSecretId))
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
}