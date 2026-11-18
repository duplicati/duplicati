using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// What removing a folder tree from a destination did, or in a dry run would do.
/// </summary>
/// <param name="FolderRemoved">Whether the folder itself is gone; false when the destination cannot delete folders, when something below the folder is kept, or when the folder could not be deleted</param>
/// <param name="FilesDeleted">The number of files deleted one by one; zero when the folder was deleted in one call at the first attempt</param>
/// <param name="SizeOfDeletedFiles">The size of the files deleted one by one</param>
/// <param name="FilesFailed">The number of files that could not be deleted</param>
/// <param name="EntriesKept">The number of files and folders below the folder that are outside the scope of the delete and are kept</param>
/// <param name="DeletedFiles">The paths of the files deleted one by one, or in a dry run that would be</param>
internal sealed record DeleteTreeResult(bool FolderRemoved, long FilesDeleted, long SizeOfDeletedFiles, long FilesFailed, long EntriesKept, IReadOnlyList<string> DeletedFiles);

/// <summary>
/// Removes a folder tree from a sync destination. Shared by snapshot retention, which
/// removes old snapshot folders, and <see cref="Options.SyncThenDelete"/>, which removes
/// destination folders that no longer exist in the source.
/// </summary>
internal static class SyncRemoteFolders
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SyncRemoteFolders));

    /// <summary>
    /// Deletes a folder with everything in it. A backend that can delete a folder in one
    /// call does so; otherwise every file is deleted one by one, and the empty folders
    /// stay behind because the backend has no way to remove them. A file that cannot be
    /// deleted is counted and does not stop the other files from being deleted.
    /// <para>
    /// With a scope, the tree is listed first and only the files and folders in scope are
    /// deleted; an entry out of scope is kept, and so is the folder holding it, so the
    /// files in scope are then deleted one by one. The folder is deleted in one call only
    /// when everything below it is in scope.
    /// </para>
    /// <para>
    /// A one-call delete that fails, because the destination refuses a folder with too many
    /// files in it, say, falls back to deleting the files one by one, after which the emptied
    /// folder is deleted in one call again.
    /// </para>
    /// </summary>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="folder">The relative path of the folder; must not be empty</param>
    /// <param name="inScope">Whether an entry below the folder may be deleted, given its path relative to the folder ('/'-separated) and whether it is a folder; null when everything may be deleted</param>
    /// <param name="dryRun">Whether nothing is deleted; the folders are still listed when the files have to be deleted one by one, so the result says what a real run would do</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>What was deleted, or in a dry run would be</returns>
    public static async Task<DeleteTreeResult> DeleteTreeAsync(IBackendManager backendManager, string folder, Func<string, bool, bool>? inScope, bool dryRun, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folder))
            throw new ArgumentException("A folder is required", nameof(folder));

        var oneCallFailed = false;
        if (backendManager.SupportsFolderDelete && inScope == null)
        {
            if (dryRun || await TryDeleteInOneCallAsync(backendManager, folder, cancellationToken).ConfigureAwait(false))
                return new DeleteTreeResult(true, 0, 0, 0, 0, []);
            oneCallFailed = true;
        }

        // Find the files to delete, and what is kept
        long entriesKept = 0;
        var files = new List<(string Path, long Size)>();
        var folders = new Stack<string>();
        folders.Push("");
        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var currentRel = folders.Pop();
            var current = currentRel.Length == 0 ? folder : folder + "/" + currentRel;
            IEnumerable<IFileEntry> entries;
            try
            {
                entries = await backendManager.ListAsync(current, cancellationToken).ConfigureAwait(false);
            }
            catch (FolderMissingException)
            {
                // A folder that is already gone has nothing left to delete
                if (currentRel.Length == 0)
                    return new DeleteTreeResult(true, 0, 0, 0, 0, []);
                continue;
            }

            foreach (var entry in entries)
            {
                var name = entry.Name?.TrimEnd('/', '\\');
                if (string.IsNullOrEmpty(name) || name == "." || name == "..")
                    continue;

                // A name that is not a single path segment (a recursive listing, say) cannot
                // be checked against the scope, so with a scope it is kept, and the folder too
                if (name.Contains('/') || name.Contains('\\'))
                {
                    if (inScope != null)
                        entriesKept++;
                    continue;
                }

                var rel = currentRel.Length == 0 ? name : currentRel + "/" + name;
                if (inScope != null && !inScope(rel, entry.IsFolder))
                {
                    entriesKept++;
                    continue;
                }

                if (entry.IsFolder)
                    folders.Push(rel);
                else
                    files.Add((folder + "/" + rel, entry.Size));
            }
        }

        if (entriesKept == 0 && backendManager.SupportsFolderDelete && !oneCallFailed)
        {
            if (dryRun || await TryDeleteInOneCallAsync(backendManager, folder, cancellationToken).ConfigureAwait(false))
                return new DeleteTreeResult(true, 0, 0, 0, 0, []);
            oneCallFailed = true;
        }

        long sizeOfDeletedFiles = 0, filesFailed = 0;
        var deletedFiles = new List<string>();
        foreach (var (path, size) in files)
        {
            if (!dryRun)
            {
                try
                {
                    await backendManager.DeleteWithPathAsync(path, size, true, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
                {
                    filesFailed++;
                    Logging.Log.WriteWarningMessage(LOGTAG, "DeleteFailed", ex, "Failed to delete file: {0}", path);
                    continue;
                }
            }

            deletedFiles.Add(path);
            sizeOfDeletedFiles += Math.Max(size, 0);
        }

        // With the files gone, the emptied folder is small enough to delete in one call
        var folderRemoved = false;
        if (oneCallFailed && entriesKept == 0 && filesFailed == 0)
        {
            try
            {
                await backendManager.DeleteFolderRecursiveAsync(folder, cancellationToken).ConfigureAwait(false);
                folderRemoved = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "EmptiedFolderDeleteFailed", ex, "The files of {0} are deleted, but the emptied folder could not be deleted: {1}", folder, ex.Message);
            }
        }

        if (entriesKept > 0)
            Logging.Log.WriteVerboseMessage(LOGTAG, "EntriesKept", "{0} entries below {1} are outside the scope of the delete, so they and the folders holding them are kept", entriesKept, folder);
        else if (!backendManager.SupportsFolderDelete)
            Logging.Log.WriteVerboseMessage(LOGTAG, "EmptyFoldersLeft", "The destination cannot delete folders, so the empty folders of {0} are left behind", folder);
        return new DeleteTreeResult(folderRemoved, deletedFiles.Count, sizeOfDeletedFiles, filesFailed, entriesKept, deletedFiles);
    }

    /// <summary>
    /// Deletes a folder with everything in it in one call, and reports a failure instead of
    /// throwing it, so the caller can fall back to deleting the files one by one
    /// </summary>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="folder">The relative path of the folder</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>True if the folder is deleted</returns>
    private static async Task<bool> TryDeleteInOneCallAsync(IBackendManager backendManager, string folder, CancellationToken cancellationToken)
    {
        try
        {
            await backendManager.DeleteFolderRecursiveAsync(folder, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "FolderDeleteFallback", "The folder {0} could not be deleted in one call, so its files are deleted one by one: {1}", folder, ex.Message);
            return false;
        }
    }
}
