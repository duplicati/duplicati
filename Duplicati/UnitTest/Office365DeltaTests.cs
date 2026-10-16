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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365;
using Duplicati.Proprietary.Office365.SourceItems;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The delta walks of the Microsoft 365 provider against a fake Graph API
/// </summary>
public class Office365DeltaTests
{
    /// <summary>
    /// Answers Graph requests from canned responses, matched on a fragment of the decoded URL.
    /// Routes are tried in the order they were added; a fragment can be answered by a sequence
    /// of responses, one per request, the last one repeating.
    /// </summary>
    private sealed class FakeGraphHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, Queue<Func<HttpResponseMessage>> Responses)> _routes = [];

        /// <summary>The decoded URLs of the requests received</summary>
        public List<string> Requests { get; } = [];

        public FakeGraphHandler Json(string fragment, string json)
            => Sequence(fragment, () => Response(HttpStatusCode.OK, json));

        public FakeGraphHandler Sequence(string fragment, params Func<HttpResponseMessage>[] responses)
        {
            _routes.Add((fragment, new Queue<Func<HttpResponseMessage>>(responses)));
            return this;
        }

        public static HttpResponseMessage Response(HttpStatusCode status, string body)
            => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

        public static HttpResponseMessage GraphError(HttpStatusCode status, string code)
            => Response(status, $"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{code}\"}}}}");

        public static HttpResponseMessage Throttled()
        {
            var resp = Response(HttpStatusCode.TooManyRequests, "{}");
            resp.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            return resp;
        }

        public int CountRequests(string fragment) => Requests.Count(x => x.Contains(fragment, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri);
            Requests.Add(url);

            foreach (var (fragment, responses) in _routes)
            {
                if (!url.Contains(fragment, StringComparison.Ordinal))
                    continue;

                var factory = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
                var response = factory();
                response.RequestMessage = request;
                return Task.FromResult(response);
            }

            var notFound = GraphError(HttpStatusCode.NotFound, "itemNotFound");
            notFound.RequestMessage = request;
            return Task.FromResult(notFound);
        }
    }

    private const string GRAPH = "https://graph.test";
    private static readonly string Mount = Util.AppendDirSeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "o365")));

    private static string D(params string[] parts) => Util.AppendDirSeparator(Path.Combine(parts));
    private static string F(params string[] parts) => Path.Combine(parts);

    private static SourceProvider CreateProvider(FakeGraphHandler handler, IReadOnlyDictionary<string, string>? state, Action<Dictionary<string, string?>>? configure = null)
    {
        var options = new Dictionary<string, string?>
        {
            ["office365-tenant-id"] = "tenant",
            ["office365-client-id"] = "client",
            ["office365-client-secret"] = "secret",
            ["office365-graph-base-url"] = GRAPH,
            ["store-metadata-content-in-database"] = "true",
            ["machine-id"] = "test-machine",
        };
        configure?.Invoke(options);

        var timeouts = new TimeoutOptionsHelper.Timeouts(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        var api = Office365TestSupport.CreateApiHelper(handler, GRAPH, timeouts);
        var provider = new SourceProvider("office365://tenant", Mount, options, api);
        if (state != null)
            provider.LoadChangeTrackingState(state);
        return provider;
    }

    private static async Task<List<ISourceProviderEntry>> CollectAsync(ISourceProviderEntry entry, bool recurse)
    {
        var result = new List<ISourceProviderEntry>();
        await foreach (var child in entry.Enumerate(CancellationToken.None))
        {
            result.Add(child);
            if (recurse && child.IsFolder)
                result.AddRange(await CollectAsync(child, true));
        }
        return result;
    }

    private static string Page(string items, string? nextLink = null, string? deltaLink = null)
    {
        var sb = new StringBuilder("{\"value\":[").Append(items).Append(']');
        if (nextLink != null)
            sb.Append(",\"@odata.nextLink\":\"").Append(nextLink).Append('"');
        if (deltaLink != null)
            sb.Append(",\"@odata.deltaLink\":\"").Append(deltaLink).Append('"');
        return sb.Append('}').ToString();
    }

    private static string MailLink(string token) => $"{GRAPH}/v1.0/users/u1/mailFolders/f1/messages/delta?$deltatoken={token}";
    private static string DriveLink(string token) => $"{GRAPH}/v1.0/drives/d1/root/delta?$select=id&token={token}";
    private static string MailState(string token) => token;

    private static string Message(string id) => $"{{\"id\":\"{id}\",\"subject\":\"{id}\"}}";
    private static string RemovedMessage(string id) => $"{{\"id\":\"{id}\",\"@removed\":{{\"reason\":\"deleted\"}}}}";
    private static string Folder(string id, string parent, string? name = null) => $"{{\"id\":\"{id}\",\"name\":\"{name ?? id}\",\"folder\":{{\"childCount\":1}},\"parentReference\":{{\"driveId\":\"d1\",\"id\":\"{parent}\"}}}}";
    private static string File(string id, string parent) => $"{{\"id\":\"{id}\",\"name\":\"{id}.txt\",\"size\":3,\"file\":{{\"mimeType\":\"text/plain\"}},\"parentReference\":{{\"driveId\":\"d1\",\"id\":\"{parent}\"}}}}";
    private static string Deleted(string id) => $"{{\"id\":\"{id}\",\"deleted\":{{\"state\":\"deleted\"}}}}";

    private static readonly GraphUser User = new() { Id = "u1" };
    private static readonly GraphMailFolder MailFolder = new() { Id = "f1", ParentFolderId = "root", DisplayName = "Inbox" };
    private static readonly string MailboxPath = D(Mount, "users", "u1", "mailbox");
    private static readonly string FolderPath = D(MailboxPath, "f1");
    private const string MailKey = "mail:u1:root:f1";

    private static readonly GraphDrive Drive = new() { Id = "d1", Name = "Documents" };
    private static readonly string SitePath = D(Mount, "sites", "s1");
    private static readonly string DrivePath = D(SitePath, "d1");
    private const string DriveKey = "drive:d1";

    private static string StoredMap(string token, params (string Folder, string Parent)[] folders)
    {
        var map = new DriveFolderMap { Token = token, RootId = "root" };
        foreach (var (folder, parent) in folders)
            map.SetParent(folder, parent);
        return map.Serialize();
    }

    private static List<LogEntry> CaptureLog(out IDisposable scope)
    {
        var messages = new List<LogEntry>();
        var isolating = Log.StartIsolatingScope(true);
        var inner = Log.StartScope(messages.Add);
        scope = new ScopeDisposer(inner, isolating);
        return messages;
    }

    private sealed class ScopeDisposer : IDisposable
    {
        private readonly IDisposable[] _scopes;
        public ScopeDisposer(params IDisposable[] scopes) { _scopes = scopes; }
        public void Dispose()
        {
            foreach (var s in _scopes)
                s.Dispose();
        }
    }

    [Test]
    [Category("Office365")]
    public async Task SkipPagingThatDoesNotAdvanceStopsTheListingAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/next-page", Page("{\"id\":\"c\"}", nextLink: $"{GRAPH}/v1.0/users/u1/mailFolders/f1/childFolders?$skip=0"))
            .Json("/childFolders?$skip=0", Page("{\"id\":\"a\"},{\"id\":\"b\"}", nextLink: $"{GRAPH}/next-page?$skip=10"))
            .Json("/childFolders", Page("{\"id\":\"a\"},{\"id\":\"b\"}", nextLink: $"{GRAPH}/next-page?$skip=10"));
        var provider = CreateProvider(handler, null);

        var messages = CaptureLog(out var scope);
        using (scope)
        {
            var folders = await provider.UserEmailApi.ListMailChildFoldersAsync("u1", "f1", CancellationToken.None).ToListAsync();
            Assert.That(folders.Select(x => x.Id), Is.EqualTo(new[] { "a", "b", "c" }), "The listing ends when the next page offset does not advance");
            Assert.That(messages.Any(x => x.Id == "PagingRestartDetected"), Is.True, "The restart is logged");
        }

        Assert.That(handler.Requests.Count, Is.EqualTo(2));
    }

    [Test]
    [Category("Office365")]
    public async Task MailInitialWalkListsEverythingAndStoresTheLinkAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1") + "," + Message("m2"), nextLink: $"{GRAPH}/mail-next"))
            .Json("/mail-next", Page(Message("m3"), deltaLink: MailLink("tok1")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entry = new UserMailboxFolderSourceEntry(provider, User, MailboxPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar + "f1", MailFolder);
        var entries = await CollectAsync(entry, false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(FolderPath, "m1"), D(FolderPath, "m2"), D(FolderPath, "m3") }));
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [MailKey] = MailState("tok1") }));
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m9", "content.eml")), Is.True, "A full walk examines everything");
        Assert.That(handler.CountRequests("/messages/delta"), Is.EqualTo(1));
    }

    [Test]
    [Category("Office365")]
    public async Task MailIncrementalWalkListsChangesAndTombstonesRemovalsAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page(Message("m2") + "," + RemovedMessage("m3"), deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page("{\"id\":\"child\",\"parentFolderId\":\"f1\"}"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [MailKey] = MailState("tok1"),
            ["mail:u1:f1:child"] = MailState("child"),
            ["mail:u1:f1:gone"] = MailState("gone"),
        });

        var entry = new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder);
        var entries = await CollectAsync(entry, false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(FolderPath, "m2"), D(FolderPath, "child") }), "Only the changed message and the child folder are listed");
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")));
        Assert.That(handler.Requests[0], Does.Contain("/messages/delta?$deltatoken=tok1"), "The stored token is sent without a select, which Graph encodes in the token");

        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m1", "content.eml")), Is.False, "An unchanged message is carried forward");
        Assert.That(provider.WasPathEnumerated(D(FolderPath, "m1")), Is.False);
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m2", "content.eml")), Is.True, "A changed message was examined");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m3", "content.eml")), Is.True, "A removed message was examined and is dropped");
        Assert.That(provider.WasPathEnumerated(D(FolderPath, "child")), Is.True, "A listed child folder entry was examined");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "child", "m5", "content.eml")), Is.False, "The child folder's contents are decided by its own walk");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "gone", "m6", "content.eml")), Is.True, "A child folder that is no longer listed is dropped with its contents");
    }

    [Test]
    [Category("Office365")]
    public async Task MailIncrementalWalkLetsTheLastReportOfAMessageWinAsync()
    {
        // A message that was changed and then removed since the stored token is reported twice, and so is
        // one that was removed and then recreated with the same id; the later report is the newer state
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page(Message("m1") + "," + RemovedMessage("m2"), nextLink: $"{GRAPH}/mail-next-1"))
            .Json("/mail-next-1", Page(RemovedMessage("m1") + "," + Message("m2") + "," + Message("m3"), deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(FolderPath, "m2"), D(FolderPath, "m3") }), "A message removed after it was changed is not listed, one changed after it was removed is, in the order first reported");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m1", "content.eml")), Is.True, "The removed message is dropped");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m4", "content.eml")), Is.False, "An unreported message is carried forward");
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")));
        Assert.That(handler.CountRequests("/mail-next-1"), Is.EqualTo(1), "Every page is read once");
    }

    [Test]
    [Category("Office365")]
    public async Task MailStaleTokenRestartsWithAFullWalkAsync()
    {
        var handler = new FakeGraphHandler()
            .Sequence("deltatoken=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Gone, "syncStateNotFound"))
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1") + "," + Message("m2"), deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(FolderPath, "m1"), D(FolderPath, "m2") }));
        Assert.That(messages.Any(x => x.Id == "MailDeltaResync" && x.Level == LogMessageType.Warning), Is.True);
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")));
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m9", "content.eml")), Is.True, "After a resync the folder was walked in full");
    }

    [Test]
    [Category("Office365")]
    public async Task MailThrottledPageIsRetriedAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page(Message("m2"), nextLink: $"{GRAPH}/mail-next"))
            .Sequence("/mail-next", FakeGraphHandler.Throttled, () => FakeGraphHandler.Response(HttpStatusCode.OK, Page(Message("m4"), deltaLink: MailLink("tok2"))))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(FolderPath, "m2"), D(FolderPath, "m4") }));
        Assert.That(handler.CountRequests("/mail-next"), Is.EqualTo(2), "The throttled page is requested again");
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")));
    }

    [Test]
    [Category("Office365")]
    public async Task MailFailedWalkKeepsThePreviousLinkAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page(Message("m2"), nextLink: $"{GRAPH}/mail-next"))
            .Sequence("/mail-next", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var entries = new List<ISourceProviderEntry>();
        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var e in new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder).Enumerate(CancellationToken.None))
                entries.Add(e);
        });

        Assert.That(entries, Is.Empty, "An incremental walk collects its reports before listing any, so a walk that fails lists nothing");
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok1")), "The previous link is kept so the changes are reported again");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m1", "content.eml")), Is.False, "Unlisted messages are carried forward");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m2", "content.eml")), Is.False, "The message the failed walk reported is carried forward and reported again next time");
    }

    [Test]
    [Category("Office365")]
    public async Task MailDownloadFailureHoldsTheLinkBackAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page(Message("m2"), deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""))
            .Sequence("/users/u1/messages/m2/$value", () => FakeGraphHandler.GraphError(HttpStatusCode.InternalServerError, "generalException"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")), "Before any download fails the new link is exported");

        var content = (await CollectAsync(entries[0], false)).First(x => x.Path.EndsWith("content.eml", StringComparison.Ordinal));
        Assert.ThrowsAsync<HttpRequestException>(async () => await content.OpenRead(CancellationToken.None));

        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok1")), "A failed download holds the folder at the previous link");
    }

    [Test]
    [Category("Office365")]
    public async Task MailDeltaOffListsInFullAndStoresNothingAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1"), deltaLink: MailLink("tok1")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("stale") }, o => o["office365-disable-mail-delta"] = "true");

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(provider.ExportChangeTrackingState(), Is.Empty);
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m9", "content.eml")), Is.True);
    }

    [Test]
    [Category("Office365")]
    public async Task MailDeltaLinkWithoutTokenStoresNothingAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1"), deltaLink: $"{GRAPH}/v1.0/users/u1/mailFolders/f1/messages/delta"))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var messages = CaptureLog(out var scope);
        using (scope)
            await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        Assert.That(messages.Any(x => x.Id == "MailDeltaTokenMissing" && x.Level == LogMessageType.Warning), Is.True);
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [MailKey] = "" }), "The folder is recorded without a token, so its parent still knows it and the next backup lists it in full");
    }

    [Test]
    [Category("Office365")]
    public async Task MailFailedFirstWalkRecordsTheFolderWithoutATokenAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1"), nextLink: $"{GRAPH}/mail-next"))
            .Sequence("/mail-next", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        Assert.ThrowsAsync<HttpRequestException>(async () => await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false));

        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [MailKey] = "" }), "A failed walk without a previous token records the folder, so a later deletion of it is noticed");
    }

    [Test]
    [Category("Office365")]
    public async Task MailDeletedChildFolderWithoutATokenIsDroppedAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page("", deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [MailKey] = MailState("tok1"),
            ["mail:u1:f1:gone"] = "",
        });

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        Assert.That(entries, Is.Empty);
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "gone", "m6", "content.eml")), Is.True, "A child folder known without a token that is no longer listed is dropped with its contents");
        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m1", "content.eml")), Is.False, "The folder's own messages are still carried forward");
        Assert.That(provider.ExportChangeTrackingState().ContainsKey("mail:u1:f1:gone"), Is.False, "The deleted folder's key is not carried along");
    }

    [Test]
    [Category("Office365")]
    public async Task MailStaleTokenIsNotRestoredByALaterDownloadFailureAsync()
    {
        var handler = new FakeGraphHandler()
            .Sequence("deltatoken=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Gone, "syncStateNotFound"))
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1"), deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""))
            .Sequence("/users/u1/messages/m1/$value", () => FakeGraphHandler.GraphError(HttpStatusCode.InternalServerError, "generalException"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")));

        var content = (await CollectAsync(entries[0], false)).First(x => x.Path.EndsWith("content.eml", StringComparison.Ordinal));
        Assert.ThrowsAsync<HttpRequestException>(async () => await content.OpenRead(CancellationToken.None));

        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(""), "The token the service rejected is not stored again; the next backup lists the folder in full");
    }

    [Test]
    [Category("Office365")]
    public async Task MailRepeatedlyFailingWalkResetsTheFolderAsync()
    {
        var handler = new FakeGraphHandler()
            .Sequence("deltatoken=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [MailKey] = MailState("tok1"),
            ["failures:" + MailKey] = (ChangeTracker.MAX_CONSECUTIVE_FAILURES - 1).ToString(),
        });

        Assert.ThrowsAsync<HttpRequestException>(async () => await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false));

        Assert.That(provider.WasPathEnumerated(F(FolderPath, "m1", "content.eml")), Is.False, "This run still carries the folder forward");
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [MailKey] = "" }), "After repeated failures the token is dropped, so the next backup lists the folder in full instead of carrying stale content forward forever");
    }

    [Test]
    [Category("Office365")]
    public async Task MailStoredValueIsOnlyTheTokenAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1"), deltaLink: MailLink("a%2Bb%3D")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        var stored = provider.ExportChangeTrackingState()[MailKey];
        Assert.That(stored, Is.EqualTo("a+b="), "The stored value is the decoded token alone");

        // The next walk sends the token back encoded
        var next = new FakeGraphHandler()
            .Json("deltatoken=a+b=", Page("", deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var nextProvider = CreateProvider(next, new Dictionary<string, string> { [MailKey] = stored });
        await CollectAsync(new UserMailboxFolderSourceEntry(nextProvider, User, FolderPath, MailFolder), false);
        Assert.That(next.Requests[0], Does.Contain("$deltatoken=a+b="), "The decoded request URL carries the original token");
    }

    private static FakeGraphHandler FullDriveHandler(string latestToken)
        => new FakeGraphHandler()
            .Json("token=latest", Page("", deltaLink: DriveLink(latestToken)))
            .Json("/drives/d1/root?$select=id", "{\"id\":\"root\"}")
            .Json("/drives/d1/root/children", Page(Folder("F1", "root") + "," + File("A", "root")))
            .Json("/drives/d1/items/F1/children", Page(Folder("F2", "F1") + "," + File("B", "F1")))
            .Json("/drives/d1/items/F2/children", Page(File("C", "F2")));

    [Test]
    [Category("Office365")]
    public async Task DriveFirstRunWalksChildrenAndStoresLatestLinkAndMapAsync()
    {
        var handler = FullDriveHandler("tok1");
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Select(x => x.Path), Is.EquivalentTo(new[] { D(DrivePath, "F1"), D(DrivePath, "F1", "F2"), F(DrivePath, "F1", "F2", "C"), F(DrivePath, "F1", "B"), F(DrivePath, "A") }));
        Assert.That(handler.Requests[0], Does.Contain("token=latest"), "The token is taken before the walk");
        Assert.That(handler.CountRequests("/children"), Is.EqualTo(3));

        var state = provider.ExportChangeTrackingState();
        Assert.That(DriveFolderMap.Deserialize(state[DriveKey])!.Token, Is.EqualTo("tok1"));
        var map = DriveFolderMap.Deserialize(state[DriveKey])!;
        Assert.That(map.RootId, Is.EqualTo("root"));
        Assert.That(map.TryGetParent("F1", out var p1) && p1 == "root", Is.True);
        Assert.That(map.TryGetParent("F2", out var p2) && p2 == "F1", Is.True);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "X")), Is.True);
    }

    [Test]
    [Category("Office365")]
    public async Task DriveIncrementalRunListsOnlyTheChangeAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(File("C2", "F2") + "," + File("B", "F1"), nextLink: $"{GRAPH}/drive-next"))
            .Json("/drive-next", Page(File("B", "F1"), deltaLink: DriveLink("tok2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Select(x => x.Path), Is.EquivalentTo(new[] { F(DrivePath, "F1", "F2", "C2"), F(DrivePath, "F1", "B") }), "Changed files are listed at their resolved paths, without their unchanged parents");
        Assert.That(handler.CountRequests("/children"), Is.EqualTo(0), "No folder listing is needed");
        Assert.That(handler.CountRequests("/items/"), Is.EqualTo(0), "No item lookup is needed when the map knows the ancestors");

        var state = provider.ExportChangeTrackingState();
        Assert.That(DriveFolderMap.Deserialize(state[DriveKey])!.Token, Is.EqualTo("tok2"));
        Assert.That(DriveFolderMap.Deserialize(state[DriveKey])!.Count, Is.EqualTo(2));

        Assert.That(provider.WasPathEnumerated(F(DrivePath, "A")), Is.False, "An unchanged file is carried forward");
        Assert.That(provider.WasPathEnumerated(D(DrivePath, "F1")), Is.False, "An unchanged folder is carried forward");
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "F2", "C")), Is.False);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "B")), Is.True, "A changed file was examined");
        Assert.That(provider.WasPathEnumerated(D(DrivePath)), Is.True, "The drive entry is listed by the site");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveDeletedFolderDropsItsSubtreeAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(Deleted("F1") + "," + Deleted("B"), deltaLink: DriveLink("tok2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1"), ("G1", "root")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        Assert.That(entries, Is.Empty);

        Assert.That(provider.WasPathEnumerated(D(DrivePath, "F1")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "B")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "F2", "C")), Is.True, "Everything below the removed folder is dropped");
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "A")), Is.False);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "G1", "X")), Is.False);

        var map = DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!;
        Assert.That(map.Contains("F1"), Is.False);
        Assert.That(map.Contains("F2"), Is.False, "Folders below the removed folder are pruned");
        Assert.That(map.Contains("G1"), Is.True);
    }

    [Test]
    [Category("Office365")]
    public async Task DriveMovedFolderIsWalkedAtItsNewPathAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(Folder("F2", "root") + "," + File("C", "F2"), deltaLink: DriveLink("tok2")))
            .Json("/drives/d1/items/F2/children", Page(File("C", "F2") + "," + Folder("F3", "F2")))
            .Json("/drives/d1/items/F3/children", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Select(x => x.Path), Is.EquivalentTo(new[] { D(DrivePath, "F2"), F(DrivePath, "F2", "C"), D(DrivePath, "F2", "F3") }), "The moved folder is listed in full at its new path, and the changed file inside it only once");
        Assert.That(handler.CountRequests("/items/F2/children"), Is.EqualTo(1));

        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "F2", "C")), Is.True, "The old path of the moved folder is gone");
        Assert.That(provider.WasPathEnumerated(D(DrivePath, "F1", "F2")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "B")), Is.False);

        var map = DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!;
        Assert.That(map.TryGetParent("F2", out var p2) && p2 == "root", Is.True);
        Assert.That(map.TryGetParent("F3", out var p3) && p3 == "F2", Is.True, "Folders found by the walk of the moved folder are recorded");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveRenamedFolderKeepsItsPathAndChildrenAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(Folder("F2", "F1", "renamed"), deltaLink: DriveLink("tok2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(DrivePath, "F1", "F2") }), "The renamed folder is listed without its children");
        Assert.That(handler.CountRequests("/children"), Is.EqualTo(0));
        Assert.That(provider.WasPathEnumerated(D(DrivePath, "F1", "F2")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "F2", "C")), Is.False, "The children keep their paths and are carried forward");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveNewNestedFoldersAreListedAsATreeAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(File("N", "F3") + "," + Folder("F3", "F2") + "," + Folder("F2", "F1"), deltaLink: DriveLink("tok2")))
            .Json("/drives/d1/items/F2/children", Page(Folder("F3", "F2")))
            .Json("/drives/d1/items/F3/children", Page(File("N", "F3")));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root")),
        });

        var drive = new DriveSourceEntry(provider, SitePath, Drive);
        var top = await CollectAsync(drive, false);
        Assert.That(top.Select(x => x.Path), Is.EqualTo(new[] { D(DrivePath, "F1", "F2") }), "Only the topmost new folder is listed by the drive");
        var all = await CollectAsync(drive, true);
        Assert.That(all.Select(x => x.Path), Is.EqualTo(new[] { D(DrivePath, "F1", "F2"), D(DrivePath, "F1", "F2", "F3"), F(DrivePath, "F1", "F2", "F3", "N") }), "The rest are listed below their new parents");
        Assert.That(handler.CountRequests("/children"), Is.EqualTo(2), "A folder the map does not know is walked in full, as it may have been moved while unknown, and the reported items below it are not listed twice");
        var map = DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!;
        Assert.That(map.TryGetParent("F3", out var p3) && p3 == "F2", Is.True, "Folders found by the walk are in the exported map");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveUnknownAncestorIsLookedUpAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(File("X", "F9"), deltaLink: DriveLink("tok2")))
            .Json("/drives/d1/items/F9?", Folder("F9", "F1"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { F(DrivePath, "F1", "F9", "X") }));
        Assert.That(handler.CountRequests("/items/F9?"), Is.EqualTo(1));
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Contains("F9"), Is.True, "The looked up folder is added to the map");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveStaleTokenFallsBackToAFullWalkAsync()
    {
        var handler = FullDriveHandler("tok3")
            .Sequence("token=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Gone, "resyncRequired"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1"), ("Stale", "root")),
        });

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Count, Is.EqualTo(5));
        Assert.That(messages.Any(x => x.Id == "DriveDeltaResync" && x.Level == LogMessageType.Warning), Is.True);
        var state = provider.ExportChangeTrackingState();
        Assert.That(DriveFolderMap.Deserialize(state[DriveKey])!.Token, Is.EqualTo("tok3"));
        Assert.That(DriveFolderMap.Deserialize(state[DriveKey])!.Contains("Stale"), Is.False, "The map is rebuilt");
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "Stale", "X")), Is.True, "After the full walk everything was examined");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveTooManyChangesFallsBackToAFullWalkAsync()
    {
        var handler = FullDriveHandler("tok3")
            .Json("token=tok1", Page(File("X", "root") + "," + File("Y", "root") + "," + File("Z", "root"), deltaLink: DriveLink("tok2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1")),
        }, o => o["office365-drive-delta-max-items"] = "2");

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Count, Is.EqualTo(5));
        Assert.That(messages.Any(x => x.Id == "DriveDeltaTooLarge" && x.Level == LogMessageType.Information), Is.True);
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Token, Is.EqualTo("tok3"));
    }

    [Test]
    [Category("Office365")]
    public async Task DriveFailedDeltaKeepsThePreviousStateAsync()
    {
        var handler = new FakeGraphHandler()
            .Sequence("token=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var storedMap = StoredMap("tok1", ("F1", "root"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [DriveKey] = storedMap });

        Assert.ThrowsAsync<HttpRequestException>(async () => await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true));

        var state = provider.ExportChangeTrackingState();
        Assert.That(state[DriveKey], Is.EqualTo(storedMap));
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "B")), Is.False, "Everything in the drive is carried forward");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveReachedThroughTwoPathsSharesOneTokenAsync()
    {
        var handler = FullDriveHandler("tok1");
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        await CollectAsync(new DriveSourceEntry(provider, D(Mount, "groups", "g1", "files"), Drive), true);

        var state = provider.ExportChangeTrackingState();
        Assert.That(state.Keys, Is.EqualTo(new[] { DriveKey }), "The token describes the drive, not the path");
        Assert.That(DriveFolderMap.Deserialize(state[DriveKey])!.Token, Is.EqualTo("tok1"));
    }

    [Test]
    [Category("Office365")]
    public async Task DriveDeltaOffWalksChildrenAndStoresNothingAsync()
    {
        var handler = FullDriveHandler("tok1");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [DriveKey] = "stale" }, o => o["office365-disable-drive-delta"] = "true");

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        Assert.That(entries.Count, Is.EqualTo(5));
        Assert.That(handler.CountRequests("/delta"), Is.EqualTo(0));
        Assert.That(provider.ExportChangeTrackingState(), Is.Empty);
    }

    [Test]
    [Category("Office365")]
    public async Task DriveDownloadFailureHoldsTheLinkBackAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(File("B", "F1"), deltaLink: DriveLink("tok2")))
            .Sequence("/drives/d1/items/B/content", () => FakeGraphHandler.GraphError(HttpStatusCode.InternalServerError, "generalException"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Token, Is.EqualTo("tok2"));

        Assert.ThrowsAsync<HttpRequestException>(async () => await entries[0].OpenRead(CancellationToken.None));
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Token, Is.EqualTo("tok1"), "A failed download holds the drive at the previous link");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveFailedFirstWalkStoresNoLinkAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=latest", Page("", deltaLink: DriveLink("tok1")))
            .Json("/drives/d1/root?$select=id", "{\"id\":\"root\"}")
            .Json("/drives/d1/root/children", Page(File("A", "root"), nextLink: $"{GRAPH}/root-next"))
            .Sequence("/root-next", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entries = new List<ISourceProviderEntry>();
        Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await foreach (var e in new DriveSourceEntry(provider, SitePath, Drive).Enumerate(CancellationToken.None))
                entries.Add(e);
        });

        Assert.That(entries.Count, Is.EqualTo(1), "The first page was listed before the failure");
        Assert.That(provider.ExportChangeTrackingState()[DriveKey], Is.EqualTo(""), "A link taken before a walk that did not complete is not stored, so the next run walks the drive in full");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveFailedFolderListingInFirstWalkStoresNoLinkAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=latest", Page("", deltaLink: DriveLink("tok1")))
            .Json("/drives/d1/root?$select=id", "{\"id\":\"root\"}")
            .Json("/drives/d1/root/children", Page(Folder("F1", "root") + "," + File("A", "root")))
            .Sequence("/drives/d1/items/F1/children", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var top = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), false);
        Assert.That(provider.ExportChangeTrackingState().ContainsKey(DriveKey), Is.True, "The drive's own listing completed");

        var folder = top.Single(x => x.IsFolder);
        Assert.ThrowsAsync<HttpRequestException>(async () => await CollectAsync(folder, false));
        Assert.That(provider.ExportChangeTrackingState()[DriveKey], Is.EqualTo(""), "A folder that could not be listed holds the drive's link back, as the backup skips the folder and continues");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveStoredTokenIsSentWithoutTheQueryOptionsAsync()
    {
        var handler = FullDriveHandler("tok1")
            .Json("token=tok1", Page(File("B", "F1"), deltaLink: DriveLink("tok2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>());
        await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        var latest = handler.Requests.Single(x => x.Contains("token=latest", StringComparison.Ordinal));
        Assert.That(latest, Does.Contain("$select=").And.Contain("$top="), "The request that establishes the token carries the query options, which Graph encodes in the token");

        provider = CreateProvider(handler, new Dictionary<string, string> { [DriveKey] = StoredMap("tok1", ("F1", "root")) });
        await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);

        var continued = handler.Requests.Single(x => x.Contains("token=tok1", StringComparison.Ordinal));
        Assert.That(continued, Does.EndWith("/drives/d1/root/delta?token=tok1"), "A stored token is sent alone, as a delta link would be, since Graph does not accept the query options again");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveRepeatedlyFailingDeltaResetsTheDriveAsync()
    {
        var handler = new FakeGraphHandler()
            .Sequence("token=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root")),
            ["failures:" + DriveKey] = (ChangeTracker.MAX_CONSECUTIVE_FAILURES - 1).ToString(),
        });

        Assert.ThrowsAsync<HttpRequestException>(async () => await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true));

        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "B")), Is.False, "This run still carries the drive forward");
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [DriveKey] = "" }), "After repeated failures the state is dropped, so the next backup walks the drive in full instead of carrying stale content forward forever");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveStaleTokenIsNotRestoredByALaterDownloadFailureAsync()
    {
        var handler = FullDriveHandler("tok3")
            .Sequence("token=tok1", () => FakeGraphHandler.GraphError(HttpStatusCode.Gone, "resyncRequired"))
            .Sequence("/drives/d1/items/A/content", () => FakeGraphHandler.GraphError(HttpStatusCode.InternalServerError, "generalException"));
        var storedMap = StoredMap("tok1", ("F1", "root"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [DriveKey] = storedMap });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Token, Is.EqualTo("tok3"));

        var file = entries.Single(x => x.Path == F(DrivePath, "A"));
        Assert.ThrowsAsync<HttpRequestException>(async () => await file.OpenRead(CancellationToken.None));

        Assert.That(provider.ExportChangeTrackingState()[DriveKey], Is.EqualTo(""), "The state the service rejected is not stored again; the next backup walks the drive in full");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveFoldersListedInFullAreNotIncrementalAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(Folder("F2", "root") + "," + Folder("F3", "F1"), deltaLink: DriveLink("tok2")))
            .Json("/drives/d1/items/F2/children", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("F1", "root"), ("F2", "F1"), ("F3", "F1")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        Assert.That(entries.Select(x => x.Path), Is.EquivalentTo(new[] { D(DrivePath, "F2"), D(DrivePath, "F1", "F3") }));

        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(DrivePath)), Is.True, "The drive lists only its changes");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(DrivePath, "F1", "F3")), Is.True, "A changed folder lists only its changed children, so an empty listing does not make it empty");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(DrivePath, "F2")), Is.False, "A moved folder was listed in full, so an empty listing means it is empty");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveFailedMovedFolderListingKeepsThePreviousLinkAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(Folder("F2", "root"), deltaLink: DriveLink("tok2")))
            .Sequence("/drives/d1/items/F2/children", () => FakeGraphHandler.GraphError(HttpStatusCode.Forbidden, "accessDenied"));
        var storedMap = StoredMap("tok1", ("F1", "root"), ("F2", "F1"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [DriveKey] = storedMap });

        var top = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), false);
        Assert.That(top.Select(x => x.Path), Is.EqualTo(new[] { D(DrivePath, "F2") }), "The moved folder is listed at its new path");
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Token, Is.EqualTo("tok2"));

        Assert.ThrowsAsync<HttpRequestException>(async () => await CollectAsync(top[0], false));
        Assert.That(provider.ExportChangeTrackingState()[DriveKey], Is.EqualTo(storedMap), "The previous link and map are kept so the move is reported again");
    }

    [Test]
    [Category("Office365")]
    public async Task DriveDeletedFolderUnknownToTheMapDropsItsSubtreeAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("token=tok1", Page(Deleted("F1"), deltaLink: DriveLink("tok2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [DriveKey] = StoredMap("tok1", ("G1", "root")),
        });

        var entries = await CollectAsync(new DriveSourceEntry(provider, SitePath, Drive), true);
        Assert.That(entries, Is.Empty);

        Assert.That(provider.WasPathEnumerated(D(DrivePath, "F1")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "F1", "F2", "C")), Is.True, "Everything below a removed folder is dropped, whether or not the map knew the folder");
        Assert.That(provider.WasPathEnumerated(F(DrivePath, "G1", "X")), Is.False);
        Assert.That(DriveFolderMap.Deserialize(provider.ExportChangeTrackingState()[DriveKey])!.Token, Is.EqualTo("tok2"));
    }

    [Test]
    [Category("Office365")]
    public async Task MailPageOfRepeatedMessagesDoesNotEndTheWalkAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("deltatoken=tok1", Page(Message("m1") + "," + Message("m2"), nextLink: $"{GRAPH}/mail-next-1"))
            .Json("/mail-next-1", Page(Message("m2") + "," + Message("m1"), nextLink: $"{GRAPH}/mail-next-2"))
            .Json("/mail-next-2", Page(Message("m3"), deltaLink: MailLink("tok2")))
            .Json("/users/u1/mailFolders/f1/childFolders", Page(""));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MailKey] = MailState("tok1") });

        var entries = await CollectAsync(new UserMailboxFolderSourceEntry(provider, User, FolderPath, MailFolder), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(FolderPath, "m1"), D(FolderPath, "m2"), D(FolderPath, "m3") }), "Repeated messages are listed once and the pages after them are still read");
        Assert.That(provider.ExportChangeTrackingState()[MailKey], Is.EqualTo(MailState("tok2")), "The walk reached its delta link");
        Assert.That(handler.CountRequests("/mail-next-2"), Is.EqualTo(1));
    }

    [Test]
    [Category("Office365")]
    public async Task DeltaWalkWhoseNextLinkRepeatsStopsAsync()
    {
        var handler = new FakeGraphHandler()
            .Json("/users/u1/mailFolders/f1/messages/delta", Page(Message("m1"), nextLink: $"{GRAPH}/mail-loop"))
            .Json("/mail-loop", Page(Message("m2"), nextLink: $"{GRAPH}/mail-loop"));
        var provider = CreateProvider(handler, null);

        var messages = CaptureLog(out var scope);
        using (scope)
        {
            var holder = new DeltaLinkHolder();
            var items = await provider.UserEmailApi.ListMessagesDeltaAsync("u1", "f1", null, holder, true, CancellationToken.None).ToListAsync();
            Assert.That(items.Select(x => x.Id), Is.EqualTo(new[] { "m1", "m2" }));
            Assert.That(holder.DeltaLink, Is.Null, "A walk that loops never reaches a delta link");
            Assert.That(messages.Any(x => x.Id == "DeltaPagingRepeatDetected"), Is.True, "The loop is logged");
        }

        Assert.That(handler.Requests.Count, Is.EqualTo(2), "The repeated link is not requested again");
    }
}
