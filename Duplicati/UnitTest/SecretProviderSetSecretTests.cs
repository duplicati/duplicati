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
using System.IO;
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
        var accessKey = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AWSSM_ACCESS_KEY");
        var secretKey = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AWSSM_SECRET_KEY");
        var region = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AWSSM_REGION");
        var secretName = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AWSSM_SECRET_NAME");

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(secretName))
            Assert.Ignore("AWS secret provider tests require DUPLICATI_TEST_AWSSM_* environment variables");

        AmazonSecretsManagerClient cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            var awsConfig = new AmazonSecretsManagerConfig
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            };

            cleanupClient = new AmazonSecretsManagerClient(accessKey, secretKey, awsConfig);

            var query = $"access-key={Uri.EscapeDataString(accessKey)}&secret-key={Uri.EscapeDataString(secretKey)}&region={Uri.EscapeDataString(region)}&secrets={Uri.EscapeDataString(secretName)}";
            var provider = new AWSSecretProvider();
            await provider.InitializeAsync(new Uri($"awssm://localhost/?{query}"), CancellationToken.None);

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
                        await cleanupClient.DeleteSecretAsync(new DeleteSecretRequest
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
        var vaultUri = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AZKV_VAULT_URI");
        var tenantId = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AZKV_TENANT_ID");
        var clientId = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AZKV_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("DUPLICATI_TEST_AZKV_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(vaultUri) || string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            Assert.Ignore("Azure secret provider tests require DUPLICATI_TEST_AZKV_* environment variables");

        var query = string.Join('&', new[]
        {
            $"vault-uri={Uri.EscapeDataString(vaultUri)}",
            "auth-type=clientsecret",
            $"tenant-id={Uri.EscapeDataString(tenantId)}",
            $"client-id={Uri.EscapeDataString(clientId)}",
            $"client-secret={Uri.EscapeDataString(clientSecret)}"
        });

        var provider = new AzureSecretProvider();
        await provider.InitializeAsync(new Uri($"azkv://localhost/?{query}"), CancellationToken.None);

        var key = $"duplicati-az-{Guid.NewGuid():N}";
        await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
        var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
        Assert.AreEqual("value1", secrets[key]);

        NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

        await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
        var updated = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
        Assert.AreEqual("value3", updated[key]);
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task GcsProvider_SetSecret_Works()
    {
        var projectId = Environment.GetEnvironmentVariable("DUPLICATI_TEST_GCS_PROJECT_ID");
        var accessToken = Environment.GetEnvironmentVariable("DUPLICATI_TEST_GCS_ACCESS_TOKEN");
        var secretId = Environment.GetEnvironmentVariable("DUPLICATI_TEST_GCS_SECRET_ID");

        if (string.IsNullOrWhiteSpace(projectId) || string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(secretId))
            Assert.Ignore("GCS secret provider tests require DUPLICATI_TEST_GCS_* environment variables");

        var query = string.Join('&', new[]
        {
            $"project-id={Uri.EscapeDataString(projectId)}",
            $"access-token={Uri.EscapeDataString(accessToken)}",
            $"version=latest"
        });

        var provider = new GCSSecretProvider();
        await provider.InitializeAsync(new Uri($"gcsm://localhost/?{query}"), CancellationToken.None);

        var key = $"duplicati-gcs-{Guid.NewGuid():N}";
        await provider.SetSecretAsync(key, "value1", overwrite: false, CancellationToken.None);
        var secrets = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
        Assert.AreEqual("value1", secrets[key]);

        NUnit.Framework.Assert.ThrowsAsync<InvalidOperationException>(() => provider.SetSecretAsync(key, "value2", overwrite: false, CancellationToken.None));

        await provider.SetSecretAsync(key, "value3", overwrite: true, CancellationToken.None);
        var updated = await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None);
        Assert.AreEqual("value3", updated[key]);
    }

    [Test]
    [Category("SecretProviders.Remote")]
    public async Task HcVaultProvider_SetSecret_Works()
    {
        var host = Environment.GetEnvironmentVariable("DUPLICATI_TEST_HCV_HOST");
        var token = Environment.GetEnvironmentVariable("DUPLICATI_TEST_HCV_TOKEN");
        var mount = Environment.GetEnvironmentVariable("DUPLICATI_TEST_HCV_MOUNT") ?? "secret";
        var secretList = Environment.GetEnvironmentVariable("DUPLICATI_TEST_HCV_SECRETS");

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secretList))
            Assert.Ignore("HashiCorp Vault provider tests require DUPLICATI_TEST_HCV_* environment variables");

        VaultClient cleanupClient = null;
        string createdSecretId = string.Empty;

        try
        {
            var uri = new Uri($"{host}?token={Uri.EscapeDataString(token)}&secrets={Uri.EscapeDataString(secretList)}&mount={Uri.EscapeDataString(mount)}");
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
}