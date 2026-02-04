// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider
{
    internal OnenoteApiImpl OnenoteApi => new OnenoteApiImpl(_apiHelper);

    internal class OnenoteApiImpl(APIHelper provider)
    {
        internal Task<GraphNotebook> CreateUserNotebookAsync(string userIdOrUpn, string displayName, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/onenote/notebooks";

            var content = new StringContent(JsonSerializer.Serialize(new { displayName }), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphNotebook>(url, content, ct);
        }

        internal Task<GraphNotebook> CreateGroupNotebookAsync(string groupId, string displayName, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}/onenote/notebooks";

            var content = new StringContent(JsonSerializer.Serialize(new { displayName }), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphNotebook>(url, content, ct);
        }

        internal Task<GraphOnenoteSection> CreateSectionAsync(string parentId, string displayName, bool isNotebookParent, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var parent = Uri.EscapeDataString(parentId);
            var url = isNotebookParent
                ? $"{baseUrl}/v1.0/onenote/notebooks/{parent}/sections"
                : $"{baseUrl}/v1.0/onenote/sectionGroups/{parent}/sections";

            var content = new StringContent(JsonSerializer.Serialize(new { displayName }), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphOnenoteSection>(url, content, ct);
        }

        internal Task<GraphOnenoteSectionGroup> CreateSectionGroupAsync(string parentId, string displayName, bool isNotebookParent, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var parent = Uri.EscapeDataString(parentId);
            var url = isNotebookParent
                ? $"{baseUrl}/v1.0/onenote/notebooks/{parent}/sectionGroups"
                : $"{baseUrl}/v1.0/onenote/sectionGroups/{parent}/sectionGroups";

            var content = new StringContent(JsonSerializer.Serialize(new { displayName }), Encoding.UTF8, "application/json");
            return provider.PostGraphItemAsync<GraphOnenoteSectionGroup>(url, content, ct);
        }

        internal Task<GraphOnenotePage> CreatePageAsync(string sectionId, Stream contentStream, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var section = Uri.EscapeDataString(sectionId);
            var url = $"{baseUrl}/v1.0/onenote/sections/{section}/pages";

            return provider.PostGraphItemStreamAsync<GraphOnenotePage>(url, contentStream, "text/html", ct);
        }

        internal IAsyncEnumerable<GraphNotebook> GetUserNotebooks(string userIdOrUpn, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var user = Uri.EscapeDataString(userIdOrUpn);
            var url = $"{baseUrl}/v1.0/users/{user}/onenote/notebooks";

            return provider.GetAllGraphItemsAsync<GraphNotebook>(url, ct);
        }

        internal IAsyncEnumerable<GraphNotebook> GetGroupNotebooks(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}/onenote/notebooks";

            return provider.GetAllGraphItemsAsync<GraphNotebook>(url, ct);
        }
    }

    private NotebookRestoreHelper? _notebookRestoreHelper = null;
    internal NotebookRestoreHelper NotebookRestore => _notebookRestoreHelper ??= new NotebookRestoreHelper(this);

    internal class NotebookRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private string? _targetGroupId = null;
        private bool _hasLoadedTarget = false;

        private ConcurrentDictionary<string, string> _targetNotebookIds = new();

        public async Task<string?> GetTargetNotebookId(string originalPath, string displayName, CancellationToken cancel)
        {
            if (_targetNotebookIds.TryGetValue(originalPath, out var notebookId))
                return notebookId;

            if (!_hasLoadedTarget)
            {
                _hasLoadedTarget = true;
                var target = Provider.RestoreTarget;
                if (target == null)
                    throw new InvalidOperationException("Restore target is not set");

                if (target.Type == SourceItemType.User)
                {
                    _targetUserId = target.Metadata.GetValueOrDefault("o365:Id");
                }
                else if (target.Type == SourceItemType.Group || target.Type == SourceItemType.GroupTeams)
                {
                    _targetGroupId = target.Metadata.GetValueOrDefault("o365:Id");
                }
                else if (target.Type == SourceItemType.Notebook)
                {
                    var targetId = target.Metadata.GetValueOrDefault("o365:Id");
                    if (!string.IsNullOrWhiteSpace(targetId))
                    {
                        return targetId;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(_targetUserId))
            {
                // Check if notebook exists
                if (!Provider._ignoreExisting)
                {
                    await EnsureExistingNotebooksLoaded(_targetUserId, null, cancel);
                    if (_existingNotebooks.TryGetValue(displayName, out var existingId))
                    {
                        _targetNotebookIds[originalPath] = existingId;
                        return existingId;
                    }
                }

                // Create new
                var newNotebook = await Provider.OnenoteApi.CreateUserNotebookAsync(_targetUserId, displayName, cancel);
                _targetNotebookIds[originalPath] = newNotebook.Id;
                return newNotebook.Id;
            }
            else if (!string.IsNullOrWhiteSpace(_targetGroupId))
            {
                // Check if notebook exists
                if (!Provider._ignoreExisting)
                {
                    await EnsureExistingNotebooksLoaded(null, _targetGroupId, cancel);
                    if (_existingNotebooks.TryGetValue(displayName, out var existingId))
                    {
                        _targetNotebookIds[originalPath] = existingId;
                        return existingId;
                    }
                }

                // Create new
                var newNotebook = await Provider.OnenoteApi.CreateGroupNotebookAsync(_targetGroupId, displayName, cancel);
                _targetNotebookIds[originalPath] = newNotebook.Id;
                return newNotebook.Id;
            }

            return null;
        }

        private Dictionary<string, string> _existingNotebooks = new(StringComparer.OrdinalIgnoreCase);
        private bool _hasLoadedExistingNotebooks = false;

        private async Task EnsureExistingNotebooksLoaded(string? userId, string? groupId, CancellationToken cancel)
        {
            if (_hasLoadedExistingNotebooks)
                return;

            _hasLoadedExistingNotebooks = true;

            IAsyncEnumerable<GraphNotebook>? notebooks = null;
            if (userId != null)
                notebooks = Provider.OnenoteApi.GetUserNotebooks(userId, cancel);
            else if (groupId != null)
                notebooks = Provider.OnenoteApi.GetGroupNotebooks(groupId, cancel);

            if (notebooks != null)
            {
                await foreach (var notebook in notebooks)
                {
                    if (!string.IsNullOrWhiteSpace(notebook.DisplayName) && !string.IsNullOrWhiteSpace(notebook.Id))
                    {
                        _existingNotebooks.TryAdd(notebook.DisplayName, notebook.Id);
                    }
                }
            }
        }
    }

    private async Task RestoreNotebooks(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var notebooks = GetMetadataByType(SourceItemType.Notebook);
        if (notebooks.Count == 0)
            return;

        foreach (var notebook in notebooks)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = notebook.Key;
                var metadata = notebook.Value;
                var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? "Restored Notebook";

                var notebookId = await NotebookRestore.GetTargetNotebookId(originalPath, displayName, cancel);
                if (notebookId != null)
                {
                    _restoredNotebookMap[originalPath] = notebookId;
                    _metadata.TryRemove(originalPath, out _);
                }
                else
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreNotebooksMissingTarget", null, $"Could not determine target for notebook {notebook.Key}");
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreNotebooksFailed", ex, $"Failed to restore notebook {notebook.Key}");
            }
        }
    }

    private async Task RestoreNotebookSectionGroups(CancellationToken cancel)
    {
        var sectionGroups = GetMetadataByType(SourceItemType.NotebookSectionGroup);
        if (sectionGroups.Count == 0)
            return;

        // Sort by path length to ensure parents are created first
        var sortedGroups = sectionGroups.OrderBy(k => k.Key.Split(Path.DirectorySeparatorChar).Length).ToList();

        foreach (var group in sortedGroups)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = group.Key;
                var metadata = group.Value;
                var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? "Restored Section Group";

                // Find parent
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));

                string? parentId = null;
                bool isNotebookParent = false;

                if (parentPath != null)
                {
                    if (_restoredNotebookMap.TryGetValue(parentPath, out var notebookId))
                    {
                        parentId = notebookId;
                        isNotebookParent = true;
                    }
                    else if (_restoredSectionGroupMap.TryGetValue(parentPath, out var groupId))
                    {
                        parentId = groupId;
                        isNotebookParent = false;
                    }
                }

                if (parentId == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreNotebookSectionGroupsMissingParent", null, $"Could not find parent for section group {originalPath}, skipping.");
                    continue;
                }

                var newGroup = await OnenoteApi.CreateSectionGroupAsync(parentId, displayName, isNotebookParent, cancel);
                _restoredSectionGroupMap[originalPath] = newGroup.Id;
                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreNotebookSectionGroupsFailed", ex, $"Failed to restore section group {group.Key}");
            }
        }
    }

    private async Task RestoreNotebookSections(CancellationToken cancel)
    {
        var sections = GetMetadataByType(SourceItemType.NotebookSection);
        if (sections.Count == 0)
            return;

        foreach (var section in sections)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = section.Key;
                var metadata = section.Value;
                var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? "Restored Section";

                // Find parent
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));

                string? parentId = null;
                bool isNotebookParent = false;

                if (parentPath != null)
                {
                    if (_restoredNotebookMap.TryGetValue(parentPath, out var notebookId))
                    {
                        parentId = notebookId;
                        isNotebookParent = true;
                    }
                    else if (_restoredSectionGroupMap.TryGetValue(parentPath, out var groupId))
                    {
                        parentId = groupId;
                        isNotebookParent = false;
                    }
                }

                if (parentId == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreNotebookSectionsMissingParent", null, $"Could not find parent for section {originalPath}, skipping.");
                    continue;
                }

                var newSection = await OnenoteApi.CreateSectionAsync(parentId, displayName, isNotebookParent, cancel);
                _restoredSectionMap[originalPath] = newSection.Id;
                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreNotebookSectionsFailed", ex, $"Failed to restore section {section.Key}");
            }
        }
    }

    private async Task RestoreNotebookPages(CancellationToken cancel)
    {
        // Iterate over temporary files to find pages
        // Pages are .html files whose parent is a restored section

        var files = _temporaryFiles.Keys.ToList();
        foreach (var filePath in files)
        {
            if (cancel.IsCancellationRequested)
                break;

            if (!filePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
                continue;

            var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(filePath.TrimEnd(Path.DirectorySeparatorChar)));
            if (parentPath == null)
                continue;

            if (_restoredSectionMap.TryGetValue(parentPath, out var sectionId))
            {
                try
                {
                    var contentEntry = _temporaryFiles[filePath];
                    using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                    {
                        await OnenoteApi.CreatePageAsync(sectionId, contentStream, cancel);
                    }

                    _temporaryFiles.TryRemove(filePath, out var file);
                    file?.Dispose();
                    _metadata.TryRemove(filePath, out _);
                }
                catch (Exception ex)
                {
                    Log.WriteErrorMessage(LOGTAG, "RestoreNotebookPagesFailed", ex, $"Failed to restore page {filePath}");
                }
            }
        }
    }
}
