// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
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
    internal readonly bool _ignoreExisting;

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
            timeouts: TimeoutOptionsHelper.Parse(options),
            scope: parsedOptions.Scope
        );

        _ignoreExisting = Utility.ParseBoolOption(options, OptionsHelper.OFFICE_IGNORE_EXISTING_OPTION);
        var overwrite = Utility.ParseBoolOption(options, "overwrite");
        if (!overwrite)
            throw new UserInformationException(Strings.RestoreTargetMissingOverwriteOption(OptionsHelper.OFFICE_IGNORE_EXISTING_OPTION), "OverwriteOptionNotSet");

        var sourceOpts = new Dictionary<string, string?>(options);
        sourceOpts["store-metadata-content-in-database"] = "true";

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
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands.Concat([new CommandLineArgument(OptionsHelper.OFFICE_IGNORE_EXISTING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.OfficeIgnoreExistingOptionShort, Strings.OfficeIgnoreExistingOptionLong)]).ToList();

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

    /// <summary>
    /// Replaces the extension of a path with a new extension
    /// </summary>
    /// <param name="path">The path to modify</param>
    /// <param name="oldExtension">The extension to replace</param>
    /// <param name="newExtension">The new extension</param>
    /// <returns>The modified path</returns>
    private static string ReplaceExtension(string path, string oldExtension, string newExtension)
    {
        if (path.EndsWith(oldExtension, StringComparison.OrdinalIgnoreCase))
            return path.Substring(0, path.Length - oldExtension.Length) + newExtension;

        return path;
    }

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
            await RestoreContactGroups(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreContactGroupsFailed", ex, $"Failed to restore contact groups: {ex.Message}");
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

        try
        {
            await ChannelRestore.EndMigrationMode(cancel);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "EndMigrationModeFailed", ex, $"Failed to end migration mode for channels: {ex.Message}");
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
            await RestoreUserProfile(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserProfileFailed", ex, $"Failed to restore user profile: {ex.Message}");
        }

        RemoveMetaEntriesForNonRestoredItems();

        // We should have restored all items by now
        if (_metadata.Count > 0 || _temporaryFiles.Count > 0)
        {
            var displayItems = _metadata.Values
                .Select(x => (Type: x.GetValueOrDefault("o365:Type"), Name: x.GetValueOrDefault("o365:Name") ?? x.GetValueOrDefault("o365:DisplayName")))
                .Where(x => !string.IsNullOrWhiteSpace(x.Type) && !string.IsNullOrWhiteSpace(x.Name))
                .Take(10);

            foreach (var item in displayItems)
                Log.WriteWarningMessage(LOGTAG, "FinalizeIncompleteItem", null, $"Unrestored item - Type: {item.Type}, Name: {item.Name}");

            if (_metadata.Count > displayItems.Count())
                Log.WriteWarningMessage(LOGTAG, "FinalizeIncomplete", null, $"... and {_metadata.Count - displayItems.Count()} more unrestored items.");
        }

        var tempFiles = _temporaryFiles.Values.ToList();
        _temporaryFiles.Clear();
        foreach (var file in tempFiles)
            file.Dispose();
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
    /// Removes metadata entries for items that were not restored and are not required to be processed
    /// </summary>
    private void RemoveMetaEntriesForNonRestoredItems()
    {
        var toRemove = new HashSet<string>();
        foreach (var kv in _metadata)
        {
            // Some intermediate directories may not have a type
            if (!kv.Value.ContainsKey("o365:Type"))
            {
                toRemove.Add(kv.Key);
                continue;
            }

            var key = kv.Value.GetValueOrDefault("o365:Type");
            if (!Enum.TryParse<SourceItemType>(key, out var type))
                continue;

            switch (type)
            {
                // Various meta-types that are not restored directly
                case SourceItemType.User:
                case SourceItemType.UserMailbox:
                case SourceItemType.UserContacts:
                case SourceItemType.Calendar:
                case SourceItemType.Drive:
                    toRemove.Add(kv.Key);
                    break;

                default:
                    break;
            }

            // If we are restoring TO a channel, we don't need to restore the channel itself
            if (type == SourceItemType.GroupChannel && RestoreTarget != null && RestoreTarget.Type == SourceItemType.GroupChannel)
                toRemove.Add(kv.Key);

            // If we are restoring TO a site, we don't need to restore the site itself
            if (type == SourceItemType.Site && RestoreTarget != null && RestoreTarget.Type == SourceItemType.Site)
                toRemove.Add(kv.Key);
        }

        foreach (var key in toRemove)
        {
            _metadata.TryRemove(key, out _);
            _temporaryFiles.TryRemove(key, out var f);
            f?.Dispose();
        }
    }

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

                if (!_ignoreExisting && !string.IsNullOrWhiteSpace(internetMessageId) && await EmailApi.EmailExistsInFolderByInternetMessageIdAsync(userId, targetFolderId, internetMessageId, cancel))
                {
                    Log.WriteInformationMessage(LOGTAG, "RestoreUserEmailsSkipExisting", $"Email with InternetMessageId {internetMessageId} already exists in target mailbox, skipping restore for {emailSource.Key}.");
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

        var restoreTarget = await DriveRestore.GetDefaultDriveAndFolder(cancel);
        if (!string.IsNullOrWhiteSpace(restoreTarget.FolderId))
        {
            var rootFolderPath = Util.AppendDirSeparator(Path.GetDirectoryName(sortedFolders.First().Key.TrimEnd(Path.DirectorySeparatorChar)));
            _restoredDriveFolderMap.AddOrUpdate(rootFolderPath, restoreTarget.FolderId, (k, v) => v);
        }

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
                .Where(k => originalPath.StartsWith(Util.AppendDirSeparator(k)))
                .OrderByDescending(k => k.Length)
                .FirstOrDefault();

            string? driveId = null;
            string defaultRootId = "root";

            if (drivePath != null)
            {
                driveId = _restoredDriveMap[drivePath];
            }
            else
            {
                driveId = restoreTarget.DriveId;
                if (restoreTarget.FolderId != null) defaultRootId = restoreTarget.FolderId;
            }

            if (driveId == null)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreDriveFoldersMissingDrive", null, $"Could not find restored drive for folder {originalPath}, skipping.");
                continue;
            }

            // Determine parent folder ID
            var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
            var parentId = defaultRootId;

            if (parentPath != null && (drivePath == null || parentPath.TrimEnd(Path.DirectorySeparatorChar) != drivePath.TrimEnd(Path.DirectorySeparatorChar)))
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
                // Skip if folder already exists
                var restoredFolder = await DriveApi.GetDriveItemAsync(driveId, parentId, displayName, cancel);
                if (restoredFolder != null)
                {
                    _restoredDriveFolderMap[originalPath] = restoredFolder.Id;
                    continue;
                }

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
                    .Where(k => originalPath.StartsWith(Util.AppendDirSeparator(k)))
                    .OrderByDescending(k => k.Length)
                    .FirstOrDefault();

                string? driveId = null;
                string defaultRootId = "root";

                if (drivePath != null)
                {
                    driveId = _restoredDriveMap[drivePath];
                }
                else
                {
                    var defaults = await DriveRestore.GetDefaultDriveAndFolder(cancel);
                    driveId = defaults.DriveId;
                    if (defaults.FolderId != null) defaultRootId = defaults.FolderId;
                }

                if (driveId == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreDriveFilesMissingDrive", null, $"Could not find restored drive for file {originalPath}, skipping.");
                    continue;
                }

                // Determine parent folder ID
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
                var parentId = defaultRootId;

                if (parentPath != null && (drivePath == null || parentPath.TrimEnd(Path.DirectorySeparatorChar) != drivePath.TrimEnd(Path.DirectorySeparatorChar)))
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

                if (!_ignoreExisting)
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
                            string? sourceHash = null;
                            var sourceHashStr = file.Value.GetValueOrDefault("o365:Hashes");
                            if (sourceHashStr != null)
                            {
                                var sourceHashes = JsonSerializer.Deserialize<GraphDriveHashes>(sourceHashStr);
                                sourceHash = sourceHashes?.QuickXorHash ?? sourceHashes?.Sha1Hash;
                            }
                            var existingHash = existingItem.File?.Hashes?.QuickXorHash ?? existingItem.File?.Hashes?.Sha1Hash;

                            if (!string.IsNullOrWhiteSpace(sourceHash) && !string.IsNullOrWhiteSpace(existingHash))
                            {
                                if (string.Equals(sourceHash, existingHash, StringComparison.OrdinalIgnoreCase))
                                {
                                    Log.WriteVerboseMessage(LOGTAG, "RestoreDriveFilesSkipDuplicate", $"Skipping existing file {originalPath} (Name: {displayName}, Hash match)");
                                    _metadata.TryRemove(originalPath, out _);
                                    _temporaryFiles.TryRemove(originalPath, out var cFile);
                                    cFile?.Dispose();
                                    continue;
                                }
                            }
                            else
                            {
                                // Fallback to size only if hash is missing
                                Log.WriteVerboseMessage(LOGTAG, "RestoreDriveFilesSkipDuplicate", $"Skipping existing file {originalPath} (Name: {displayName}, Size match)");
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

                if (DateTimeOffset.TryParse(createdStr, CultureInfo.InvariantCulture, out var c)) created = c;
                if (DateTimeOffset.TryParse(modifiedStr, CultureInfo.InvariantCulture, out var m)) modified = m;

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

        (var userId, var calendarId) = await CalendarRestore.GetUserIdAndCalendarTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(calendarId))
            return;

        // Group attachments by event path
        var attachments = GetMetadataByType(SourceItemType.CalendarEventAttachment)
            .GroupBy(k => Util.AppendDirSeparator(Path.GetDirectoryName(k.Key.TrimEnd(Path.DirectorySeparatorChar))))
            .ToDictionary(g => g.Key, g => g.ToList());

        var singles = new List<(string Path, GraphEvent Event)>();
        var masters = new List<(string Path, GraphEvent Event)>();
        var exceptions = new List<(string Path, GraphEvent Event)>();

        foreach (var eventItem in events)
        {
            if (cancel.IsCancellationRequested) break;

            var originalPath = eventItem.Key;
            GraphEvent? graphEvent = null;

            try
            {
                var contentPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                if (_temporaryFiles.TryRemove(contentPath, out var contentFile))
                {
                    using var tf = contentFile;
                    using var stream = SystemIO.IO_OS.FileOpenRead(tf);
                    graphEvent = await JsonSerializer.DeserializeAsync<GraphEvent>(stream, cancellationToken: cancel);
                }

                // Clean up content file metadata
                _metadata.TryRemove(contentPath, out var _);

                // Temporary patch for missing ICalUId
                _metadata.TryGetValue(originalPath, out var eventMeta);
                if (string.IsNullOrWhiteSpace(graphEvent?.ICalUId) && eventMeta != null && eventMeta.TryGetValue("o365:CalUId", out var iCalUId))
                    graphEvent!.ICalUId = iCalUId;

                if (graphEvent == null) continue;

                if (graphEvent.Type == "seriesMaster")
                    masters.Add((originalPath, graphEvent));
                else if (graphEvent.Type == "exception")
                    exceptions.Add((originalPath, graphEvent));
                else if (graphEvent.Type == "occurrence")
                    continue; // Skip occurrences
                else
                    singles.Add((originalPath, graphEvent));
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreCalendarEventAnalysisFailed", ex, $"Failed to analyze event {originalPath}: {ex.Message}");
            }
        }

        // Restore singles
        foreach (var item in singles)
            await RestoreSingleEvent(userId, calendarId, item.Path, item.Event, attachments, cancel);

        // Restore masters
        var masterMap = new Dictionary<string, string>(); // OldId -> NewId
        foreach (var item in masters)
        {
            var newId = await RestoreMasterEvent(userId, calendarId, item.Path, item.Event, attachments, cancel);
            if (newId != null && !string.IsNullOrWhiteSpace(item.Event.Id))
                masterMap[item.Event.Id] = newId;
        }

        // Restore exceptions
        foreach (var item in exceptions)
            await RestoreExceptionEvent(userId, calendarId, item.Path, item.Event, masterMap, attachments, cancel);
    }

    private async Task RestoreCalendarEventAttachments(string userId, string calendarId, string eventId, string eventPath, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
    {
        var eventPathWithSep = Util.AppendDirSeparator(eventPath);
        if (!attachments.TryGetValue(eventPathWithSep, out var eventAttachments))
            return;

        foreach (var att in eventAttachments)
        {
            try
            {
                var attPath = att.Key;
                var metadata = att.Value;
                var name = metadata.GetValueOrDefault("o365:Name") ?? "attachment";
                var contentType = metadata.GetValueOrDefault("o365:ContentType") ?? "application/octet-stream";

                if (!_temporaryFiles.TryRemove(attPath, out var contentFile))
                    continue;

                using var tf = contentFile;
                using var stream = SystemIO.IO_OS.FileOpenRead(tf);
                await CalendarApi.AddAttachmentAsync(userId, calendarId, eventId, name, contentType, stream, cancel);

                _metadata.TryRemove(attPath, out _);
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreCalendarAttachmentFailed", ex, $"Failed to restore attachment for event {eventPath}: {ex.Message}");
            }
        }
    }

    private async Task RestoreSingleEvent(string userId, string calendarId, string path, GraphEvent eventItem, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
    {
        try
        {
            if (!_ignoreExisting && !string.IsNullOrWhiteSpace(eventItem.ICalUId))
            {
                var existing = await CalendarApi.FindEventsAsync(userId, calendarId, eventItem, cancel);
                if (existing.Count > 0)
                {
                    Log.WriteInformationMessage(LOGTAG, "RestoreSingleEventSkipDuplicate", $"Skipping duplicate event {path} (Subject: {eventItem.Subject})");
                    _metadata.TryRemove(path, out _);
                    return;
                }
            }

            // Clean up properties that shouldn't be sent on creation
            eventItem.Id = null!;
            eventItem.CreatedDateTime = null;
            eventItem.LastModifiedDateTime = null;

            var created = await CalendarApi.CreateCalendarEventAsync(userId, calendarId, eventItem, cancel);
            await RestoreCalendarEventAttachments(userId, calendarId, created.Id, path, attachments, cancel);
            _metadata.TryRemove(path, out _);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "RestoreSingleEventFailed", ex, $"Failed to restore event {path}: {ex.Message}");
        }
    }

    private async Task<string?> RestoreMasterEvent(string userId, string calendarId, string path, GraphEvent eventItem, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
    {
        try
        {
            if (!_ignoreExisting && !string.IsNullOrWhiteSpace(eventItem.ICalUId))
            {
                var existing = await CalendarApi.FindEventsAsync(userId, calendarId, eventItem, cancel);
                if (existing.Count > 0)
                {
                    Log.WriteInformationMessage(LOGTAG, "RestoreMasterEventSkipDuplicate", $"Skipping duplicate master event {path} (Subject: {eventItem.Subject})");
                    _metadata.TryRemove(path, out _);
                    // Return the ID of the existing event so exceptions can be attached to it?
                    // If we skip it, we should probably return the existing ID.
                    return existing[0].Id;
                }
            }

            eventItem.Id = null!;
            eventItem.CreatedDateTime = null;
            eventItem.LastModifiedDateTime = null;

            var created = await CalendarApi.CreateCalendarEventAsync(userId, calendarId, eventItem, cancel);
            await RestoreCalendarEventAttachments(userId, calendarId, created.Id, path, attachments, cancel);
            _metadata.TryRemove(path, out _);
            return created.Id;
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "RestoreMasterEventFailed", ex, $"Failed to restore master event {path}: {ex.Message}");
            return null;
        }
    }

    private async Task RestoreExceptionEvent(string userId, string calendarId, string path, GraphEvent eventItem, Dictionary<string, string> masterMap, Dictionary<string, List<KeyValuePair<string, Dictionary<string, string?>>>> attachments, CancellationToken cancel)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(eventItem.SeriesMasterId) || !masterMap.TryGetValue(eventItem.SeriesMasterId, out var newMasterId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreExceptionEventMissingMaster", null, $"Could not find restored master for exception event {path}, skipping.");
                return;
            }

            if (eventItem.OriginalStart == null)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreExceptionEventMissingOriginalStart", null, $"Missing original start time for exception event {path}, skipping.");
                return;
            }

            // Find the occurrence
            // We search for instances around the original start time
            var searchStart = eventItem.OriginalStart.Value.AddMinutes(-1);
            var searchEnd = eventItem.OriginalStart.Value.AddMinutes(1);

            var instances = await CalendarApi.GetCalendarEventInstancesAsync(userId, newMasterId, searchStart, searchEnd, cancel);

            var instance = instances.FirstOrDefault(i =>
            {
                // Parse start time from instance
                if (i.Start is JsonElement startElem && startElem.ValueKind == JsonValueKind.Object)
                {
                    if (startElem.TryGetProperty("dateTime", out var dtProp) && dtProp.GetString() is string dtStr)
                    {
                        if (DateTimeOffset.TryParse(dtStr, out var dt))
                        {
                            return Math.Abs((dt - eventItem.OriginalStart.Value).TotalSeconds) < 5;
                        }
                    }
                }
                return false;
            });

            if (instance == null)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreExceptionEventInstanceNotFound", null, $"Could not find occurrence instance for exception event {path}, skipping.");
                return;
            }

            // Update the instance
            eventItem.Id = null!;
            eventItem.SeriesMasterId = null;
            eventItem.Type = null;
            eventItem.CreatedDateTime = null;
            eventItem.LastModifiedDateTime = null;
            eventItem.OriginalStart = null;

            var updated = await CalendarApi.UpdateCalendarEventAsync(userId, instance.Id, eventItem, cancel);
            await RestoreCalendarEventAttachments(userId, calendarId, updated.Id, path, attachments, cancel);
            _metadata.TryRemove(path, out _);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "RestoreExceptionEventFailed", ex, $"Failed to restore exception event {path}: {ex.Message}");
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

                if (!_ignoreExisting)
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
                                    Log.WriteInformationMessage(LOGTAG, "RestoreContactsSkipDuplicate", $"Skipping duplicate contact {originalPath} (Name: {displayName}, Email: {email})");
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
                var photoPath = ReplaceExtension(originalPath, ".json", ".photo");
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


    private async Task RestoreContactGroups(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var groups = GetMetadataByType(SourceItemType.UserContactGroup);
        if (groups.Count == 0)
            return;

        (var userId, var defaultFolderId) = await ContactRestore.GetUserIdAndContactFolderTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(defaultFolderId))
            return;

        foreach (var group in groups)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = group.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupsMissingContent", null, $"Missing content for {originalPath}, skipping.");
                    continue;
                }

                // Determine target folder
                var targetFolderId = defaultFolderId;
                var parentPath = Util.AppendDirSeparator(Path.GetDirectoryName(originalPath.TrimEnd(Path.DirectorySeparatorChar)));
                if (parentPath != null && _restoredContactFolderMap.TryGetValue(parentPath, out var mappedFolderId))
                    targetFolderId = mappedFolderId;

                GraphContactGroup? restoredGroup = null;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    var groupData = await JsonSerializer.DeserializeAsync<GraphContactGroup>(contentStream, cancellationToken: cancel);
                    if (groupData == null)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupsInvalidContent", null, $"Invalid content for {originalPath}, skipping.");
                        continue;
                    }

                    var displayName = groupData.DisplayName ?? group.Value.GetValueOrDefault("o365:DisplayName") ?? "Restored Group";

                    if (!_ignoreExisting)
                    {
                        var existing = await ContactApi.FindContactGroupsAsync(userId, targetFolderId, displayName, cancel);
                        if (existing.Count > 0)
                        {
                            Log.WriteInformationMessage(LOGTAG, "RestoreContactGroupsSkipDuplicate", $"Skipping duplicate contact group {originalPath} (Name: {displayName})");
                            restoredGroup = existing[0];
                        }
                    }

                    if (restoredGroup == null)
                        restoredGroup = await ContactApi.CreateContactGroupAsync(userId, displayName, targetFolderId, cancel);
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();

                // Restore members
                var membersPath = ReplaceExtension(originalPath, ".json", ".members.json");
                if (_temporaryFiles.TryGetValue(membersPath, out var membersEntry))
                {
                    try
                    {
                        using (var membersStream = SystemIO.IO_OS.FileOpenRead(membersEntry))
                        {
                            var members = await JsonSerializer.DeserializeAsync<List<GraphContact>>(membersStream, cancellationToken: cancel);
                            if (members != null)
                            {
                                foreach (var member in members)
                                {
                                    var contactEmail = member.EmailAddresses?.FirstOrDefault();
                                    if (contactEmail == null)
                                    {
                                        Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupMemberMissingEmail", null, $"Member {member.DisplayName} has no email address, skipping.");
                                        continue;
                                    }

                                    var contacts = await ContactApi.FindContactsAsync(userId, targetFolderId, contactEmail.Address, member.DisplayName, cancel);
                                    if (contacts.Count > 0)
                                    {
                                        try
                                        {
                                            var address = new GraphEmailAddress
                                            {
                                                Address = contactEmail.Address,
                                                Name = member.DisplayName
                                            };

                                            await ContactApi.AddContactGroupMemberAsync(userId, targetFolderId, restoredGroup.Id, address, cancel);
                                        }
                                        catch (Exception ex)
                                        {
                                            // Member might already exist
                                            Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupMemberFailed", ex, $"Failed to add member {member.DisplayName} to group {restoredGroup.DisplayName}: {ex.Message}");
                                        }
                                    }
                                    else
                                    {
                                        Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupMemberNotFound", null, $"Could not find contact for member {member.DisplayName} ({contactEmail}) in target folder.");
                                    }
                                }
                            }
                        }
                        _temporaryFiles.TryRemove(membersPath, out var membersFile);
                        membersFile?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupMembersFailed", ex, $"Failed to restore members for group {originalPath}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreContactGroupsFailed", ex, $"Failed to restore contact group {group.Key}: {ex.Message}");
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

                if (!_ignoreExisting)
                {
                    if (!existingTasksByPlan.TryGetValue(planId, out var existingTasks))
                    {
                        existingTasks = await PlannerApi.GetPlannerTasksAsync(planId, cancel);
                        existingTasksByPlan[planId] = existingTasks;
                    }

                    if (existingTasks.Any(t => t.Title == title))
                    {
                        Log.WriteInformationMessage(LOGTAG, "RestorePlannerTasksSkipDuplicate", $"Skipping duplicate task {originalPath} (Title: {title})");
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

                if (!_ignoreExisting && existingTasksByPlan.TryGetValue(planId, out var currentTasks))
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
