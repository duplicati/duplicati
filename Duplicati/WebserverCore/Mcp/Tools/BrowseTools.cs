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

using System.ComponentModel;
using Duplicati.Library.Interface;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Duplicati.WebserverCore.Mcp.Tools;

/// <summary>
/// MCP tools for looking at what a backup contains
/// </summary>
/// <param name="connection">The server database connection</param>
/// <param name="queueRunnerService">The task queue</param>
/// <param name="mcpTaskService">The service listing versions</param>
/// <param name="scope">What the caller is allowed to do</param>
/// <remarks>
/// The default values on the tool methods are what marks an argument as optional in the tool schema,
/// so an agent can leave it out. They are part of the MCP interface, not a convenience for callers in code.
/// </remarks>
[McpServerToolType]
public class BrowseTools(Connection connection, IQueueRunnerService queueRunnerService, McpTaskService mcpTaskService, McpScope scope)
{
    /// <summary>
    /// The page size used when the caller does not give one
    /// </summary>
    private const int DefaultPageSize = 200;

    /// <summary>
    /// The largest page size a caller can ask for
    /// </summary>
    private const int MaxPageSize = 1000;

    [McpServerTool(Name = "list_versions", Title = "List backup versions", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the versions (points in time) of a backup, newest first. Pass the time of a version to restore, list_files or search_files. Version numbers shift each time a backup runs; times do not. A backup that has not run yet has no versions.")]
    public Task<McpVersionList> ListVersions(
        [Description("The ID of the backup from list_backups, or its exact name")] string backup)
        => McpHelpers.RunAsync("list_versions", backup, scope, McpTokenLevel.Read, async () =>
            new McpVersionList(await mcpTaskService.ListVersionsAsync(McpHelpers.ResolveBackup(connection, scope, backup)).ConfigureAwait(false)));

    [McpServerTool(Name = "list_files", Title = "List files in a backup", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Lists the files and folders directly inside one folder of a backup version, one page at a time. Leave out path to get the top-level folders of the backup, which are returned on a single page. totalCount is only given with the first page; a page with fewer entries than pageSize is the last one.")]
    public Task<McpFileList> ListFiles(
        [Description("The ID of the backup from list_backups, or its exact name")] string backup,
        [Description("The folder to list, as a full path. Leave out for the top level.")] string? path = null,
        [Description("The time of the version to look in, from list_versions or a restore point; a time between two versions selects the older one. Leave out for the newest version.")] string? time = null,
        [Description("The page to return, starting at 0. Leave out for the first page.")] int? page = null,
        [Description("The number of entries per page, at most 1000. Leave out for 200.")] int? pageSize = null)
        => McpHelpers.RunAsync("list_files", backup, scope, McpTokenLevel.Read, async () =>
        {
            var bk = McpHelpers.ResolveBackup(connection, scope, backup);

            var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
            var pageNumber = Math.Max(page ?? 0, 0);
            var topLevel = string.IsNullOrWhiteSpace(path);

            // The list needs the exact time of a version
            var version = await mcpTaskService.ResolveExistingVersionAsync(bk, time).ConfigureAwait(false);

            // The engine returns the whole top level whatever page is asked for,
            // so a caller reading page after page until one is empty would never stop
            if (topLevel && pageNumber > 0)
                return new McpFileList([], pageNumber, size, null);

            var r = await queueRunnerService.RunImmediatelyAsync(Runner.CreateListFolderContents(
                bk,
                topLevel ? null : [path!],
                version.Time,
                size,
                pageNumber,
                false)).ConfigureAwait(false) as IListFolderResults
                ?? throw new McpException("The list operation returned no result.");

            return new McpFileList(
                r.Entries.Items.Select(x => new McpFileEntry(x.Path, x.IsDirectory, x.Size, x.LastModified, null)).ToList(),
                pageNumber,
                size,
                GetTotalCount(r.Entries.TotalCount));
        });

    [McpServerTool(Name = "search_files", Title = "Search files in a backup", ReadOnly = true, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description("Finds files and folders in a backup whose path matches a filter, one page at a time. The filter is text with * and ? wildcards, such as *report*.docx. totalCount is only given with the first page; a page with fewer entries than pageSize is the last one.")]
    public Task<McpFileList> SearchFiles(
        [Description("The ID of the backup from list_backups, or its exact name")] string backup,
        [Description("The filter to match paths against, such as *.sqlite")] string filter,
        [Description("The time of the version to search, from list_versions or a restore point; a time between two versions selects the older one. Leave out for the newest version.")] string? time = null,
        [Description("The page to return, starting at 0. Leave out for the first page.")] int? page = null,
        [Description("The number of entries per page, at most 1000. Leave out for 200.")] int? pageSize = null)
        => McpHelpers.RunAsync("search_files", backup, scope, McpTokenLevel.Read, async () =>
        {
            if (string.IsNullOrWhiteSpace(filter))
                throw new McpException("No filter given.");

            var bk = McpHelpers.ResolveBackup(connection, scope, backup);

            var size = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);
            var pageNumber = Math.Max(page ?? 0, 0);

            // A search covers every version up to the time it is given, and all versions without one,
            // so the single version to search is given by its number, where 0 is the newest
            var version = await mcpTaskService.ResolveExistingVersionAsync(bk, time).ConfigureAwait(false);
            var r = await queueRunnerService.RunImmediatelyAsync(Runner.CreateSearchEntriesTask(
                bk,
                [filter],
                false,
                [],
                new DateTime(0),
                [version.Version],
                size,
                pageNumber,
                false,
                false)).ConfigureAwait(false) as ISearchFilesResults
                ?? throw new McpException("The search operation returned no result.");

            return new McpFileList(
                r.FileVersions.Items.Select(x => new McpFileEntry(x.Path, x.IsDirectory, x.Size, x.LastModified, x.Version)).ToList(),
                pageNumber,
                size,
                GetTotalCount(r.FileVersions.TotalCount));
        });

    /// <summary>
    /// Gets the total number of entries from the count the engine reports,
    /// which is negative on the pages where it does not count them
    /// </summary>
    /// <param name="totalCount">The count from the engine</param>
    /// <returns>The total, or <c>null</c> if it was not counted</returns>
    private static long? GetTotalCount(long totalCount)
        => totalCount < 0 ? null : totalCount;
}
