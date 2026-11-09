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
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Restores group document library items into a group of a simulated tenant
/// </summary>
[NonParallelizable]
public class Office365RestoreProviderTests
{
    private const string GRAPH = "https://graph.simulated.test";
    private const string GROUP_ID = "group-1";
    private const string DEFAULT_DRIVE_ID = "drive-documents";
    private const string OTHER_DRIVE_ID = "drive-archive";

    /// <summary>
    /// An item in a drive of the simulated tenant
    /// </summary>
    /// <param name="Id">The item ID</param>
    /// <param name="DriveId">The drive the item is in</param>
    /// <param name="ParentId">The ID of the parent folder, or "root"</param>
    /// <param name="Name">The item name</param>
    /// <param name="Content">The file content, or null for a folder</param>
    private sealed record SimItem(string Id, string DriveId, string ParentId, string Name, byte[]? Content);

    /// <summary>
    /// Answers the Graph requests made when restoring drive items into a group
    /// </summary>
    private sealed class FakeGraph : HttpMessageHandler
    {
        public readonly List<SimItem> Items = [];
        public readonly List<string> UnexpectedRequests = [];
        private int _nextId;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(request.RequestUri!.AbsolutePath);
            var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var method = request.Method.Method;

            lock (Items)
            {
                if (method == "GET" && path == "/v1.0/groups")
                    return Json(new { value = new[] { new { id = GROUP_ID, displayName = "Team", groupTypes = new[] { "Unified" }, mailEnabled = true, securityEnabled = false, createdDateTime = "2024-01-01T00:00:00Z" } } });

                if (method == "GET" && path == $"/v1.0/groups/{GROUP_ID}/drive")
                    return Json(new { id = DEFAULT_DRIVE_ID, name = "Documents" });

                if (method == "GET" && path == $"/v1.0/groups/{GROUP_ID}/drives")
                    return Json(new { value = new[] { new { id = DEFAULT_DRIVE_ID, name = "Documents" }, new { id = OTHER_DRIVE_ID, name = "Archive" } } });
            }

            // /v1.0/drives/{drive}/items/{item}...
            if (segments.Length >= 5 && segments[1] == "drives" && segments[3] == "items")
            {
                var driveId = segments[2];
                var rest = string.Join('/', segments.Skip(4));
                var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

                lock (Items)
                {
                    // GET {parent}:/{name}
                    if (method == "GET" && rest.Contains(":/"))
                    {
                        var parts = rest.Split(":/", 2);
                        var existing = Items.FirstOrDefault(x => x.DriveId == driveId && x.ParentId == parts[0] && x.Name == parts[1]);
                        return existing == null
                            ? new HttpResponseMessage(HttpStatusCode.NotFound)
                            : Json(ToJson(existing));
                    }

                    // POST {parent}/children
                    if (method == "POST" && rest.EndsWith("/children", StringComparison.Ordinal))
                    {
                        var parentId = rest[..^"/children".Length];
                        var name = JsonNode.Parse(body!)!["name"]!.GetValue<string>();
                        var folder = new SimItem($"folder-{++_nextId}", driveId, parentId, name, null);
                        Items.Add(folder);
                        return Json(ToJson(folder));
                    }

                    // PUT {parent}:/{name}:/content
                    if (method == "PUT" && rest.EndsWith(":/content", StringComparison.Ordinal))
                    {
                        var parts = rest[..^":/content".Length].Split(":/", 2);
                        var file = new SimItem($"file-{++_nextId}", driveId, parts[0], parts[1], body);
                        Items.Add(file);
                        return Json(ToJson(file));
                    }

                    // PATCH {item}, updating the timestamps
                    if (method == "PATCH" && !rest.Contains('/'))
                        return Json(new { id = rest });
                }
            }

            lock (Items)
                UnexpectedRequests.Add($"{method} {path}");
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static object ToJson(SimItem item)
            => item.Content == null
                ? new { id = item.Id, name = item.Name, folder = new { childCount = 0 } }
                : new { id = item.Id, name = item.Name, size = item.Content.LongLength, file = new { mimeType = "application/octet-stream" } };

        private static HttpResponseMessage Json(object value)
            => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };

        /// <summary>
        /// Gets the path of an item from the drive root, made from the item names
        /// </summary>
        public string PathOf(SimItem item)
        {
            var names = new List<string> { item.Name };
            var parentId = item.ParentId;
            while (parentId != "root")
            {
                var parent = Items.Single(x => x.Id == parentId);
                names.Insert(0, parent.Name);
                parentId = parent.ParentId;
            }
            return string.Join('/', names);
        }
    }

    /// <summary>
    /// Builds a restore path from its segments, ending with a separator for a folder
    /// </summary>
    private static string P(bool folder, params string[] segments)
        => string.Join(Path.DirectorySeparatorChar, segments) + (folder ? Path.DirectorySeparatorChar.ToString() : "");

    private static Dictionary<string, string?> DriveFileMetadata(string id, string name)
        => new()
        {
            ["o365:v"] = "1",
            ["o365:Id"] = id,
            ["o365:Type"] = "DriveFile",
            ["o365:Name"] = name,
            ["o365:LastModifiedDateTime"] = "2024-01-02T00:00:00Z",
        };

    private static Dictionary<string, string?> DriveFolderMetadata(string id, string name)
        => new()
        {
            ["o365:v"] = "1",
            ["o365:Id"] = id,
            ["o365:Type"] = "DriveFolder",
            ["o365:Name"] = name,
        };

    private static Dictionary<string, string?> DriveMetadata(string id, string name)
        => new()
        {
            ["o365:v"] = "1",
            ["o365:Id"] = id,
            ["o365:Type"] = "Drive",
            ["o365:Name"] = name,
        };

    /// <summary>
    /// Restores entries into the group, the way the restore hands them to the provider
    /// </summary>
    /// <param name="graph">The simulated tenant</param>
    /// <param name="entries">The restored entries with their metadata and, for files, their content</param>
    /// <returns>The warnings and errors written during the restore</returns>
    private static async Task<List<LogEntry>> RestoreToGroupAsync(FakeGraph graph, IEnumerable<(string Path, Dictionary<string, string?> Metadata, byte[]? Content)> entries)
    {
        var options = new Dictionary<string, string?>
        {
            ["office365-tenant-id"] = "tenant",
            ["office365-client-id"] = "client",
            ["office365-client-secret"] = "secret",
            ["office365-graph-base-url"] = GRAPH,
            ["overwrite"] = "true",
        };

        var messages = new List<LogEntry>();
        using (Log.StartIsolatingScope(true))
        using (Log.StartScope(messages.Add, x => x.Level >= LogMessageType.Warning))
        using (Office365TestSupport.UseGraphHandler(graph))
        // The target path uses the platform separator and is encoded, as the restore page sends it
        using (var provider = new RestoreProvider($"office365://{Uri.EscapeDataString(P(true, "groups", GROUP_ID))}", options))
        {
            await provider.Initialize(CancellationToken.None);
            Assert.That(provider.RestoreTarget?.Type.ToString(), Is.EqualTo("Group"), "The restore target is the group");

            foreach (var (path, metadata, content) in entries)
            {
                if (content != null)
                    using (var stream = await provider.OpenWrite(path, CancellationToken.None))
                        await stream.WriteAsync(content);

                await provider.WriteMetadata(path, metadata, false, false, false, CancellationToken.None);
            }

            await provider.Finalize(null, true, CancellationToken.None);
        }

        return messages;
    }

    [Test]
    [Category("Office365")]
    public async Task SingleGroupFileIsRestoredToTheDefaultLibrary()
    {
        // A single file selected for restore has its path compressed to below the target
        var graph = new FakeGraph();
        var content = Encoding.UTF8.GetBytes("agenda");
        var messages = await RestoreToGroupAsync(graph, [
            (P(false, "groups", GROUP_ID, "item-agenda"), DriveFileMetadata("item-agenda", "Agenda.pdf"), content)
        ]);

        Assert.That(messages.Select(x => x.FormattedMessage), Is.Empty, "The restore writes no warnings");
        Assert.That(graph.UnexpectedRequests, Is.Empty);

        var file = graph.Items.Single(x => x.Content != null);
        Assert.That(file.DriveId, Is.EqualTo(DEFAULT_DRIVE_ID), "The file is restored to the group's default library");
        Assert.That(graph.PathOf(file), Is.EqualTo("Restored/Agenda.pdf"));
        Assert.That(file.Content, Is.EqualTo(content));
    }

    [Test]
    [Category("Office365")]
    public async Task SelectedGroupFolderIsRestoredWithItsContents()
    {
        // The selected folder is kept and the path above it is compressed
        var graph = new FakeGraph();
        var content = Encoding.UTF8.GetBytes("notes");
        var messages = await RestoreToGroupAsync(graph, [
            (P(true, "groups", GROUP_ID, "item-meetings"), DriveFolderMetadata("item-meetings", "Meetings"), null),
            (P(true, "groups", GROUP_ID, "item-meetings", "item-2024"), DriveFolderMetadata("item-2024", "2024"), null),
            (P(false, "groups", GROUP_ID, "item-meetings", "item-2024", "item-notes"), DriveFileMetadata("item-notes", "Notes.txt"), content),
            (P(false, "groups", GROUP_ID, "item-meetings", "item-top"), DriveFileMetadata("item-top", "Top.txt"), content),
        ]);

        Assert.That(messages.Select(x => x.FormattedMessage), Is.Empty, "The restore writes no warnings");
        Assert.That(graph.UnexpectedRequests, Is.Empty);
        Assert.That(graph.Items.Where(x => x.Content != null).Select(graph.PathOf).Order(),
            Is.EqualTo(new[] { "Restored/Meetings/2024/Notes.txt", "Restored/Meetings/Top.txt" }));
        Assert.That(graph.Items.All(x => x.DriveId == DEFAULT_DRIVE_ID), Is.True);
    }

    [Test]
    [Category("Office365")]
    public async Task GroupLibraryIsRestoredToTheLibraryWithTheSameName()
    {
        // Selecting the group keeps the full path below it
        var graph = new FakeGraph();
        var content = Encoding.UTF8.GetBytes("report");
        var drivePath = P(true, "groups", GROUP_ID, GROUP_ID, "files", "source-archive");
        var messages = await RestoreToGroupAsync(graph, [
            (P(true, "groups", GROUP_ID, GROUP_ID, "files"), new Dictionary<string, string?> { ["o365:Type"] = "GroupFiles", ["o365:Id"] = GROUP_ID, ["o365:Name"] = "Files" }, null),
            (drivePath, DriveMetadata("source-archive", "Archive"), null),
            (drivePath + "item-root-file", DriveFileMetadata("item-root-file", "Root.txt"), content),
            (drivePath + P(true, "item-reports"), DriveFolderMetadata("item-reports", "Reports"), null),
            (drivePath + P(false, "item-reports", "item-q1"), DriveFileMetadata("item-q1", "Q1.txt"), content),
        ]);

        Assert.That(messages.Select(x => x.FormattedMessage), Is.Empty, "The restore writes no warnings");
        Assert.That(graph.UnexpectedRequests, Is.Empty);
        Assert.That(graph.Items.All(x => x.DriveId == OTHER_DRIVE_ID), Is.True, "The library is restored to the library with the same name");
        Assert.That(graph.Items.Where(x => x.Content != null).Select(graph.PathOf).Order(),
            Is.EqualTo(new[] { "Reports/Q1.txt", "Root.txt" }));
    }
}
