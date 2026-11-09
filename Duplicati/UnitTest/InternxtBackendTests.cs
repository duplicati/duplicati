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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Internxt;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using CollectionAssert = NUnit.Framework.Legacy.CollectionAssert;
using StringAssert = NUnit.Framework.Legacy.StringAssert;
using Vectors = Duplicati.UnitTest.InternxtCryptoTests.Vectors;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Drives the Internxt backend against an in-memory fake of the Drive and
/// network APIs. The fake stores whatever bytes are PUT to its pre-signed
/// URLs and serves them back, so a Put followed by a Get checks the whole
/// chain: login, key derivation, encryption, shard hashing and the metadata
/// calls, in the order and with the headers the real server expects.
/// </summary>
[TestFixture]
public class InternxtBackendTests
{
    private const string Gateway = "https://gateway.invalid";
    private const string Storage = "https://storage.invalid";
    private const string RootFolder = "00000000-0000-0000-0000-000000000001";
    private const string Email = "user@example.invalid";

    /// <summary>
    /// A file entry in the fake Drive
    /// </summary>
    private sealed class FakeFile
    {
        public string Uuid = Guid.NewGuid().ToString();
        public string PlainName = string.Empty;
        public string Type = string.Empty;
        public string? FileId;
        public long Size;
        public string Status = "EXISTS";
    }

    /// <summary>
    /// A network object in the fake bridge
    /// </summary>
    private sealed class FakeObject
    {
        public string Index = string.Empty;
        public string Hash = string.Empty;
        public byte[] Data = [];
        public int Version = 2;
    }

    /// <summary>
    /// The in-memory server
    /// </summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public readonly Dictionary<string, (string Name, string Parent)> Folders = new() { [RootFolder] = ("", "") };
        public readonly List<FakeFile> Files = [];
        public readonly ConcurrentDictionary<string, FakeObject> Objects = new();
        public readonly ConcurrentDictionary<string, (string FileId, byte[] Data)> PendingUploads = new();
        public readonly List<string> Log = [];
        public int Logins;
        public int Refreshes;
        public bool RequireTfa;
        /// <summary>
        /// When set, the server accepts the TOTP code for this secret instead of the fixed code
        /// </summary>
        public string? TotpSecret;
        public bool PlanWithoutCliAccess;
        public int RateLimitNextDriveCalls;
        public long? MaxUploadFileSize;
        public Func<FakeObject, byte[]>? CorruptDownload;
        public string Password = Vectors.Password;
        public string ExpectedClientName = InternxtClient.DefaultClientName;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var path = uri.AbsolutePath;
            Log.Add($"{request.Method} {path}{uri.Query}");
            // The gateway's Cloudflare front blocks POSTs without a User-Agent
            Assert.IsTrue(request.Headers.UserAgent.Any(ua => ua.Product?.Name == "Duplicati"), $"every request carries a User-Agent: {request.Method} {path}");

            if (uri.Host == new Uri(Storage).Host)
                return await HandleStorageAsync(request, cancellationToken);

            Assert.AreEqual(ExpectedClientName, request.Headers.GetValues("internxt-client").Single(), "every API call carries the client header");
            Assert.IsTrue(request.Headers.Contains("internxt-version"), "every API call carries the version header");

            if (path.StartsWith("/network/"))
                return await HandleNetworkAsync(request, path["/network".Length..], cancellationToken);
            if (path.StartsWith("/drive/"))
                return await HandleDriveAsync(request, path["/drive".Length..], uri, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static string Jwt()
        {
            static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            var exp = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds();
            return $"{B64("{\"alg\":\"HS256\"}")}.{B64($"{{\"exp\":{exp}}}")}.sig";
        }

        private static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
            => new(status) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        private static async Task<JsonNode> Body(HttpRequestMessage request, CancellationToken ct)
            => JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!;

        private object FileJson(FakeFile f) => new
        {
            uuid = f.Uuid,
            fileId = f.FileId,
            plainName = f.PlainName,
            name = "encrypted-" + f.PlainName,
            type = f.Type,
            size = f.Size.ToString(),
            bucket = Vectors.BucketId,
            folderUuid = RootFolder,
            status = f.Status,
            modificationTime = "2026-01-02T03:04:05.000Z",
            createdAt = "2026-01-02T03:04:05.000Z",
            updatedAt = "2026-01-02T03:04:05.000Z"
        };

        private async Task<HttpResponseMessage> HandleDriveAsync(HttpRequestMessage request, string path, Uri uri, CancellationToken ct)
        {
            if (path == "/auth/login" && request.Method == HttpMethod.Post)
            {
                var body = await Body(request, ct);
                if ((string?)body["email"] != Email)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return Json(new { sKey = Vectors.EncryptedSalt, tfa = RequireTfa });
            }

            if (path == "/auth/cli/login/access" && request.Method == HttpMethod.Post)
            {
                Logins++;
                var body = await Body(request, ct);
                var sentHash = InternxtCrypto.DecryptText((string)body["password"]!, InternxtCrypto.AppCryptoSecret);
                if (sentHash != InternxtCrypto.PasswordHash(Password, Vectors.SaltHex))
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                if (RequireTfa)
                {
                    var sent = (string?)body["tfa"];
                    var now = DateTimeOffset.UtcNow;
                    // Accept the current and the previous step, like a real verifier
                    var accepted = TotpSecret == null
                        ? sent == "123456"
                        : sent == InternxtTotp.Compute(TotpSecret, now) || sent == InternxtTotp.Compute(TotpSecret, now.AddSeconds(-InternxtTotp.StepSeconds));
                    if (!accepted)
                        return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }
                if (PlanWithoutCliAccess)
                    return Json(new { message = "This user current tier does not allow CLI/Rclone access" }, HttpStatusCode.PaymentRequired);

                return Json(new
                {
                    user = new
                    {
                        email = Email,
                        userId = Vectors.UserId,
                        mnemonic = Vectors.EncryptedMnemonic,
                        rootFolderId = RootFolder,
                        uuid = Guid.NewGuid().ToString(),
                        bucket = Vectors.BucketId,
                        bridgeUser = Vectors.BridgeUser
                    },
                    token = "legacy",
                    newToken = Jwt()
                });
            }

            // Everything below needs a bearer token
            if (request.Headers.Authorization?.Scheme != "Bearer" || string.IsNullOrEmpty(request.Headers.Authorization.Parameter))
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);

            if (RateLimitNextDriveCalls > 0)
            {
                RateLimitNextDriveCalls--;
                var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMilliseconds(10));
                return r;
            }

            if (path == "/users/cli/refresh")
            {
                Refreshes++;
                return Json(new { newToken = Jwt(), user = new { } });
            }

            if (path == "/users/usage") return Json(new { drive = 1234L, backup = 0L, mail = 0L, total = 1234L });
            if (path == "/users/limit") return Json(new { maxSpaceBytes = 10_000_000L });
            if (path == "/files/limits") return Json(new { maxUploadFileSize = MaxUploadFileSize, versioning = new { }, photosAccess = false });

            if (path == "/folders/meta")
            {
                var q = System.Web.HttpUtility.ParseQueryString(uri.Query)["path"]!.Trim('/');
                var parent = RootFolder;
                foreach (var seg in q.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    var hit = Folders.FirstOrDefault(f => f.Value.Parent == parent && f.Value.Name == seg);
                    if (hit.Key == null)
                        return new HttpResponseMessage(HttpStatusCode.NotFound);
                    parent = hit.Key;
                }
                return Json(new { uuid = parent, plainName = Folders[parent].Name, status = "EXISTS" });
            }

            if (path == "/folders" && request.Method == HttpMethod.Post)
            {
                var body = await Body(request, ct);
                var name = (string)body["plainName"]!;
                var parent = (string)body["parentFolderUuid"]!;
                if (Folders.Any(f => f.Value.Parent == parent && f.Value.Name == name))
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                var uuid = Guid.NewGuid().ToString();
                Folders[uuid] = (name, parent);
                return Json(new { uuid, plainName = name, parentUuid = parent, status = "EXISTS" });
            }

            var m = Regex.Match(path, "^/folders/v2/content/([^/]+)/(files|folders)$");
            if (m.Success)
            {
                var folder = m.Groups[1].Value;
                if (!Folders.ContainsKey(folder))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (m.Groups[2].Value == "folders")
                    return Json(new { folders = Folders.Where(f => f.Value.Parent == folder && f.Key != RootFolder).Select(f => new { uuid = f.Key, plainName = f.Value.Name, status = "EXISTS" }), nextCursor = (string?)null });

                // Two pages, to prove the cursor is followed
                var all = Files.ToList();
                var cursor = System.Web.HttpUtility.ParseQueryString(uri.Query)["cursor"];
                var start = cursor == null ? 0 : int.Parse(cursor);
                var page = all.Skip(start).Take(Math.Max(1, (all.Count + 1) / 2)).ToList();
                var next = start + page.Count < all.Count ? (start + page.Count).ToString() : null;
                return Json(new { files = page.Select(FileJson), nextCursor = next });
            }

            m = Regex.Match(path, "^/folders/content/([^/]+)/files/existence$");
            if (m.Success)
            {
                var body = await Body(request, ct);
                var wanted = body["files"]!.AsArray().Select(n => ((string)n!["plainName"]!, (string?)n!["type"] ?? ""));
                var hits = Files.Where(f => wanted.Contains((f.PlainName, f.Type)) && f.Status == "EXISTS");
                return Json(new { existentFiles = hits.Select(FileJson) });
            }

            if (path == "/files" && request.Method == HttpMethod.Post)
            {
                var body = await Body(request, ct);
                Assert.AreEqual(Vectors.BucketId, (string?)body["bucket"]);
                Assert.AreEqual("03-aes", (string?)body["encryptVersion"]);
                var f = new FakeFile
                {
                    PlainName = (string)body["plainName"]!,
                    Type = (string?)body["type"] ?? "",
                    FileId = (string?)body["fileId"],
                    Size = (long)body["size"]!
                };
                if (f.Size > 0)
                {
                    Assert.IsNotNull(f.FileId, "a non-empty file needs a network id");
                    Assert.IsTrue(Objects.ContainsKey(f.FileId!), "the network object must be finished before the entry is created");
                }
                else
                    Assert.IsNull(f.FileId, "an empty file must not have a network id");
                if (Files.Any(x => x.PlainName == f.PlainName && x.Type == f.Type && x.Status == "EXISTS"))
                    return new HttpResponseMessage(HttpStatusCode.Conflict);
                Files.Add(f);
                return Json(FileJson(f));
            }

            m = Regex.Match(path, "^/files/([^/]+)$");
            if (m.Success)
            {
                var f = Files.FirstOrDefault(x => x.Uuid == m.Groups[1].Value && x.Status == "EXISTS");
                if (f == null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                if (request.Method == HttpMethod.Delete)
                {
                    f.Status = "DELETED";
                    if (f.FileId != null)
                        Objects.TryRemove(f.FileId, out _);
                    return new HttpResponseMessage(HttpStatusCode.OK);
                }
                if (request.Method == HttpMethod.Put)
                {
                    var body = await Body(request, ct);
                    if (f.FileId != null)
                        Objects.TryRemove(f.FileId, out _);
                    f.FileId = (string?)body["fileId"];
                    f.Size = (long)body["size"]!;
                    return Json(FileJson(f));
                }
            }

            m = Regex.Match(path, "^/files/([^/]+)/meta$");
            if (m.Success && request.Method == HttpMethod.Put)
            {
                var f = Files.FirstOrDefault(x => x.Uuid == m.Groups[1].Value && x.Status == "EXISTS");
                if (f == null)
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                var body = await Body(request, ct);
                f.PlainName = (string)body["plainName"]!;
                f.Type = (string?)body["type"] ?? "";
                return Json(FileJson(f));
            }

            if (path == "/storage/trash/add" && request.Method == HttpMethod.Post)
            {
                var body = await Body(request, ct);
                foreach (var item in body["items"]!.AsArray())
                {
                    var f = Files.FirstOrDefault(x => x.Uuid == (string)item!["uuid"]!);
                    if (f != null)
                        f.Status = "TRASHED";
                }
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unhandled " + path) };
        }

        private async Task<HttpResponseMessage> HandleNetworkAsync(HttpRequestMessage request, string path, CancellationToken ct)
        {
            var expectedAuth = InternxtCrypto.NetworkBasicAuth(Vectors.BridgeUser, Vectors.UserId);
            Assert.AreEqual(expectedAuth, request.Headers.GetValues("Authorization").Single(), "network calls use basic auth with the hashed user id");

            var m = Regex.Match(path, $"^/v2/buckets/{Vectors.BucketId}/files/start$");
            if (m.Success)
            {
                var parts = int.Parse(System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query)["multiparts"]!);
                var body = await Body(request, ct);
                var size = (long)body["uploads"]![0]!["size"]!;
                if (parts > 1 && size < 100L * 1024 * 1024)
                    return Json(new { error = "File too small for multipart" }, HttpStatusCode.BadRequest);
                var uuid = Guid.NewGuid().ToString();
                PendingUploads[uuid] = (string.Empty, []);
                if (parts == 1)
                    return Json(new { uploads = new[] { new { index = 0, uuid, url = $"{Storage}/put/{uuid}", urls = (string[]?)null, UploadId = (string?)null } } });
                return Json(new { uploads = new[] { new { index = 0, uuid, url = (string?)null, urls = Enumerable.Range(1, parts).Select(p => $"{Storage}/put/{uuid}/part/{p}").ToArray(), UploadId = "upload-" + uuid } } });
            }

            m = Regex.Match(path, $"^/v2/buckets/{Vectors.BucketId}/files/finish$");
            if (m.Success)
            {
                var body = await Body(request, ct);
                var shard = body["shards"]![0]!;
                var uuid = (string)shard["uuid"]!;
                Assert.IsTrue(PendingUploads.TryRemove(uuid, out var pending), "finish refers to a started upload");
                var data = pending.Data;
                if (shard["parts"] is JsonArray parts)
                {
                    Assert.IsNotNull(shard["UploadId"]);
                    var ordered = parts.OrderBy(p => (int)p!["PartNumber"]!).Select(p => (string)p!["ETag"]!);
                    data = ordered.SelectMany(etag => PartStore[etag]).ToArray();
                }
                var expectedHash = InternxtCrypto.ShardHash(SHA256.HashData(data));
                if (expectedHash != (string)shard["hash"]!)
                    return Json(new { error = "hash mismatch" }, HttpStatusCode.BadRequest);
                var fileId = Guid.NewGuid().ToString("N")[..24];
                Objects[fileId] = new FakeObject { Index = (string)body["index"]!, Hash = expectedHash, Data = data };
                return Json(new { id = fileId, index = body["index"]!.ToString(), version = 2, bucket = Vectors.BucketId });
            }

            m = Regex.Match(path, $"^/buckets/{Vectors.BucketId}/files/([^/]+)/info$");
            if (m.Success)
            {
                Assert.AreEqual("2", request.Headers.GetValues("x-api-version").Single());
                if (!Objects.TryGetValue(m.Groups[1].Value, out var obj))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                return Json(new { index = obj.Index, size = obj.Data.Length, version = obj.Version, shards = new[] { new { index = 0, hash = obj.Hash, url = $"{Storage}/get/{m.Groups[1].Value}" } } });
            }

            m = Regex.Match(path, $"^/v2/buckets/{Vectors.BucketId}/files/([^/]+)$");
            if (m.Success && request.Method == HttpMethod.Delete)
            {
                Objects.TryRemove(m.Groups[1].Value, out _);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("unhandled network " + path) };
        }

        private readonly ConcurrentDictionary<string, byte[]> PartStore = new();

        private async Task<HttpResponseMessage> HandleStorageAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Assert.IsFalse(request.Headers.Contains("Authorization"), "pre-signed URLs carry no auth header");

            var m = Regex.Match(path, "^/put/([^/]+)$");
            if (m.Success && request.Method == HttpMethod.Put)
            {
                var data = await request.Content!.ReadAsByteArrayAsync(ct);
                Assert.AreEqual(data.Length, request.Content.Headers.ContentLength, "the content length is declared up front");
                PendingUploads[m.Groups[1].Value] = (string.Empty, data);
                return new HttpResponseMessage(HttpStatusCode.OK);
            }

            m = Regex.Match(path, "^/put/([^/]+)/part/([0-9]+)$");
            if (m.Success && request.Method == HttpMethod.Put)
            {
                var data = await request.Content!.ReadAsByteArrayAsync(ct);
                var etag = $"\"etag-{m.Groups[1].Value}-{m.Groups[2].Value}\"";
                PartStore[etag] = data;
                var r = new HttpResponseMessage(HttpStatusCode.OK);
                r.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
                return r;
            }

            m = Regex.Match(path, "^/get/([^/]+)$");
            if (m.Success && request.Method == HttpMethod.Get)
            {
                if (!Objects.TryGetValue(m.Groups[1].Value, out var obj))
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                var data = CorruptDownload?.Invoke(obj) ?? obj.Data;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) };
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }
    }

    private static (InternxtBackend Backend, FakeServer Server) Create(string path = "backups/duplicati", string? password = null, Dictionary<string, string?>? extra = null)
    {
        var server = new FakeServer();
        var options = new Dictionary<string, string?>
        {
            ["auth-username"] = Email,
            ["auth-password"] = password ?? Vectors.Password,
            ["api-url"] = Gateway,
        };
        if (extra != null)
            foreach (var kv in extra)
                options[kv.Key] = kv.Value;

        var backend = new InternxtBackend($"internxt://{path}", options)
        {
            CreateHttpClient = () => new HttpClient(server) { Timeout = Timeout.InfiniteTimeSpan }
        };
        return (backend, server);
    }

    private static byte[] RandomBytes(int n)
    {
        var b = new byte[n];
        new Random(42).NextBytes(b);
        return b;
    }

    [Test]
    [Category("Backend")]
    public async Task PutThenGetRoundTripsThroughEncryption()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        var content = RandomBytes(100_000);
        await backend.PutAsync("duplicati-b0001.dblock.zip.aes", new MemoryStream(content), CancellationToken.None);

        // The bytes on the "wire" are not the plain text, and decrypt with the reference derivation
        var obj = server.Objects.Values.Single();
        Assert.AreEqual(content.Length, obj.Data.Length);
        Assert.IsFalse(obj.Data.AsSpan().SequenceEqual(content));
        var (key, iv) = InternxtCrypto.DeriveFileKey(InternxtCrypto.Bip39Seed(Vectors.Mnemonic), Vectors.BucketId, Convert.FromHexString(obj.Index));
        using var reference = new AesCtrStream(new MemoryStream(obj.Data), key, iv, false);
        using var referenceOut = new MemoryStream();
        await reference.CopyToAsync(referenceOut);
        Assert.IsTrue(referenceOut.ToArray().AsSpan().SequenceEqual(content), "an official client would decrypt the same bytes");

        var stored = server.Files.Single();
        Assert.AreEqual("duplicati-b0001.dblock.zip", stored.PlainName);
        Assert.AreEqual("aes", stored.Type);

        using var output = new MemoryStream();
        await backend.GetAsync("duplicati-b0001.dblock.zip.aes", output, CancellationToken.None);
        Assert.IsTrue(output.ToArray().AsSpan().SequenceEqual(content));

        Assert.AreEqual(1, server.Logins, "one login for the instance");
    }

    [Test]
    [Category("Backend")]
    public async Task ListJoinsNameAndTypeAndFollowsTheCursor()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        var names = new[] { "duplicati-20260101T000000Z.dlist.zip.aes", "duplicati-b0001.dblock.zip.aes", "duplicati-i0001.dindex.zip.aes", "noext", "duplicati-verification.json" };
        foreach (var n in names)
            await backend.PutAsync(n, new MemoryStream(RandomBytes(10)), CancellationToken.None);

        var listed = await backend.ListAsync(CancellationToken.None).ToListAsync();
        CollectionAssert.AreEquivalent(names, listed.Where(e => !e.IsFolder).Select(e => e.Name).ToList());
        Assert.IsTrue(listed.Where(e => !e.IsFolder).All(e => e.Size == 10));
        Assert.IsTrue(server.Log.Count(l => l.Contains("/folders/v2/content/") && l.Contains("/files")) >= 2, "the listing was paged");
    }

    [Test]
    [Category("Backend")]
    public async Task EmptyFileHasNoNetworkObject()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        await backend.PutAsync("empty.txt", new MemoryStream(), CancellationToken.None);
        Assert.IsEmpty(server.Objects);
        Assert.IsNull(server.Files.Single().FileId);

        using var output = new MemoryStream();
        await backend.GetAsync("empty.txt", output, CancellationToken.None);
        Assert.AreEqual(0, output.Length);
    }

    [Test]
    [Category("Backend")]
    public async Task OverwritingReplacesTheEntryInsteadOfDuplicating()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        await backend.PutAsync("duplicati-verification.json", new MemoryStream(RandomBytes(100)), CancellationToken.None);
        var second = RandomBytes(200);
        await backend.PutAsync("duplicati-verification.json", new MemoryStream(second), CancellationToken.None);

        Assert.AreEqual(1, server.Files.Count(f => f.Status == "EXISTS"));
        Assert.AreEqual(1, server.Objects.Count, "the old network object was released");

        using var output = new MemoryStream();
        await backend.GetAsync("duplicati-verification.json", output, CancellationToken.None);
        Assert.IsTrue(output.ToArray().AsSpan().SequenceEqual(second));
    }

    [Test]
    [Category("Backend")]
    public async Task DeleteRenameAndMissingFiles()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        await backend.PutAsync("a.aes", new MemoryStream(RandomBytes(10)), CancellationToken.None);
        await backend.RenameAsync("a.aes", "b.zip.aes", CancellationToken.None);
        Assert.AreEqual(("b.zip", "aes"), (server.Files.Single().PlainName, server.Files.Single().Type));

        using var output = new MemoryStream();
        await backend.GetAsync("b.zip.aes", output, CancellationToken.None);
        Assert.AreEqual(10, output.Length);
        Assert.ThrowsAsync<FileMissingException>(() => backend.GetAsync("a.aes", new MemoryStream(), CancellationToken.None));

        await backend.DeleteAsync("b.zip.aes", CancellationToken.None);
        Assert.AreEqual("DELETED", server.Files.Single().Status);
        Assert.IsEmpty(server.Objects);
        Assert.ThrowsAsync<FileMissingException>(() => backend.DeleteAsync("b.zip.aes", CancellationToken.None));
        Assert.IsEmpty(await backend.ListAsync(CancellationToken.None).Where(e => !e.IsFolder).ToListAsync());
    }

    [Test]
    [Category("Backend")]
    public async Task MoveToTrashUsesTheTrashEndpoint()
    {
        var (backend, server) = Create(extra: new() { ["move-to-trash"] = "true" });
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        await backend.PutAsync("a.aes", new MemoryStream(RandomBytes(10)), CancellationToken.None);
        await backend.DeleteAsync("a.aes", CancellationToken.None);
        Assert.AreEqual("TRASHED", server.Files.Single().Status);
        Assert.IsTrue(server.Log.Any(l => l.EndsWith("/drive/storage/trash/add")));
    }

    [Test]
    [Category("Backend")]
    public async Task CorruptedDownloadIsRejected()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);
        await backend.PutAsync("a.aes", new MemoryStream(RandomBytes(1000)), CancellationToken.None);

        server.CorruptDownload = obj =>
        {
            var copy = (byte[])obj.Data.Clone();
            copy[500] ^= 0xff;
            return copy;
        };

        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.GetAsync("a.aes", new MemoryStream(), CancellationToken.None));
        Assert.AreEqual("InternxtShardHashMismatch", ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public async Task LegacyFileVersionIsRefused()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);
        await backend.PutAsync("a.aes", new MemoryStream(RandomBytes(10)), CancellationToken.None);
        server.Objects.Values.Single().Version = 1;

        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.GetAsync("a.aes", new MemoryStream(), CancellationToken.None));
        Assert.AreEqual("InternxtUnsupportedFileVersion", ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public void WrongPasswordIsReportedAsALoginError()
    {
        var (backend, _) = Create(password: "wrong");
        using var __ = backend;
        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());
        Assert.AreEqual("InternxtLoginRejected", ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public void TwoFactorIsRequiredWhenEnabled()
    {
        var (backend, server) = Create();
        using var _ = backend;
        server.RequireTfa = true;
        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());
        Assert.AreEqual("InternxtTwoFactorRequired", ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public async Task TwoFactorCodeIsSent()
    {
        var (backend, server) = Create(extra: new() { ["two-factor-code"] = "123456" });
        using var _ = backend;
        server.RequireTfa = true;
        await backend.CreateFolderAsync(CancellationToken.None);
        Assert.AreEqual(1, server.Logins);
    }

    [Test]
    [Category("Backend")]
    public async Task TwoFactorSecretProducesACodeAtEachLogin()
    {
        const string secret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var (backend, server) = Create(extra: new() { ["two-factor-secret"] = secret });
        using var _ = backend;
        server.RequireTfa = true;
        server.TotpSecret = secret;
        await backend.CreateFolderAsync(CancellationToken.None);
        Assert.AreEqual(1, server.Logins);
    }

    [Test]
    [Category("Backend")]
    public void TwoFactorCodeWinsOverTheSecret()
    {
        // An explicit code is sent as given, even when a secret is also configured
        var (backend, server) = Create(extra: new() { ["two-factor-secret"] = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", ["two-factor-code"] = "000000" });
        using var _ = backend;
        server.RequireTfa = true;
        server.TotpSecret = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";
        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());
        Assert.AreEqual("InternxtLoginRejected", ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public void MalformedTwoFactorSecretIsReported()
    {
        var (backend, server) = Create(extra: new() { ["two-factor-secret"] = "this is not base32!" });
        using var _ = backend;
        server.RequireTfa = true;
        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());
        Assert.AreEqual("InternxtTwoFactorSecretInvalid", ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public void PlanWithoutCliAccessIsExplained()
    {
        var (backend, server) = Create();
        using var _ = backend;
        server.PlanWithoutCliAccess = true;
        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());
        Assert.AreEqual("InternxtPlanWithoutApiAccess", ex!.HelpID);
        StringAssert.Contains("CLI/Rclone", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public async Task RateLimitIsWaitedOut()
    {
        var (backend, server) = Create();
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);
        server.RateLimitNextDriveCalls = 1;
        var listed = await backend.ListAsync(CancellationToken.None).ToListAsync();
        Assert.IsEmpty(listed.Where(e => !e.IsFolder));
    }

    [Test]
    [Category("Backend")]
    public async Task FolderIsResolvedByPathAndCreatedWhenMissing()
    {
        var (backend, server) = Create("a/b/c");
        using var _ = backend;

        Assert.ThrowsAsync<FolderMissingException>(() => backend.ListAsync(CancellationToken.None).ToListAsync().AsTask());

        await backend.CreateFolderAsync(CancellationToken.None);
        Assert.AreEqual(4, server.Folders.Count, "root plus three levels");

        // A second instance resolves the path in one call
        var (second, _) = Create("a/b/c");
        second.CreateHttpClient = () => new HttpClient(server);
        using var ___ = second;
        Assert.IsEmpty(await second.ListAsync(CancellationToken.None).ToListAsync());
        Assert.IsTrue(server.Log.Any(l => l.StartsWith("GET /drive/folders/meta?path=")));
    }

    [Test]
    [Category("Backend")]
    public async Task QuotaComesFromLimitAndUsage()
    {
        var (backend, _) = Create();
        using var __ = backend;
        var quota = await backend.GetQuotaInfoAsync(CancellationToken.None);
        Assert.AreEqual(10_000_000L, quota!.TotalQuotaSpace);
        Assert.AreEqual(10_000_000L - 1234L, quota.FreeQuotaSpace);
    }

    [Test]
    [Category("Backend")]
    public async Task FilesAboveTheAccountLimitAreRefusedBeforeUpload()
    {
        var (backend, server) = Create();
        using var _ = backend;
        server.MaxUploadFileSize = 5_000_000L;
        await backend.CreateFolderAsync(CancellationToken.None);

        var ex = Assert.ThrowsAsync<UserInformationException>(() => backend.PutAsync("big.aes", new MemoryStream(new byte[5_000_001]), CancellationToken.None));
        Assert.AreEqual("InternxtFileTooLarge", ex!.HelpID);
        Assert.IsFalse(server.Log.Any(l => l.Contains("/files/start")), "nothing was uploaded");
    }

    [Test]
    [Category("Backend")]
    public async Task MultipartUploadContinuesTheCounterAcrossParts()
    {
        // The backend raises the threshold to 100 MiB like the real network, so
        // the multipart path needs a stream of that size; keep the parts small
        var (backend, server) = Create(extra: new() { ["multipart-part-size"] = "5MiB" });
        using var _ = backend;
        await backend.CreateFolderAsync(CancellationToken.None);

        var size = 100L * 1024 * 1024 + 12345;
        var content = new byte[size];
        new Random(7).NextBytes(content);
        await backend.PutAsync("big.dblock.zip.aes", new MemoryStream(content), CancellationToken.None);

        var obj = server.Objects.Values.Single();
        Assert.AreEqual(size, obj.Data.Length);
        Assert.IsTrue(server.Log.Any(l => l.Contains("multiparts=21")), "21 parts of 5 MiB");

        var (key, iv) = InternxtCrypto.DeriveFileKey(InternxtCrypto.Bip39Seed(Vectors.Mnemonic), Vectors.BucketId, Convert.FromHexString(obj.Index));
        using var reference = new AesCtrStream(new MemoryStream(obj.Data), key, iv, false);
        var decrypted = new byte[size];
        var read = 0;
        while (read < size)
        {
            var n = await reference.ReadAsync(decrypted.AsMemory(read));
            Assert.Greater(n, 0);
            read += n;
        }
        Assert.IsTrue(decrypted.AsSpan().SequenceEqual(content), "the parts form one continuous CTR stream");

        using var output = new MemoryStream();
        await backend.GetAsync("big.dblock.zip.aes", output, CancellationToken.None);
        Assert.IsTrue(output.ToArray().AsSpan().SequenceEqual(content));
    }
}
