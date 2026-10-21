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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend.Duplicati;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using static Duplicati.UnitTest.DuplicatiBackendTestServer;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// With a point in time set, the Duplicati backend forwards the option to the S3 backend in direct
/// mode, which resolves the object versions. The gateway keeps one immutable copy of each locked file
/// and no versions, so gateway mode refuses the option instead of serving the current state. Writes
/// are refused in both modes.
/// </summary>
[TestFixture]
public class DuplicatiBackendPointInTimeTests
{
    private const string DirectCredentialsWithObjectLock = "{\"mode\":\"direct\",\"endpoint\":\"s3.example.com\",\"accessKeyId\":\"access\",\"secretKey\":\"secret\",\"bucket\":\"bucket\",\"prefix\":\"backup-1\",\"objectLockEnabled\":true,\"lockMode\":\"governance\",\"uploadsDisabled\":false,\"accountDisabled\":false}";
    private const string DirectCredentialsWithoutObjectLock = "{\"mode\":\"direct\",\"endpoint\":\"s3.example.com\",\"accessKeyId\":\"access\",\"secretKey\":\"secret\",\"bucket\":\"bucket\",\"prefix\":\"backup-1\",\"objectLockEnabled\":false,\"uploadsDisabled\":false,\"accountDisabled\":false}";
    private const string DirectCredentialsFromOlderServer = "{\"mode\":\"direct\",\"endpoint\":\"s3.example.com\",\"accessKeyId\":\"access\",\"secretKey\":\"secret\",\"bucket\":\"bucket\",\"prefix\":\"backup-1\",\"uploadsDisabled\":false,\"accountDisabled\":false}";

    private static Dictionary<string, string?> PointInTime(string value)
        => new() { [PointInTimeOptionsHelper.PointInTimeOption] = value };

    private static FakeServer GatewayServer()
        => new FakeServer()
            .On("/credentials", HttpStatusCode.OK, GatewayCredentials, "application/json")
            .On("/list", HttpStatusCode.OK, "[]", "application/json")
            .On("/get/", HttpStatusCode.OK, "content")
            .On("/put/", HttpStatusCode.OK, "OK");

    private static async Task<int> ListAsync(DuplicatiBackend backend)
    {
        var count = 0;
        await foreach (var _ in backend.ListAsync(CancellationToken.None))
            count++;
        return count;
    }

    [Test]
    [Category("Backend")]
    public void ListInGatewayModeIsRefused()
    {
        var server = GatewayServer();
        using var backend = CreateBackend(server, PointInTime("7D"));

        var ex = Assert.CatchAsync<UserInformationException>(async () => await ListAsync(backend));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_POINT_IN_TIME_NOT_AVAILABLE, ex!.HelpID);
        Assert.IsFalse(server.Requests.Contains("/list"), "The current state must not be listed");
    }

    [Test]
    [Category("Backend")]
    public void GetInGatewayModeIsRefused()
    {
        var server = GatewayServer();
        using var backend = CreateBackend(server, PointInTime("7D"));

        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.GetAsync("file.zip", new MemoryStream(), CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_POINT_IN_TIME_NOT_AVAILABLE, ex!.HelpID);
        Assert.IsFalse(server.Requests.Any(x => x.StartsWith("/get/")), "The current version must not be downloaded");
    }

    [Test]
    [Category("Backend")]
    public void GatewayModeIsRefusedOnEveryRead()
    {
        // The refusal happens while resolving the mode, which is not cached when refused, so every read fails the same way
        var server = GatewayServer();
        using var backend = CreateBackend(server, PointInTime("7D"));

        Assert.CatchAsync<UserInformationException>(async () => await ListAsync(backend));
        Assert.CatchAsync<UserInformationException>(async () => await ListAsync(backend));

        Assert.AreEqual(2, server.Requests.Count(x => x == "/credentials"));
        Assert.IsFalse(server.Requests.Contains("/list"));
    }

    [Test]
    [Category("Backend")]
    public void WritesAreRefused()
    {
        var server = GatewayServer();
        using var backend = CreateBackend(server, PointInTime("7D"));
        var lockUntil = DateTime.UtcNow.AddDays(30);

        var writes = new Dictionary<string, Func<Task>>
        {
            ["put"] = () => backend.PutAsync("file.zip", new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None),
            ["delete"] = () => backend.DeleteAsync("file.zip", CancellationToken.None),
            ["rename"] = () => backend.RenameAsync("file.zip", "other.zip", CancellationToken.None),
            ["createfolder"] = () => backend.CreateFolderAsync(CancellationToken.None),
            ["set-lock"] = () => backend.SetObjectLockUntilAsync("file.zip", lockUntil, CancellationToken.None),
        };

        foreach (var (name, write) in writes)
        {
            var ex = Assert.CatchAsync<UserInformationException>(async () => await write(), name);
            Assert.AreEqual(PointInTimeOptionsHelper.HelpIdDestinationReadOnly, ex!.HelpID, name);
        }

        Assert.IsEmpty(server.Requests, "A refused write must not reach the server");
    }

    [Test]
    [Category("Backend")]
    public async Task DirectModeWithObjectLockForwardsTheOption()
    {
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, DirectCredentialsWithObjectLock, "application/json");
        using var backend = CreateBackend(server, PointInTime("7D"));

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsNotNull(backend.DirectModeOptions);
        Assert.AreEqual("7D", backend.DirectModeOptions![PointInTimeOptionsHelper.PointInTimeOption]);
    }

    [TestCase(DirectCredentialsWithoutObjectLock, TestName = "DirectModeWithoutObjectLockIsRefused")]
    [TestCase(DirectCredentialsFromOlderServer, TestName = "DirectModeWithoutObjectLockStateIsRefused")]
    [Category("Backend")]
    public void DirectModeWithoutObjectLockIsRefused(string credentials)
    {
        // Older versions only exist on a bucket with object locking, so without it the option cannot be honoured
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, credentials, "application/json");
        using var backend = CreateBackend(server, PointInTime("7D"));

        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.EnsureCredentialsAsync(CancellationToken.None));

        Assert.AreEqual(DuplicatiBackend.HELP_ID_POINT_IN_TIME_NOT_AVAILABLE, ex!.HelpID);
        Assert.IsNull(backend.DirectModeOptions, "No S3 backend must be created");
    }

    [Test]
    [Category("Backend")]
    public async Task DirectModeWithoutObjectLockStillWorksWithoutTheOption()
    {
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, DirectCredentialsWithoutObjectLock, "application/json");
        using var backend = CreateBackend(server);

        await backend.EnsureCredentialsAsync(CancellationToken.None);

        Assert.IsNotNull(backend.DirectModeOptions);
    }

    [Test]
    [Category("Backend")]
    public async Task DirectModeStillRefusesWrites()
    {
        var server = new FakeServer().On("/credentials", HttpStatusCode.OK, DirectCredentialsWithObjectLock, "application/json");
        using var backend = CreateBackend(server, PointInTime("7D"));

        await backend.EnsureCredentialsAsync(CancellationToken.None);
        var ex = Assert.CatchAsync<UserInformationException>(async () => await backend.DeleteAsync("file.zip", CancellationToken.None));

        Assert.AreEqual(PointInTimeOptionsHelper.HelpIdDestinationReadOnly, ex!.HelpID);
    }

    [Test]
    [Category("Backend")]
    public async Task WithoutTheOptionNothingChanges()
    {
        var server = GatewayServer();
        using var backend = CreateBackend(server);

        await ListAsync(backend);
        await backend.GetAsync("file.zip", new MemoryStream(), CancellationToken.None);
        await backend.PutAsync("file.zip", new MemoryStream(new byte[] { 1, 2, 3 }), CancellationToken.None);

        Assert.AreEqual(string.Empty, server.RecordedRequests.Single(x => x.Path == "/list").Query);
        Assert.AreEqual(string.Empty, server.RecordedRequests.Single(x => x.Path == "/get/file.zip").Query);
        Assert.IsNull(backend.DirectModeOptions);
    }

    [Test]
    [Category("Backend")]
    public void InvalidTimeIsReportedWhenTheBackendIsCreated()
    {
        var ex = Assert.Throws<UserInformationException>(() => CreateBackend(new FakeServer(), PointInTime("last tuesday")));

        Assert.AreEqual(PointInTimeOptionsHelper.HelpIdInvalidPointInTime, ex!.HelpID);
    }
}
