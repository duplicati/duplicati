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
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using Duplicati.Proprietary.GoogleWorkspace;
using NUnit.Framework;

namespace Duplicati.UnitTest.GoogleWorkspace;

/// <summary>
/// Runs real backups of the Google Workspace provider against a simulated tenant that changes
/// between runs, and checks after every run that the newest fileset is exactly the tenant.
/// The delta walks carry state between runs, so a path that is neither re-listed nor carried
/// forward, or listed twice, only shows up after the right sequence of changes; the random
/// mutations search for such sequences and a failing seed reproduces them.
/// </summary>
[NonParallelizable]
public class GoogleWorkspaceSimulationTests : BasicSetupHelper
{
    private static readonly DateTime EPOCH = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string FOLDER_MIME = "application/vnd.google-apps.folder";

    #region Tenant model

    private sealed class SimMessage
    {
        public string Id = "";
        public readonly HashSet<string> Labels = new(StringComparer.Ordinal);
        public byte[] Content = [];
        /// <summary>The sequence number of the last change, which the history reports</summary>
        public long Version;
        public long? DeletedAt;
    }

    private sealed class SimMailbox
    {
        public readonly List<string> Labels = ["INBOX", "SENT"];
        public readonly Dictionary<string, SimMessage> Messages = new(StringComparer.Ordinal);
        /// <summary>History ids below this are answered with not found</summary>
        public long MinValidHistory;

        public IEnumerable<SimMessage> LiveMessages => Messages.Values.Where(x => x.DeletedAt == null);
    }

    private sealed class SimItem
    {
        public string Id = "";
        public string Name = "";
        public string ParentId = "";
        public bool IsFolder;
        public byte[] Content = [];
        public long Version;
        public long? DeletedAt;
        /// <summary>Whether the removal is reported as the file being trashed rather than removed</summary>
        public bool ReportTrashed;
    }

    private sealed class SimDrive
    {
        /// <summary>The shared drive id, or <c>null</c> for a user's My Drive</summary>
        public string? Id;
        public string Name = "";
        public string RootId = "";
        public readonly Dictionary<string, SimItem> Items = new(StringComparer.Ordinal);
        /// <summary>Change tokens below this are rejected</summary>
        public long MinValidToken;

        public string LogName => Id ?? $"My Drive {RootId}";
        public IEnumerable<SimItem> LiveItems => Items.Values.Where(x => x.DeletedAt == null);
        public IEnumerable<SimItem> LiveChildren(string parentId) => LiveItems.Where(x => x.ParentId == parentId);
        public IEnumerable<SimItem> LiveFolders => LiveItems.Where(x => x.IsFolder);

        public IEnumerable<string> AncestorChain(SimItem item)
        {
            var chain = new List<string>();
            var current = item.ParentId;
            while (current != RootId)
            {
                chain.Add(current);
                current = Items[current].ParentId;
            }
            chain.Reverse();
            return chain;
        }

        public bool IsBelow(SimItem item, SimItem folder)
        {
            var current = item.ParentId;
            while (current != RootId)
            {
                if (current == folder.Id)
                    return true;
                current = Items[current].ParentId;
            }
            return false;
        }

        public IEnumerable<SimItem> LiveDescendants(SimItem folder) => LiveItems.Where(x => IsBelow(x, folder));
    }

    private sealed class SimEvent
    {
        public string Id = "";
        public string Summary = "";
        public long Version;
        public long? DeletedAt;
    }

    private sealed class SimCalendar
    {
        public string Id = "";
        public readonly Dictionary<string, SimEvent> Events = new(StringComparer.Ordinal);
        /// <summary>Sync tokens below this are answered with gone</summary>
        public long MinValidToken;

        public IEnumerable<SimEvent> LiveEvents => Events.Values.Where(x => x.DeletedAt == null);
    }

    private sealed class SimContact
    {
        public string Id = "";
        public string Name = "";
        public long Version;
        public long? DeletedAt;
    }

    private sealed class SimUser
    {
        public string Id = "";
        public string Email = "";
        public readonly SimMailbox Mailbox = new();
        public SimDrive MyDrive = new();
        public readonly List<SimCalendar> Calendars = [];
        public readonly Dictionary<string, SimContact> Contacts = new(StringComparer.Ordinal);
        /// <summary>Contact sync tokens below this are answered as expired</summary>
        public long ContactsMinValidToken;

        public IEnumerable<SimContact> LiveContacts => Contacts.Values.Where(x => x.DeletedAt == null);
    }

    private sealed class Tenant
    {
        public long Seq;
        public readonly List<SimUser> Users = [];
        public readonly List<SimDrive> SharedDrives = [];
        private int _ids;

        public long Next() => ++Seq;
        public string NewId(string prefix) => $"{prefix}{++_ids:D4}";

        public IEnumerable<SimDrive> AllDrives => Users.Select(u => u.MyDrive).Concat(SharedDrives);
        public IEnumerable<SimCalendar> AllCalendars => Users.SelectMany(u => u.Calendars);

        public static Tenant Create(Random rng)
        {
            var tenant = new Tenant();
            for (var u = 0; u < 2; u++)
            {
                var user = new SimUser { Id = tenant.NewId("uid-"), Email = $"user{u}@sim.test" };
                user.MyDrive = new SimDrive { RootId = tenant.NewId("root-"), Name = "My Drive" };
                user.Mailbox.Labels.Add(tenant.NewId("Label_"));
                for (var i = rng.Next(2, 5); i > 0; i--)
                    tenant.AddMessage(user.Mailbox, rng);

                var folder = tenant.AddFolder(user.MyDrive, user.MyDrive.RootId);
                tenant.AddFile(user.MyDrive, user.MyDrive.RootId, rng);
                tenant.AddFile(user.MyDrive, folder.Id, rng);
                var nested = tenant.AddFolder(user.MyDrive, folder.Id);
                tenant.AddFile(user.MyDrive, nested.Id, rng);

                var calendar = new SimCalendar { Id = $"cal-{user.Email}" };
                user.Calendars.Add(calendar);
                for (var i = rng.Next(1, 4); i > 0; i--)
                    tenant.AddEvent(calendar, rng);

                for (var i = rng.Next(1, 4); i > 0; i--)
                    tenant.AddContact(user, rng);

                tenant.Users.Add(user);
            }

            for (var d = 0; d < 2; d++)
            {
                var id = tenant.NewId("shared-");
                var drive = new SimDrive { Id = id, RootId = id, Name = $"Shared {d}" };
                var folder = tenant.AddFolder(drive, drive.RootId);
                tenant.AddFile(drive, drive.RootId, rng);
                tenant.AddFile(drive, folder.Id, rng);
                tenant.SharedDrives.Add(drive);
            }

            return tenant;
        }

        public SimMessage AddMessage(SimMailbox mailbox, Random rng)
        {
            var msg = new SimMessage { Id = NewId("msg-"), Version = Next() };
            foreach (var label in mailbox.Labels.Where(_ => rng.Next(2) == 0))
                msg.Labels.Add(label);
            if (msg.Labels.Count == 0)
                msg.Labels.Add(mailbox.Labels[rng.Next(mailbox.Labels.Count)]);
            msg.Content = Encoding.UTF8.GetBytes($"From: a@b\r\nSubject: {msg.Id}\r\n\r\n{RandomText(rng)}");
            mailbox.Messages[msg.Id] = msg;
            return msg;
        }

        public SimItem AddFile(SimDrive drive, string parentId, Random rng)
        {
            var item = new SimItem { Id = NewId("file-"), ParentId = parentId, Version = Next(), Content = Encoding.UTF8.GetBytes(RandomText(rng)) };
            item.Name = item.Id + ".txt";
            drive.Items[item.Id] = item;
            return item;
        }

        public SimItem AddFolder(SimDrive drive, string parentId)
        {
            var item = new SimItem { Id = NewId("folder-"), ParentId = parentId, IsFolder = true, Version = Next() };
            item.Name = item.Id;
            drive.Items[item.Id] = item;
            return item;
        }

        public SimEvent AddEvent(SimCalendar calendar, Random rng)
        {
            var evt = new SimEvent { Id = NewId("evt-"), Version = Next(), Summary = RandomText(rng) };
            calendar.Events[evt.Id] = evt;
            return evt;
        }

        public SimContact AddContact(SimUser user, Random rng)
        {
            var contact = new SimContact { Id = NewId("c"), Version = Next(), Name = RandomText(rng) };
            user.Contacts[contact.Id] = contact;
            return contact;
        }

        public static string RandomText(Random rng)
        {
            var words = new[] { "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel" };
            return string.Join(" ", Enumerable.Range(0, rng.Next(2, 8)).Select(_ => words[rng.Next(words.Length)]));
        }
    }

    #endregion

    #region Mutations

    private sealed class Mutator(Tenant tenant, Random rng)
    {
        /// <summary>Whether a change in this run makes a walk fail, so the fileset cannot be expected to match until a clean run</summary>
        public bool HardFault;
        public readonly List<string> IgnoredWarnings = [];
        public readonly List<string> Log = [];

        public void ApplyRandomChanges(SimulatedGoogle google)
        {
            HardFault = false;
            IgnoredWarnings.Clear();
            Log.Clear();

            var count = rng.Next(1, 9);
            for (var i = 0; i < count; i++)
                ApplyOne(google);
        }

        private T Pick<T>(IList<T> items) => items[rng.Next(items.Count)];

        private void ApplyOne(SimulatedGoogle google)
        {
            var users = tenant.Users;
            var drives = tenant.AllDrives.ToList();
            var calendars = tenant.AllCalendars.ToList();
            switch (rng.Next(100))
            {
                case < 8:
                    {
                        var user = Pick(users);
                        var msg = tenant.AddMessage(user.Mailbox, rng);
                        Log.Add($"add message {msg.Id} [{string.Join(",", msg.Labels)}] for {user.Email}");
                        break;
                    }
                case < 14:
                    {
                        var candidates = users.SelectMany(u => u.Mailbox.LiveMessages.Select(m => (u, m))).ToList();
                        if (candidates.Count == 0) break;
                        var (user, msg) = Pick(candidates);
                        msg.Version = tenant.Next();
                        msg.Content = Encoding.UTF8.GetBytes($"From: a@b\r\nSubject: {msg.Id} (edited)\r\n\r\n{Tenant.RandomText(rng)}");
                        Log.Add($"modify message {msg.Id} of {user.Email}");
                        break;
                    }
                case < 20:
                    {
                        var candidates = users.SelectMany(u => u.Mailbox.LiveMessages.Select(m => (u, m))).ToList();
                        if (candidates.Count == 0) break;
                        var (user, msg) = Pick(candidates);
                        msg.DeletedAt = tenant.Next();
                        msg.Version = msg.DeletedAt.Value;
                        Log.Add($"delete message {msg.Id} of {user.Email}");
                        break;
                    }
                case < 28:
                    {
                        // Toggle a label on a message; a message always keeps at least one label
                        var candidates = users.SelectMany(u => u.Mailbox.LiveMessages.Select(m => (u, m))).ToList();
                        if (candidates.Count == 0) break;
                        var (user, msg) = Pick(candidates);
                        var label = Pick(user.Mailbox.Labels);
                        if (msg.Labels.Contains(label) && msg.Labels.Count > 1)
                            msg.Labels.Remove(label);
                        else
                            msg.Labels.Add(label);
                        msg.Version = tenant.Next();
                        Log.Add($"relabel message {msg.Id} of {user.Email} to [{string.Join(",", msg.Labels)}]");
                        break;
                    }
                case < 30:
                    {
                        var user = Pick(users);
                        var label = tenant.NewId("Label_");
                        user.Mailbox.Labels.Add(label);
                        Log.Add($"add label {label} for {user.Email}");
                        break;
                    }
                case < 32:
                    {
                        // Deleting a label takes it off every message, which the history reports
                        var candidates = users.SelectMany(u => u.Mailbox.Labels.Where(l => l.StartsWith("Label_")).Select(l => (u, l))).ToList();
                        if (candidates.Count == 0) break;
                        var (user, label) = Pick(candidates);
                        user.Mailbox.Labels.Remove(label);
                        foreach (var msg in user.Mailbox.LiveMessages.Where(m => m.Labels.Contains(label)).ToList())
                        {
                            msg.Labels.Remove(label);
                            if (msg.Labels.Count == 0)
                                msg.Labels.Add("INBOX");
                            msg.Version = tenant.Next();
                        }
                        Log.Add($"delete label {label} of {user.Email}");
                        break;
                    }
                case < 42:
                    {
                        var drive = Pick(drives);
                        var parent = Pick(drive.LiveFolders.Select(x => x.Id).Append(drive.RootId).ToList());
                        var file = tenant.AddFile(drive, parent, rng);
                        Log.Add($"add file {file.Id} below {parent} in {drive.LogName}");
                        break;
                    }
                case < 50:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Where(x => !x.IsFolder).Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, file) = Pick(candidates);
                        file.Version = tenant.Next();
                        file.Content = Encoding.UTF8.GetBytes(Tenant.RandomText(rng));
                        Log.Add($"modify file {file.Id} in {drive.LogName}");
                        break;
                    }
                case < 58:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Where(x => !x.IsFolder).Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, file) = Pick(candidates);
                        file.DeletedAt = tenant.Next();
                        file.ReportTrashed = rng.Next(2) == 0;
                        Log.Add($"{(file.ReportTrashed ? "trash" : "delete")} file {file.Id} in {drive.LogName}");
                        break;
                    }
                case < 63:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Where(x => !x.IsFolder).Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, file) = Pick(candidates);
                        var targets = drive.LiveFolders.Select(x => x.Id).Append(drive.RootId).Where(x => x != file.ParentId).ToList();
                        if (targets.Count == 0) break;
                        var target = Pick(targets);
                        file.ParentId = target;
                        file.Version = tenant.Next();
                        Log.Add($"move file {file.Id} to {target} in {drive.LogName}");
                        break;
                    }
                case < 68:
                    {
                        var drive = Pick(drives);
                        var parent = Pick(drive.LiveFolders.Select(x => x.Id).Append(drive.RootId).ToList());
                        var folder = tenant.AddFolder(drive, parent);
                        if (rng.Next(2) == 0)
                            tenant.AddFile(drive, folder.Id, rng);
                        Log.Add($"add folder {folder.Id} below {parent} in {drive.LogName}");
                        break;
                    }
                case < 73:
                    {
                        var candidates = drives.SelectMany(d => d.LiveFolders.Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, folder) = Pick(candidates);
                        var targets = drive.LiveFolders.Where(x => x.Id != folder.Id && !drive.IsBelow(x, folder)).Select(x => x.Id).Append(drive.RootId).Where(x => x != folder.ParentId).ToList();
                        if (targets.Count == 0) break;
                        var target = Pick(targets);
                        folder.ParentId = target;
                        folder.Version = tenant.Next();
                        Log.Add($"move folder {folder.Id} to {target} in {drive.LogName}");
                        break;
                    }
                case < 76:
                    {
                        var candidates = drives.SelectMany(d => d.LiveFolders.Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, folder) = Pick(candidates);
                        folder.Name += " renamed";
                        folder.Version = tenant.Next();
                        Log.Add($"rename folder {folder.Id} in {drive.LogName}");
                        break;
                    }
                case < 80:
                    {
                        var candidates = drives.SelectMany(d => d.LiveFolders.Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, folder) = Pick(candidates);
                        var report = rng.Next(2) == 0;
                        foreach (var item in drive.LiveDescendants(folder).ToList())
                        {
                            item.DeletedAt = tenant.Next();
                            // Drive does not always report the contents of a removed folder
                            if (!report)
                                item.DeletedAt = -item.DeletedAt;
                        }
                        folder.DeletedAt = tenant.Next();
                        Log.Add($"delete folder {folder.Id} in {drive.LogName} ({(report ? "with" : "without")} tombstones for its contents)");
                        break;
                    }
                case < 84:
                    {
                        var calendar = Pick(calendars);
                        var evt = tenant.AddEvent(calendar, rng);
                        Log.Add($"add event {evt.Id} in {calendar.Id}");
                        break;
                    }
                case < 87:
                    {
                        var candidates = calendars.SelectMany(c => c.LiveEvents.Select(e => (c, e))).ToList();
                        if (candidates.Count == 0) break;
                        var (calendar, evt) = Pick(candidates);
                        evt.Version = tenant.Next();
                        evt.Summary += " (edited)";
                        Log.Add($"modify event {evt.Id} in {calendar.Id}");
                        break;
                    }
                case < 90:
                    {
                        var candidates = calendars.SelectMany(c => c.LiveEvents.Select(e => (c, e))).ToList();
                        if (candidates.Count == 0) break;
                        var (calendar, evt) = Pick(candidates);
                        evt.DeletedAt = tenant.Next();
                        Log.Add($"delete event {evt.Id} in {calendar.Id}");
                        break;
                    }
                case < 93:
                    {
                        var user = Pick(users);
                        var contact = tenant.AddContact(user, rng);
                        Log.Add($"add contact {contact.Id} for {user.Email}");
                        break;
                    }
                case < 95:
                    {
                        var candidates = users.SelectMany(u => u.LiveContacts.Select(c => (u, c))).ToList();
                        if (candidates.Count == 0) break;
                        var (user, contact) = Pick(candidates);
                        if (rng.Next(2) == 0)
                        {
                            contact.DeletedAt = tenant.Next();
                            Log.Add($"delete contact {contact.Id} of {user.Email}");
                        }
                        else
                        {
                            contact.Version = tenant.Next();
                            contact.Name += " (edited)";
                            Log.Add($"modify contact {contact.Id} of {user.Email}");
                        }
                        break;
                    }
                case < 97:
                    {
                        // Tokens issued so far become stale; the one issued by this run's walk stays
                        // valid. The provider reports the rejection at information level and lists
                        // in full, so no warning is expected
                        switch (rng.Next(4))
                        {
                            case 0:
                                {
                                    var user = Pick(users);
                                    user.Mailbox.MinValidHistory = tenant.Next();
                                    Log.Add($"expire the history id of {user.Email}");
                                    break;
                                }
                            case 1:
                                {
                                    var drive = Pick(drives);
                                    drive.MinValidToken = tenant.Next();
                                    Log.Add($"reject the change token of {drive.LogName}");
                                    break;
                                }
                            case 2:
                                {
                                    var calendar = Pick(calendars);
                                    calendar.MinValidToken = tenant.Next();
                                    Log.Add($"expire the sync token of {calendar.Id}");
                                    break;
                                }
                            default:
                                {
                                    var user = Pick(users);
                                    user.ContactsMinValidToken = tenant.Next();
                                    Log.Add($"expire the contacts sync token of {user.Email}");
                                    break;
                                }
                        }
                        break;
                    }
                case < 99:
                    {
                        google.ThrottleNext = true;
                        Log.Add("throttle the next request");
                        break;
                    }
                default:
                    {
                        google.FailNextContaining = rng.Next(3) switch { 0 => "/history", 1 => "/changes?", _ => "/events" };
                        HardFault = true;
                        IgnoredWarnings.Add("PathProcessingErrorEnumerate");
                        Log.Add($"fail the next request to {google.FailNextContaining}");
                        break;
                    }
            }
        }
    }

    #endregion

    #region Simulated Google

    /// <summary>
    /// Answers the Google API requests of the backup from the tenant. Listings are paged in
    /// small pages so that paging is exercised, and the incremental endpoints answer from the
    /// sequence numbers of the changes: a token is the sequence number the walk is as of.
    /// </summary>
    private sealed class SimulatedGoogle(Tenant tenant) : HttpMessageHandler
    {
        private const int PAGE_SIZE = 2;
        private readonly object _lock = new();

        public readonly List<string> Requests = [];
        public readonly List<string> Unhandled = [];
        public bool ThrottleNext;
        public string? FailNextContaining;
        /// <summary>Called with the decoded url of every request before it is answered, so a test can act while the backup runs</summary>
        public Action<string>? OnRequest;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var decoded = Uri.UnescapeDataString(uri.AbsoluteUri);
            var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var user = request.Headers.TryGetValues(APIHelper.TEST_USER_HEADER, out var values) ? values.FirstOrDefault() : null;
            OnRequest?.Invoke(decoded);
            HttpResponseMessage response;
            lock (_lock)
            {
                Requests.Add(decoded);

                if (ThrottleNext)
                {
                    ThrottleNext = false;
                    response = GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.TooManyRequests, "rateLimitExceeded", "Rate limit exceeded");
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                }
                else if (FailNextContaining != null && decoded.Contains(FailNextContaining, StringComparison.Ordinal))
                {
                    FailNextContaining = null;
                    response = GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Forbidden, "forbidden", "Simulated permission failure");
                }
                else
                {
                    try
                    {
                        response = Route(uri, decoded, body, user) ?? Unknown(decoded);
                    }
                    catch (Exception ex)
                    {
                        Unhandled.Add($"{decoded}: {ex}");
                        response = GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Conflict, "simulationFailure", "The simulation failed");
                    }
                }
            }

            response.RequestMessage = request;
            return response;
        }

        private HttpResponseMessage Unknown(string url)
        {
            Unhandled.Add(url);
            return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", "Not simulated: " + url);
        }

        private static HttpResponseMessage Json(object body)
            => GoogleWorkspaceTestSupport.Json(HttpStatusCode.OK, JsonSerializer.Serialize(body));

        private static HttpResponseMessage Bytes(byte[] body)
            => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

        private static string Stamp(long version) => EPOCH.AddSeconds(Math.Abs(version)).ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'fff'Z'", System.Globalization.CultureInfo.InvariantCulture);

        private static string? Query(Uri uri, string name)
            => HttpUtility.ParseQueryString(uri.Query).Get(name);

        private static readonly Regex DirectoryUsersRx = new(@"^/admin/directory/v1/users$");
        private static readonly Regex DirectoryUserRx = new(@"^/admin/directory/v1/users/([^/]+)$");
        private static readonly Regex GmailRx = new(@"^/gmail/v1/users/([^/]+)/(labels|profile|history|messages|settings/[a-zA-Z]+)$");
        private static readonly Regex GmailMessageRx = new(@"^/gmail/v1/users/([^/]+)/messages/([^/]+)$");
        private static readonly Regex BatchRx = new(@"^/batch");
        private static readonly Regex DrivesRx = new(@"^/drive/v3/drives$");
        private static readonly Regex FilesRx = new(@"^/drive/v3/files$");
        private static readonly Regex FileRx = new(@"^/drive/v3/files/([^/]+)$");
        private static readonly Regex FileChildRx = new(@"^/drive/v3/files/([^/]+)/(permissions|comments|revisions)$");
        private static readonly Regex StartTokenRx = new(@"^/drive/v3/changes/startPageToken$");
        private static readonly Regex ChangesRx = new(@"^/drive/v3/changes$");
        private static readonly Regex CalendarListRx = new(@"^/calendar/v3/users/me/calendarList$");
        private static readonly Regex EventsRx = new(@"^/calendar/v3/calendars/([^/]+)/events$");
        private static readonly Regex ConnectionsRx = new(@"^/v1/people/me/connections$");
        private static readonly Regex ContactGroupsRx = new(@"^/v1/contactGroups$");

        private HttpResponseMessage? Route(Uri uri, string url, string? body, string? user)
        {
            var path = Uri.UnescapeDataString(uri.AbsolutePath);
            Match m;

            if (DirectoryUsersRx.IsMatch(path))
                return Json(new { users = tenant.Users.Select(u => new { id = u.Id, primaryEmail = u.Email, suspended = false, archived = false }) });
            if ((m = DirectoryUserRx.Match(path)).Success)
            {
                var u = FindUser(m.Groups[1].Value);
                return Json(new { id = u.Id, primaryEmail = u.Email, name = new { fullName = u.Email } });
            }

            if (BatchRx.IsMatch(path))
                return Batch(body);

            if ((m = GmailMessageRx.Match(path)).Success)
            {
                var mailbox = FindUser(m.Groups[1].Value).Mailbox;
                if (!mailbox.Messages.TryGetValue(m.Groups[2].Value, out var msg) || msg.DeletedAt != null)
                    return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", "Requested entity was not found.");
                return Json(new { id = msg.Id, threadId = "t-" + msg.Id, labelIds = msg.Labels.OrderBy(x => x).ToList(), raw = GoogleWorkspaceTestSupport.Base64Url(msg.Content) });
            }

            if ((m = GmailRx.Match(path)).Success)
            {
                var mailbox = FindUser(m.Groups[1].Value).Mailbox;
                switch (m.Groups[2].Value)
                {
                    case "labels":
                        return Json(new { labels = mailbox.Labels.Select(l => new { id = l, name = l, type = l.StartsWith("Label_") ? "user" : "system" }) });
                    case "profile":
                        return Json(new { emailAddress = m.Groups[1].Value, historyId = tenant.Seq.ToString(), messagesTotal = mailbox.LiveMessages.Count() });
                    case "history":
                        return History(mailbox, uri);
                    case "messages":
                        return MessageList(mailbox, uri);
                    case "settings/filters":
                        return Json(new { filter = Array.Empty<object>() });
                    case "settings/forwardingAddresses":
                        return Json(new { forwardingAddresses = Array.Empty<object>() });
                    case "settings/autoForwarding":
                        return Json(new { enabled = false });
                    case "settings/vacation":
                        return Json(new { enableAutoReply = false });
                    case "settings/sendAs":
                        return Json(new { sendAs = Array.Empty<object>() });
                }
            }

            if (DrivesRx.IsMatch(path))
                return Json(new { drives = tenant.SharedDrives.Select(d => new { id = d.Id, name = d.Name, kind = "drive#drive" }) });
            if (FilesRx.IsMatch(path))
                return FileList(uri, user);
            if (StartTokenRx.IsMatch(path))
            {
                var drive = FindDrive(Query(uri, "driveId"), user);
                return drive.Id != null || user != null ? Json(new { startPageToken = tenant.Seq.ToString() }) : Unknown(url);
            }
            if (ChangesRx.IsMatch(path))
                return Changes(FindDrive(Query(uri, "driveId"), user), uri);
            if ((m = FileChildRx.Match(path)).Success)
                return m.Groups[2].Value switch
                {
                    "permissions" => Json(new { permissions = Array.Empty<object>() }),
                    "comments" => Json(new { comments = Array.Empty<object>() }),
                    _ => Json(new { revisions = Array.Empty<object>() }),
                };
            if ((m = FileRx.Match(path)).Success)
                return FileGet(m.Groups[1].Value, uri, user);

            if (CalendarListRx.IsMatch(path))
                return Json(new { items = FindUserOrThrow(user).Calendars.Select(c => new { id = c.Id, summary = c.Id }) });
            if ((m = EventsRx.Match(path)).Success)
                return Events(tenant.AllCalendars.First(c => c.Id == m.Groups[1].Value), uri);

            if (ConnectionsRx.IsMatch(path))
                return Connections(FindUserOrThrow(user), uri);
            if (ContactGroupsRx.IsMatch(path))
                return Json(new { contactGroups = Array.Empty<object>() });

            return null;
        }

        private SimUser FindUser(string email) => tenant.Users.First(u => u.Email == email);
        private SimUser FindUserOrThrow(string? email) => FindUser(email ?? throw new InvalidOperationException("A user-scoped request without a user"));

        private SimDrive FindDrive(string? driveId, string? user)
            => driveId != null ? tenant.SharedDrives.First(d => d.Id == driveId) : FindUserOrThrow(user).MyDrive;

        /// <summary>Finds the drive holding a folder, where <c>root</c> is the user's My Drive</summary>
        private SimDrive FindDriveOfFolder(string folderId, string? user)
        {
            if (folderId == "root")
                return FindUserOrThrow(user).MyDrive;
            return tenant.AllDrives.First(d => d.RootId == folderId || d.Items.ContainsKey(folderId));
        }

        private static (long From, long AsOf, int Page) ParseToken(string? token, long currentSeq)
        {
            if (string.IsNullOrEmpty(token))
                return (-1, currentSeq, 0);
            var parts = token.Split(':');
            return parts.Length == 1 ? (long.Parse(parts[0]), currentSeq, 0) : (long.Parse(parts[0]), long.Parse(parts[1]), int.Parse(parts[2]));
        }

        private static string NextToken((long From, long AsOf, int Page) state) => $"{state.From}:{state.AsOf}:{state.Page + 1}";

        private static Dictionary<string, object?> ItemJson(SimDrive drive, SimItem item) => new()
        {
            ["id"] = item.Id,
            ["name"] = item.Name,
            ["mimeType"] = item.IsFolder ? FOLDER_MIME : "text/plain",
            ["parents"] = new[] { item.ParentId },
            ["createdTime"] = Stamp(1),
            ["modifiedTime"] = Stamp(item.Version),
            ["size"] = item.IsFolder ? null : item.Content.Length.ToString(),
            ["driveId"] = drive.Id,
            ["trashed"] = false,
        };

        private HttpResponseMessage Batch(string? body)
        {
            var parts = new List<(HttpStatusCode, string)>();
            foreach (Match m in Regex.Matches(body ?? "", @"/gmail/v1/users/([^/]+)/messages/([^?\s]+)"))
            {
                var mailbox = FindUser(Uri.UnescapeDataString(m.Groups[1].Value)).Mailbox;
                if (!mailbox.Messages.TryGetValue(m.Groups[2].Value, out var msg) || msg.DeletedAt != null)
                    parts.Add((HttpStatusCode.NotFound, GoogleWorkspaceTestSupport.NOT_FOUND_PART));
                else
                    parts.Add((HttpStatusCode.OK, JsonSerializer.Serialize(new { id = msg.Id, threadId = "t-" + msg.Id, labelIds = msg.Labels.OrderBy(x => x).ToList(), sizeEstimate = msg.Content.Length, internalDate = "1700000000000" })));
            }
            return GoogleWorkspaceTestSupport.BatchResponse(parts);
        }

        private HttpResponseMessage MessageList(SimMailbox mailbox, Uri uri)
        {
            var label = Query(uri, "labelIds") ?? "";
            var all = mailbox.LiveMessages.Where(x => x.Labels.Contains(label)).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var page = int.Parse(Query(uri, "pageToken") ?? "0");
            var items = all.Skip(page * PAGE_SIZE).Take(PAGE_SIZE).Select(x => new { id = x.Id, threadId = "t-" + x.Id }).ToList();
            var result = new Dictionary<string, object?> { ["messages"] = items };
            if ((page + 1) * PAGE_SIZE < all.Count)
                result["nextPageToken"] = (page + 1).ToString();
            return Json(result);
        }

        private HttpResponseMessage History(SimMailbox mailbox, Uri uri)
        {
            var start = long.Parse(Query(uri, "startHistoryId") ?? "0");
            if (start < mailbox.MinValidHistory)
                return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", "Requested entity was not found.");

            var state = ParseToken(Query(uri, "pageToken"), tenant.Seq);
            var asOf = state.Page == 0 ? tenant.Seq : state.AsOf;
            var touched = mailbox.Messages.Values.Where(x => x.Version > start && x.Version <= asOf).OrderBy(x => x.Version).ToList();
            var records = touched.Select(x => x.DeletedAt != null
                ? new Dictionary<string, object?> { ["id"] = x.Version.ToString(), ["messagesDeleted"] = new[] { new { message = new { id = x.Id } } } }
                : new Dictionary<string, object?> { ["id"] = x.Version.ToString(), ["labelsAdded"] = new[] { new { message = new { id = x.Id }, labelIds = x.Labels.ToList() } } }).ToList();

            var pageItems = records.Skip(state.Page * PAGE_SIZE).Take(PAGE_SIZE).ToList();
            var result = new Dictionary<string, object?> { ["history"] = pageItems, ["historyId"] = asOf.ToString() };
            if ((state.Page + 1) * PAGE_SIZE < records.Count)
                result["nextPageToken"] = NextToken((start, asOf, state.Page));
            return Json(result);
        }

        private HttpResponseMessage FileList(Uri uri, string? user)
        {
            var q = Query(uri, "q") ?? "";
            var m = Regex.Match(q, @"'([^']+)' in parents");
            if (!m.Success)
                return Json(new { files = Array.Empty<object>() });

            var drive = FindDriveOfFolder(m.Groups[1].Value, user);
            var parentId = m.Groups[1].Value == "root" ? drive.RootId : m.Groups[1].Value;
            var all = drive.LiveChildren(parentId).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            var page = int.Parse(Query(uri, "pageToken") ?? "0");
            var result = new Dictionary<string, object?> { ["files"] = all.Skip(page * PAGE_SIZE).Take(PAGE_SIZE).Select(x => ItemJson(drive, x)).ToList() };
            if ((page + 1) * PAGE_SIZE < all.Count)
                result["nextPageToken"] = (page + 1).ToString();
            return Json(result);
        }

        private HttpResponseMessage FileGet(string id, Uri uri, string? user)
        {
            if (id == "root")
                return Json(new { id = FindUserOrThrow(user).MyDrive.RootId });

            var drive = tenant.AllDrives.FirstOrDefault(d => d.Items.ContainsKey(id));
            if (drive == null || drive.Items[id].DeletedAt != null)
                return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.NotFound, "notFound", "File not found: " + id);

            var item = drive.Items[id];
            if (Query(uri, "alt") == "media")
                return Bytes(item.Content);
            return Json(ItemJson(drive, item));
        }

        private HttpResponseMessage Changes(SimDrive drive, Uri uri)
        {
            var state = ParseToken(Query(uri, "pageToken"), tenant.Seq);
            if (state.From < drive.MinValidToken)
                return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.BadRequest, "invalid", "Invalid Value");

            var changes = new List<Dictionary<string, object?>>();
            foreach (var item in drive.Items.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (item.DeletedAt != null)
                {
                    // A negative deletion is one Drive does not report, as the contents of a removed folder
                    if (item.DeletedAt > state.From && item.DeletedAt <= state.AsOf)
                        changes.Add(item.ReportTrashed
                            ? new Dictionary<string, object?> { ["fileId"] = item.Id, ["removed"] = false, ["changeType"] = "file", ["file"] = new Dictionary<string, object?>(ItemJson(drive, item)) { ["trashed"] = true } }
                            : new Dictionary<string, object?> { ["fileId"] = item.Id, ["removed"] = true, ["changeType"] = "file" });
                }
                else if (item.Version > state.From && item.Version <= state.AsOf)
                    changes.Add(new Dictionary<string, object?> { ["fileId"] = item.Id, ["removed"] = false, ["changeType"] = "file", ["file"] = ItemJson(drive, item) });
            }

            // With restrictToMyDrive, Drive reports any activity on an item outside My Drive, such
            // as a change in a shared drive, as a removal from the My Drive view. The ids of such
            // items are never part of the My Drive tree, and must not affect the shared drive's own walk
            if (drive.Id == null)
                foreach (var item in tenant.SharedDrives.SelectMany(d => d.Items.Values).OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                    var at = item.DeletedAt.HasValue ? Math.Abs(item.DeletedAt.Value) : item.Version;
                    if (at > state.From && at <= state.AsOf)
                        changes.Add(new Dictionary<string, object?> { ["fileId"] = item.Id, ["removed"] = true, ["changeType"] = "file" });
                }

            var result = new Dictionary<string, object?> { ["changes"] = changes.Skip(state.Page * PAGE_SIZE).Take(PAGE_SIZE).ToList() };
            if ((state.Page + 1) * PAGE_SIZE < changes.Count)
                result["nextPageToken"] = NextToken(state);
            else
                result["newStartPageToken"] = state.AsOf.ToString();
            return Json(result);
        }

        private HttpResponseMessage Events(SimCalendar calendar, Uri uri)
        {
            var sync = Query(uri, "syncToken");
            var state = ParseToken(Query(uri, "pageToken") ?? sync, tenant.Seq);
            if (sync != null && state.From < calendar.MinValidToken)
                return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.Gone, "fullSyncRequired", "Sync token is no longer valid, a full sync is required.");

            var events = calendar.Events.Values.OrderBy(x => x.Id, StringComparer.Ordinal)
                .Where(x => state.From < 0 ? x.DeletedAt == null : (x.DeletedAt ?? x.Version) > state.From && (x.DeletedAt ?? x.Version) <= state.AsOf)
                .Select(x => new Dictionary<string, object?>
                {
                    ["id"] = x.Id,
                    ["status"] = x.DeletedAt != null ? "cancelled" : "confirmed",
                    ["summary"] = x.Summary,
                    ["updated"] = Stamp(x.Version),
                    ["start"] = new { dateTime = Stamp(100) },
                    ["end"] = new { dateTime = Stamp(200) },
                }).ToList();

            var result = new Dictionary<string, object?> { ["items"] = events.Skip(state.Page * PAGE_SIZE).Take(PAGE_SIZE).ToList() };
            if ((state.Page + 1) * PAGE_SIZE < events.Count)
                result["nextPageToken"] = NextToken(state);
            else
                result["nextSyncToken"] = state.AsOf.ToString();
            return Json(result);
        }

        private HttpResponseMessage Connections(SimUser user, Uri uri)
        {
            var sync = Query(uri, "syncToken");
            var state = ParseToken(Query(uri, "pageToken") ?? sync, tenant.Seq);
            if (sync != null && state.From < user.ContactsMinValidToken)
                return GoogleWorkspaceTestSupport.GoogleError(HttpStatusCode.BadRequest, "EXPIRED_SYNC_TOKEN", "Sync token is expired. Clear local cache and retry call without the sync token.");

            var people = user.Contacts.Values.OrderBy(x => x.Id, StringComparer.Ordinal)
                .Where(x => state.From < 0 ? x.DeletedAt == null : (x.DeletedAt ?? x.Version) > state.From && (x.DeletedAt ?? x.Version) <= state.AsOf)
                .Select(x => new Dictionary<string, object?>
                {
                    ["resourceName"] = "people/" + x.Id,
                    ["etag"] = x.Version.ToString(),
                    ["names"] = new[] { new { displayName = x.Name, givenName = x.Name } },
                    ["metadata"] = x.DeletedAt != null ? new { deleted = true } : null,
                }).ToList();

            var result = new Dictionary<string, object?> { ["connections"] = people.Skip(state.Page * PAGE_SIZE).Take(PAGE_SIZE).ToList() };
            if ((state.Page + 1) * PAGE_SIZE < people.Count)
                result["nextPageToken"] = NextToken(state);
            else if (Query(uri, "requestSyncToken") == "true" || sync != null)
                result["nextSyncToken"] = state.AsOf.ToString();
            return Json(result);
        }
    }

    #endregion

    #region Expected content

    private static string Mount => Util.AppendDirSeparator(Path.GetFullPath(Path.Combine(BASEFOLDER, "gws")));

    private static (HashSet<string> Paths, Dictionary<string, byte[]> Contents) Expected(Tenant tenant)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string D(params string[] parts) => Util.AppendDirSeparator(Path.Combine(parts));
        string F(params string[] parts) => Path.Combine(parts);

        void AddDriveTree(SimDrive drive, string treePath)
        {
            paths.Add(treePath);
            foreach (var item in drive.LiveItems)
            {
                var parent = drive.AncestorChain(item).Aggregate(treePath, (p, id) => D(p, id));
                var itemPath = D(parent, item.Id);
                paths.Add(itemPath);
                if (item.IsFolder)
                    continue;

                paths.Add(F(itemPath, "metadata.json"));
                paths.Add(F(itemPath, "permissions.json"));
                paths.Add(F(itemPath, "content"));
                paths.Add(F(itemPath, "comments.json"));
                paths.Add(D(itemPath, "Revisions"));
                contents[F(itemPath, "content")] = item.Content;
            }
        }

        void AddSharedDrives(string parentPath)
        {
            paths.Add(parentPath);
            foreach (var drive in tenant.SharedDrives)
            {
                var drivePath = D(parentPath, drive.Id!);
                paths.Add(drivePath);
                paths.Add(F(drivePath, "metadata.json"));
                paths.Add(F(drivePath, "permissions.json"));
                AddDriveTree(drive, D(drivePath, drive.Id!));
            }
        }

        paths.Add(Mount);
        paths.Add(F(Mount, "google-workspace-backup-description.json"));
        paths.Add(D(Mount, "Users"));

        foreach (var user in tenant.Users)
        {
            var userPath = D(Mount, "Users", user.Email);
            paths.Add(userPath);
            paths.Add(F(userPath, "profile.json"));

            var gmail = D(userPath, "UserGmail");
            paths.Add(gmail);
            foreach (var label in user.Mailbox.Labels)
            {
                paths.Add(D(gmail, label));
                foreach (var msg in user.Mailbox.LiveMessages.Where(m => m.Labels.Contains(label)))
                {
                    paths.Add(F(gmail, label, msg.Id + ".eml"));
                    contents[F(gmail, label, msg.Id + ".eml")] = msg.Content;
                }
            }
            paths.Add(D(gmail, "Settings"));
            foreach (var file in new[] { "filters.json", "forwarding.json", "vacation.json", "signatures.json" })
                paths.Add(F(gmail, "Settings", file));

            var drive = D(userPath, "UserDrive");
            paths.Add(drive);
            AddDriveTree(user.MyDrive, D(drive, "root"));
            AddSharedDrives(D(drive, "Shared Drives"));

            var calendars = D(userPath, "UserCalendar");
            paths.Add(calendars);
            foreach (var calendar in user.Calendars)
            {
                var calendarPath = D(calendars, calendar.Id);
                paths.Add(calendarPath);
                paths.Add(F(calendarPath, "metadata.json"));
                foreach (var evt in calendar.LiveEvents)
                {
                    paths.Add(D(calendarPath, evt.Id));
                    paths.Add(F(calendarPath, evt.Id, "event.ics"));
                    paths.Add(F(calendarPath, evt.Id, "content.json"));
                }
            }

            var contacts = D(userPath, "UserContacts");
            paths.Add(contacts);
            paths.Add(D(contacts, "Entries"));
            paths.Add(D(contacts, "Groups"));
            foreach (var contact in user.LiveContacts)
            {
                paths.Add(D(contacts, "Entries", contact.Id));
                paths.Add(F(contacts, "Entries", contact.Id, "contact.vcf"));
                paths.Add(F(contacts, "Entries", contact.Id, "content.json"));
            }
        }

        AddSharedDrives(D(Mount, "Shared Drives"));
        return (paths, contents);
    }

    #endregion

    private Dictionary<string, string> Prepare()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new SourceProvider());
        return new Dictionary<string, string>(TestOptions)
        {
            ["no-encryption"] = "true",
            ["store-metadata-content-in-database"] = "true",
            ["machine-id"] = "simulated-machine",
        };
    }

    /// <summary>
    /// The source, with the provider options in the url so that only the backup sees them
    /// </summary>
    private string Source => "@" + Mount.TrimEnd(Path.DirectorySeparatorChar) + "|googleworkspace://tenant"
        + "?google-client-id=client&google-client-secret=secret&google-refresh-token=refresh"
        + "&google-included-root-types=Users%2CSharedDrives&google-included-user-types=Gmail%2CDrive%2CCalendar%2CContacts"
        + "&google-ignore-calendar-acl=true";

    private async Task<HashSet<string>> BackupAndListAsync(Dictionary<string, string> options, IEnumerable<string> ignoredWarnings)
    {
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([Source]), ignoredWarnings.ToArray());

        using (var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 0 }), null))
        {
            var r = await c.ListAsync("*");
            TestUtils.AssertResults(r);
            return r.Files.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
        }
    }

    private static void AssertSamePaths(HashSet<string> actual, HashSet<string> expected, string context)
    {
        var missing = expected.Except(actual).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected).OrderBy(x => x, StringComparer.Ordinal).ToList();
        if (missing.Count == 0 && extra.Count == 0)
            return;

        var sb = new StringBuilder();
        sb.AppendLine($"The fileset differs from the tenant after {context}");
        if (missing.Count > 0)
            sb.AppendLine("Missing from the fileset:").AppendLine(string.Join(Environment.NewLine, missing.Take(40)));
        if (extra.Count > 0)
            sb.AppendLine("Not in the tenant:").AppendLine(string.Join(Environment.NewLine, extra.Take(40)));
        Assert.Fail(sb.ToString());
    }

    private async Task AssertRestoredContentsAsync(Dictionary<string, string> options, Dictionary<string, byte[]> expected, string context)
    {
        if (Directory.Exists(RESTOREFOLDER))
            Directory.Delete(RESTOREFOLDER, true);
        Directory.CreateDirectory(RESTOREFOLDER);

        using (var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 0, restore_path = RESTOREFOLDER }), null))
            TestUtils.AssertResults(await c.RestoreAsync(["*"]));

        var restored = Directory.EnumerateFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories).ToList();
        foreach (var (path, content) in expected)
        {
            var relative = path.Substring(Mount.Length);
            var match = restored.FirstOrDefault(x => x.EndsWith(Path.DirectorySeparatorChar + relative, StringComparison.Ordinal))
                ?? throw new AssertionException($"{relative} was not restored after {context}");
            Assert.That(File.ReadAllBytes(match), Is.EqualTo(content), $"The content of {relative} differs after {context}");
        }
    }

    private static bool UsesStoredToken(string url)
        => url.Contains("/history?", StringComparison.Ordinal)
            || (url.Contains("/changes?", StringComparison.Ordinal) && url.Contains("pageToken=", StringComparison.Ordinal))
            || url.Contains("syncToken=", StringComparison.Ordinal);

    [Test]
    [Category("GoogleWorkspace")]
    public async Task StoppedBackupStoresNoTokensAndTheNextRunWalksEverythingAsync()
    {
        var rng = new Random(7);
        var tenant = Tenant.Create(rng);
        var google = new SimulatedGoogle(tenant);
        var options = Prepare();

        using var handlerScope = GoogleWorkspaceTestSupport.UseHandler(google);
        var (paths, _) = Expected(tenant);
        AssertSamePaths(await BackupAndListAsync(options, []), paths, "the first backup");
        Assert.That(google.Unhandled, Is.Empty);

        // The stopped run uploads a changed file from the first user's drive, so its fileset is
        // kept, while the tenant-level shared drives, one of them with a new file, are never reached
        var firstDrive = tenant.Users[0].MyDrive;
        var changed = firstDrive.LiveItems.First(x => !x.IsFolder);
        changed.Version = tenant.Next();
        changed.Content = Encoding.UTF8.GetBytes("changed before the stop");
        var lastDrive = tenant.SharedDrives[^1];
        tenant.AddFile(lastDrive, lastDrive.RootId, rng);

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var stopped = false;
            var sharedListings = 0;
            google.OnRequest = url =>
            {
                // The shared drives are listed under each user first; the stop arrives at the tenant-level listing
                if (!stopped && url.Contains("/drive/v3/drives", StringComparison.Ordinal) && ++sharedListings > tenant.Users.Count)
                {
                    stopped = true;
                    c.StopAsync().GetAwaiter().GetResult();
                }
            };

            var r = await c.BackupAsync([Source]);
            Assert.That(r.PartialBackup, Is.True, "The stop arrived while the backup was listing the tenant-level shared drives");
            Assert.That(stopped, Is.True, "The backup reached the tenant-level shared drives");
        }
        google.OnRequest = null;

        // The stopped run has no state, so the next run lists everything and the fileset is complete again
        var requestsBefore = google.Requests.Count;
        Dictionary<string, byte[]> contents;
        (paths, contents) = Expected(tenant);
        AssertSamePaths(await BackupAndListAsync(options, []), paths, "the run after a stopped backup");
        Assert.That(google.Unhandled, Is.Empty);
        Assert.That(google.Requests.Skip(requestsBefore).Where(UsesStoredToken), Is.Empty, "Nothing is walked from a token after a stopped backup");
        await AssertRestoredContentsAsync(options, contents, "the run after a stopped backup");

        // The run after that continues incrementally from the complete fileset
        requestsBefore = google.Requests.Count;
        tenant.AddFile(firstDrive, firstDrive.RootId, rng);
        (paths, contents) = Expected(tenant);
        AssertSamePaths(await BackupAndListAsync(options, []), paths, "the second run after a stopped backup");
        var requests = google.Requests.Skip(requestsBefore).ToList();
        Assert.That(requests.Any(x => x.Contains("/history?", StringComparison.Ordinal)), Is.True, "The mailboxes are walked from the history ids of the complete run");
        Assert.That(requests.Any(x => x.Contains("/changes?", StringComparison.Ordinal)), Is.True, "The drives are walked from the tokens of the complete run");
        Assert.That(requests.Any(x => x.Contains("syncToken=", StringComparison.Ordinal)), Is.True, "The calendars and contacts are walked from their sync tokens");
    }

    /// <summary>
    /// A wider search over seeds, for use when hunting a bug rather than on every test run
    /// </summary>
    [Test]
    [Explicit]
    [Category("GoogleWorkspace")]
    public async Task RandomTenantChangesConvergeOverManySeedsAsync()
    {
        for (var seed = 100; seed < 130; seed++)
        {
            BasicHelperSetUp();
            try
            {
                await RandomTenantChangesConvergeAsync(seed, seed % 2 == 0, 12);
            }
            finally
            {
                BasicHelperTearDown();
            }
        }
    }

    [Test]
    [Category("GoogleWorkspace")]
    [TestCase(1, false, 20)]
    [TestCase(2, false, 20)]
    [TestCase(3, true, 20)]
    [TestCase(4, true, 20)]
    public async Task RandomTenantChangesConvergeAsync(int seed, bool fileScanner, int runs)
    {
        var rng = new Random(seed);
        var tenant = Tenant.Create(rng);
        var google = new SimulatedGoogle(tenant);
        var mutator = new Mutator(tenant, rng);
        var options = Prepare();
        options["disable-file-scanner"] = fileScanner ? "false" : "true";

        using var handlerScope = GoogleWorkspaceTestSupport.UseHandler(google);
        {
            var (paths, contents) = Expected(tenant);
            AssertSamePaths(await BackupAndListAsync(options, []), paths, "the first backup");
            Assert.That(google.Unhandled, Is.Empty, "Every request must be answered by the simulation");
            await AssertRestoredContentsAsync(options, contents, "the first backup");

            var history = new List<string>();
            for (var run = 1; run <= runs; run++)
            {
                mutator.ApplyRandomChanges(google);
                history.Add($"run {run}: {string.Join("; ", mutator.Log)}");
                var context = $"seed {seed} {string.Join(Environment.NewLine, history.TakeLast(3))}";

                var requestsBefore = google.Requests.Count;
                var actual = await BackupAndListAsync(options, mutator.IgnoredWarnings);
                Assert.That(google.Unhandled, Is.Empty, $"Every request must be answered by the simulation ({context})");

                var requests = google.Requests.Skip(requestsBefore).ToList();
                Assert.That(requests.Any(UsesStoredToken), Is.True, $"The run should have used stored tokens ({context})");

                if (mutator.HardFault)
                {
                    // The failed walk carried its mailbox, drive or calendar forward unchanged; a clean run converges
                    history.Add($"run {run} (recovery)");
                    actual = await BackupAndListAsync(options, []);
                    Assert.That(google.Unhandled, Is.Empty);
                }

                (paths, contents) = Expected(tenant);
                AssertSamePaths(actual, paths, context);

                if (run % 5 == 0 || run == runs)
                    await AssertRestoredContentsAsync(options, contents, context);
            }

            TestContext.Out.WriteLine(string.Join(Environment.NewLine, history));
        }
    }
}
