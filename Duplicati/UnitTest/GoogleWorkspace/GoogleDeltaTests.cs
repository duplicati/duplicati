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
using Duplicati.Library.SourceProvider.ChangeTracking;
using Duplicati.Proprietary.GoogleWorkspace;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Calendar.v3.Data;
using NUnit.Framework;

namespace Duplicati.UnitTest.GoogleWorkspace;

/// <summary>
/// The delta walks of the Google Workspace provider against fake Google APIs
/// </summary>
public class GoogleDeltaTests
{
    /// <summary>
    /// Answers Google API requests from canned responses, matched on a fragment of the decoded
    /// URL. Routes are tried in the order they were added; a fragment can be answered by a
    /// sequence of responses, one per request, the last one repeating. Gmail batch requests
    /// are answered from the message table, with a not-found part for an unknown id.
    /// </summary>
    private sealed class FakeGoogleHandler : HttpMessageHandler
    {
        private readonly List<(string Fragment, Queue<Func<HttpResponseMessage>> Responses)> _routes = [];

        /// <summary>The decoded URLs of the requests received</summary>
        public List<string> Requests { get; } = [];

        /// <summary>The message ids each batch request asked for</summary>
        public List<List<string>> BatchRequests { get; } = [];

        /// <summary>The metadata answered for a message id in a batch</summary>
        public Dictionary<string, string> Messages { get; } = new(StringComparer.Ordinal);

        /// <summary>The error answered for a message id in a batch, instead of its metadata</summary>
        public Dictionary<string, HttpStatusCode> MessageErrors { get; } = new(StringComparer.Ordinal);

        public FakeGoogleHandler Json(string fragment, string json)
            => Sequence(fragment, () => GoogleWorkspaceTestSupport.Json(HttpStatusCode.OK, json));

        public FakeGoogleHandler Sequence(string fragment, params Func<HttpResponseMessage>[] responses)
        {
            _routes.Add((fragment, new Queue<Func<HttpResponseMessage>>(responses)));
            return this;
        }

        public FakeGoogleHandler Message(string id, params string[] labels)
        {
            Messages[id] = $"{{\"id\":\"{id}\",\"threadId\":\"t-{id}\",\"labelIds\":[{string.Join(",", labels.Select(x => $"\"{x}\""))}],\"sizeEstimate\":10,\"internalDate\":\"1700000000000\"}}";
            return this;
        }

        public int CountRequests(string fragment) => Requests.Count(x => x.Contains(fragment, StringComparison.Ordinal));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.AbsoluteUri);
            lock (Requests)
                Requests.Add(url);

            HttpResponseMessage response;
            if (request.RequestUri.AbsolutePath.StartsWith("/batch", StringComparison.Ordinal))
            {
                var ids = GoogleWorkspaceTestSupport.BatchRequestedIds(await request.Content!.ReadAsStringAsync(cancellationToken));
                lock (Requests)
                    BatchRequests.Add(ids);
                response = GoogleWorkspaceTestSupport.BatchResponse(ids.Select(id =>
                    MessageErrors.TryGetValue(id, out var status) ? (status, $"{{\"error\":{{\"code\":{(int)status},\"message\":\"Simulated failure\",\"errors\":[{{\"message\":\"Simulated failure\",\"domain\":\"global\",\"reason\":\"failure\"}}]}}}}")
                    : Messages.TryGetValue(id, out var json) ? (HttpStatusCode.OK, json)
                    : (HttpStatusCode.NotFound, GoogleWorkspaceTestSupport.NOT_FOUND_PART)));
            }
            else
            {
                response = null!;
                foreach (var (fragment, responses) in _routes)
                {
                    if (!url.Contains(fragment, StringComparison.Ordinal))
                        continue;

                    var factory = responses.Count > 1 ? responses.Dequeue() : responses.Peek();
                    response = factory();
                    break;
                }

                response ??= GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", $"No fake answer for {url}");
            }

            response.RequestMessage = request;
            return response;
        }
    }

    private static readonly string Mount = Util.AppendDirSeparator(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "gws")));
    private const string USER = "u@x.test";

    private static string D(params string[] parts) => Util.AppendDirSeparator(Path.Combine(parts));
    private static string F(params string[] parts) => Path.Combine(parts);

    private static readonly string UserPath = D(Mount, "Users", USER);
    private static readonly string GmailPath = D(UserPath, "UserGmail");
    private static readonly string DrivePath = D(UserPath, "UserDrive");
    private static readonly string MyDrivePath = D(DrivePath, "root");
    private static readonly string CalendarsPath = D(UserPath, "UserCalendar");
    private static readonly string ContactsPath = D(UserPath, "UserContacts", "Entries");

    private static readonly string GmailKey = ChangeTrackingKeys.Gmail(USER);
    private static readonly string MyDriveKey = ChangeTrackingKeys.MyDrive(USER);
    private static readonly string CalendarKey = ChangeTrackingKeys.Calendar(USER, "cal1");
    private static readonly string ContactsKey = ChangeTrackingKeys.Contacts(USER);

    private const string FOLDER_MIME = "application/vnd.google-apps.folder";

    private static SourceProvider CreateProvider(FakeGoogleHandler handler, IReadOnlyDictionary<string, string>? state, Action<Dictionary<string, string?>>? configure = null)
    {
        var options = new Dictionary<string, string?>
        {
            ["google-client-id"] = "client",
            ["google-client-secret"] = "secret",
            ["google-refresh-token"] = "refresh",
            ["google-ignore-calendar-acl"] = "true",
            ["store-metadata-content-in-database"] = "true",
            ["machine-id"] = "test-machine",
        };
        configure?.Invoke(options);

        SourceProvider provider;
        using (GoogleWorkspaceTestSupport.UseHandler(handler))
            provider = new SourceProvider("googleworkspace://", Mount, options);
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

    private static List<LogEntry> CaptureLog(out IDisposable scope)
    {
        var messages = new List<LogEntry>();
        var isolating = Log.StartIsolatingScope(true);
        var inner = Log.StartScope(messages.Add);
        scope = new ScopeDisposer(inner, isolating);
        return messages;
    }

    private sealed class ScopeDisposer(params IDisposable[] scopes) : IDisposable
    {
        public void Dispose()
        {
            foreach (var s in scopes)
                s.Dispose();
        }
    }

    #region Gmail

    private static string Labels(params string[] ids) => $"{{\"labels\":[{string.Join(",", ids.Select(x => $"{{\"id\":\"{x}\",\"name\":\"{x}\",\"type\":\"system\"}}"))}]}}";
    private static string Profile(string historyId) => $"{{\"emailAddress\":\"{USER}\",\"historyId\":\"{historyId}\",\"messagesTotal\":1}}";
    private static string MessageList(params string[] ids) => $"{{\"messages\":[{string.Join(",", ids.Select(x => $"{{\"id\":\"{x}\"}}"))}]}}";
    private static string Raw(string id) => $"{{\"id\":\"{id}\",\"raw\":\"{GoogleWorkspaceTestSupport.Base64Url(Encoding.UTF8.GetBytes("Subject: " + id))}\"}}";
    private static string Added(string id) => $"{{\"id\":\"1\",\"messagesAdded\":[{{\"message\":{{\"id\":\"{id}\"}}}}]}}";
    private static string DeletedRecord(string id) => $"{{\"id\":\"1\",\"messagesDeleted\":[{{\"message\":{{\"id\":\"{id}\"}}}}]}}";
    private static string LabelRemoved(string id, string label) => $"{{\"id\":\"1\",\"labelsRemoved\":[{{\"message\":{{\"id\":\"{id}\"}},\"labelIds\":[\"{label}\"]}}]}}";
    private static string History(string historyId, params string[] records) => $"{{\"history\":[{string.Join(",", records)}],\"historyId\":\"{historyId}\"}}";
    private static string Settings() => "{}";

    private static FakeGoogleHandler GmailSettings(FakeGoogleHandler handler)
        => handler.Json("/settings/filters", "{\"filter\":[]}").Json("/settings/forwardingAddresses", "{\"forwardingAddresses\":[]}")
            .Json("/settings/autoForwarding", Settings()).Json("/settings/vacation", Settings()).Json("/settings/sendAs", "{\"sendAs\":[]}");

    private static UserTypeSourceEntry GmailEntry(SourceProvider provider)
        => new(provider, UserPath, USER, false, "Gmail", SourceItemType.UserGmail);

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailInitialWalkListsEverythingAndStoresTheProfileHistoryIdAsync()
    {
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX", "SENT"))
            .Json("/profile", Profile("100"))
            .Json("/messages?labelIds=INBOX", MessageList("m1", "m2"))
            .Json("/messages?labelIds=SENT", MessageList("m2")))
            .Message("m1", "INBOX").Message("m2", "INBOX", "SENT");
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => !x.IsFolder).Select(x => x.Path).Where(x => x.EndsWith(".eml")), Is.EquivalentTo(new[] { F(GmailPath, "INBOX", "m1.eml"), F(GmailPath, "INBOX", "m2.eml"), F(GmailPath, "SENT", "m2.eml") }));
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [GmailKey] = "100" }));
        Assert.That(handler.Requests.FindIndex(x => x.Contains("/profile")), Is.LessThan(handler.Requests.FindIndex(x => x.Contains("/messages?"))), "The history id is taken before the listing");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m9.eml")), Is.True, "A full walk examines everything");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(GmailPath, "INBOX")), Is.False);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailIncrementalWalkListsTouchedMessagesUnderTheirLabelsAsync()
    {
        // m1 is untouched, m2 is new in both labels, m3 was deleted, m4 lost INBOX and keeps SENT
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX", "SENT"))
            .Json("/history?", History("200", Added("m2"), DeletedRecord("m3"), LabelRemoved("m4", "INBOX"))))
            .Message("m2", "INBOX", "SENT").Message("m4", "SENT");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => x.Path.EndsWith(".eml")).Select(x => x.Path), Is.EquivalentTo(new[] { F(GmailPath, "INBOX", "m2.eml"), F(GmailPath, "SENT", "m2.eml"), F(GmailPath, "SENT", "m4.eml") }));
        Assert.That(provider.ExportChangeTrackingState()[GmailKey], Is.EqualTo("200"));
        Assert.That(handler.Requests.Single(x => x.Contains("/history?")), Does.Contain("startHistoryId=100"));
        Assert.That(handler.CountRequests("/messages?"), Is.EqualTo(0), "No label is listed in full");
        Assert.That(handler.BatchRequests, Has.Count.EqualTo(1));
        Assert.That(handler.BatchRequests[0], Is.EquivalentTo(new[] { "m2", "m3", "m4" }), "Every touched message is fetched once");

        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(GmailPath, "INBOX")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m1.eml")), Is.False, "An untouched message is carried forward");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m2.eml")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m3.eml")), Is.True, "A deleted message is dropped from every label");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "SENT", "m3.eml")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m4.eml")), Is.True, "The label the message lost is dropped");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "OLD", "m1.eml")), Is.True, "A label that is no longer listed is dropped with its messages");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "Settings", "filters.json")), Is.True, "The settings are listed every run");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailExpiredHistoryIdRestartsWithAFullWalkAsync()
    {
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Sequence("/history?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", "Requested entity was not found."))
            .Json("/profile", Profile("300"))
            .Json("/messages?labelIds=INBOX", MessageList("m1")))
            .Message("m1", "INBOX");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => x.Path.EndsWith(".eml")).Select(x => x.Path), Is.EqualTo(new[] { F(GmailPath, "INBOX", "m1.eml") }));
        Assert.That(messages.Any(x => x.Id == "GmailHistoryExpired" && x.Level == LogMessageType.Information), Is.True);
        Assert.That(provider.ExportChangeTrackingState()[GmailKey], Is.EqualTo("300"));
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m9.eml")), Is.True, "After a resync the mailbox was walked in full");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailFailedWalkKeepsThePreviousIdAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Sequence("/history?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "The caller does not have permission"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        Assert.ThrowsAsync<Google.GoogleApiException>(() => CollectAsync(GmailEntry(provider), true));

        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [GmailKey] = "100", ["failures:" + GmailKey] = "1" }), "The failed walk holds the mailbox at the previous id");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m1.eml")), Is.False, "The labels were marked incremental before the history was read, so the mailbox is carried forward unchanged");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "OLD", "m1.eml")), Is.True, "A label that is no longer listed is still dropped");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailFailedDownloadHoldsTheIdBackAsync()
    {
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Json("/history?", History("200", Added("m2")))
            .Sequence("/messages/m2?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "nope")))
            .Message("m2", "INBOX");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var entries = await CollectAsync(GmailEntry(provider), true);
        var message = entries.Single(x => x.Path.EndsWith("m2.eml"));
        Assert.ThrowsAsync<Google.GoogleApiException>(() => message.OpenRead(CancellationToken.None));

        Assert.That(provider.ExportChangeTrackingState()[GmailKey], Is.EqualTo("100"), "A failed download holds the mailbox at the previous id");
        Assert.That(provider.ExportChangeTrackingState()["failures:" + GmailKey], Is.EqualTo("1"));
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailRepeatedlyFailingWalkResetsTheMailboxAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Sequence("/history?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "nope"));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [GmailKey] = "100",
            ["failures:" + GmailKey] = (ChangeTracker.MAX_CONSECUTIVE_FAILURES - 1).ToString(),
        });

        Assert.ThrowsAsync<Google.GoogleApiException>(() => CollectAsync(GmailEntry(provider), true));

        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [GmailKey] = "" }), "After repeated failures the mailbox is listed in full next time");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailStaleIdIsNotRestoredByALaterDownloadFailureAsync()
    {
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Sequence("/history?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", "gone"))
            .Json("/profile", Profile("300"))
            .Json("/messages?labelIds=INBOX", MessageList("m1"))
            .Sequence("/messages/m1?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "nope")))
            .Message("m1", "INBOX");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var entries = await CollectAsync(GmailEntry(provider), true);
        Assert.ThrowsAsync<Google.GoogleApiException>(() => entries.Single(x => x.Path.EndsWith("m1.eml")).OpenRead(CancellationToken.None));

        Assert.That(provider.ExportChangeTrackingState()[GmailKey], Is.EqualTo(""), "The rejected id is not stored again; the next run lists in full");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailDeltaOffListsInFullAndStoresNothingAsync()
    {
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Json("/messages?labelIds=INBOX", MessageList("m1")))
            .Message("m1", "INBOX");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" }, o => o["google-disable-gmail-delta"] = "true");

        var entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => x.Path.EndsWith(".eml")).Select(x => x.Path), Is.EqualTo(new[] { F(GmailPath, "INBOX", "m1.eml") }));
        Assert.That(provider.ExportChangeTrackingState(), Is.Empty);
        Assert.That(handler.CountRequests("/history?"), Is.EqualTo(0));
        Assert.That(handler.CountRequests("/profile"), Is.EqualTo(0));
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m9.eml")), Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailMessageWithoutLabelsIsDroppedAsync()
    {
        // A message that lost every label is not listed under any folder, as the full walk would not list it either
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Json("/history?", History("200", LabelRemoved("m1", "INBOX"))))
            .Message("m1");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => x.Path.EndsWith(".eml")), Is.Empty);
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m1.eml")), Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailUnreadableMessageIsCarriedForwardAndHoldsTheIdBackAsync()
    {
        // m2 is touched but its metadata cannot be read; m3 is touched and read
        var handler = GmailSettings(new FakeGoogleHandler()
            .Json("/labels", Labels("INBOX"))
            .Json("/history?", History("200", Added("m2"), Added("m3"))))
            .Message("m3", "INBOX");
        handler.MessageErrors["m2"] = HttpStatusCode.Forbidden;
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => x.Path.EndsWith(".eml")).Select(x => x.Path), Is.EquivalentTo(new[] { F(GmailPath, "INBOX", "m3.eml") }));
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m2.eml")), Is.False, "A message that could not be read keeps its previous paths instead of vanishing from this backup");
        Assert.That(provider.WasPathEnumerated(F(GmailPath, "INBOX", "m3.eml")), Is.True);

        var state = provider.ExportChangeTrackingState();
        Assert.That(state[GmailKey], Is.EqualTo("100"), "The unreadable message holds the history id back so it is reported again");
        Assert.That(state.Keys, Does.Contain("failed-items:" + GmailKey), "The failure is recorded against the message, so a message that never becomes readable is given up on eventually");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task GmailLabelCreatedDuringTheWalkIsListedAsync()
    {
        // The user label Label_7 is created and applied to m2 after the labels were listed, so the
        // history mentions it while the first label listing does not
        var handler = GmailSettings(new FakeGoogleHandler()
            .Sequence("/labels", () => GoogleWorkspaceTestSupport.Json(HttpStatusCode.OK, Labels("INBOX")), () => GoogleWorkspaceTestSupport.Json(HttpStatusCode.OK, Labels("INBOX", "Label_7")))
            .Json("/history?", History("200", Added("m2"))))
            .Message("m2", "INBOX", "Label_7");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [GmailKey] = "100" });

        var entries = await CollectAsync(GmailEntry(provider), true);

        Assert.That(entries.Where(x => x.Path.EndsWith(".eml")).Select(x => x.Path), Is.EquivalentTo(new[] { F(GmailPath, "INBOX", "m2.eml"), F(GmailPath, "Label_7", "m2.eml") }), "The new label is listed with the message that got it");
        Assert.That(handler.CountRequests("/labels"), Is.EqualTo(2), "The labels are listed again only because the history mentioned an unknown one");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(GmailPath, "Label_7")), Is.True);
        Assert.That(provider.ExportChangeTrackingState()[GmailKey], Is.EqualTo("200"));
    }

    #endregion

    #region Drive

    private static string DriveFile(string id, string parent, string? name = null) => $"{{\"id\":\"{id}\",\"name\":\"{name ?? id + ".txt"}\",\"mimeType\":\"text/plain\",\"size\":\"3\",\"parents\":[\"{parent}\"],\"modifiedTime\":\"2024-01-01T00:00:00Z\"}}";
    private static string DriveFolder(string id, string parent, string? name = null) => $"{{\"id\":\"{id}\",\"name\":\"{name ?? id}\",\"mimeType\":\"{FOLDER_MIME}\",\"parents\":[\"{parent}\"]}}";
    private static string FileList(params string[] files) => $"{{\"files\":[{string.Join(",", files)}]}}";
    private static string Change(string fileJson) => $"{{\"fileId\":{fileJson.Substring(fileJson.IndexOf(':') + 1, fileJson.IndexOf(',') - fileJson.IndexOf(':') - 1)},\"removed\":false,\"changeType\":\"file\",\"file\":{fileJson}}}";
    private static string Removed(string id) => $"{{\"fileId\":\"{id}\",\"removed\":true,\"changeType\":\"file\"}}";
    private static string Trashed(string id, string parent) => $"{{\"fileId\":\"{id}\",\"removed\":false,\"changeType\":\"file\",\"file\":{{\"id\":\"{id}\",\"name\":\"{id}\",\"mimeType\":\"text/plain\",\"parents\":[\"{parent}\"],\"trashed\":true}}}}";
    private static string Changes(string newToken, params string[] changes) => $"{{\"newStartPageToken\":\"{newToken}\",\"changes\":[{string.Join(",", changes)}]}}";
    private static string StartToken(string token) => $"{{\"startPageToken\":\"{token}\"}}";

    private static string StoredMap(string token, string rootId, params (string Folder, string Parent)[] folders)
    {
        var map = new FolderMap { Token = token, RootId = rootId };
        foreach (var (folder, parent) in folders)
            map.SetParent(folder, parent);
        return map.Serialize();
    }

    private static FakeGoogleHandler FileChildren(FakeGoogleHandler handler, string id)
        => handler.Json($"/files/{id}/permissions", "{\"permissions\":[]}").Json($"/files/{id}/comments", "{\"comments\":[]}").Json($"/files/{id}/revisions", "{\"revisions\":[]}");

    private static DriveTreeSourceEntry MyDrive(SourceProvider provider)
        => new(provider, DrivePath, USER, false, "My Drive", "root", null, provider.ApiHelper.GetDriveService(USER));

    private static IEnumerable<string> FolderPaths(IEnumerable<ISourceProviderEntry> entries)
        => entries.Where(x => x.IsFolder).Select(x => x.Path);

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveFirstRunWalksChildrenAndStoresStartTokenRootIdAndMapAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/changes/startPageToken", StartToken("5"))
            .Json("/files/root?", "{\"id\":\"R\"}")
            .Json("'root' in parents", FileList(DriveFolder("F1", "R"), DriveFile("A", "R")))
            .Json("'F1' in parents", FileList(DriveFile("B", "F1")));
        FileChildren(FileChildren(handler, "A"), "B");
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EquivalentTo(new[] { D(MyDrivePath, "F1"), D(MyDrivePath, "A"), D(MyDrivePath, "F1", "B"), D(MyDrivePath, "A", "Revisions"), D(MyDrivePath, "F1", "B", "Revisions") }));
        Assert.That(handler.Requests.FindIndex(x => x.Contains("startPageToken")), Is.LessThan(handler.Requests.FindIndex(x => x.Contains("in parents"))), "The token is taken before the listing");

        var map = FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!;
        Assert.That(map.Token, Is.EqualTo("5"));
        Assert.That(map.RootId, Is.EqualTo("R"), "The real root id is stored, not the alias");
        Assert.That(map.TryGetParent("F1", out var parent) && parent == "R", Is.True);
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "Z", "content")), Is.True, "A full walk examines everything");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveIncrementalRunListsOnlyTheChangeAsync()
    {
        var handler = FileChildren(new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFile("B", "F1")))), "B");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R", ("F1", "R")) });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EquivalentTo(new[] { D(MyDrivePath, "F1", "B"), D(MyDrivePath, "F1", "B", "Revisions") }), "The changed file is listed at its resolved path below the unchanged folder");
        Assert.That(handler.Requests.Single(x => x.Contains("/changes?")), Does.Contain("pageToken=5").And.Contain("restrictToMyDrive=true"));
        Assert.That(handler.CountRequests("in parents"), Is.EqualTo(0), "No folder is listed");
        Assert.That(FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!.Token, Is.EqualTo("9"));

        Assert.That(provider.IsIncrementallyEnumeratedFolder(MyDrivePath), Is.True);
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(MyDrivePath, "F1")), Is.True, "The unchanged folder is not listed, so it is not empty either");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "A", "content")), Is.False, "An unchanged file is carried forward");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "B", "content")), Is.True, "Everything below the changed file was examined");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "B", "Revisions", "r1")), Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveGoogleSiteHasNoContentOrRevisionsAsync()
    {
        // A Site can neither be downloaded nor exported, so listing its content would fail
        // on every run and hold the drive back for nothing
        var site = "{\"id\":\"S\",\"name\":\"Team site\",\"mimeType\":\"application/vnd.google-apps.site\",\"parents\":[\"R\"],\"modifiedTime\":\"2024-01-01T00:00:00Z\"}";
        var handler = FileChildren(new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(site))), "S");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(entries.Select(x => x.Path), Is.EquivalentTo(new[] { D(MyDrivePath, "S"), F(MyDrivePath, "S", "metadata.json"), F(MyDrivePath, "S", "permissions.json"), F(MyDrivePath, "S", "comments.json") }), "A Site lists what Drive knows about it and no content or revisions");
        Assert.That(handler.CountRequests("alt=media"), Is.EqualTo(0));
        Assert.That(FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!.Token, Is.EqualTo("9"), "Nothing fails, so the drive advances");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveDeletedFolderDropsItsSubtreeAsync()
    {
        var handler = new FakeGoogleHandler().Json("/changes?", Changes("9", Removed("F1")));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R", ("F1", "R"), ("F2", "F1")) });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(entries, Is.Empty);
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "F2", "B", "content")), Is.True, "Everything below the removed folder is dropped");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "A", "content")), Is.False);
        var map = FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!;
        Assert.That(map.Contains("F1") || map.Contains("F2"), Is.False, "The removed folder and its descendants leave the map");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveTrashedFileIsARemovalAsync()
    {
        var handler = new FakeGoogleHandler().Json("/changes?", Changes("9", Trashed("A", "R")));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(entries, Is.Empty);
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "A", "content")), Is.True, "A trashed file is dropped like the full walk leaves it out");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveMovedFolderIsWalkedAtItsNewPathAsync()
    {
        var handler = FileChildren(new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFolder("F1", "F2"))))
            .Json("'F1' in parents", FileList(DriveFile("B", "F1"))), "B");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R", ("F1", "R"), ("F2", "R")) });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EquivalentTo(new[] { D(MyDrivePath, "F2", "F1"), D(MyDrivePath, "F2", "F1", "B"), D(MyDrivePath, "F2", "F1", "B", "Revisions") }), "The moved folder is listed in full at its new path");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "B", "content")), Is.True, "The old path is dropped");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F2", "F1", "C", "content")), Is.True, "A full listing below the moved folder drops what it did not list");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(MyDrivePath, "F2", "F1")), Is.False);
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F2", "D", "content")), Is.False, "The unchanged parent's other content is carried forward");
        var map = FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!;
        Assert.That(map.TryGetParent("F1", out var parent) && parent == "F2", Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveRenamedFolderKeepsItsPathAndChildrenAsync()
    {
        var handler = new FakeGoogleHandler().Json("/changes?", Changes("9", Change(DriveFolder("F1", "R", "Renamed"))));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R", ("F1", "R")) });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EqualTo(new[] { D(MyDrivePath, "F1") }), "The renamed folder is listed with no children of its own");
        Assert.That((await entries[0].GetMinorMetadata(CancellationToken.None))["gsuite:Name"], Is.EqualTo("Renamed"));
        Assert.That(handler.CountRequests("in parents"), Is.EqualTo(0));
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "B", "content")), Is.False, "The children of a renamed folder are carried forward");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(MyDrivePath, "F1")), Is.True, "A folder listing only its changes is not excluded as empty");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveNewNestedFoldersAreListedAsATreeAsync()
    {
        var handler = FileChildren(new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFolder("N1", "R")), Change(DriveFolder("N2", "N1")), Change(DriveFile("C", "N2"))))
            .Json("'N1' in parents", FileList(DriveFolder("N2", "N1")))
            .Json("'N2' in parents", FileList(DriveFile("C", "N2"))), "C");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EquivalentTo(new[] { D(MyDrivePath, "N1"), D(MyDrivePath, "N1", "N2"), D(MyDrivePath, "N1", "N2", "C"), D(MyDrivePath, "N1", "N2", "C", "Revisions") }), "A new folder is walked in full and its contents are not listed twice");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveUnknownAncestorIsLookedUpAsync()
    {
        var handler = FileChildren(new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFile("C", "F9"))))
            .Json("/files/F9?", "{\"id\":\"F9\",\"parents\":[\"R\"]}"), "C");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries).First(), Is.EqualTo(D(MyDrivePath, "F9", "C")));
        Assert.That(handler.CountRequests("/files/F9?"), Is.EqualTo(1));
        Assert.That(FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!.Contains("F9"), Is.True, "The looked up folder is added to the map");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveChainLeavingTheDriveDropsTheItemAsync()
    {
        // The parent of the changed file is a folder in a shared drive, so the file left My Drive
        var handler = new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFile("C", "F9"))))
            .Json("/files/F9?", "{\"id\":\"F9\",\"parents\":[\"S\"],\"driveId\":\"S\"}");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") });

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(entries, Is.Empty, "An item outside the drive is not listed");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "C", "content")), Is.True, "Its previous path is dropped");
        Assert.That(FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!.Token, Is.EqualTo("9"));
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveRejectedTokenFallsBackToAFullWalkAsync()
    {
        var handler = new FakeGoogleHandler()
            .Sequence("/changes?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.BadRequest, "invalid", "Invalid Value"))
            .Json("/changes/startPageToken", StartToken("7"))
            .Json("/files/root?", "{\"id\":\"R\"}")
            .Json("'root' in parents", FileList(DriveFolder("F1", "R")))
            .Json("'F1' in parents", FileList());
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R", ("Stale", "R")) });

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EqualTo(new[] { D(MyDrivePath, "F1") }));
        Assert.That(messages.Any(x => x.Id == "DriveChangesTokenRejected" && x.Level == LogMessageType.Information), Is.True);
        var map = FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!;
        Assert.That(map.Token, Is.EqualTo("7"));
        Assert.That(map.Contains("Stale"), Is.False, "The map is rebuilt");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "A", "content")), Is.True, "After a resync the drive was walked in full");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(D(MyDrivePath, "F1")), Is.False);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveTooManyChangesFallsBackToAFullWalkAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFile("A", "R")), Change(DriveFile("B", "R"))))
            .Json("/changes/startPageToken", StartToken("7"))
            .Json("/files/root?", "{\"id\":\"R\"}")
            .Json("'root' in parents", FileList());
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") }, o => o["google-drive-delta-max-items"] = "1");

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(entries, Is.Empty);
        Assert.That(FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!.Token, Is.EqualTo("7"));
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "A", "content")), Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveFailedReadKeepsThePreviousStateAsync()
    {
        var handler = new FakeGoogleHandler()
            .Sequence("/changes?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "The caller does not have permission"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R", ("F1", "R")) });

        Assert.ThrowsAsync<Google.GoogleApiException>(() => CollectAsync(MyDrive(provider), true));

        var state = provider.ExportChangeTrackingState();
        Assert.That(state[MyDriveKey], Is.EqualTo(StoredMap("5", "R", ("F1", "R"))), "The failed read keeps the previous token and map");
        Assert.That(state["failures:" + MyDriveKey], Is.EqualTo("1"));
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "F1", "B", "content")), Is.False, "The drive is carried forward unchanged");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveFailedRequestBelowAFileHoldsTheTokenBackAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/changes?", Changes("9", Change(DriveFile("A", "R"))))
            .Sequence("/files/A/permissions", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "nope"))
            .Json("/files/A/comments", "{\"comments\":[]}").Json("/files/A/revisions", "{\"revisions\":[]}");
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") });

        var entries = await CollectAsync(MyDrive(provider), true);
        var permissions = entries.Single(x => x.Path.EndsWith("permissions.json"));
        Assert.ThrowsAsync<Google.GoogleApiException>(() => permissions.OpenRead(CancellationToken.None));

        Assert.That(FolderMap.Deserialize(provider.ExportChangeTrackingState()[MyDriveKey])!.Token, Is.EqualTo("5"), "A failed request below a file holds the drive at the previous token");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveItemThatKeepsFailingIsGivenUpAfterTheLimitAsync()
    {
        // The content of A cannot be read in any run, as for a Google Doc over the export
        // limit. The drive is held back while the failures are below the limit, then advances
        // past the item instead of being reset to a full walk that would fail on it again
        var state = new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") };
        for (var run = 1; run <= ChangeTracker.MAX_CONSECUTIVE_FAILURES; run++)
        {
            var handler = FileChildren(new FakeGoogleHandler()
                .Json("/changes?", Changes("9", Change(DriveFile("A", "R"))))
                .Sequence("/files/A?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "exportSizeLimitExceeded", "This file is too large to be exported.")), "A");
            var provider = CreateProvider(handler, state);

            var entries = await CollectAsync(MyDrive(provider), true);
            var content = entries.Single(x => x.Path == F(MyDrivePath, "A", "content"));
            Assert.CatchAsync<Exception>(() => content.OpenRead(CancellationToken.None), $"Run {run}: the download fails");

            var messages = CaptureLog(out var scope);
            using (scope)
                state = new Dictionary<string, string>(provider.ExportChangeTrackingState());

            if (run < ChangeTracker.MAX_CONSECUTIVE_FAILURES)
            {
                Assert.That(FolderMap.Deserialize(state[MyDriveKey])!.Token, Is.EqualTo("5"), $"Run {run}: the drive is held at the previous token");
                Assert.That(state["failures:" + MyDriveKey], Is.EqualTo(run.ToString()));
                Assert.That(state.Keys, Does.Contain("failed-items:" + MyDriveKey), $"Run {run}: the failed item is remembered");
            }
            else
            {
                Assert.That(FolderMap.Deserialize(state[MyDriveKey])!.Token, Is.EqualTo("9"), "After the limit the drive advances past the item that keeps failing");
                Assert.That(state.Keys, Is.EquivalentTo(new[] { MyDriveKey }), "The counters are dropped with the advance");
                Assert.That(messages.Any(x => x.Id == "ChangeTrackingItemsGivenUp" && x.Level == LogMessageType.Warning), Is.True, "The user is told which item was given up on");
            }
        }
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveFailedFirstWalkStoresNoTokenAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/changes/startPageToken", StartToken("7"))
            .Json("/files/root?", "{\"id\":\"R\"}")
            .Sequence("'root' in parents", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "nope"));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        Assert.ThrowsAsync<Google.GoogleApiException>(() => CollectAsync(MyDrive(provider), true));

        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [MyDriveKey] = "" }), "A token taken before an incomplete listing is not stored");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveSharedDriveUnderTwoUsersKeepsTwoTokensAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("pageToken=1&", Changes("11", Change(DriveFile("A", "S"))))
            .Json("pageToken=2&", Changes("12"));
        FileChildren(handler, "A");
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [ChangeTrackingKeys.SharedDrive("u1", "S")] = StoredMap("1", "S"),
            [ChangeTrackingKeys.SharedDrive("u2", "S")] = StoredMap("2", "S"),
        });

        var sharedPath = D(Mount, "Shared Drives", "S");
        var first = await CollectAsync(new DriveTreeSourceEntry(provider, sharedPath, "u1", false, "Content", "S", "S", provider.ApiHelper.GetDriveService("u1")), true);
        var second = await CollectAsync(new DriveTreeSourceEntry(provider, D(Mount, "Users", "u2", "UserDrive", "Shared Drives", "S"), "u2", false, "Content", "S", "S", provider.ApiHelper.GetDriveService("u2")), true);

        Assert.That(FolderPaths(first).First(), Is.EqualTo(D(sharedPath, "S", "A")));
        Assert.That(second, Is.Empty);
        Assert.That(handler.Requests.Where(x => x.Contains("/changes?")).All(x => x.Contains("driveId=S") && x.Contains("includeItemsFromAllDrives=true")), Is.True);
        var state = provider.ExportChangeTrackingState();
        Assert.That(FolderMap.Deserialize(state[ChangeTrackingKeys.SharedDrive("u1", "S")])!.Token, Is.EqualTo("11"));
        Assert.That(FolderMap.Deserialize(state[ChangeTrackingKeys.SharedDrive("u2", "S")])!.Token, Is.EqualTo("12"));
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveRemovalReportedByAnotherTreeDoesNotDropItsPathsAsync()
    {
        // The My Drive feed reports activity on a shared drive folder as a removal of an id the
        // My Drive tree never held; the shared drive's own feed reports the folder changed in
        // place. The unchanged file below that folder must still be carried forward
        var handler = new FakeGoogleHandler()
            .Json("restrictToMyDrive=true", Changes("9", Removed("SF")))
            .Json("driveId=S", Changes("12", Change(DriveFolder("SF", "S"))));
        var provider = CreateProvider(handler, new Dictionary<string, string>
        {
            [MyDriveKey] = StoredMap("5", "R"),
            [ChangeTrackingKeys.SharedDrive(USER, "S")] = StoredMap("2", "S", ("SF", "S")),
        });

        var sharedPath = D(DrivePath, "Shared Drives", "S");
        var myDrive = await CollectAsync(MyDrive(provider), true);
        var shared = await CollectAsync(new DriveTreeSourceEntry(provider, sharedPath, USER, false, "Content", "S", "S", provider.ApiHelper.GetDriveService(USER)), true);

        Assert.That(myDrive, Is.Empty);
        Assert.That(FolderPaths(shared), Is.EqualTo(new[] { D(sharedPath, "S", "SF") }), "The shared drive re-lists the changed folder only");
        Assert.That(provider.WasPathEnumerated(F(sharedPath, "S", "SF", "X", "content")), Is.False, "The removal reported by the My Drive feed does not reach the shared drive's tree");
        Assert.That(provider.WasPathEnumerated(F(MyDrivePath, "SF", "X", "content")), Is.True, "Below My Drive the id is gone");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task DriveDeltaOffWalksChildrenAndStoresNothingAsync()
    {
        var handler = new FakeGoogleHandler().Json("'root' in parents", FileList(DriveFolder("F1", "R"))).Json("'F1' in parents", FileList());
        var provider = CreateProvider(handler, new Dictionary<string, string> { [MyDriveKey] = StoredMap("5", "R") }, o => o["google-disable-drive-delta"] = "true");

        var entries = await CollectAsync(MyDrive(provider), true);

        Assert.That(FolderPaths(entries), Is.EqualTo(new[] { D(MyDrivePath, "F1") }));
        Assert.That(provider.ExportChangeTrackingState(), Is.Empty);
        Assert.That(handler.CountRequests("/changes"), Is.EqualTo(0));
    }

    #endregion

    #region Calendar

    private static string Event(string id, string status = "confirmed", string? recurringEventId = null)
        => $"{{\"id\":\"{id}\",\"status\":\"{status}\",\"summary\":\"{id}\"{(recurringEventId == null ? "" : $",\"recurringEventId\":\"{recurringEventId}\"")}}}";
    private static string Events(string? syncToken, string? nextPageToken, params string[] events)
        => $"{{\"items\":[{string.Join(",", events)}]{(syncToken == null ? "" : $",\"nextSyncToken\":\"{syncToken}\"")}{(nextPageToken == null ? "" : $",\"nextPageToken\":\"{nextPageToken}\"")}}}";

    private static CalendarSourceEntry Calendar(SourceProvider provider)
        => new(provider, CalendarsPath, USER, new CalendarListEntry { Id = "cal1", Summary = "Calendar" }, provider.ApiHelper.GetCalendarService(USER), provider.ApiHelper.GetDriveService(USER), null);

    private static readonly string CalendarPath = D(CalendarsPath, "cal1");

    [Test]
    [Category("GoogleWorkspace")]
    public async Task CalendarInitialWalkStoresTheSyncTokenFromTheLastPageAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("/events?pageToken=p2", Events("cal-tok-1", null, Event("e2")))
            .Json("/events", Events(null, "p2", Event("e1")));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entries = await CollectAsync(Calendar(provider), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { F(CalendarPath, "metadata.json"), D(CalendarPath, "e1"), D(CalendarPath, "e2") }));
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [CalendarKey] = "cal-tok-1" }));
        Assert.That(handler.Requests.First(), Does.Not.Contain("syncToken"));
        Assert.That(provider.WasPathEnumerated(F(CalendarPath, "e9", "event.ics")), Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task CalendarIncrementalWalkListsChangesAndDropsCancelledEventsAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("syncToken=cal-tok-1", Events("cal-tok-2", null, Event("e2"), Event("e3", "cancelled"), Event("e4", "cancelled", "series")));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [CalendarKey] = "cal-tok-1" });

        var entries = await CollectAsync(Calendar(provider), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { F(CalendarPath, "metadata.json"), D(CalendarPath, "e2"), D(CalendarPath, "e4") }), "A deleted event is dropped, a cancelled instance of a series is kept as the full walk keeps it");
        Assert.That(provider.ExportChangeTrackingState()[CalendarKey], Is.EqualTo("cal-tok-2"));
        Assert.That(provider.IsIncrementallyEnumeratedFolder(CalendarPath), Is.True);
        Assert.That(provider.WasPathEnumerated(F(CalendarPath, "e1", "event.ics")), Is.False, "An unchanged event is carried forward");
        Assert.That(provider.WasPathEnumerated(F(CalendarPath, "e2", "event.ics")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(CalendarPath, "e3", "event.ics")), Is.True, "The deleted event is dropped");
        Assert.That(provider.WasPathEnumerated(F(CalendarPath, "metadata.json")), Is.True, "The metadata is listed every run");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task CalendarExpiredSyncTokenRestartsWithAFullWalkAsync()
    {
        var handler = new FakeGoogleHandler()
            .Sequence("syncToken=cal-tok-1", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Gone, "fullSyncRequired", "Sync token is no longer valid, a full sync is required."))
            .Json("/events", Events("cal-tok-3", null, Event("e1")));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [CalendarKey] = "cal-tok-1" });

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(Calendar(provider), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { F(CalendarPath, "metadata.json"), D(CalendarPath, "e1") }));
        Assert.That(messages.Any(x => x.Id == "CalendarSyncTokenExpired" && x.Level == LogMessageType.Information), Is.True);
        Assert.That(provider.ExportChangeTrackingState()[CalendarKey], Is.EqualTo("cal-tok-3"));
        Assert.That(provider.WasPathEnumerated(F(CalendarPath, "e9", "event.ics")), Is.True, "After a resync the calendar was walked in full");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(CalendarPath), Is.False);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task CalendarFailedAttachmentDownloadHoldsTheTokenBackAsync()
    {
        var handler = new FakeGoogleHandler()
            .Json("syncToken=cal-tok-1", Events("cal-tok-2", null, "{\"id\":\"e2\",\"status\":\"confirmed\",\"attachments\":[{\"fileId\":\"att\",\"title\":\"doc.pdf\"}]}"))
            .Sequence("/files/att?", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "nope"));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [CalendarKey] = "cal-tok-1" });

        var entries = await CollectAsync(Calendar(provider), true);
        var attachment = entries.Single(x => x.Path.EndsWith("doc.pdf"));
        Assert.ThrowsAsync<Google.GoogleApiException>(() => attachment.OpenRead(CancellationToken.None));

        Assert.That(provider.ExportChangeTrackingState()[CalendarKey], Is.EqualTo("cal-tok-1"));
    }

    #endregion

    #region Contacts

    private static string Person(string id, bool deleted = false)
        => $"{{\"resourceName\":\"people/{id}\",\"etag\":\"e\",\"names\":[{{\"displayName\":\"{id}\"}}]{(deleted ? ",\"metadata\":{\"deleted\":true}" : "")}}}";
    private static string Connections(string? syncToken, params string[] people)
        => $"{{\"connections\":[{string.Join(",", people)}]{(syncToken == null ? "" : $",\"nextSyncToken\":\"{syncToken}\"")}}}";

    private static ContactsFolderSourceEntry Contacts(SourceProvider provider)
        => new(provider, D(UserPath, "UserContacts"), USER, false, provider.ApiHelper.GetPeopleService(USER));

    [Test]
    [Category("GoogleWorkspace")]
    public async Task ContactsInitialWalkStoresTheSyncTokenAsync()
    {
        var handler = new FakeGoogleHandler().Json("/people/me/connections", Connections("ppl-tok-1", Person("c1"), Person("c2")));
        var provider = CreateProvider(handler, new Dictionary<string, string>());

        var entries = await CollectAsync(Contacts(provider), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(ContactsPath, "c1"), D(ContactsPath, "c2") }));
        Assert.That(provider.ExportChangeTrackingState(), Is.EqualTo(new Dictionary<string, string> { [ContactsKey] = "ppl-tok-1" }));
        Assert.That(handler.Requests.Single(), Does.Contain("requestSyncToken=true").And.Not.Contain("syncToken=ppl"));
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task ContactsIncrementalWalkListsChangesAndDropsDeletedAsync()
    {
        var handler = new FakeGoogleHandler().Json("syncToken=ppl-tok-1", Connections("ppl-tok-2", Person("c2"), Person("c3", deleted: true)));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [ContactsKey] = "ppl-tok-1" });

        var entries = await CollectAsync(Contacts(provider), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(ContactsPath, "c2") }));
        Assert.That(provider.ExportChangeTrackingState()[ContactsKey], Is.EqualTo("ppl-tok-2"));
        Assert.That(provider.WasPathEnumerated(F(ContactsPath, "c1", "contact.vcf")), Is.False, "An unchanged contact is carried forward");
        Assert.That(provider.WasPathEnumerated(F(ContactsPath, "c2", "contact.vcf")), Is.True);
        Assert.That(provider.WasPathEnumerated(F(ContactsPath, "c3", "contact.vcf")), Is.True, "The deleted contact is dropped");
        Assert.That(provider.IsIncrementallyEnumeratedFolder(ContactsPath), Is.True);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public async Task ContactsExpiredSyncTokenRestartsWithAFullWalkAsync()
    {
        var handler = new FakeGoogleHandler()
            .Sequence("syncToken=ppl-tok-1", () => GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.BadRequest, "EXPIRED_SYNC_TOKEN", "Sync token is expired."))
            .Json("/people/me/connections", Connections("ppl-tok-3", Person("c1")));
        var provider = CreateProvider(handler, new Dictionary<string, string> { [ContactsKey] = "ppl-tok-1" });

        var messages = CaptureLog(out var scope);
        List<ISourceProviderEntry> entries;
        using (scope)
            entries = await CollectAsync(Contacts(provider), false);

        Assert.That(entries.Select(x => x.Path), Is.EqualTo(new[] { D(ContactsPath, "c1") }));
        Assert.That(messages.Any(x => x.Id == "ContactsSyncTokenExpired" && x.Level == LogMessageType.Information), Is.True);
        Assert.That(provider.ExportChangeTrackingState()[ContactsKey], Is.EqualTo("ppl-tok-3"));
        Assert.That(provider.WasPathEnumerated(F(ContactsPath, "c9", "contact.vcf")), Is.True);
    }

    #endregion
}
