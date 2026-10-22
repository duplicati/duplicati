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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Duplicati;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using static Duplicati.UnitTest.DuplicatiBackendTestServer;
using StringAssert = NUnit.Framework.Legacy.StringAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The Duplicati backend applies and reads immutable object locks through the storage server
/// in gateway mode, and through the wrapped S3 backend in direct mode. These tests cover the
/// gateway requests, the capability flag the server reports, and the lock mode passed to S3.
/// </summary>
[TestFixture]
public class DuplicatiBackendLockingTests
{
    private const string ObjectLockDisabledCredentials = "{\"mode\":\"gateway\",\"objectLockEnabled\":false,\"uploadsDisabled\":false,\"accountDisabled\":false}";
    private const string DirectCredentials = "{\"mode\":\"direct\",\"endpoint\":\"s3.example.com\",\"accessKeyId\":\"access\",\"secretKey\":\"secret\",\"bucket\":\"bucket\",\"prefix\":\"backup-1\",\"uploadsDisabled\":false,\"accountDisabled\":false}";
    private const string DirectCredentialsWithLockMode = "{\"mode\":\"direct\",\"endpoint\":\"s3.example.com\",\"accessKeyId\":\"access\",\"secretKey\":\"secret\",\"bucket\":\"bucket\",\"prefix\":\"backup-1\",\"lockMode\":\"compliance\",\"uploadsDisabled\":false,\"accountDisabled\":false}";

    private const string LockedUntilJson = "{\"lockUntil\":\"2026-11-15T10:00:00Z\"}";
    private static readonly DateTime LockedUntil = new(2026, 11, 15, 10, 0, 0, DateTimeKind.Utc);

    private static FakeServer GatewayServer()
        => new FakeServer().On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json");

    [Test]
    [Category("Backend")]
    public void BackendSupportsObjectLocking()
    {
        using var backend = CreateBackend(GatewayServer());

        Assert.IsInstanceOf<ILockingBackend>(backend);
    }

    [Test]
    [Category("Backend")]
    public async Task GetLockReturnsTheServerDate()
    {
        var server = GatewayServer().On("/lock/", HttpStatusCode.OK, LockedUntilJson, "application/json");
        using var backend = CreateBackend(server);

        var lockUntil = await backend.GetObjectLockUntilAsync("file.zip", CancellationToken.None);

        Assert.AreEqual(LockedUntil, lockUntil);
        Assert.AreEqual(DateTimeKind.Utc, lockUntil!.Value.Kind);
        var request = server.RecordedRequests.Single(x => x.Path.StartsWith("/lock/"));
        Assert.AreEqual(HttpMethod.Get, request.Method);
        Assert.AreEqual("/lock/file.zip", request.Path);
    }

    [Test]
    [Category("Backend")]
    public async Task GetLockReturnsNullWhenUnlocked()
    {
        var server = GatewayServer().On("/lock/", HttpStatusCode.OK, "{\"lockUntil\":null}", "application/json");
        using var backend = CreateBackend(server);

        var lockUntil = await backend.GetObjectLockUntilAsync("file.zip", CancellationToken.None);

        Assert.IsNull(lockUntil);
    }

    [Test]
    [Category("Backend")]
    public void GetLockOnMissingFileIsReportedAsMissing()
    {
        var server = GatewayServer().On("/lock/", HttpStatusCode.NotFound, "Not found");
        using var backend = CreateBackend(server);

        Assert.CatchAsync<FileMissingException>(async () => await backend.GetObjectLockUntilAsync("file.zip", CancellationToken.None));
    }

    [Test]
    [Category("Backend")]
    public async Task SetLockPostsTheDateInUtc()
    {
        var server = GatewayServer().On("/lock/", HttpStatusCode.OK, LockedUntilJson, "application/json");
        using var backend = CreateBackend(server);

        // A local-kind date is sent as the same instant in UTC
        await backend.SetObjectLockUntilAsync("file.zip", LockedUntil.ToLocalTime(), CancellationToken.None);

        var request = server.RecordedRequests.Single(x => x.Path.StartsWith("/lock/"));
        Assert.AreEqual(HttpMethod.Post, request.Method);
        Assert.AreEqual("/lock/file.zip", request.Path);
        Assert.AreEqual("{\"lockUntil\":\"2026-11-15T10:00:00Z\"}", request.Body);
    }

    [Test]
    [Category("Backend")]
    public void SetLockOnMissingFileIsReportedAsMissing()
    {
        var server = GatewayServer().On("/lock/", HttpStatusCode.NotFound, "Not found");
        using var backend = CreateBackend(server);

        Assert.CatchAsync<FileMissingException>(async () => await backend.SetObjectLockUntilAsync("file.zip", LockedUntil, CancellationToken.None));
    }

    [Test]
    [Category("Backend")]
    public void SetLockWithoutEchoedDateFails()
    {
        var server = GatewayServer().On("/lock/", HttpStatusCode.OK, "{}", "application/json");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<Exception>(async () => await backend.SetObjectLockUntilAsync("file.zip", LockedUntil, CancellationToken.None));

        StringAssert.Contains("no retention date", ex!.Message);
    }

    [Test]
    [Category("Backend")]
    public void LockOnAccountWithoutObjectLockIsExplained()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, ObjectLockDisabledCredentials, "application/json")
            .On("/lock/", HttpStatusCode.OK, LockedUntilJson, "application/json");
        using var backend = CreateBackend(server);

        var getEx = Assert.CatchAsync<ObjectLockNotSupportedException>(async () => await backend.GetObjectLockUntilAsync("file.zip", CancellationToken.None));
        var setEx = Assert.CatchAsync<ObjectLockNotSupportedException>(async () => await backend.SetObjectLockUntilAsync("file.zip", LockedUntil, CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_OBJECT_LOCK_NOT_ENABLED, getEx!.HelpID);
        Assert.AreEqual(DuplicatiBackend.HELP_ID_OBJECT_LOCK_NOT_ENABLED, setEx!.HelpID);
        Assert.IsFalse(server.Requests.Any(x => x.StartsWith("/lock/")), "No lock request should reach the server");
    }

    [TestCase(HttpStatusCode.NotImplemented)]
    [TestCase(HttpStatusCode.MethodNotAllowed)]
    [Category("Backend")]
    public void LockOnOldServerIsExplained(HttpStatusCode status)
    {
        var server = GatewayServer().On("/lock/", status, "Unknown route");
        using var backend = CreateBackend(server);

        var getEx = Assert.CatchAsync<ObjectLockNotSupportedException>(async () => await backend.GetObjectLockUntilAsync("file.zip", CancellationToken.None));
        var setEx = Assert.CatchAsync<ObjectLockNotSupportedException>(async () => await backend.SetObjectLockUntilAsync("file.zip", LockedUntil, CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_OBJECT_LOCK_NOT_ENABLED, getEx!.HelpID);
        Assert.AreEqual(DuplicatiBackend.HELP_ID_OBJECT_LOCK_NOT_ENABLED, setEx!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public void LockFailureOnDisabledAccountIsExplained()
    {
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, DisabledGatewayCredentials, "application/json")
            .On("/lock/", HttpStatusCode.Forbidden, "Gateway mode is disabled");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.SetObjectLockUntilAsync("file.zip", LockedUntil, CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_ACCOUNT_DISABLED, ex!.HelpID);
        StringAssert.Contains("Gateway mode is disabled", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public void LockFailureWithQuotaExceededIsNotBlamedOnTheQuota()
    {
        // Locking is not an upload, so a full quota does not explain a failed lock
        var server = new FakeServer()
            .On("/credentials", HttpStatusCode.OK, QuotaExceededCredentials, "application/json")
            .On("/lock/", HttpStatusCode.InternalServerError, "Internal server error");
        using var backend = CreateBackend(server);

        var ex = Assert.CatchAsync<HttpRequestException>(async () => await backend.SetObjectLockUntilAsync("file.zip", LockedUntil, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex!.StatusCode);
        StringAssert.Contains("Internal server error", ex.Message);
    }

    [Test]
    [Category("Backend")]
    public async Task DirectModePassesTheLockModeToS3()
    {
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, DirectCredentialsWithLockMode, "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsNotNull(backend.DirectModeOptions);
        Assert.AreEqual("compliance", backend.DirectModeOptions!["s3-lock-mode"]);
    }

    [Test]
    [Category("Backend")]
    public async Task DirectModeWithoutLockModeLeavesTheS3Default()
    {
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, DirectCredentials, "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsNotNull(backend.DirectModeOptions);
        Assert.IsFalse(backend.DirectModeOptions!.ContainsKey("s3-lock-mode"));
    }
}
