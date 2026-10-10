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
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.SecretProvider;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Resolves secrets from a local listener that answers like the KV v2 engine of a HashiCorp Vault server
/// </summary>
[TestFixture]
public class HCVaultSecretProviderResolveTests
{
    private const string Mount = "kv";

    /// <summary>
    /// Serves the given KV v2 secrets (path to key/value pairs) until disposed
    /// </summary>
    private sealed class FakeVault : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Dictionary<string, Dictionary<string, string>> _secrets;
        private readonly Task _loop;

        public int Port { get; }

        public FakeVault(Dictionary<string, Dictionary<string, string>> secrets)
        {
            _secrets = secrets;
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            Port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _loop = Task.Run(ServeAsync);
        }

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await _listener.GetContextAsync().ConfigureAwait(false);
                }
                catch (Exception) when (!_listener.IsListening)
                {
                    return;
                }

                var prefix = $"/v1/{Mount}/data/";
                var path = context.Request.Url!.AbsolutePath;
                if (context.Request.HttpMethod == "GET" && path.StartsWith(prefix, StringComparison.Ordinal)
                    && _secrets.TryGetValue(path.Substring(prefix.Length), out var data))
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        request_id = Guid.NewGuid().ToString(),
                        lease_id = "",
                        renewable = false,
                        lease_duration = 0,
                        data = new
                        {
                            data,
                            metadata = new { created_time = "2026-10-04T00:00:00Z", deletion_time = "", destroyed = false, version = 1 }
                        }
                    });
                    var bytes = Encoding.UTF8.GetBytes(body);
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = 200;
                    await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                }
                else
                {
                    var bytes = Encoding.UTF8.GetBytes("{\"errors\":[]}");
                    context.Response.ContentType = "application/json";
                    context.Response.StatusCode = 404;
                    await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
                }
                context.Response.Close();
            }
        }

        public void Dispose()
        {
            _listener.Stop();
            _listener.Close();
            try { _loop.Wait(TimeSpan.FromSeconds(5)); } catch { }
        }
    }

    private static async Task<Dictionary<string, string>> ResolveAsync(FakeVault vault, string secrets, string key)
    {
        var provider = new HCVaultSecretProvider();
        await provider.InitializeAsync(new Uri($"hcv://localhost:{vault.Port}/?token=test-token&connection-type=http&mount={Mount}&secrets={secrets}"), CancellationToken.None).ConfigureAwait(false);
        return await provider.ResolveSecretsAsync(new[] { key }, CancellationToken.None).ConfigureAwait(false);
    }

    [Test]
    public async Task ResolvesAKeyFromTheListedSecret_Async()
    {
        using var vault = new FakeVault(new()
        {
            ["duplicati"] = new() { ["ENCRYPTION_KEY"] = "value-from-secret", ["OTHER"] = "other" }
        });

        var result = await ResolveAsync(vault, "duplicati", "ENCRYPTION_KEY");

        Assert.That(result["ENCRYPTION_KEY"], Is.EqualTo("value-from-secret"));
    }

    [Test]
    public async Task ResolvesAKeyStoredUnderItsOwnPath_Async()
    {
        using var vault = new FakeVault(new()
        {
            ["probe"] = new() { ["dummy"] = "value" },
            ["ENCRYPTION_KEY"] = new() { ["ENCRYPTION_KEY"] = "value-from-path" }
        });

        var result = await ResolveAsync(vault, "probe", "ENCRYPTION_KEY");

        Assert.That(result["ENCRYPTION_KEY"], Is.EqualTo("value-from-path"));
    }

    [Test]
    public async Task ResolvesTheOnlyValueStoredUnderTheKeyPath_Async()
    {
        using var vault = new FakeVault(new()
        {
            ["probe"] = new() { ["dummy"] = "value" },
            ["ENCRYPTION_KEY"] = new() { ["value"] = "only-value" }
        });

        var result = await ResolveAsync(vault, "probe", "ENCRYPTION_KEY");

        Assert.That(result["ENCRYPTION_KEY"], Is.EqualTo("only-value"));
    }
}
