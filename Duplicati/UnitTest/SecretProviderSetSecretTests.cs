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
using System.Linq;
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
}