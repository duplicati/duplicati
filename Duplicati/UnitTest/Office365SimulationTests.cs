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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Main;
using Duplicati.Proprietary.Office365;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Runs real backups of the Microsoft 365 provider against a simulated tenant that changes
/// between runs, and checks after every run that the newest fileset is exactly the tenant.
/// The delta walks carry state between runs, so a path that is neither re-listed nor carried
/// forward, or listed twice, only shows up after the right sequence of changes; the random
/// mutations search for such sequences and a failing seed reproduces them.
/// </summary>
[NonParallelizable]
public class Office365SimulationTests : BasicSetupHelper
{
    private const string GRAPH = "https://graph.simulated.test";
    private static readonly DateTime EPOCH = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    #region Tenant model

    private sealed class SimMessage
    {
        public string Id = "";
        public string Subject = "";
        public byte[] Content = [];
        public long Version;
        public long? DeletedAt;
    }

    private sealed class SimMailFolder
    {
        public string Id = "";
        public string Name = "";
        public SimMailFolder? Parent;
        public readonly List<SimMailFolder> Children = [];
        public readonly Dictionary<string, SimMessage> Messages = new(StringComparer.Ordinal);
        public long? DeletedAt;
        /// <summary>Tokens below this are answered with a resync error</summary>
        public long MinValidToken;

        public IEnumerable<SimMailFolder> LiveChildren => Children.Where(x => x.DeletedAt == null);
        public IEnumerable<SimMessage> LiveMessages => Messages.Values.Where(x => x.DeletedAt == null);
        public IEnumerable<SimMailFolder> LiveDescendantsAndSelf => new[] { this }.Concat(LiveChildren.SelectMany(x => x.LiveDescendantsAndSelf));
    }

    private sealed class SimUser
    {
        public string Id = "";
        public string Name = "";
        public SimMailFolder Root = new();
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
        /// <summary>Whether the delta reports the removal; Graph does not always list the children of a removed folder</summary>
        public bool ReportTombstone = true;
    }

    private sealed class SimDrive
    {
        public string Id = "";
        public string Name = "";
        public string RootId = "";
        public readonly Dictionary<string, SimItem> Items = new(StringComparer.Ordinal);
        public long MinValidToken;

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

    private sealed class SimSite
    {
        public string Id = "";
        public string Name = "";
        public readonly List<SimDrive> Drives = [];
    }

    private sealed class Tenant
    {
        public long Seq;
        public readonly List<SimUser> Users = [];
        public readonly List<SimSite> Sites = [];
        private int _ids;

        public long Next() => ++Seq;
        public string NewId(string prefix) => $"{prefix}{++_ids:D4}";

        public IEnumerable<SimMailFolder> AllFolders => Users.SelectMany(u => u.Root.LiveDescendantsAndSelf);
        public IEnumerable<SimMailFolder> AllFoldersIncludingDeleted => Users.SelectMany(u => Descend(u.Root));
        private static IEnumerable<SimMailFolder> Descend(SimMailFolder f) => new[] { f }.Concat(f.Children.SelectMany(Descend));
        public IEnumerable<SimDrive> AllDrives => Sites.SelectMany(s => s.Drives);

        public static Tenant Create(Random rng)
        {
            var t = new Tenant();
            for (var u = 0; u < 2; u++)
            {
                var user = new SimUser { Id = t.NewId("user-"), Name = $"User {u}" };
                user.Root = new SimMailFolder { Id = t.NewId("mf-"), Name = "root" };
                for (var m = 0; m < 3; m++)
                    t.AddMessage(user.Root, rng);
                for (var f = 0; f < 2; f++)
                {
                    var folder = t.AddMailFolder(user.Root);
                    for (var m = 0; m < 3; m++)
                        t.AddMessage(folder, rng);
                    if (f == 0)
                    {
                        var nested = t.AddMailFolder(folder);
                        t.AddMessage(nested, rng);
                    }
                }
                t.Users.Add(user);
            }

            for (var s = 0; s < 2; s++)
            {
                var site = new SimSite { Id = t.NewId("site-"), Name = $"Site {s}" };
                for (var d = 0; d < 1 + s; d++)
                {
                    var drive = new SimDrive { Id = t.NewId("drive-"), Name = $"Documents {d}", RootId = t.NewId("root-") };
                    t.AddFile(drive, drive.RootId, rng);
                    var f1 = t.AddFolder(drive, drive.RootId);
                    t.AddFile(drive, f1.Id, rng);
                    t.AddFile(drive, f1.Id, rng);
                    var f2 = t.AddFolder(drive, f1.Id);
                    t.AddFile(drive, f2.Id, rng);
                    var f3 = t.AddFolder(drive, f2.Id);
                    t.AddFile(drive, f3.Id, rng);
                    t.AddFolder(drive, drive.RootId);
                    site.Drives.Add(drive);
                }
                t.Sites.Add(site);
            }

            return t;
        }

        public SimMessage AddMessage(SimMailFolder folder, Random rng)
        {
            var msg = new SimMessage { Id = NewId("msg-"), Version = Next() };
            msg.Subject = $"Subject {msg.Id}";
            msg.Content = Encoding.UTF8.GetBytes($"From: a@b\r\nSubject: {msg.Subject}\r\n\r\n{RandomText(rng)}");
            folder.Messages[msg.Id] = msg;
            return msg;
        }

        public SimMailFolder AddMailFolder(SimMailFolder parent)
        {
            var folder = new SimMailFolder { Id = NewId("mf-"), Parent = parent };
            folder.Name = $"Folder {folder.Id}";
            parent.Children.Add(folder);
            return folder;
        }

        public SimItem AddFile(SimDrive drive, string parentId, Random rng)
        {
            var item = new SimItem { Id = NewId("item-"), ParentId = parentId, Version = Next(), Content = Encoding.UTF8.GetBytes(RandomText(rng)) };
            item.Name = $"{item.Id}.txt";
            drive.Items[item.Id] = item;
            return item;
        }

        public SimItem AddFolder(SimDrive drive, string parentId)
        {
            var item = new SimItem { Id = NewId("folder-"), ParentId = parentId, Version = Next(), IsFolder = true };
            item.Name = $"Folder {item.Id}";
            drive.Items[item.Id] = item;
            return item;
        }

        public static string RandomText(Random rng)
        {
            var sb = new StringBuilder();
            var words = rng.Next(5, 60);
            for (var i = 0; i < words; i++)
                sb.Append("word").Append(rng.Next(1000)).Append(' ');
            return sb.ToString();
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

        public void ApplyRandomChanges(SimulatedGraph graph)
        {
            HardFault = false;
            IgnoredWarnings.Clear();
            Log.Clear();

            var count = rng.Next(1, 9);
            for (var i = 0; i < count; i++)
                ApplyOne(graph);
        }

        private T Pick<T>(IList<T> items) => items[rng.Next(items.Count)];

        private void ApplyOne(SimulatedGraph graph)
        {
            var folders = tenant.AllFolders.ToList();
            var drives = tenant.AllDrives.ToList();
            switch (rng.Next(100))
            {
                case < 14:
                    {
                        var folder = Pick(folders);
                        var msg = tenant.AddMessage(folder, rng);
                        Log.Add($"add message {msg.Id} in {folder.Id}");
                        break;
                    }
                case < 24:
                    {
                        var candidates = folders.SelectMany(f => f.LiveMessages.Select(m => (f, m))).ToList();
                        if (candidates.Count == 0) break;
                        var (folder, msg) = Pick(candidates);
                        msg.Version = tenant.Next();
                        msg.Subject += " (edited)";
                        msg.Content = Encoding.UTF8.GetBytes($"From: a@b\r\nSubject: {msg.Subject}\r\n\r\n{Tenant.RandomText(rng)}");
                        Log.Add($"modify message {msg.Id} in {folder.Id}");
                        break;
                    }
                case < 34:
                    {
                        var candidates = folders.SelectMany(f => f.LiveMessages.Select(m => (f, m))).ToList();
                        if (candidates.Count == 0) break;
                        var (folder, msg) = Pick(candidates);
                        msg.DeletedAt = tenant.Next();
                        Log.Add($"delete message {msg.Id} in {folder.Id}");
                        break;
                    }
                case < 38:
                    {
                        // A moved message gets a new id in the target folder and a tombstone in the source folder
                        var candidates = folders.SelectMany(f => f.LiveMessages.Select(m => (f, m))).ToList();
                        if (candidates.Count == 0) break;
                        var (folder, msg) = Pick(candidates);
                        var target = Pick(folders);
                        msg.DeletedAt = tenant.Next();
                        var copy = new SimMessage { Id = tenant.NewId("msg-"), Subject = msg.Subject, Content = msg.Content, Version = tenant.Next() };
                        target.Messages[copy.Id] = copy;
                        Log.Add($"move message {msg.Id} from {folder.Id} to {target.Id} as {copy.Id}");
                        break;
                    }
                case < 42:
                    {
                        var parent = Pick(folders);
                        var folder = tenant.AddMailFolder(parent);
                        tenant.AddMessage(folder, rng);
                        Log.Add($"add mail folder {folder.Id} below {parent.Id}");
                        break;
                    }
                case < 46:
                    {
                        var candidates = folders.Where(f => f.Parent != null).ToList();
                        if (candidates.Count == 0) break;
                        var folder = Pick(candidates);
                        foreach (var f in folder.LiveDescendantsAndSelf.ToList())
                        {
                            foreach (var m in f.LiveMessages)
                                m.DeletedAt = tenant.Next();
                            f.DeletedAt = tenant.Next();
                        }
                        Log.Add($"delete mail folder {folder.Id}");
                        break;
                    }
                case < 58:
                    {
                        var drive = Pick(drives);
                        var parent = rng.Next(3) == 0 ? drive.RootId : Pick(drive.LiveFolders.ToList().Concat([new SimItem { Id = drive.RootId }]).ToList()).Id;
                        var file = tenant.AddFile(drive, parent, rng);
                        Log.Add($"add file {file.Id} in {drive.Id}/{parent}");
                        break;
                    }
                case < 68:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Where(x => !x.IsFolder).Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, file) = Pick(candidates);
                        file.Version = tenant.Next();
                        file.Content = Encoding.UTF8.GetBytes(Tenant.RandomText(rng));
                        Log.Add($"modify file {file.Id} in {drive.Id}");
                        break;
                    }
                case < 74:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Where(x => !x.IsFolder).Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, file) = Pick(candidates);
                        file.DeletedAt = tenant.Next();
                        Log.Add($"delete file {file.Id} in {drive.Id}");
                        break;
                    }
                case < 78:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, item) = Pick(candidates);
                        item.Version = tenant.Next();
                        item.Name = "renamed " + item.Name;
                        Log.Add($"rename {(item.IsFolder ? "folder" : "file")} {item.Id} in {drive.Id}");
                        break;
                    }
                case < 84:
                    {
                        var candidates = drives.SelectMany(d => d.LiveItems.Where(x => !x.IsFolder).Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, file) = Pick(candidates);
                        var targets = drive.LiveFolders.Select(x => x.Id).Append(drive.RootId).Where(x => x != file.ParentId).ToList();
                        if (targets.Count == 0) break;
                        var target = Pick(targets);
                        file.ParentId = target;
                        file.Version = tenant.Next();
                        Log.Add($"move file {file.Id} to {target} in {drive.Id}");
                        break;
                    }
                case < 88:
                    {
                        var drive = Pick(drives);
                        var parent = Pick(drive.LiveFolders.Select(x => x.Id).Append(drive.RootId).ToList());
                        var folder = tenant.AddFolder(drive, parent);
                        if (rng.Next(2) == 0)
                        {
                            tenant.AddFile(drive, folder.Id, rng);
                            var nested = tenant.AddFolder(drive, folder.Id);
                            tenant.AddFile(drive, nested.Id, rng);
                        }
                        Log.Add($"add folder {folder.Id} below {parent} in {drive.Id}");
                        break;
                    }
                case < 92:
                    {
                        var candidates = drives.SelectMany(d => d.LiveFolders.Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, folder) = Pick(candidates);
                        var targets = drive.LiveFolders.Where(x => x.Id != folder.Id && !drive.IsBelow(x, folder)).Select(x => x.Id).Append(drive.RootId).Where(x => x != folder.ParentId).ToList();
                        if (targets.Count == 0) break;
                        var target = Pick(targets);
                        folder.ParentId = target;
                        folder.Version = tenant.Next();
                        Log.Add($"move folder {folder.Id} to {target} in {drive.Id}");
                        break;
                    }
                case < 95:
                    {
                        var candidates = drives.SelectMany(d => d.LiveFolders.Select(x => (d, x))).ToList();
                        if (candidates.Count == 0) break;
                        var (drive, folder) = Pick(candidates);
                        var report = rng.Next(2) == 0;
                        foreach (var item in drive.LiveDescendants(folder).ToList())
                        {
                            item.DeletedAt = tenant.Next();
                            item.ReportTombstone = report;
                        }
                        folder.DeletedAt = tenant.Next();
                        Log.Add($"delete folder {folder.Id} in {drive.Id} ({(report ? "with" : "without")} tombstones for its contents)");
                        break;
                    }
                case < 97:
                    {
                        // Tokens issued so far become stale; the one issued by this run's walk stays valid
                        var drive = Pick(drives);
                        drive.MinValidToken = tenant.Next();
                        IgnoredWarnings.Add("DriveDeltaResync");
                        Log.Add($"invalidate the delta token of {drive.Id}");
                        break;
                    }
                case < 98:
                    {
                        var folder = Pick(folders);
                        folder.MinValidToken = tenant.Next();
                        IgnoredWarnings.Add("MailDeltaResync");
                        Log.Add($"invalidate the delta token of mail folder {folder.Id}");
                        break;
                    }
                case < 99:
                    {
                        graph.ThrottleNext = true;
                        Log.Add("throttle the next request");
                        break;
                    }
                default:
                    {
                        graph.FailNextContaining = rng.Next(2) == 0 ? "/messages/delta" : "/root/delta";
                        HardFault = true;
                        IgnoredWarnings.Add("PathProcessingErrorEnumerate");
                        Log.Add($"fail the next request to {graph.FailNextContaining}");
                        break;
                    }
            }
        }
    }

    #endregion

    #region Simulated Graph

    /// <summary>
    /// Answers the Graph requests the provider makes for users with mailboxes and sites with
    /// document libraries from the tenant model. Delta tokens are the tenant's change sequence
    /// at the end of the walk; a walk from a token reports what changed after it.
    /// </summary>
    private sealed class SimulatedGraph(Tenant tenant) : HttpMessageHandler
    {
        private const int PAGE_SIZE = 3;
        private readonly object _lock = new();

        public readonly List<string> Requests = [];
        public readonly List<string> Unhandled = [];
        public bool ThrottleNext;
        public string? FailNextContaining;
        /// <summary>Called with the decoded url of every request before it is answered, so a test can act while the backup runs</summary>
        public Action<string>? OnRequest;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            var decoded = Uri.UnescapeDataString(uri.AbsoluteUri);
            OnRequest?.Invoke(decoded);
            HttpResponseMessage response;
            lock (_lock)
            {
                Requests.Add(decoded);

                if (ThrottleNext)
                {
                    ThrottleNext = false;
                    response = new HttpResponseMessage(HttpStatusCode.TooManyRequests) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
                    response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
                }
                else if (FailNextContaining != null && decoded.Contains(FailNextContaining, StringComparison.Ordinal))
                {
                    FailNextContaining = null;
                    response = Error(HttpStatusCode.InternalServerError, "generalException");
                }
                else
                {
                    try
                    {
                        response = Route(uri.AbsolutePath, decoded) ?? Unknown(decoded);
                    }
                    catch (Exception ex)
                    {
                        Unhandled.Add($"{decoded}: {ex}");
                        response = Error(HttpStatusCode.InternalServerError, "simulationFailure");
                    }
                }
            }

            response.RequestMessage = request;
            return Task.FromResult(response);
        }

        private HttpResponseMessage Unknown(string url)
        {
            Unhandled.Add(url);
            return Error(HttpStatusCode.NotFound, "itemNotFound");
        }

        private static HttpResponseMessage Json(object body)
            => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json") };

        private static HttpResponseMessage Bytes(byte[] body)
            => new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

        private static HttpResponseMessage Error(HttpStatusCode status, string code)
            => new(status) { Content = new StringContent($"{{\"error\":{{\"code\":\"{code}\",\"message\":\"{code}\"}}}}", Encoding.UTF8, "application/json") };

        private static string Stamp(long version) => EPOCH.AddSeconds(version).ToString("o");

        private static readonly Regex UsersRx = new(@"^/v1\.0/users$");
        private static readonly Regex MailRootRx = new(@"^/v1\.0/users/([^/]+)/mailFolders/msgfolderroot$");
        private static readonly Regex ChildFoldersRx = new(@"^/v1\.0/users/([^/]+)/mailFolders/([^/]+)/childFolders$");
        private static readonly Regex MailDeltaRx = new(@"^/v1\.0/users/([^/]+)/mailFolders/([^/]+)/messages/delta$");
        private static readonly Regex MailContentRx = new(@"^/v1\.0/users/([^/]+)/messages/([^/]+)/\$value$");
        private static readonly Regex MailMetadataRx = new(@"^/v1\.0/users/([^/]+)/messages/([^/]+)$");
        private static readonly Regex MailSettingsRx = new(@"^/v1\.0/users/([^/]+)/mailboxSettings$");
        private static readonly Regex MailRulesRx = new(@"^/v1\.0/users/([^/]+)/mailFolders/inbox/messageRules$");
        private static readonly Regex SitesRx = new(@"^/v1\.0/sites/getAllSites$");
        private static readonly Regex SiteRx = new(@"^/v1\.0/sites/([^/]+)$");
        private static readonly Regex SiteDrivesRx = new(@"^/v1\.0/sites/([^/]+)/drives$");
        private static readonly Regex SiteListsRx = new(@"^/v1\.0/sites/([^/]+)/lists$");
        private static readonly Regex SubsitesRx = new(@"^/v1\.0/sites/([^/]+)/sites$");
        private static readonly Regex DriveRootRx = new(@"^/v1\.0/drives/([^/]+)/root$");
        private static readonly Regex DriveRootChildrenRx = new(@"^/v1\.0/drives/([^/]+)/root/children$");
        private static readonly Regex DriveDeltaRx = new(@"^/v1\.0/drives/([^/]+)/root/delta$");
        private static readonly Regex DriveItemChildrenRx = new(@"^/v1\.0/drives/([^/]+)/items/([^/]+)/children$");
        private static readonly Regex DriveItemContentRx = new(@"^/v1\.0/drives/([^/]+)/items/([^/]+)/content$");
        private static readonly Regex DriveItemRx = new(@"^/v1\.0/drives/([^/]+)/items/([^/]+)$");

        private HttpResponseMessage? Route(string path, string url)
        {
            Match m;
            if (UsersRx.IsMatch(path))
                return Json(new Dictionary<string, object?>
                {
                    ["value"] = tenant.Users.Select(u => new Dictionary<string, object?>
                    {
                        ["id"] = u.Id,
                        ["displayName"] = u.Name,
                        ["userPrincipalName"] = $"{u.Id}@contoso.test",
                        ["accountEnabled"] = true,
                        ["assignedLicenses"] = new object[] { new { skuId = "sku" } },
                    }).ToList()
                });

            if ((m = MailRootRx.Match(path)).Success)
            {
                var user = tenant.Users.First(u => u.Id == m.Groups[1].Value);
                return Json(MailFolderJson(user.Root));
            }

            if ((m = ChildFoldersRx.Match(path)).Success)
            {
                var folder = FindFolder(m.Groups[2].Value);
                return folder == null ? null : Json(new Dictionary<string, object?> { ["value"] = folder.LiveChildren.Select(MailFolderJson).ToList() });
            }

            if ((m = MailDeltaRx.Match(path)).Success)
            {
                var folder = FindFolder(m.Groups[2].Value);
                return folder == null ? null : MailDelta(folder, url);
            }

            if ((m = MailContentRx.Match(path)).Success)
            {
                var msg = FindMessage(m.Groups[2].Value);
                return msg == null || msg.DeletedAt != null ? Error(HttpStatusCode.NotFound, "ErrorItemNotFound") : Bytes(msg.Content);
            }

            if ((m = MailMetadataRx.Match(path)).Success)
            {
                var msg = FindMessage(m.Groups[2].Value);
                return msg == null || msg.DeletedAt != null ? Error(HttpStatusCode.NotFound, "ErrorItemNotFound") : Json(MessageJson(msg));
            }

            if (MailSettingsRx.IsMatch(path))
                return Json(new { timeZone = "UTC", language = new { locale = "en-US" } });

            if (MailRulesRx.IsMatch(path))
                return Json(new Dictionary<string, object?> { ["value"] = new List<object>() });

            if (SitesRx.IsMatch(path))
                return Json(new Dictionary<string, object?>
                {
                    ["value"] = tenant.Sites.Select(s => new Dictionary<string, object?>
                    {
                        ["id"] = s.Id,
                        ["displayName"] = s.Name,
                        ["name"] = s.Name,
                        ["webUrl"] = $"https://contoso.sharepoint.test/sites/{s.Id}",
                    }).ToList()
                });

            if ((m = SiteDrivesRx.Match(path)).Success)
            {
                var site = tenant.Sites.First(s => s.Id == m.Groups[1].Value);
                return Json(new Dictionary<string, object?> { ["value"] = site.Drives.Select(d => new { id = d.Id, name = d.Name, driveType = "documentLibrary" }).ToList() });
            }

            if (SiteListsRx.IsMatch(path) || SubsitesRx.IsMatch(path))
                return Json(new Dictionary<string, object?> { ["value"] = new List<object>() });

            if ((m = SiteRx.Match(path)).Success)
            {
                var site = tenant.Sites.First(s => s.Id == m.Groups[1].Value);
                return Json(new { id = site.Id, displayName = site.Name, webUrl = $"https://contoso.sharepoint.test/sites/{site.Id}" });
            }

            if ((m = DriveRootRx.Match(path)).Success)
                return Json(new { id = FindDrive(m.Groups[1].Value).RootId });

            if ((m = DriveRootChildrenRx.Match(path)).Success)
            {
                var drive = FindDrive(m.Groups[1].Value);
                return Json(new Dictionary<string, object?> { ["value"] = drive.LiveChildren(drive.RootId).Select(x => ItemJson(drive, x)).ToList() });
            }

            if ((m = DriveDeltaRx.Match(path)).Success)
                return DriveDelta(FindDrive(m.Groups[1].Value), url);

            if ((m = DriveItemChildrenRx.Match(path)).Success)
            {
                var drive = FindDrive(m.Groups[1].Value);
                if (!drive.Items.TryGetValue(m.Groups[2].Value, out var folder) || folder.DeletedAt != null)
                    return Error(HttpStatusCode.NotFound, "itemNotFound");
                return Json(new Dictionary<string, object?> { ["value"] = drive.LiveChildren(folder.Id).Select(x => ItemJson(drive, x)).ToList() });
            }

            if ((m = DriveItemContentRx.Match(path)).Success)
            {
                var drive = FindDrive(m.Groups[1].Value);
                return !drive.Items.TryGetValue(m.Groups[2].Value, out var item) || item.DeletedAt != null
                    ? Error(HttpStatusCode.NotFound, "itemNotFound")
                    : Bytes(item.Content);
            }

            if ((m = DriveItemRx.Match(path)).Success)
            {
                var drive = FindDrive(m.Groups[1].Value);
                return !drive.Items.TryGetValue(m.Groups[2].Value, out var item) || item.DeletedAt != null
                    ? Error(HttpStatusCode.NotFound, "itemNotFound")
                    : Json(ItemJson(drive, item));
            }

            return null;
        }

        private SimMailFolder? FindFolder(string id) => tenant.AllFoldersIncludingDeleted.FirstOrDefault(f => f.Id == id && f.DeletedAt == null);
        private SimMessage? FindMessage(string id) => tenant.AllFoldersIncludingDeleted.SelectMany(f => f.Messages.Values).FirstOrDefault(x => x.Id == id);
        private SimDrive FindDrive(string id) => tenant.AllDrives.First(d => d.Id == id);

        private static Dictionary<string, object?> MailFolderJson(SimMailFolder folder) => new()
        {
            ["id"] = folder.Id,
            ["displayName"] = folder.Name,
            ["parentFolderId"] = folder.Parent?.Id,
            ["childFolderCount"] = folder.LiveChildren.Count(),
            ["totalItemCount"] = folder.LiveMessages.Count(),
        };

        private static Dictionary<string, object?> MessageJson(SimMessage msg) => new()
        {
            ["id"] = msg.Id,
            ["subject"] = msg.Subject,
            ["receivedDateTime"] = Stamp(msg.Version),
            ["sentDateTime"] = Stamp(msg.Version),
            ["createdDateTime"] = Stamp(msg.Version),
            ["lastModifiedDateTime"] = Stamp(msg.Version),
            ["hasAttachments"] = false,
        };

        private static Dictionary<string, object?> ItemJson(SimDrive drive, SimItem item)
        {
            var json = new Dictionary<string, object?>
            {
                ["id"] = item.Id,
                ["name"] = item.Name,
                ["createdDateTime"] = Stamp(1),
                ["lastModifiedDateTime"] = Stamp(item.Version),
                ["eTag"] = $"etag-{item.Version}",
                ["cTag"] = $"ctag-{item.Version}",
                ["parentReference"] = new Dictionary<string, object?> { ["driveId"] = drive.Id, ["id"] = item.ParentId },
            };
            if (item.IsFolder)
                json["folder"] = new { childCount = drive.LiveChildren(item.Id).Count() };
            else
            {
                json["file"] = new { mimeType = "text/plain" };
                json["size"] = item.Content.Length;
            }
            return json;
        }

        /// <summary>
        /// The paging state of a delta walk: the token it started from, the sequence it reports up to, and the page
        /// </summary>
        private static (long From, long AsOf, int Page) ParseDeltaState(string url, string tokenParameter, long currentSeq)
        {
            var skip = APIHelper.TryGetQueryValue(url, "$skiptoken");
            if (skip != null)
            {
                var parts = skip.Split(':');
                return (long.Parse(parts[0]), long.Parse(parts[1]), int.Parse(parts[2]));
            }

            var token = APIHelper.TryGetQueryValue(url, tokenParameter);
            return (string.IsNullOrEmpty(token) ? -1 : long.Parse(token), currentSeq, 0);
        }

        private static HttpResponseMessage DeltaPage(List<Dictionary<string, object?>> items, string baseUrl, (long From, long AsOf, int Page) state, string tokenParameter, bool overlap)
        {
            var start = state.Page * PAGE_SIZE;
            // An item may be reported again on the next page of the same walk; the later report is the newer state
            var first = overlap && start > 0 ? start - 1 : start;
            var page = items.Skip(first).Take(start - first + PAGE_SIZE).ToList();
            var body = new Dictionary<string, object?> { ["value"] = page };
            if (start + PAGE_SIZE < items.Count)
                body["@odata.nextLink"] = $"{baseUrl}?$skiptoken={state.From}:{state.AsOf}:{state.Page + 1}";
            else
                body["@odata.deltaLink"] = $"{baseUrl}?{tokenParameter}={state.AsOf}";
            return Json(body);
        }

        private HttpResponseMessage MailDelta(SimMailFolder folder, string url)
        {
            var state = ParseDeltaState(url, "$deltatoken", tenant.Seq);
            if (state.From >= 0 && state.From < folder.MinValidToken)
                return Error(HttpStatusCode.Gone, "syncStateNotFound");

            var items = new List<Dictionary<string, object?>>();
            foreach (var msg in folder.Messages.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (state.From < 0)
                {
                    if (msg.DeletedAt == null)
                        items.Add(MessageJson(msg));
                }
                else if (msg.DeletedAt != null)
                {
                    if (msg.DeletedAt > state.From && msg.DeletedAt <= state.AsOf)
                        items.Add(new Dictionary<string, object?> { ["id"] = msg.Id, ["@removed"] = new { reason = "deleted" } });
                }
                else if (msg.Version > state.From && msg.Version <= state.AsOf)
                    items.Add(MessageJson(msg));
            }

            var baseUrl = url.Substring(0, url.IndexOf('?') < 0 ? url.Length : url.IndexOf('?'));
            return DeltaPage(items, baseUrl, state, "$deltatoken", false);
        }

        private HttpResponseMessage DriveDelta(SimDrive drive, string url)
        {
            var token = APIHelper.TryGetQueryValue(url, "token");
            var baseUrl = url.Substring(0, url.IndexOf('?') < 0 ? url.Length : url.IndexOf('?'));
            if (token == "latest")
                return Json(new Dictionary<string, object?> { ["value"] = new List<object>(), ["@odata.deltaLink"] = $"{baseUrl}?token={tenant.Seq}" });

            var state = ParseDeltaState(url, "token", tenant.Seq);
            if (state.From >= 0 && state.From < drive.MinValidToken)
                return Error(HttpStatusCode.Gone, "resyncRequired");

            var items = new List<Dictionary<string, object?>>();
            if (state.From < 0)
                items.Add(new Dictionary<string, object?> { ["id"] = drive.RootId, ["name"] = "root", ["root"] = new { }, ["folder"] = new { childCount = 0 }, ["parentReference"] = new Dictionary<string, object?> { ["driveId"] = drive.Id } });

            foreach (var item in drive.Items.Values.OrderBy(x => x.Id, StringComparer.Ordinal))
            {
                if (state.From < 0)
                {
                    if (item.DeletedAt == null)
                        items.Add(ItemJson(drive, item));
                }
                else if (item.DeletedAt != null)
                {
                    if (item.ReportTombstone && item.DeletedAt > state.From && item.DeletedAt <= state.AsOf)
                    {
                        var tombstone = new Dictionary<string, object?> { ["id"] = item.Id, ["name"] = item.Name, ["deleted"] = new { state = "deleted" } };
                        // Graph does not always say where a removed item was
                        if (!item.IsFolder && item.Id[^1] % 2 == 0)
                            tombstone["parentReference"] = new Dictionary<string, object?> { ["driveId"] = drive.Id, ["id"] = item.ParentId };
                        items.Add(tombstone);
                    }
                }
                else if (item.Version > state.From && item.Version <= state.AsOf)
                    items.Add(ItemJson(drive, item));
            }

            return DeltaPage(items, baseUrl, state, "token", state.AsOf % 2 == 0);
        }
    }

    #endregion

    #region Expected content

    private static string Mount => Util.AppendDirSeparator(Path.GetFullPath(Path.Combine(BASEFOLDER, "o365")));

    private static (HashSet<string> Paths, Dictionary<string, byte[]> Contents) Expected(Tenant tenant)
    {
        var paths = new HashSet<string>(StringComparer.Ordinal);
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string D(params string[] parts) => Util.AppendDirSeparator(Path.Combine(parts));
        string F(params string[] parts) => Path.Combine(parts);

        paths.Add(Mount);
        paths.Add(F(Mount, "office-365-backup-description.json"));
        paths.Add(D(Mount, "users"));
        paths.Add(D(Mount, "sites"));

        foreach (var user in tenant.Users)
        {
            var userPath = D(Mount, "users", user.Id);
            var mailbox = D(userPath, "mailbox");
            paths.Add(userPath);
            paths.Add(mailbox);
            paths.Add(F(mailbox, "settings.json"));
            paths.Add(D(mailbox, "rules"));

            void AddFolder(SimMailFolder folder, string folderPath)
            {
                foreach (var msg in folder.LiveMessages)
                {
                    var msgPath = D(folderPath, msg.Id);
                    paths.Add(msgPath);
                    paths.Add(F(msgPath, "content.eml"));
                    paths.Add(F(msgPath, "metadata.json"));
                    contents[F(msgPath, "content.eml")] = msg.Content;
                }

                foreach (var child in folder.LiveChildren)
                {
                    var childPath = D(folderPath, child.Id);
                    paths.Add(childPath);
                    AddFolder(child, childPath);
                }
            }

            AddFolder(user.Root, mailbox);
        }

        foreach (var site in tenant.Sites)
        {
            var sitePath = D(Mount, "sites", site.Id);
            paths.Add(sitePath);
            paths.Add(F(sitePath, "metadata.json"));
            foreach (var drive in site.Drives)
            {
                var drivePath = D(sitePath, drive.Id);
                paths.Add(drivePath);
                foreach (var item in drive.LiveItems)
                {
                    var parent = drive.AncestorChain(item).Aggregate(drivePath, (p, id) => D(p, id));
                    if (item.IsFolder)
                        paths.Add(D(parent, item.Id));
                    else
                    {
                        paths.Add(F(parent, item.Id));
                        contents[F(parent, item.Id)] = item.Content;
                    }
                }
            }
        }

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
    private string Source => "@" + Mount.TrimEnd(Path.DirectorySeparatorChar) + "|office365://tenant"
        + "?office365-tenant-id=tenant&office365-client-id=client&office365-client-secret=secret"
        + "&office365-graph-base-url=" + Uri.EscapeDataString(GRAPH)
        + "&office365-included-root-types=Users%2CSites&office365-included-user-types=Mailbox";

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

    [Test]
    [Category("Office365")]
    public async Task StoppedBackupStoresNoTokensAndTheNextRunWalksEverythingAsync()
    {
        var rng = new Random(7);
        var tenant = Tenant.Create(rng);
        var graph = new SimulatedGraph(tenant);
        var options = Prepare();

        using var handlerScope = SourceProvider.UseTestMessageHandler(graph);
        var (paths, _) = Expected(tenant);
        AssertSamePaths(await BackupAndListAsync(options, []), paths, "the first backup");
        Assert.That(graph.Unhandled, Is.Empty);

        // The stopped run uploads a changed file from the first library, so its fileset is kept,
        // while the last site's libraries, one of them with a new file, are never reached
        var firstDrive = tenant.Sites[0].Drives[0];
        var changed = firstDrive.LiveItems.First(x => !x.IsFolder);
        changed.Version = tenant.Next();
        changed.Content = Encoding.UTF8.GetBytes("changed before the stop");
        var lastSite = tenant.Sites[^1];
        tenant.AddFile(lastSite.Drives[0], lastSite.Drives[0].RootId, rng);

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var stopped = false;
            graph.OnRequest = url =>
            {
                if (!stopped && url.Contains($"/sites/{lastSite.Id}/drives", StringComparison.Ordinal))
                {
                    stopped = true;
                    c.StopAsync().GetAwaiter().GetResult();
                }
            };

            var r = await c.BackupAsync([Source]);
            Assert.That(r.PartialBackup, Is.True, "The stop arrived while the backup was listing the last site");
            Assert.That(stopped, Is.True, "The backup reached the last site");
        }
        graph.OnRequest = null;

        // The stopped run has no state, so the next run lists everything and the fileset is complete again
        var requestsBefore = graph.Requests.Count;
        Dictionary<string, byte[]> contents;
        (paths, contents) = Expected(tenant);
        AssertSamePaths(await BackupAndListAsync(options, []), paths, "the run after a stopped backup");
        Assert.That(graph.Unhandled, Is.Empty);

        var requests = graph.Requests.Skip(requestsBefore).ToList();
        Assert.That(requests.Where(x => x.Contains("$deltatoken=", StringComparison.Ordinal)), Is.Empty, "No mail folder is walked from a token after a stopped backup");
        Assert.That(requests.Where(x => x.Contains("/root/delta?", StringComparison.Ordinal) && x.Contains("token=", StringComparison.Ordinal) && !x.Contains("token=latest", StringComparison.Ordinal)), Is.Empty, "No library is walked from a token after a stopped backup");
        await AssertRestoredContentsAsync(options, contents, "the run after a stopped backup");

        // The run after that continues incrementally from the complete fileset
        requestsBefore = graph.Requests.Count;
        var added = tenant.AddFile(firstDrive, firstDrive.RootId, rng);
        (paths, contents) = Expected(tenant);
        AssertSamePaths(await BackupAndListAsync(options, []), paths, "the second run after a stopped backup");
        Assert.That(graph.Requests.Skip(requestsBefore).Any(x => x.Contains("$deltatoken=", StringComparison.Ordinal)), Is.True, "The mail folders are walked from the tokens of the complete run");
        Assert.That(graph.Requests.Skip(requestsBefore).Any(x => x.Contains($"/drives/{firstDrive.Id}/root/delta?", StringComparison.Ordinal) && x.Contains("token=", StringComparison.Ordinal) && !x.Contains("token=latest", StringComparison.Ordinal)), Is.True, $"The library with {added.Id} is walked from its token");
    }

    /// <summary>
    /// A wider search over seeds, for use when hunting a bug rather than on every test run
    /// </summary>
    [Test]
    [Explicit]
    [Category("Office365")]
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
    [Category("Office365")]
    [TestCase(1, false, 20)]
    [TestCase(2, false, 20)]
    [TestCase(3, true, 20)]
    [TestCase(4, true, 20)]
    // Seed 102 moved a nested folder before the stored map knew it, which once left the old path behind
    [TestCase(102, true, 12)]
    [TestCase(102, false, 12)]
    public async Task RandomTenantChangesConvergeAsync(int seed, bool fileScanner, int runs)
    {
        var rng = new Random(seed);
        var tenant = Tenant.Create(rng);
        var graph = new SimulatedGraph(tenant);
        var mutator = new Mutator(tenant, rng);
        var options = Prepare();
        options["disable-file-scanner"] = fileScanner ? "false" : "true";

        using var handlerScope = SourceProvider.UseTestMessageHandler(graph);
        {
            var (paths, contents) = Expected(tenant);
            AssertSamePaths(await BackupAndListAsync(options, []), paths, "the first backup");
            Assert.That(graph.Unhandled, Is.Empty, "Every request must be answered by the simulation");
            await AssertRestoredContentsAsync(options, contents, "the first backup");

            var history = new List<string>();
            for (var run = 1; run <= runs; run++)
            {
                mutator.ApplyRandomChanges(graph);
                history.Add($"run {run}: {string.Join("; ", mutator.Log)}");
                var context = $"seed {seed} {string.Join(Environment.NewLine, history.TakeLast(3))}";

                var requestsBefore = graph.Requests.Count;
                var actual = await BackupAndListAsync(options, mutator.IgnoredWarnings);
                Assert.That(graph.Unhandled, Is.Empty, $"Every request must be answered by the simulation ({context})");

                var requests = graph.Requests.Skip(requestsBefore).ToList();
                Assert.That(requests.Any(x => x.Contains("$deltatoken=", StringComparison.Ordinal) || x.Contains("/root/delta?", StringComparison.Ordinal) && x.Contains("token=", StringComparison.Ordinal) && !x.Contains("token=latest", StringComparison.Ordinal)),
                    Is.True, $"The run should have used stored delta tokens ({context})");

                if (mutator.HardFault)
                {
                    // The failed walk carried its folder or drive forward unchanged; a clean run converges
                    history.Add($"run {run} (recovery)");
                    actual = await BackupAndListAsync(options, []);
                    Assert.That(graph.Unhandled, Is.Empty);
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
