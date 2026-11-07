using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

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
    /// stay behind because the backend has no way to remove them.
    /// </summary>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="folder">The relative path of the folder; must not be empty</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public static async Task DeleteTreeAsync(IBackendManager backendManager, string folder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(folder))
            throw new System.ArgumentException("A folder is required", nameof(folder));

        if (backendManager.SupportsFolderDelete)
        {
            await backendManager.DeleteFolderRecursiveAsync(folder, cancellationToken).ConfigureAwait(false);
            return;
        }

        var folders = new Stack<string>();
        folders.Push(folder);
        while (folders.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = folders.Pop();
            foreach (var entry in await backendManager.ListAsync(current, cancellationToken).ConfigureAwait(false))
            {
                var name = entry.Name?.TrimEnd('/', '\\');
                if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.Contains('/') || name.Contains('\\'))
                    continue;

                var path = current + "/" + name;
                if (entry.IsFolder)
                    folders.Push(path);
                else
                    await backendManager.DeleteWithPathAsync(path, entry.Size, true, cancellationToken).ConfigureAwait(false);
            }
        }

        Logging.Log.WriteVerboseMessage(LOGTAG, "EmptyFoldersLeft", "The destination cannot delete folders, so the empty folders of {0} were left behind", folder);
    }
}
