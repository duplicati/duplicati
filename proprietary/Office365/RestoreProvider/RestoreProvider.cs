// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;
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
            graphBaseUrl: parsedOptions.GraphBaseUrl
        );

        var sourceOpts = new Dictionary<string, string?>(options)
        {
            { "store-metadata-content-in-database", "true" }
        };

        SourceProvider = new SourceProvider("office365://", "", sourceOpts);
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
    public async Task Finalize(CancellationToken cancel)
    {
        try
        {
            await RestoreUserEmailFolders(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserEmailFoldersFailed", ex, $"Failed to restore user email folders: {ex.Message}");
        }

        try
        {
            await RestoreUserEmails(cancel).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.WriteErrorMessage(LOGTAG, "FinalizeRestoreUserEmailsFailed", ex, $"Failed to restore user emails: {ex.Message}");
        }

        await RestoreDrives(cancel).ConfigureAwait(false);
        await RestoreDriveFolders(cancel).ConfigureAwait(false);
        await RestoreDriveFiles(cancel).ConfigureAwait(false);

        await RestoreCalendarEvents(cancel).ConfigureAwait(false);
        await RestoreContacts(cancel).ConfigureAwait(false);

        await RestoreGroupChannels(cancel).ConfigureAwait(false);
        await RestoreGroupConversations(cancel).ConfigureAwait(false);

        await RestorePlannerTasks(cancel).ConfigureAwait(false);

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
    }

    private async Task RestoreDriveFolders(CancellationToken cancel)
    {
    }

    private async Task RestoreDriveFiles(CancellationToken cancel)
    {
    }

    private async Task RestoreCalendarEvents(CancellationToken cancel)
    {
    }

    private async Task RestoreContacts(CancellationToken cancel)
    {
    }

    private async Task RestoreGroupChannels(CancellationToken cancel)
    {
    }

    private async Task RestoreGroupConversations(CancellationToken cancel)
    {
    }

    private async Task RestorePlannerTasks(CancellationToken cancel)
    {
    }
}
