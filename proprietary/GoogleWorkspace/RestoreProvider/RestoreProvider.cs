// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;

namespace Duplicati.Proprietary.GoogleWorkspace;

public partial class RestoreProvider : IRestoreDestinationProviderModule
{
    /// <summary>
    /// Log tag for RestoreProvider
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<RestoreProvider>();

    /// <summary>
    /// API helper for accessing Google Workspace APIs
    /// </summary>
    private readonly APIHelper _apiHelper;
    /// <summary>
    /// Path to restore to
    /// </summary>
    private readonly string _restorePath;
    /// <summary>
    /// Whether the overwrite option has been set
    /// </summary>
    private readonly bool _hasSetOverwriteOption;
    /// <summary>
    /// Source provider for accessing currently existing items on the destination
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
    /// Map of restored Gmail labels to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredLabelMap = new();

    /// <summary>
    /// Map of restored Drive folders to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredDriveFolderMap = new();

    /// <summary>
    /// Map of restored calendars to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredCalendarMap = new();

    /// <summary>
    /// Map of restored task lists to their corresponding IDs
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _restoredTaskListMap = new();

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
        _hasSetOverwriteOption = false;
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

        var parsedOptions = OptionsHelper.ParseOptions(options);
        _apiHelper = new APIHelper(parsedOptions, isRestoreOperation: true);

        _ignoreExisting = Utility.ParseBoolOption(options, OptionsHelper.GOOGLE_IGNORE_EXISTING_OPTION);
        _hasSetOverwriteOption = Utility.ParseBoolOption(options, "overwrite");

        var sourceOpts = new Dictionary<string, string?>(options);
        sourceOpts["store-metadata-content-in-database"] = "true";

        SourceProvider = new SourceProvider("googleworkspace://", "", sourceOpts, true);
    }

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public string DisplayName => Strings.Common.DisplayName;

    /// <inheritdoc />
    public string Description => Strings.Common.Description;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.RestoreSupportedCommands.ToList();

    /// <inheritdoc />
    public string TargetDestination => _restorePath;

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
    {
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task Initialize(CancellationToken cancel)
    {
        if (!_hasSetOverwriteOption)
            throw new UserInformationException(Strings.RestoreTargetMissingOverwriteOption("overwrite", OptionsHelper.GOOGLE_IGNORE_EXISTING_OPTION), "OverwriteOptionNotSet");

        await SourceProvider.Initialize(cancel);
        var entry = await SourceProvider.GetEntry(_restorePath, isFolder: true, cancel);
        if (entry == null)
            throw new UserInformationException(Strings.RestoreTargetNotFound(_restorePath), "RestoreTargetNotFound");
        var metadata = await entry.GetMinorMetadata(cancel);
        var type = metadata.TryGetValue("gsuite:Type", out var typeStr)
            && Enum.TryParse<SourceItemType>(typeStr, out var sourceItemType)
            ? sourceItemType
            : throw new UserInformationException(Strings.InvalidRestoreTargetType(typeStr), "InvalidRestoreTargetType");

        RestoreTarget = new RestoreTargetData(_restorePath, entry, metadata, type);
    }

    /// <inheritdoc />
    public async Task Test(CancellationToken cancellationToken)
    {
        _apiHelper.TestConnection();

        // If we have no path, just check that we can connect
        if (string.IsNullOrWhiteSpace(_restorePath))
            return;

        // Otherwise, check that the path exists and is a valid target
        await Initialize(cancellationToken);
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
        => throw new NotImplementedException("File deletion is not supported in Google Workspace RestoreProvider");

    /// <inheritdoc />
    public async Task Finalize(Action<double>? progressCallback, CancellationToken cancel)
    {
        var totalFiles = _metadata.Count;
        var completedPhases = 0;
        const int totalPhases = 13;

        // Phase 1: Restore Gmail Labels
        try
        {
            await RestoreGmailLabels(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGmailLabelsFailed", ex, $"Failed to restore Gmail labels");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 2: Restore Gmail Messages
        try
        {
            await RestoreGmailMessages(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGmailMessagesFailed", ex, $"Failed to restore Gmail messages");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 3: Restore Gmail Settings
        try
        {
            await RestoreGmailSettings(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreGmailSettingsFailed", ex, $"Failed to restore Gmail settings");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 4: Restore Drive Folders
        try
        {
            await RestoreDriveFolders(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreDriveFoldersFailed", ex, $"Failed to restore Drive folders");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 5: Restore Drive Files
        try
        {
            await RestoreDriveFiles(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreDriveFilesFailed", ex, $"Failed to restore Drive files");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 6: Restore Drive Permissions
        try
        {
            await RestoreDrivePermissions(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreDrivePermissionsFailed", ex, $"Failed to restore Drive permissions");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 7: Restore Calendar Events
        try
        {
            await RestoreCalendarEvents(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreCalendarEventsFailed", ex, $"Failed to restore calendar events");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 8: Restore Calendar ACLs
        try
        {
            await RestoreCalendarAcls(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreCalendarEventsFailed", ex, $"Failed to restore calendar events");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 9: Restore Contacts
        try
        {
            await RestoreContacts(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreContactsFailed", ex, $"Failed to restore contacts");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 10: Restore Contact Groups
        try
        {
            await RestoreContactGroups(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreContactGroupsFailed", ex, $"Failed to restore contact groups");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 11: Restore Task Lists and Tasks
        try
        {
            await RestoreTaskLists(cancel).ConfigureAwait(false);
            await RestoreTasks(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreTasksFailed", ex, $"Failed to restore tasks");
        }
        completedPhases++;
        progressCallback?.Invoke(completedPhases / (double)totalPhases);

        // Phase 12: Restore Keep Notes (if target supports it)
        try
        {
            await RestoreKeepNotes(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreKeepNotesFailed", ex, $"Failed to restore Keep notes");
        }

        // Phase 13: Restore Chat Messages (if target supports it)
        try
        {
            await RestoreChatMessages(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreChatMessagesFailed", ex, $"Failed to restore chat messages");
        }

        RemoveMetaEntriesForNonRestoredItems();

        // We should have restored all items by now
        if (_metadata.Count > 0 || _temporaryFiles.Count > 0)
        {
            var displayItems = _metadata.Values
                .Select(x => (Type: x.GetValueOrDefault("gsuite:Type"), Name: x.GetValueOrDefault("gsuite:Name") ?? x.GetValueOrDefault("gsuite:DisplayName")))
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
            .Where(kv => kv.Value.TryGetValue("gsuite:Type", out var typeStr)
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
            if (!kv.Value.ContainsKey("gsuite:Type"))
            {
                toRemove.Add(kv.Key);
                continue;
            }

            var key = kv.Value.GetValueOrDefault("gsuite:Type");
            if (!Enum.TryParse<SourceItemType>(key, out var type))
                continue;

            switch (type)
            {
                // Various meta-types that are not restored directly
                case SourceItemType.User:
                case SourceItemType.UserGmail:
                case SourceItemType.UserDrive:
                case SourceItemType.UserCalendar:
                case SourceItemType.UserContacts:
                case SourceItemType.UserTasks:
                case SourceItemType.UserKeep:
                case SourceItemType.UserChat:
                case SourceItemType.Drive:
                case SourceItemType.Calendar:
                    toRemove.Add(kv.Key);
                    break;

                default:
                    break;
            }

            // If we are restoring TO a specific label, we don't need to restore the label itself
            if (type == SourceItemType.GmailLabel && RestoreTarget != null && RestoreTarget.Type == SourceItemType.GmailLabel)
                toRemove.Add(kv.Key);

            // If we are restoring TO a specific folder, we don't need to restore the folder itself
            if (type == SourceItemType.DriveFolder && RestoreTarget != null && RestoreTarget.Type == SourceItemType.DriveFolder)
                toRemove.Add(kv.Key);
        }

        foreach (var key in toRemove)
        {
            _metadata.TryRemove(key, out _);
            _temporaryFiles.TryRemove(key, out var f);
            f?.Dispose();
        }
    }
}
