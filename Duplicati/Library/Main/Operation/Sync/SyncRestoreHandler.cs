using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Database.Sync;
using Duplicati.Library.Common.IO;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// Lists and restores what a sync job has written to its destination, with the same
/// shapes the restore of a backup uses: a snapshot folder is a version (a mirror without
/// snapshot folders is a single version), a folder is listed under the original source
/// path, and the selected files are copied back to the original location or a new folder.
/// </summary>
internal static class SyncRestoreHandler
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SyncRestoreHandler));

    /// <summary>
    /// The suffix the restore flow puts on a selected folder to mean "everything below it"
    /// </summary>
    private const string FolderWildcard = "*";

    /// <summary>
    /// One version of a sync destination.
    /// </summary>
    /// <param name="Version">The version number; zero is the newest</param>
    /// <param name="Time">The time the version was written, in UTC</param>
    /// <param name="Folder">The snapshot folder, or an empty string for the destination root of a mirror</param>
    private sealed record SyncVersion(long Version, DateTime Time, string Folder);

    /// <summary>
    /// A source of the sync job and where its files are on the destination.
    /// </summary>
    /// <param name="LocalPath">The source folder, with a trailing directory separator</param>
    /// <param name="RemotePath">The folder below the version folder that holds the source's files, or empty</param>
    private sealed record SourceRoot(string LocalPath, string RemotePath);

    /// <summary>
    /// A file selected for restore.
    /// </summary>
    /// <param name="RemotePath">The relative path on the destination</param>
    /// <param name="LocalPath">The original path of the file</param>
    /// <param name="Size">The size of the file</param>
    /// <param name="LastModified">The modification time on the destination, in UTC</param>
    private sealed record RestoreItem(string RemotePath, string LocalPath, long Size, DateTime LastModified);

    /// <summary>
    /// An entry of a folder listing.
    /// </summary>
    private sealed record SyncFolderEntry(string Path, long Size, bool IsDirectory, bool IsSymlink, DateTime LastModified, Dictionary<string, string> Metadata) : IListFolderEntry;

    /// <summary>
    /// Lists the versions of a sync destination, newest first.
    /// </summary>
    /// <param name="options">The options of the sync job</param>
    /// <param name="result">The result to fill in</param>
    /// <param name="backendManager">The backend manager of the destination</param>
    public static async Task ListVersionsAsync(Options options, ListFilesetResults result, IBackendManager backendManager)
    {
        var versions = await GetVersionsAsync(options, backendManager, result.TaskControl.ProgressToken).ConfigureAwait(false);
        result.EncryptedFiles = false;
        result.Filesets = versions
            .Select(x => new ListFilesetResultFileset(x.Version, x.Time, true, null, null))
            .ToArray();
    }

    /// <summary>
    /// Lists a folder of one version, with the paths of the original sources. Without a
    /// folder the source folders themselves are listed, as the restore of a backup does.
    /// </summary>
    /// <param name="options">The options of the sync job, with <c>time</c> or <c>version</c> selecting the version</param>
    /// <param name="result">The result to fill in</param>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="sources">The source folders of the sync job</param>
    /// <param name="folders">The folder to list, or none for the source folders</param>
    /// <param name="offset">The number of entries to skip</param>
    /// <param name="limit">The maximum number of entries to return; zero or less returns all</param>
    public static async Task ListFolderAsync(Options options, ListFolderResults result, IBackendManager backendManager, string[] sources, string[]? folders, long offset, long limit)
    {
        var ct = result.TaskControl.ProgressToken;
        var version = await SelectVersionAsync(options, backendManager, ct).ConfigureAwait(false);
        var roots = MapSources(sources);

        List<IListFolderEntry> entries;
        if (folders == null || folders.Length == 0 || string.IsNullOrEmpty(folders[0]))
        {
            if (folders != null && folders.Length > 1)
                throw new UserInformationException("When no folder is specified, only one folder can be listed", "MultipleFoldersFound");
            entries = roots
                .Select(x => (IListFolderEntry)new SyncFolderEntry(x.LocalPath, 0, true, false, version.Time, []))
                .ToList();
        }
        else
        {
            if (folders.Length > 1)
                throw new UserInformationException("Only one folder can be listed at a time", "MultipleFoldersFound");

            var folder = Util.AppendDirSeparator(folders[0]);
            var remoteFolder = ToRemotePath(version, roots, folder)
                ?? throw new UserInformationException($"The folder {folder} is not part of the sync job", "FolderNotInSyncJob");

            entries = [];
            foreach (var entry in await ListRemoteFolderAsync(backendManager, remoteFolder, ct).ConfigureAwait(false))
            {
                var name = entry.Name.TrimEnd('/', '\\');
                entries.Add(new SyncFolderEntry(
                    entry.IsFolder ? Util.AppendDirSeparator(folder + name) : folder + name,
                    entry.IsFolder ? 0 : Math.Max(entry.Size, 0),
                    entry.IsFolder,
                    false,
                    entry.LastModification.ToUniversalTime(),
                    []));
            }

            entries = entries
                .OrderByDescending(x => x.IsDirectory)
                .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        var total = entries.Count;
        var pageSize = limit > 0 ? (int)Math.Min(limit, int.MaxValue) : Math.Max(total, 1);
        var page = (int)(Math.Max(offset, 0) / pageSize);
        var items = entries.Skip((int)Math.Min(Math.Max(offset, 0), int.MaxValue)).Take(pageSize).ToArray();
        result.Entries = new PaginatedResults<IListFolderEntry>(page, pageSize, (total + pageSize - 1) / pageSize, total, items);
    }

    /// <summary>
    /// Restores files and folders from one version to their original location, or below
    /// <see cref="Options.Restorepath"/>. A path ending in a directory separator, or in a
    /// separator and <c>*</c>, selects the folder and everything below it; no path selects
    /// every source.
    /// </summary>
    /// <param name="options">The options of the sync job</param>
    /// <param name="result">The result to fill in</param>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="sources">The source folders of the sync job</param>
    /// <param name="paths">The files and folders to restore</param>
    public static async Task RestoreAsync(Options options, RestoreResults result, IBackendManager backendManager, string[] sources, string[]? paths)
    {
        var ct = result.TaskControl.ProgressToken;
        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Begin);

        var restorePath = options.Restorepath ?? "";
        if (restorePath.StartsWith('@'))
            throw new UserInformationException("A sync job can only restore to a folder on this machine", "SyncRestoreRemoteTarget");

        var version = await SelectVersionAsync(options, backendManager, ct).ConfigureAwait(false);
        var roots = MapSources(sources);
        Logging.Log.WriteInformationMessage(LOGTAG, "SyncRestoreVersion", "Restoring from {0}", string.IsNullOrEmpty(version.Folder) ? "the sync destination" : $"snapshot {version.Folder}");

        // 1. Collect the selected files, expanding folders
        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateFileList);
        var selection = paths == null || paths.Length == 0 || paths.All(string.IsNullOrWhiteSpace)
            ? roots.Select(x => x.LocalPath).ToArray()
            : paths.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

        var items = new Dictionary<string, RestoreItem>(Library.Utility.Utility.ClientFilenameStringComparer);
        var folders = new HashSet<string>(Library.Utility.Utility.ClientFilenameStringComparer);
        foreach (var selected in selection)
        {
            ct.ThrowIfCancellationRequested();

            var path = selected;
            var isFolder = false;
            if (path.EndsWith(Util.DirectorySeparatorString + FolderWildcard, StringComparison.Ordinal) || path.EndsWith("/" + FolderWildcard, StringComparison.Ordinal))
            {
                path = path[..^FolderWildcard.Length];
                isFolder = true;
            }
            else if (path.EndsWith('/') || path.EndsWith(Path.DirectorySeparatorChar))
            {
                isFolder = true;
            }

            if (isFolder)
            {
                var remoteFolder = ToRemotePath(version, roots, path)
                    ?? throw new UserInformationException($"The folder {path} is not part of the sync job", "FolderNotInSyncJob");
                await CollectFolderAsync(backendManager, remoteFolder, Util.AppendDirSeparator(path), items, folders, ct).ConfigureAwait(false);
            }
            else
            {
                await CollectFileAsync(backendManager, version, roots, path, items, ct).ConfigureAwait(false);
            }
        }

        if (items.Count == 0 && folders.Count == 0)
            throw new UserInformationException("No files matched the selection", "NoFilesSelected");

        // 2. Work out the target of every item. Restoring to the original location puts
        // each file back where it came from; restoring to a folder puts the files below
        // it relative to the deepest folder they have in common, as the restore of a
        // backup does.
        var commonPrefix = string.IsNullOrEmpty(restorePath)
            ? ""
            : GetCommonFolderPrefix(items.Values.Select(x => x.LocalPath).Concat(folders));
        if (!string.IsNullOrEmpty(restorePath))
            restorePath = Util.AppendDirSeparator(Path.GetFullPath(restorePath));
        result.RestorePath = restorePath;

        string TargetOf(string localPath)
        {
            if (string.IsNullOrEmpty(restorePath))
                return localPath;

            var relative = localPath[commonPrefix.Length..].Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(restorePath, relative));
            if (!target.StartsWith(restorePath, Library.Utility.Utility.ClientFilenameStringComparison) && !Util.AppendDirSeparator(target).Equals(restorePath, Library.Utility.Utility.ClientFilenameStringComparison))
                throw new UserInformationException($"Refusing to restore {localPath} outside the restore folder", "RestorePathOutsideTarget");
            return target;
        }

        // 3. Create the folders, so empty folders come back as well
        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_CreateTargetFolders);
        foreach (var folder in folders.OrderBy(x => x.Length))
        {
            var target = TargetOf(folder);
            if (options.Dryrun)
            {
                Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would create folder: {0}", target);
                continue;
            }
            if (!Directory.Exists(target))
            {
                Directory.CreateDirectory(target);
                result.RestoredFolders++;
            }
        }

        // 4. Copy the files back
        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_DownloadingRemoteFiles);
        result.OperationProgressUpdater.UpdatefileCount(items.Count, items.Values.Sum(x => x.Size), true);
        var restoreTime = DateTime.Now;
        long processed = 0, processedSize = 0;
        foreach (var item in items.Values.OrderBy(x => x.LocalPath, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();

            var target = TargetOf(item.LocalPath);
            result.OperationProgressUpdater.StartFile(target, item.Size);
            try
            {
                if (File.Exists(target) && !options.Overwrite)
                {
                    var existing = new FileInfo(target);
                    if (existing.Length == item.Size && existing.LastWriteTimeUtc == item.LastModified)
                    {
                        result.UnmodifiedFiles++;
                        result.SizeOfUnmodifiedFiles += item.Size;
                        continue;
                    }
                    target = GetFreeName(target, restoreTime);
                }

                if (options.Dryrun)
                {
                    Logging.Log.WriteDryrunMessage(LOGTAG, "DryRun", "Would restore {0} to {1}", item.RemotePath, target);
                    continue;
                }

                var targetFolder = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                    result.RestoredFolders++;
                }

                Logging.Log.WriteVerboseMessage(LOGTAG, "SyncRestoreFile", "Restoring {0} to {1}", item.RemotePath, target);
                using (var tf = await backendManager.GetFileUnencryptedWithPathAsync(item.RemotePath, ct).ConfigureAwait(false))
                    File.Copy(tf, target, true);
                File.SetLastWriteTimeUtc(target, item.LastModified);

                result.RestoredFiles++;
                result.SizeOfRestoredFiles += item.Size;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result.BrokenRemoteFiles.Add(item.RemotePath);
                Logging.Log.WriteErrorMessage(LOGTAG, "SyncRestoreFileFailed", ex, "Failed to restore {0}: {1}", item.LocalPath, ex.Message);
            }
            finally
            {
                processed++;
                processedSize += item.Size;
                result.OperationProgressUpdater.UpdatefilesProcessed(processed, processedSize);
            }
        }

        result.OperationProgressUpdater.UpdatePhase(OperationPhase.Restore_Complete);
    }

    /// <summary>
    /// Gets the versions of a sync destination, newest first. A destination written with
    /// snapshot folders has one version per completed snapshot; a mirror has one version,
    /// dated by the last run recorded in the sync database.
    /// </summary>
    private static async Task<IReadOnlyList<SyncVersion>> GetVersionsAsync(Options options, IBackendManager backendManager, CancellationToken ct)
    {
        if (!options.SyncSnapshotFolders)
            return [new SyncVersion(0, await GetLastRunTimeAsync(options, ct).ConfigureAwait(false), "")];

        var root = await SyncSnapshotLayout.ReadRootStateAsync(backendManager, ct).ConfigureAwait(false);
        return root.CompletedSnapshots
            .Reverse()
            .Select((name, index) =>
            {
                SyncSnapshotLayout.TryParseFolderName(name, out var time);
                return new SyncVersion(index, time, name);
            })
            .ToArray();
    }

    /// <summary>
    /// Selects the version the <c>version</c> or <c>time</c> option asks for; without
    /// either, the newest version.
    /// </summary>
    private static async Task<SyncVersion> SelectVersionAsync(Options options, IBackendManager backendManager, CancellationToken ct)
    {
        var versions = await GetVersionsAsync(options, backendManager, ct).ConfigureAwait(false);
        if (versions.Count == 0)
            throw new UserInformationException("The sync destination has no completed snapshots", "NoSnapshotsFound");

        // A mirror has only the one version, whatever time the caller remembers for it
        if (!options.SyncSnapshotFolders)
            return versions[0];

        var requested = options.Version;
        if (requested != null && requested.Length > 0)
        {
            if (requested.Length > 1)
                throw new UserInformationException("Multiple versions found, please specify a single version or time", "MultipleFilesetsFound");
            return versions.FirstOrDefault(x => x.Version == requested[0])
                ?? throw new UserInformationException($"Version {requested[0]} does not exist", "NoFilesetsFound");
        }

        if (options.Time.Ticks > 0)
        {
            // Snapshot names have whole seconds, so the time is compared at that precision
            var time = options.Time.ToUniversalTime();
            time = new DateTime(time.Ticks - time.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            return versions.FirstOrDefault(x => x.Time <= time)
                ?? throw new UserInformationException($"No snapshot was written at or before {time:u}", "NoFilesetsFound");
        }

        return versions[0];
    }

    /// <summary>
    /// Gets the time of the last run recorded in the sync database, or now when there is none.
    /// </summary>
    private static async Task<DateTime> GetLastRunTimeAsync(Options options, CancellationToken ct)
    {
        if (!options.NoLocalDb && !string.IsNullOrWhiteSpace(options.Dbpath) && File.Exists(options.Dbpath))
        {
            using var db = new LocalSyncDatabase(options.Dbpath);
            var last = await db.GetLastResultTimeAsync(ct).ConfigureAwait(false);
            if (last.HasValue)
                return last.Value;
        }

        var now = DateTime.UtcNow;
        return new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
    }

    /// <summary>
    /// Maps the sources of the job to the folders that hold them on the destination, the
    /// same way the sync writes them: one source fills the version folder, several sources
    /// each get a sub-folder named after the source folder.
    /// </summary>
    private static IReadOnlyList<SourceRoot> MapSources(string[] sources)
    {
        if (sources == null || sources.Length == 0)
            throw new UserInformationException("The sync job has no sources", "NoSources");

        return sources
            .Select(x => new SourceRoot(Util.AppendDirSeparator(x), SyncHandler.GetSourceRelativeRoot(x, sources)))
            .ToArray();
    }

    /// <summary>
    /// Translates an original path to its relative path on the destination, or returns
    /// <c>null</c> when the path is not below any source or would leave the version folder.
    /// </summary>
    private static string? ToRemotePath(SyncVersion version, IReadOnlyList<SourceRoot> roots, string localPath)
    {
        foreach (var root in roots.OrderByDescending(x => x.LocalPath.Length))
        {
            var rootWithoutSeparator = root.LocalPath.TrimEnd('/', '\\');
            string rest;
            if (localPath.StartsWith(root.LocalPath, Library.Utility.Utility.ClientFilenameStringComparison))
                rest = localPath[root.LocalPath.Length..];
            else if (localPath.Equals(rootWithoutSeparator, Library.Utility.Utility.ClientFilenameStringComparison))
                rest = "";
            else
                continue;

            var segments = rest.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(x => x is "." or ".."))
                return null;

            var relative = string.Join("/", segments);
            var below = string.IsNullOrEmpty(root.RemotePath) ? relative : (string.IsNullOrEmpty(relative) ? root.RemotePath : root.RemotePath + "/" + relative);
            return string.IsNullOrEmpty(version.Folder) ? below : SyncSnapshotLayout.Combine(version.Folder, below);
        }

        return null;
    }

    /// <summary>
    /// Lists a folder on the destination, skipping entries whose names could leave it.
    /// </summary>
    private static async Task<IEnumerable<IFileEntry>> ListRemoteFolderAsync(IBackendManager backendManager, string remoteFolder, CancellationToken ct)
        => (await backendManager.ListAsync(string.IsNullOrEmpty(remoteFolder) ? null : remoteFolder, ct).ConfigureAwait(false))
            .Where(x =>
            {
                var name = x.Name?.TrimEnd('/', '\\');
                return !string.IsNullOrEmpty(name) && name != "." && name != ".." && !name.Contains('/') && !name.Contains('\\')
                    // The marker is part of the destination layout, not of the source
                    && !(string.IsNullOrEmpty(remoteFolder) && !x.IsFolder && name == SyncSnapshotLayout.MarkerFileName);
            });

    /// <summary>
    /// Adds every file and folder below a folder to the restore selection.
    /// </summary>
    private static async Task CollectFolderAsync(IBackendManager backendManager, string remoteFolder, string localFolder, Dictionary<string, RestoreItem> items, HashSet<string> folders, CancellationToken ct)
    {
        var pending = new Stack<(string Remote, string Local)>();
        pending.Push((remoteFolder, localFolder));
        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (remote, local) = pending.Pop();
            folders.Add(local);
            foreach (var entry in await ListRemoteFolderAsync(backendManager, remote, ct).ConfigureAwait(false))
            {
                var name = entry.Name.TrimEnd('/', '\\');
                var remotePath = string.IsNullOrEmpty(remote) ? name : remote + "/" + name;
                if (entry.IsFolder)
                    pending.Push((remotePath, Util.AppendDirSeparator(local + name)));
                else
                    items[local + name] = new RestoreItem(remotePath, local + name, Math.Max(entry.Size, 0), entry.LastModification.ToUniversalTime());
            }
        }
    }

    /// <summary>
    /// Adds a single file to the restore selection, looking it up in its folder's listing.
    /// </summary>
    private static async Task CollectFileAsync(IBackendManager backendManager, SyncVersion version, IReadOnlyList<SourceRoot> roots, string localPath, Dictionary<string, RestoreItem> items, CancellationToken ct)
    {
        var remotePath = ToRemotePath(version, roots, localPath)
            ?? throw new UserInformationException($"The file {localPath} is not part of the sync job", "FileNotInSyncJob");

        var slash = remotePath.LastIndexOf('/');
        var remoteFolder = slash < 0 ? "" : remotePath[..slash];
        var name = slash < 0 ? remotePath : remotePath[(slash + 1)..];
        var entry = (await ListRemoteFolderAsync(backendManager, remoteFolder, ct).ConfigureAwait(false))
            .FirstOrDefault(x => !x.IsFolder && x.Name == name);
        if (entry == null)
        {
            Logging.Log.WriteWarningMessage(LOGTAG, "SyncRestoreFileMissing", null, "The file {0} is not in the selected version", localPath);
            return;
        }

        items[localPath] = new RestoreItem(remotePath, localPath, Math.Max(entry.Size, 0), entry.LastModification.ToUniversalTime());
    }

    /// <summary>
    /// Gets the deepest folder that holds every path, with a trailing separator.
    /// </summary>
    private static string GetCommonFolderPrefix(IEnumerable<string> paths)
    {
        string? prefix = null;
        foreach (var path in paths)
        {
            var folder = path.EndsWith('/') || path.EndsWith('\\')
                ? path
                : path[..(path.LastIndexOfAny(['/', '\\']) + 1)];
            if (prefix == null)
            {
                prefix = folder;
                continue;
            }

            var length = 0;
            var max = Math.Min(prefix.Length, folder.Length);
            while (length < max && string.Compare(prefix, length, folder, length, 1, Library.Utility.Utility.ClientFilenameStringComparison) == 0)
                length++;
            prefix = prefix[..length];
            prefix = prefix[..(prefix.LastIndexOfAny(['/', '\\']) + 1)];
        }

        return prefix ?? "";
    }

    /// <summary>
    /// Gets a name next to an existing file for a restored copy that must not overwrite
    /// it: the restore date before the extension, and a counter if that is taken too.
    /// </summary>
    private static string GetFreeName(string target, DateTime restoreTime)
    {
        var ext = Path.GetExtension(target);
        var stem = Path.ChangeExtension(target, null) + "." + restoreTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var candidate = stem + ext;
        for (var i = 1; File.Exists(candidate); i++)
            candidate = stem + " (" + i.ToString(CultureInfo.InvariantCulture) + ")" + ext;
        return candidate;
    }
}
