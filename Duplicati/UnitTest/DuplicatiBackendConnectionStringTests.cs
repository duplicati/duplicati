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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using S3 = Duplicati.Library.Backend.S3;
using Duplicati.Library.Backend.Duplicati;
using Duplicati.Library.Backend.GoogleCloudStorage;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using static Duplicati.UnitTest.DuplicatiBackendTestServer;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// In direct mode the storage server sends the connection string of the backend that holds
/// the data, and the Duplicati backend uses that backend with the connection string as
/// supplied. These tests cover that any backend can be named, when the backend is replaced,
/// what is refused, and the S3 credentials a server may send instead.
/// </summary>
[TestFixture]
public class DuplicatiBackendConnectionStringTests
{
    private const string S3Credentials = "{\"mode\":\"direct\",\"endpoint\":\"s3.example.com\",\"accessKeyId\":\"access\",\"secretKey\":\"secret\",\"bucket\":\"bucket\",\"prefix\":\"backup-1\",\"uploadsDisabled\":false,\"accountDisabled\":false}";
    private const string GcsConnectionString = "gcs://bucket-1/dev-backup-1?gcs-access-token=token-1";

    [OneTimeSetUp]
    public void RegisterTheBackendLoader()
    {
        // Using the loader registers it as the factory the Duplicati backend creates backends with
        Assert.IsNotEmpty(BackendLoader.Keys);
    }

    /// <summary>
    /// The credentials the server returns with a connection string
    /// </summary>
    /// <param name="connectionString">The connection string</param>
    /// <param name="expires">When the credentials in it expire</param>
    private static string Credentials(string connectionString, DateTimeOffset expires)
        => JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["mode"] = "direct",
            ["connectionString"] = connectionString,
            ["rotateAfter"] = expires.ToUnixTimeSeconds(),
            ["objectLockEnabled"] = false,
            ["uploadsDisabled"] = false,
            ["accountDisabled"] = false
        });

    private static FakeServer ServerWith(string connectionString)
        => new FakeServer().On("/credentials", HttpStatusCode.OK, Credentials(connectionString, DateTimeOffset.UtcNow.AddHours(1)), "application/json");

    [Test]
    [Category("Backend")]
    public async Task S3CredentialsWithoutAConnectionStringUseS3()
    {
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, S3Credentials, "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsInstanceOf<S3>(backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public async Task TheConnectionStringDecidesTheBackend()
    {
        using var backend = CreateBackend(ServerWith(GcsConnectionString));

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsInstanceOf<GoogleCloudStorage>(backend.DirectModeBackend);
        // Everything the backend needs is in the connection string
        Assert.IsFalse(backend.DirectModeOptions!.ContainsKey("aws-access-key-id"));
    }

    [Test]
    [Category("Backend")]
    public async Task TheConnectionStringWinsOverS3Credentials()
    {
        var both = S3Credentials.Replace("\"mode\":\"direct\",", "\"mode\":\"direct\",\"connectionString\":\"" + GcsConnectionString + "\",");
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, both, "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsInstanceOf<GoogleCloudStorage>(backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public async Task AnyBackendCanBeNamedAndIsUsedForTheData()
    {
        // No backend is known to the Duplicati backend by name: a local folder serves as well
        using var folder = new Library.Utility.TempFolder();
        using var backend = CreateBackend(ServerWith("file://" + (string)folder));

        using (var content = new MemoryStream(Encoding.UTF8.GetBytes("hello")))
            await backend.PutAsync("file.txt", content, CancellationToken.None);

        var listed = new List<string>();
        await foreach (var entry in backend.ListAsync(CancellationToken.None))
            listed.Add(entry.Name);
        using var downloaded = new MemoryStream();
        await backend.GetAsync("file.txt", downloaded, CancellationToken.None);

        Assert.AreEqual("hello", System.IO.File.ReadAllText(Path.Combine((string)folder, "file.txt")));
        Assert.Contains("file.txt", listed);
        Assert.AreEqual("hello", Encoding.UTF8.GetString(downloaded.ToArray()));
    }

    [Test]
    [Category("Backend")]
    public void APointInTimeIsRefusedByABackendThatCannotReadOlderVersions()
    {
        // The server reports object locking, but the backend it names keeps no versions to read
        var credentials = Credentials(GcsConnectionString + "&gcs-retention-policy-mode=Locked", DateTimeOffset.UtcNow.AddHours(1))
            .Replace("\"objectLockEnabled\":false", "\"objectLockEnabled\":true");
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, credentials, "application/json");
        using var backend = CreateBackend(server, new Dictionary<string, string?> { ["remote-point-in-time"] = "1D" });

        var ex = NUnit.Framework.Assert.ThrowsAsync<UserInformationException>(() => backend.EnsureCredentialsAsync(CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_POINT_IN_TIME_NOT_AVAILABLE, ex!.HelpID);
        Assert.IsNull(backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public async Task TheRetentionModeInTheConnectionStringReachesTheBackend()
    {
        // Object locks go to the backend the server names, which locks with the mode the server chose
        var credentials = Credentials(GcsConnectionString + "&gcs-retention-policy-mode=Locked", DateTimeOffset.UtcNow.AddHours(1))
            .Replace("\"objectLockEnabled\":false", "\"objectLockEnabled\":true");
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, credentials, "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsInstanceOf<ILockingBackend>(backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public void TheCapabilitiesAreThoseOfTheBackendInUse()
    {
        // A local folder can rename but not lock, and the check asks the server which backend is used
        using var folder = new Library.Utility.TempFolder();
        var fileServer = ServerWith("file://" + (string)folder);
        using var onFile = CreateBackend(fileServer);

        Assert.IsTrue(onFile.TryGetCapability<IRenameEnabledBackend>(out _));
        Assert.IsFalse(onFile.TryGetCapability<ILockingBackend>(out _));
        Assert.AreEqual(1, fileServer.Requests.Count(x => x == "/credentials"));

        // Google Cloud Storage can do both
        using var onGcs = CreateBackend(ServerWith(GcsConnectionString));
        Assert.IsTrue(onGcs.TryGetCapability<IRenameEnabledBackend>(out _));
        Assert.IsTrue(onGcs.TryGetCapability<ILockingBackend>(out var locking));
        Assert.AreSame(onGcs, locking);

        // The storage server answers for the quota whatever backend holds the data,
        // and a wrapper around the backend gets the same answers
        Assert.IsTrue(onFile.TryGetCapability<IQuotaEnabledBackend>(out _));
        Assert.IsTrue(onFile.TryGetCapability<IStreamingBackend>(out _));
        Assert.IsFalse(onFile.TryGetCapability<IFolderEnabledBackend>(out _));
        var wrapped = new RemoteFilenameSuffixBackend(onFile, ".suffix");
        Assert.IsTrue(wrapped.TryGetCapability<IRenameEnabledBackend>(out _));
        Assert.IsFalse(wrapped.TryGetCapability<ILockingBackend>(out _));
    }

    [Test]
    [Category("Backend")]
    public void InGatewayModeTheStorageServerRenamesAndLocks()
    {
        using var backend = CreateBackend(new FakeServer().On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json"));

        Assert.IsTrue(backend.TryGetCapability<IRenameEnabledBackend>(out _));
        Assert.IsTrue(backend.TryGetCapability<ILockingBackend>(out _));
    }

    [Test]
    [Category("Backend")]
    public async Task ValidCredentialsAreNotRequestedAgain()
    {
        var server = ServerWith(GcsConnectionString);
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);
        var first = backend.DirectModeBackend;
        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.AreEqual(1, server.Requests.Count);
        Assert.AreSame(first, backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public async Task ExpiringCredentialsAreRenewedAndANewConnectionStringReplacesTheBackend()
    {
        // The credentials expire at the time the server says, also when that is soon
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, Credentials(GcsConnectionString, DateTimeOffset.UtcNow.AddMinutes(1)), "application/json")
            .On("/credentials", HttpStatusCode.OK, Credentials(GcsConnectionString.Replace("token-1", "token-2"), DateTimeOffset.UtcNow.AddHours(1)), "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);
        var first = backend.DirectModeBackend;
        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.AreEqual(2, server.Requests.Count);
        Assert.IsInstanceOf<GoogleCloudStorage>(backend.DirectModeBackend);
        Assert.AreNotSame(first, backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public async Task AnUnchangedConnectionStringKeepsTheBackend()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, Credentials(GcsConnectionString, DateTimeOffset.UtcNow.AddMinutes(1)), "application/json")
            .On("/credentials", HttpStatusCode.OK, Credentials(GcsConnectionString, DateTimeOffset.UtcNow.AddHours(1)), "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);
        var first = backend.DirectModeBackend;
        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.AreEqual(2, server.Requests.Count);
        Assert.AreSame(first, backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public void ABackendThisVersionDoesNotHaveIsRefusedInsteadOfUsingTheGateway()
    {
        using var backend = CreateBackend(ServerWith("tape://drive-1/backup"));

        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.EnsureCredentialsAsync(CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_UNSUPPORTED_BACKEND, ex!.HelpID);
        Assert.IsNull(backend.DirectModeBackend);
    }

    [Test]
    [Category("Backend")]
    public void AConnectionStringBackToTheStorageServerIsRefused()
    {
        using var backend = CreateBackend(ServerWith("duplicati://storage.example.com?duplicati-backup-id=x"));

        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.EnsureCredentialsAsync(CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_UNSUPPORTED_BACKEND, ex!.HelpID);
    }

    /// <summary>
    /// Answers every request with an empty object listing, and records the authorization sent
    /// </summary>
    private sealed class RecordingGcsHandler : HttpMessageHandler
    {
        public List<string?> Authorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorizations.Add(request.Headers.Authorization?.ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"items\":[]}", Encoding.UTF8, "application/json")
            });
        }
    }

    [Test]
    [Category("Backend")]
    public async Task GoogleCloudStorageSendsTheAccessTokenFromItsOptions()
    {
        var handler = new RecordingGcsHandler();
        using var backend = new GoogleCloudStorage(
            "gcs://bucket-1/dev-backup-1",
            new Dictionary<string, string?> { [GoogleCloudStorage.ACCESS_TOKEN_OPTION] = "token-1" },
            new HttpClient(handler));

        await foreach (var _ in backend.ListAsync(CancellationToken.None)) { }

        Assert.AreEqual("Bearer token-1", handler.Authorizations.Single());
    }

    [Test]
    [Category("Backend")]
    public void GoogleCloudStorageNeedsNoOtherCredentialsWithAnAccessToken()
    {
        // As the backend loader creates it from the connection string the storage server sends
        using var backend = BackendLoader.GetBackend(GcsConnectionString, new Dictionary<string, string>());

        Assert.IsInstanceOf<GoogleCloudStorage>(backend);
    }
}
