// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

public partial class RestoreProvider : IRestoreDestinationProviderModule
{
    /// <summary>
    /// Log tag for RestoreProvider
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<RestoreProvider>();

    /// <summary>
    /// API helper for accessing Office365 APIs
    /// </summary>
    private readonly APIHelper _apiHelper;
    /// <summary>
    /// Path to restore to
    /// </summary>
    private readonly string _restorePath;
    /// <summary>
    /// Source provider for accessing currently existing items on the destination tenant
    /// </summary>
    internal SourceProvider SourceProvider { get; init; }

    /// <summary>
    /// Properties related to the restore target
    /// </summary>
    /// <param name="Path">The path to restore to</param>
    /// <param name="Entry">The source provider entry to restore to</param>
    /// <param name="Metadata">The metadata associated with the restore target</param>
    /// <param name="Type">The type of the restore target</param>
    internal record RestoreTargetData(
        string Path,
        ISourceProviderEntry Entry,
        Dictionary<string, string?> Metadata,
        SourceItemType Type);

    /// <summary>
    /// The restore target data
    /// </summary>
    internal RestoreTargetData? RestoreTarget { get; private set; }

    /// <summary>
    /// Temporary files created during the restore process
    /// </summary>
    private readonly ConcurrentDictionary<string, TempFile> _temporaryFiles = new();
    /// <summary>
    /// Metadata recorded during the restore process
    /// </summary>
    private readonly ConcurrentDictionary<string, Dictionary<string, string?>> _metadata = new();
    /// <summary>
    /// Map of restored email folder paths to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredEmailFolderMap = new();

    /// <summary>
    /// Map of restored contact folder paths to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredContactFolderMap = new();

    /// <summary>
    /// Map of restored drive paths to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredDriveMap = new();

    /// <summary>
    /// Map of restored drive folder paths to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredDriveFolderMap = new();

    /// <summary>
    /// Map of restored planner buckets to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredPlannerBucketMap = new();

    /// <summary>
    /// Map of restored notebooks to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredNotebookMap = new();

    /// <summary>
    /// Map of restored section groups to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredSectionGroupMap = new();

    /// <summary>
    /// Map of restored sections to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredSectionMap = new();

    /// <summary>
    /// Map of restored task lists to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredTaskListMap = new();

    /// <summary>
    /// Map of restored tasks to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredTaskMap = new();

    /// <summary>
    /// Whether to overwrite existing items
    /// </summary>
    internal readonly bool _overwrite;

    /// <summary>
    /// Default constructor for the restore provider.
    /// Only used for loading metadata about the provider.
    /// </summary>
    public RestoreProvider()
    {
        _apiHelper = null!;
        _restorePath = null!;
        SourceProvider = null!;
    }

    /// <summary>
    /// Constructs the RestoreProvider with the given URL and options.
    /// </summary>
    /// <param name="url">The destination URL for the restore operation</param>
    /// <param name="options">The options for the restore operation</param>
    public RestoreProvider(string url, Dictionary<string, string?> options)
    {
        var uri = new Library.Utility.Uri(url);
        _restorePath = uri.HostAndPath;

        var parsedOptions = OptionsHelper.ParseAndValidateOptions(url, options);
        _apiHelper = APIHelper.Create(
            tenantId: parsedOptions.TenantId,
            authOptions: parsedOptions.AuthOptions,
            graphBaseUrl: parsedOptions.GraphBaseUrl,
            certificatePath: parsedOptions.CertificatePath,
            certificatePassword: parsedOptions.CertificatePassword,
            timeouts: TimeoutOptionsHelper.Parse(options)
        );

        _overwrite = Utility.ParseBoolOption(options, "overwrite");

        var sourceOpts = new Dictionary<string, string?>(options)
        {
            { "store-metadata-content-in-database", "true" }
        };

        SourceProvider = new SourceProvider("office365://", "", sourceOpts)
        {
            // Make sure we can reach all items during restore
            UsedForRestoreOperation = true
        };
    }

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public string DisplayName => Strings.ProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.ProviderDescription;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

    /// <inheritdoc />
    public string TargetDestination => _restorePath;

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
    {
        // TODO: Do we need to do anything here?
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task Initialize(CancellationToken cancel)
    {
        await _apiHelper.AcquireAccessTokenAsync(true, cancel);
        await SourceProvider.Initialize(cancel);
        var entry = await SourceProvider.GetEntry(_restorePath, isFolder: true, cancel);
        if (entry == null)
            throw new UserInformationException($"Restore target path not found: {_restorePath}", "RestoreTargetNotFound");
        var metadata = await entry.GetMinorMetadata(cancel);
        var type = metadata.TryGetValue("o365:Type", out var typeStr)
            && Enum.TryParse<SourceItemType>(typeStr, out var sourceItemType)
            ? sourceItemType
            : throw new UserInformationException($"Invalid restore target type in metadata: {typeStr}", "InvalidRestoreTargetType");

        RestoreTarget = new RestoreTargetData(_restorePath, entry, metadata, type);
    }

    /// <summary>
    /// Normalizes the given path by applying the RemovedRestorePrefix if it has been removed
    /// </summary>
    /// <param name="path">The path to normalize</param>
    /// <returns>The normalized path</returns>
    private string NormalizePath(string path)
        => path;

    /// <inheritdoc />
    public void Dispose()
    {
        _apiHelper?.Dispose();
        SourceProvider?.Dispose();

        foreach (var file in _temporaryFiles.Values)
            file.Dispose();
        _temporaryFiles.Clear();
    }

    /// <inheritdoc />
    public async Task<bool> FileExists(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_temporaryFiles.ContainsKey(path))
            return true;

        var entry = await SourceProvider.GetEntry(path, isFolder: false, cancel).ConfigureAwait(false);
        return entry != null;
    }

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        var file = _temporaryFiles.GetOrAdd(path, _ => new TempFile());
        return Task.FromResult<Stream>(SystemIO.IO_OS.FileOpenWrite(file));
    }

    /// <inheritdoc />
    public async Task<Stream> OpenRead(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_temporaryFiles.ContainsKey(path))
            return SystemIO.IO_OS.FileOpenRead(_temporaryFiles[path]);

        var entry = await SourceProvider.GetEntry(path, isFolder: false, cancel).ConfigureAwait(false);
        if (entry != null)
            return await entry.OpenRead(cancel).ConfigureAwait(false);

        throw new FileNotFoundException($"File not found: {path}");
    }

    /// <inheritdoc />
    public async Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_temporaryFiles.ContainsKey(path))
            return SystemIO.IO_OS.FileOpenReadWrite(_temporaryFiles[path]);

        var entry = await SourceProvider.GetEntry(path, isFolder: false, cancel).ConfigureAwait(false);
        if (entry == null)
        {
            _temporaryFiles.GetOrAdd(path, _ => new TempFile());
            return SystemIO.IO_OS.FileOpenReadWrite(_temporaryFiles[path]);
        }

        if (entry.IsFolder)
            throw new FileNotFoundException($"Path is a folder: {path}");

        var file = new TempFile();
        using (var fs = SystemIO.IO_OS.FileOpenWrite(file))
        using (var stream = await entry.OpenRead(cancel).ConfigureAwait(false))
            await stream.CopyToAsync(fs).ConfigureAwait(false);

        _temporaryFiles[path] = file;
        return SystemIO.IO_OS.FileOpenReadWrite(file);
    }

    /// <inheritdoc />
    public async Task<long> GetFileLength(string path, CancellationToken cancel)
    {
        path = NormalizePath(path);

        if (_temporaryFiles.ContainsKey(path))
            return SystemIO.IO_OS.FileLength(_temporaryFiles[path]);

        var entry = await SourceProvider.GetEntry(path, isFolder: false, cancel).ConfigureAwait(false);
        if (entry == null)
            throw new FileNotFoundException($"File not found: {path}");

        return entry.Size;
    }

    /// <inheritdoc />
    public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel)
        => Task.FromResult(false);

    /// <inheritdoc />
    public Task ClearReadOnlyAttribute(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, CancellationToken cancel)
    {
        path = NormalizePath(path);

        _metadata.AddOrUpdate(path, metadata, (_, _) => metadata);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task DeleteFolder(string path, CancellationToken cancel)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task DeleteFile(string path, CancellationToken cancel)
        // Should only happen if attempting to restore a symlink, which is not supported
        => throw new NotImplementedException("File deletion is not supported in Office365 RestoreProvider");

    /// <inheritdoc />
    public async Task Finalize(Action<double>? progressCallback, CancellationToken cancel)
    {
        var totalFiles = _metadata.Count;
        try
        {
            await RestoreUserEmailFolders(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserEmailFoldersFailed", ex, $"Failed to restore user email folders: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreUserEmails(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserEmailsFailed", ex, $"Failed to restore user emails: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreUserMailboxSettings(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserMailboxSettingsFailed", ex, $"Failed to restore user mailbox settings: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreUserMailboxRules(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserMailboxRulesFailed", ex, $"Failed to restore user mailbox rules: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreDrives(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreDrivesFailed", ex, $"Failed to restore drives: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreSiteDrives(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreSiteDrivesFailed", ex, $"Failed to restore site drives: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreSharePointLists(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreSharePointListsFailed", ex, $"Failed to restore SharePoint lists: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreSharePointListItems(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreSharePointListItemsFailed", ex, $"Failed to restore SharePoint list items: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreDriveFolders(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreDriveFoldersFailed", ex, $"Failed to restore drive folders: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreDriveFiles(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreDriveFilesFailed", ex, $"Failed to restore drive files: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreCalendarEvents(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreCalendarEventsFailed", ex, $"Failed to restore calendar events: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreContactFolders(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreContactFoldersFailed", ex, $"Failed to restore contact folders: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreContacts(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreContactsFailed", ex, $"Failed to restore contacts: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreGroupChannels(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGroupChannelsFailed", ex, $"Failed to restore group channels: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreGroupSettings(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGroupSettingsFailed", ex, $"Failed to restore group settings: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreGroupMembers(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGroupMembersFailed", ex, $"Failed to restore group members: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreGroupOwners(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGroupOwnersFailed", ex, $"Failed to restore group owners: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreTeamApps(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreTeamAppsFailed", ex, $"Failed to restore team apps: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreChannelTabs(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreChannelTabsFailed", ex, $"Failed to restore channel tabs: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreChannelMessages(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreChannelMessagesFailed", ex, $"Failed to restore channel messages: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreGroupConversations(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGroupConversationsFailed", ex, $"Failed to restore group conversations: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreGroupCalendarEvents(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGroupCalendarEventsFailed", ex, $"Failed to restore group calendar events: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestorePlannerBuckets(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestorePlannerBucketsFailed", ex, $"Failed to restore planner buckets: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestorePlannerTasks(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestorePlannerTasksFailed", ex, $"Failed to restore planner tasks: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreNotebooks(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreNotebooksFailed", ex, $"Failed to restore notebooks: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreNotebookSectionGroups(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreNotebookSectionGroupsFailed", ex, $"Failed to restore notebook section groups: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreNotebookSections(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreNotebookSectionsFailed", ex, $"Failed to restore notebook sections: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreNotebookPages(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreNotebookPagesFailed", ex, $"Failed to restore notebook pages: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreTaskLists(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreTaskListsFailed", ex, $"Failed to restore task lists: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreTaskListTasks(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreTaskListTasksFailed", ex, $"Failed to restore task list tasks: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreTaskChecklistItems(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreTaskChecklistItemsFailed", ex, $"Failed to restore task checklist items: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreTaskLinkedResources(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreTaskLinkedResourcesFailed", ex, $"Failed to restore task linked resources: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreChats(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreChatsFailed", ex, $"Failed to restore chats: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreChatMessages(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreChatMessagesFailed", ex, $"Failed to restore chat messages: {ex.Message}");
        }

        progressCallback?.Invoke(_metadata.Count / (double)totalFiles);

        try
        {
            await RestoreChatHostedContent(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreChatHostedContentFailed", ex, $"Failed to restore chat hosted content: {ex.Message}");
        }

        try
        {
            await RestoreUserProfile(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserProfileFailed", ex, $"Failed to restore user profile: {ex.Message}");
        }

        // We should have restored all items by now
        if (_metadata.Count > 0 || _temporaryFiles.Count > 0)
            Log.WriteWarningMessage(LOGTAG, "FinalizeIncomplete", null, $"Some items were not restored. Remaining metadata items: {_metadata.Count}, remaining temporary files: {_temporaryFiles.Count}");

        var tempFiles = _temporaryFiles.Values.ToList();
        foreach (var file in tempFiles)
            file.Dispose();
        _temporaryFiles.Clear();
        _metadata.Clear();
    }

    /// <summary>
    /// Gets metadata entries by their source item type
    /// </summary>
    /// <param name="type">The source item type to filter by</param>
    /// <returns>The list of metadata entries matching the given type</returns>
    private List<KeyValuePair<string, Dictionary<string, string?>>> GetMetadataByType(SourceItemType type)
        => _metadata
            .Where(kv => kv.Value.TryGetValue("o365:Type", out var typeStr)
                && typeStr == type.ToString())
            .ToList();

    /// <summary>
    /// Restores user email folders and updates the folder ID map
    /// </summary>
    /// <param name="cancel">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task RestoreUserEmailFolders(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        // Find email entries in _metadata
        var emailFolders = GetMetadataByType(SourceItemType.UserMailboxFolder);
        if (emailFolders.Count == 0)
            return;

        (var userId, var mailboxId) = await EmailRestore.GetUserIdAndMailboxTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(mailboxId))
            return; // A warning has already been logged

        // Create folders
        // Sort folders by path length to ensure parents are created before children
        var sortedFolders = emailFolders.OrderBy(k => k.Key.Split(Path.DirectorySeparatorChar).Length).ToList();

        foreach (var folder in sortedFolders)
        {
            if (cancel.IsCancellationRequested)
                break;

            var originalPath = folder.Key;
            var metadata = folder.Value;
            var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? metadata.GetValueOrDefault("o365:Id");

            _metadata.TryRemove(originalPath, out _);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreUserEmailFoldersMissingName", null, $"Missing display name for folder {originalPath}, skipping.");
                continue;
            }

            // Determine parent folder ID
            // We assume the path structure reflects the hierarchy
            // If parent path is not in map, we assume it's the root (mailboxId)
            var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
            var parentId = mailboxId;

            if (parentPath != null && _restoredEmailFolderMap.TryGetValue(parentPath, out var mappedParentId))
                parentId = mappedParentId;

            try
            {
                // Check if folder exists
                var existingFolder = await EmailApi.GetChildFolderAsync(userId, parentId, displayName, cancel);
                if (existingFolder != null)
                {
                    _restoredEmailFolderMap[originalPath] = existingFolder.Id;
                }
                else
                {
                    var newFolder = await EmailApi.CreateMailFolderAsync(userId, parentId, displayName, cancel);
                    _restoredEmailFolderMap[originalPath] = newFolder.Id;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreUserEmailFoldersFailed", ex, $"Failed to restore folder {displayName} at {originalPath}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Restores user emails
    /// </summary>
    /// <param name="cancel">>The cancellation token</param>
    /// <returns>An awaitable task</returns>
    private async Task RestoreUserEmails(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        // Find email entries in _metadata
        var emailSources = GetMetadataByType(SourceItemType.UserMailboxEmail);
        if (emailSources.Count == 0)
            return;

        (var userId, var mailboxId) = await EmailRestore.GetUserIdAndMailboxTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(mailboxId))
            return; // A warning has already been logged

        // Restore emails
        foreach (var emailSource in emailSources)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var contentPath = SystemIO.IO_OS.PathCombine(emailSource.Key, "content.eml");
                var metadataPath = SystemIO.IO_OS.PathCombine(emailSource.Key, "metadata.json");

                var internetMessageId = emailSource.Value.GetValueOrDefault("o365:InternetMessageId");

                // Determine target folder
                var targetFolderId = mailboxId;
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(emailSource.Key.TrimEnd(Path.DirectorySeparatorChar)));
                if (parentPath != null && _restoredEmailFolderMap.TryGetValue(parentPath, out var mappedFolderId))
                {
                    targetFolderId = mappedFolderId;
                }

                if (!string.IsNullOrWhiteSpace(internetMessageId) && await EmailApi.EmailExistsInFolderByInternetMessageIdAsync(userId, targetFolderId, internetMessageId, cancel))
                {
                    Log.WriteInformationMessage(LOGTAG, "RestoreUserEmailsSkipExisting", null, $"Email with InternetMessageId {internetMessageId} already exists in target mailbox, skipping restore for {emailSource.Key}.");
                }
                else
                {
                    var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);
                    var metadataEntry = _temporaryFiles.GetValueOrDefault(metadataPath);

                    if (contentEntry == null)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreUserEmailsMissingContent", null, $"Missing email content for {emailSource.Key}, skipping.");
                        continue;
                    }

                    if (metadataEntry == null)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreUserEmailsMissingMetadata", null, $"Missing email metadata for {emailSource.Key}, skipping.");
                        continue;
                    }


                    using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                    using (var metadataStream = SystemIO.IO_OS.FileOpenRead(metadataEntry))
                        await EmailApi.RestoreEmailToFolderAsync(userId, targetFolderId, contentStream, metadataStream, cancel);
                }

                // Clean up when done
                _metadata.TryRemove(emailSource.Key, out _);
                _metadata.TryRemove(contentPath, out _);
                _metadata.TryRemove(metadataPath, out _);
                _temporaryFiles.TryRemove(contentPath, out var contentFile);
                _temporaryFiles.TryRemove(metadataPath, out var metadataFile);
                contentFile?.Dispose();
                metadataFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreUserEmailsFailed", ex, $"Failed to restore email at {emailSource.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreDrives(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var driveSources = GetMetadataByType(SourceItemType.Drive);
        if (driveSources.Count == 0)
            return;

        if (RestoreTarget.Type == SourceItemType.Drive)
        {
            var targetDriveId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
            if (!string.IsNullOrWhiteSpace(targetDriveId))
            {
                foreach (var driveSource in driveSources)
                {
                    _restoredDriveMap[driveSource.Key] = targetDriveId;
                }
            }
            return;
        }

        string? targetUserId = null;
        string? targetSiteId = null;
        if (RestoreTarget.Type == SourceItemType.User)
        {
            targetUserId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }
        else if (RestoreTarget.Type == SourceItemType.Site)
        {
            targetSiteId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        if (!string.IsNullOrWhiteSpace(targetUserId))
        {
            try
            {
                var primaryDrive = await SourceProvider.OneDriveApi.GetUserPrimaryDriveAsync(targetUserId, cancel);
                foreach (var driveSource in driveSources)
                {
                    _restoredDriveMap[driveSource.Key] = primaryDrive.Id;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDrivesFailed", ex, $"Failed to restore drives: {ex.Message}");
            }
        }
        else if (!string.IsNullOrWhiteSpace(targetSiteId))
        {
            try
            {
                var primaryDrive = await SourceProvider.SiteApi.GetSitePrimaryDriveAsync(targetSiteId, cancel);
                foreach (var driveSource in driveSources)
                {
                    _restoredDriveMap[driveSource.Key] = primaryDrive.Id;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDrivesFailed", ex, $"Failed to restore drives: {ex.Message}");
            }
        }
    }

    private async Task RestoreDriveFolders(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var folders = GetMetadataByType(SourceItemType.DriveFolder);
        if (folders.Count == 0)
            return;

        // Sort folders by path length to ensure parents are created before children
        var sortedFolders = folders.OrderBy(k => k.Key.Split(Path.DirectorySeparatorChar).Length).ToList();

        foreach (var folder in sortedFolders)
        {
            if (cancel.IsCancellationRequested)
                break;

            var originalPath = folder.Key;
            var metadata = folder.Value;
            var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? metadata.GetValueOrDefault("o365:Id");

            _metadata.TryRemove(originalPath, out _);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingName", null, $"Missing display name for folder {originalPath}, skipping.");
                continue;
            }

            // Find the drive ID
            var drivePath = _restoredDriveMap.Keys
                .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();

            if (drivePath == null)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingDrive", null, $"Could not find restored drive for folder {originalPath}, skipping.");
                continue;
            }

            var driveId = _restoredDriveMap[drivePath];

            // Determine parent folder ID
            var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
            var parentId = "root";

            if (parentPath != null && parentPath.TrimEnd(Path.DirectorySeparatorChar) != drivePath.TrimEnd(Path.DirectorySeparatorChar))
            {
                if (_restoredDriveFolderMap.TryGetValue(parentPath, out var mappedParentId))
                {
                    parentId = mappedParentId;
                }
                else
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingParent", null, $"Could not find parent folder for {originalPath}, skipping.");
                    continue;
                }
            }

            try
            {
                var newFolder = await DriveApi.CreateDriveFolderAsync(driveId, parentId, displayName, cancel);
                _restoredDriveFolderMap[originalPath] = newFolder.Id;

                // Restore permissions
                var permissionsJson = metadata.GetValueOrDefault("o365:Permissions");
                if (!string.IsNullOrWhiteSpace(permissionsJson))
                {
                    try
                    {
                        await DriveApi.RestoreDriveItemPermissionsAsync(driveId, newFolder.Id, permissionsJson, cancel);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreDriveFolderPermissionsFailed", ex, $"Failed to restore permissions for folder {originalPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDriveFoldersFailed", ex, $"Failed to restore folder {displayName} at {originalPath}: {ex.Message}");
            }
        }
    }

    private async Task RestoreDriveFiles(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var files = GetMetadataByType(SourceItemType.DriveFile);
        if (files.Count == 0)
            return;

        foreach (var file in files)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = file.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingContent", null, $"Missing file content for {originalPath}, skipping.");
                    continue;
                }

                // Find drive ID
                var drivePath = _restoredDriveMap.Keys
                    .Where(k => originalPath.StartsWith(k + Path.DirectorySeparatorChar))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                if (drivePath == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingDrive", null, $"Could not find restored drive for file {originalPath}, skipping.");
                    continue;
                }

                var driveId = _restoredDriveMap[drivePath];

                // Determine parent folder ID
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
                var parentId = "root";

                if (parentPath != null && parentPath.TrimEnd(Path.DirectorySeparatorChar) != drivePath.TrimEnd(Path.DirectorySeparatorChar))
                {
                    if (_restoredDriveFolderMap.TryGetValue(parentPath, out var mappedParentId))
                    {
                        parentId = mappedParentId;
                    }
                    else
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingParent", null, $"Could not find parent folder for {originalPath}, skipping.");
                        continue;
                    }
                }

                var displayName = file.Value.GetValueOrDefault("o365:Name") ?? Path.GetFileName(originalPath);

                if (!_overwrite)
                {
                    var existingItem = await DriveApi.GetDriveItemAsync(driveId, parentId, displayName, cancel);
                    if (existingItem != null)
                    {
                        // Check if it's the same file
                        // We can check size
                        var sizeStr = file.Value.GetValueOrDefault("o365:Size");
                        long? sourceSize = null;
                        if (long.TryParse(sizeStr, out var s)) sourceSize = s;
                        else
                        {
                            sourceSize = SystemIO.IO_OS.FileLength(contentEntry);
                        }

                        if (existingItem.Size == sourceSize)
                        {
                            // Check hash if available
                            var sourceHash = file.Value.GetValueOrDefault("o365:QuickXorHash") ?? file.Value.GetValueOrDefault("o365:Sha1Hash");
                            var existingHash = existingItem.File?.Hashes?.QuickXorHash ?? existingItem.File?.Hashes?.Sha1Hash;

                            if (!string.IsNullOrWhiteSpace(sourceHash) && !string.IsNullOrWhiteSpace(existingHash))
                            {
                                if (string.Equals(sourceHash, existingHash, StringComparison.OrdinalIgnoreCase))
                                {
                                    Log.WriteInformationMessage(LOGTAG, "RestoreDriveFilesSkipDuplicate", null, $"Skipping duplicate file {originalPath} (Name: {displayName}, Hash match)");
                                    _metadata.TryRemove(originalPath, out _);
                                    _temporaryFiles.TryRemove(originalPath, out var cFile);
                                    cFile?.Dispose();
                                    continue;
                                }
                            }
                            else
                            {
                                // Fallback to size only if hash is missing
                                Log.WriteInformationMessage(LOGTAG, "RestoreDriveFilesSkipDuplicate", null, $"Skipping duplicate file {originalPath} (Name: {displayName}, Size match)");
                                _metadata.TryRemove(originalPath, out _);
                                _temporaryFiles.TryRemove(originalPath, out var cFile);
                                cFile?.Dispose();
                                continue;
                            }
                        }
                    }
                }

                GraphDriveItem restoredItem;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    restoredItem = await DriveApi.RestoreDriveItemToFolderAsync(driveId, parentId, displayName, contentStream, null, cancel);
                }

                // Restore metadata (timestamps)
                var createdStr = file.Value.GetValueOrDefault("o365:CreatedDateTime");
                var modifiedStr = file.Value.GetValueOrDefault("o365:LastModifiedDateTime");

                DateTimeOffset? created = null;
                DateTimeOffset? modified = null;

                if (DateTimeOffset.TryParse(createdStr, out var c)) created = c;
                if (DateTimeOffset.TryParse(modifiedStr, out var m)) modified = m;

                if (created != null || modified != null)
                {
                    try
                    {
                        await DriveApi.UpdateDriveItemFileSystemInfoAsync(driveId, restoredItem.Id, created, modified, cancel);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreDriveFileMetadataFailed", ex, $"Failed to restore metadata for file {file.Key}: {ex.Message}");
                    }
                }

                // Restore permissions
                var permissionsJson = file.Value.GetValueOrDefault("o365:Permissions");
                if (!string.IsNullOrWhiteSpace(permissionsJson))
                {
                    try
                    {
                        await DriveApi.RestoreDriveItemPermissionsAsync(driveId, restoredItem.Id, permissionsJson, cancel);
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilePermissionsFailed", ex, $"Failed to restore permissions for file {file.Key}: {ex.Message}");
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreDriveFilesFailed", ex, $"Failed to restore file {file.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreCalendarEvents(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var events = GetMetadataByType(SourceItemType.CalendarEvent);
        if (events.Count == 0)
            return;

        try
        {
            await CalendarRestore.RestoreEvents(events, cancel);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "RestoreCalendarEventsFailed", ex, $"Failed to restore calendar events: {ex.Message}");
        }
    }

    private async Task RestoreContactFolders(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var folders = GetMetadataByType(SourceItemType.UserContactFolder);
        if (folders.Count == 0)
            return;

        (var userId, var defaultFolderId) = await ContactRestore.GetUserIdAndContactFolderTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(defaultFolderId))
            return;

        // Sort folders by path length to ensure parents are created before children
        var sortedFolders = folders.OrderBy(k => k.Key.Split(Path.DirectorySeparatorChar).Length).ToList();

        foreach (var folder in sortedFolders)
        {
            if (cancel.IsCancellationRequested)
                break;

            var originalPath = folder.Key;
            var metadata = folder.Value;
            var displayName = metadata.GetValueOrDefault("o365:Name") ?? metadata.GetValueOrDefault("o365:DisplayName") ?? metadata.GetValueOrDefault("o365:Id");

            _metadata.TryRemove(originalPath, out _);

            if (string.IsNullOrWhiteSpace(displayName))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreContactFoldersMissingName", null, $"Missing display name for folder {originalPath}, skipping.");
                continue;
            }

            // Determine parent folder ID
            var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
            var parentId = defaultFolderId;

            if (parentPath != null && _restoredContactFolderMap.TryGetValue(parentPath, out var mappedParentId))
            {
                parentId = mappedParentId;
            }

            try
            {
                // Check if folder exists
                var existingFolder = await ContactApi.GetContactFolderByNameAsync(userId, displayName, parentId, cancel);
                if (existingFolder != null)
                {
                    _restoredContactFolderMap[originalPath] = existingFolder.Id;
                }
                else
                {
                    var newFolder = await ContactApi.CreateContactFolderAsync(userId, displayName, parentId, cancel);
                    _restoredContactFolderMap[originalPath] = newFolder.Id;
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreContactFoldersFailed", ex, $"Failed to restore folder {displayName} at {originalPath}: {ex.Message}");
            }
        }
    }

    private async Task RestoreContacts(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var contacts = GetMetadataByType(SourceItemType.UserContact);
        if (contacts.Count == 0)
            return;

        (var userId, var defaultFolderId) = await ContactRestore.GetUserIdAndContactFolderTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(defaultFolderId))
            return; // A warning has already been logged

        foreach (var contact in contacts)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = contact.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactsMissingContent", null, $"Missing contact content for {originalPath}, skipping.");
                    continue;
                }

                // Determine target folder
                var targetFolderId = defaultFolderId;
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
                if (parentPath != null && _restoredContactFolderMap.TryGetValue(parentPath, out var mappedFolderId))
                {
                    targetFolderId = mappedFolderId;
                }

                if (!_overwrite)
                {
                    using (var checkStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                    {
                        var contactData = await JsonSerializer.DeserializeAsync<GraphContact>(checkStream, cancellationToken: cancel);
                        if (contactData != null)
                        {
                            string? email = null;
                            if (contactData.EmailAddresses != null && contactData.EmailAddresses.Count > 0)
                            {
                                email = contactData.EmailAddresses[0].Address;
                            }

                            var displayName = contactData.DisplayName ?? contact.Value.GetValueOrDefault("o365:DisplayName");

                            if (!string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(displayName))
                            {
                                var existing = await ContactApi.FindContactsAsync(userId, targetFolderId, email, displayName, cancel);
                                if (existing.Count > 0)
                                {
                                    Log.WriteInformationMessage(LOGTAG, "RestoreContactsSkipDuplicate", null, $"Skipping duplicate contact {originalPath} (Name: {displayName}, Email: {email})");
                                    _metadata.TryRemove(originalPath, out _);
                                    _temporaryFiles.TryRemove(originalPath, out var cFile);
                                    cFile?.Dispose();
                                    continue;
                                }
                            }
                        }
                    }
                }

                GraphContact? restoredContact = null;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    restoredContact = await ContactApi.RestoreContactToFolderAsync(userId, targetFolderId, contentStream, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();

                // Restore photo if exists
                var photoPath = originalPath.Replace(".json", ".photo");
                if (originalPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && _temporaryFiles.TryGetValue(photoPath, out var photoEntry))
                {
                    try
                    {
                        using (var photoStream = SystemIO.IO_OS.FileOpenRead(photoEntry))
                        {
                            // May have stored empty photo
                            if (photoStream.Length > 0)
                                await ContactApi.RestoreContactPhotoAsync(userId, restoredContact.Id, targetFolderId, photoStream, cancel);
                        }
                        _temporaryFiles.TryRemove(photoPath, out var photoFile);
                        photoFile?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreContactPhotoFailed", ex, $"Failed to restore photo for contact {originalPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreContactsFailed", ex, $"Failed to restore contact {contact.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestorePlannerBuckets(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var buckets = GetMetadataByType(SourceItemType.PlannerBucket);
        if (buckets.Count == 0)
            return;

        string? targetPlanId = null;
        if (RestoreTarget.Type == SourceItemType.Planner)
        {
            targetPlanId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        foreach (var bucket in buckets)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = bucket.Key;
                var metadata = bucket.Value;
                var name = metadata.GetValueOrDefault("o365:Name") ?? "Restored Bucket";
                var orderHint = metadata.GetValueOrDefault("o365:OrderHint");

                // Determine Plan ID
                var planId = targetPlanId;
                if (string.IsNullOrWhiteSpace(planId))
                {
                    // Try to extract from path: .../PlanId/buckets/BucketId
                    var parent = Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)); // .../PlanId/buckets
                    if (parent != null)
                    {
                        var grandParent = Path.GetDirectoryName(parent); // .../PlanId
                        if (grandParent != null)
                        {
                            planId = Path.GetFileName(grandParent);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(planId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestorePlannerBucketsMissingPlanId", null, $"Missing target plan ID for bucket {originalPath}, skipping.");
                    continue;
                }

                var newBucket = await PlannerApi.CreatePlannerBucketAsync(planId, name, orderHint, cancel);
                _restoredPlannerBucketMap[originalPath] = newBucket.Id;
                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestorePlannerBucketsFailed", ex, $"Failed to restore bucket {bucket.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestorePlannerTasks(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var tasks = GetMetadataByType(SourceItemType.UserPlannerTasks);
        if (tasks.Count == 0)
            return;

        string? targetPlanId = null;
        if (RestoreTarget.Type == SourceItemType.Planner)
        {
            targetPlanId = RestoreTarget.Metadata.GetValueOrDefault("o365:Id");
        }

        var existingTasksByPlan = new Dictionary<string, List<GraphPlannerTask>>();

        foreach (var task in tasks)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = task.Key;
                var metadata = task.Value;
                var title = metadata.GetValueOrDefault("o365:Name") ?? "Restored Task";
                var originalBucketId = metadata.GetValueOrDefault("o365:BucketId");

                // Determine Plan ID
                var planId = targetPlanId;
                if (string.IsNullOrWhiteSpace(planId))
                {
                    // Try to extract from path: .../PlanId/tasks/TaskId
                    var parent = Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)); // .../PlanId/tasks
                    if (parent != null)
                    {
                        var grandParent = Path.GetDirectoryName(parent); // .../PlanId
                        if (grandParent != null)
                        {
                            planId = Path.GetFileName(grandParent);
                        }
                    }
                }

                if (string.IsNullOrWhiteSpace(planId))
                {
                    Log.WriteWarningMessage(LOGTAG, "RestorePlannerTasksMissingPlanId", null, $"Missing target plan ID for task {originalPath}, skipping.");
                    continue;
                }

                if (!_overwrite)
                {
                    if (!existingTasksByPlan.TryGetValue(planId, out var existingTasks))
                    {
                        existingTasks = await PlannerApi.GetPlannerTasksAsync(planId, cancel);
                        existingTasksByPlan[planId] = existingTasks;
                    }

                    if (existingTasks.Any(t => t.Title == title))
                    {
                        Log.WriteInformationMessage(LOGTAG, "RestorePlannerTasksSkipDuplicate", null, $"Skipping duplicate task {originalPath} (Title: {title})");
                        _metadata.TryRemove(originalPath, out _);

                        // Clean up temp files
                        var dupTaskJsonPath = SystemIO.IO_OS.PathCombine(originalPath, "task.json");
                        _temporaryFiles.TryRemove(dupTaskJsonPath, out var tFile);
                        tFile?.Dispose();

                        var dupContentPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                        _temporaryFiles.TryRemove(dupContentPath, out var cFile);
                        cFile?.Dispose();

                        continue;
                    }
                }

                // Determine Bucket ID
                string? bucketId = null;
                if (!string.IsNullOrWhiteSpace(originalBucketId))
                {
                    // Assuming standard structure: .../PlanId/buckets/BucketId
                    var taskParent = Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)); // .../PlanId/tasks
                    if (taskParent != null)
                    {
                        var planPath = Path.GetDirectoryName(taskParent); // .../PlanId
                        if (planPath != null)
                        {
                            var bucketPath = SystemIO.IO_OS.PathCombine(planPath, "buckets", originalBucketId);
                            if (_restoredPlannerBucketMap.TryGetValue(bucketPath, out var mappedBucketId))
                            {
                                bucketId = mappedBucketId;
                            }
                        }
                    }
                }

                // If bucketId is still null, we might want to use the original one if we are restoring to the same plan?
                // Or just let it be null (if API allows, but API requires it).
                // If API requires it, and we don't have it, we might fail.
                // But let's try with null if we can't find it, maybe the API helper handles it or we catch the error.
                // Actually, CreatePlannerTaskAsync takes bucketId.

                // Read task.json if available
                var taskJsonPath = SystemIO.IO_OS.PathCombine(originalPath, "task.json");
                var taskJsonEntry = _temporaryFiles.GetValueOrDefault(taskJsonPath);
                GraphPlannerTask? taskData = null;

                if (taskJsonEntry != null)
                {
                    using (var stream = SystemIO.IO_OS.FileOpenRead(taskJsonEntry))
                    {
                        taskData = await JsonSerializer.DeserializeAsync<GraphPlannerTask>(stream, cancellationToken: cancel);
                    }
                    _metadata.TryRemove(taskJsonPath, out _);
                    _temporaryFiles.TryRemove(taskJsonPath, out var taskJsonFile);
                    taskJsonFile?.Dispose();
                }

                var newTask = await PlannerApi.CreatePlannerTaskAsync(
                    planId,
                    bucketId,
                    title,
                    taskData?.StartDateTime,
                    taskData?.DueDateTime,
                    taskData?.PercentComplete,
                    taskData?.Priority,
                    taskData?.Assignments,
                    taskData?.AppliedCategories,
                    cancel);

                if (!_overwrite && existingTasksByPlan.TryGetValue(planId, out var currentTasks))
                {
                    currentTasks.Add(newTask);
                }

                // Restore details
                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentPath);

                if (contentEntry != null)
                {
                    using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                    {
                        await PlannerApi.UpdatePlannerTaskDetailsAsync(newTask.Id, contentStream, cancel);
                    }

                    _metadata.TryRemove(contentPath, out _);
                    _temporaryFiles.TryRemove(contentPath, out var contentFile);
                    contentFile?.Dispose();
                }

                _metadata.TryRemove(originalPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestorePlannerTasksFailed", ex, $"Failed to restore task {task.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreUserMailboxSettings(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var settings = GetMetadataByType(SourceItemType.UserMailboxSettings);
        if (settings.Count == 0)
            return;

        (var userId, _) = await EmailRestore.GetUserIdAndMailboxTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var setting in settings)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = setting.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreUserMailboxSettingsMissingContent", null, $"Missing content for {originalPath}, skipping.");
                    continue;
                }

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    await EmailRestore.RestoreMailboxSettings(userId, contentStream, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreUserMailboxSettingsFailed", ex, $"Failed to restore mailbox settings {setting.Key}: {ex.Message}");
            }
        }
    }

    private async Task RestoreUserMailboxRules(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var rules = GetMetadataByType(SourceItemType.UserMailboxRule);
        if (rules.Count == 0)
            return;

        (var userId, _) = await EmailRestore.GetUserIdAndMailboxTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var rule in rules)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = rule.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreUserMailboxRulesMissingContent", null, $"Missing content for {originalPath}, skipping.");
                    continue;
                }

                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    await EmailRestore.RestoreMessageRule(userId, contentStream, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreUserMailboxRulesFailed", ex, $"Failed to restore mailbox rule {rule.Key}: {ex.Message}");
            }
        }
    }
}
