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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// A source provider that reads from a backend through an <see cref="IBackendManager"/>,
/// so the source gets the manager's retries, throttling and statistics and works for
/// every backend. It is what makes a Duplicati destination the source of a sync.
/// </summary>
internal sealed class BackendManagerSourceProvider : ISourceProvider
{
    /// <summary>
    /// The path of the root folder entry. Entry paths are only names to the sync
    /// handler and the filter, so one root serves every platform.
    /// </summary>
    public const string ROOT_PATH = "/";

    private static readonly string LOGTAG = Logging.Log.LogTagFromType<BackendManagerSourceProvider>();

    private readonly IBackendManager m_backendManager;
    private readonly StringComparer m_nameComparer;

    /// <summary>
    /// Creates a source provider that reads from the backend manager
    /// </summary>
    /// <param name="backendManager">The backend manager to read from; owned by the caller</param>
    /// <param name="nameComparer">The comparer that decides whether two names in a listing are the same file</param>
    public BackendManagerSourceProvider(IBackendManager backendManager, StringComparer nameComparer)
    {
        m_backendManager = backendManager;
        m_nameComparer = nameComparer;
    }

    /// <inheritdoc/>
    public string MountedPath => ROOT_PATH;

    /// <inheritdoc/>
    public bool NeedsStoredMetadata => false;

    /// <inheritdoc/>
    public bool NeedsCaseSensitiveFilters => false;

    /// <inheritdoc/>
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc/>
    public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new Entry(m_backendManager, m_nameComparer, "", true, 0, DateTime.UnixEpoch);
    }

    /// <inheritdoc/>
    public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
    {
        var relative = path.Replace('\\', '/').Trim('/');
        // Only the root is known without a listing; a file is found by enumerating its folder
        if (!isFolder || relative.Length > 0)
            return Task.FromResult<ISourceProviderEntry?>(null);

        return Task.FromResult<ISourceProviderEntry?>(new Entry(m_backendManager, m_nameComparer, "", true, 0, DateTime.UnixEpoch));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The backend manager belongs to the caller
    }

    /// <summary>
    /// A file or folder on the backend
    /// </summary>
    /// <param name="backendManager">The backend manager to read from</param>
    /// <param name="nameComparer">The comparer that decides whether two names in a listing are the same file</param>
    /// <param name="relativePath">The path below the backend root, with forward slashes and no trailing slash; empty for the root</param>
    /// <param name="isFolder">Whether the entry is a folder</param>
    /// <param name="size">The size the listing reported</param>
    /// <param name="lastModificationUtc">The modification time the listing reported</param>
    private sealed class Entry(IBackendManager backendManager, StringComparer nameComparer, string relativePath, bool isFolder, long size, DateTime lastModificationUtc) : ISourceProviderEntry
    {
        /// <inheritdoc/>
        public bool IsFolder => isFolder;
        /// <inheritdoc/>
        public bool IsMetaEntry => false;
        /// <inheritdoc/>
        public bool IsRootEntry => relativePath.Length == 0;
        /// <inheritdoc/>
        public DateTime CreatedUtc => lastModificationUtc;
        /// <inheritdoc/>
        public DateTime LastModificationUtc => lastModificationUtc;
        /// <inheritdoc/>
        public string Path => ROOT_PATH + relativePath + (isFolder && relativePath.Length > 0 ? "/" : "");
        /// <inheritdoc/>
        public long Size => size;
        /// <inheritdoc/>
        public bool IsSymlink => false;
        /// <inheritdoc/>
        public string? SymlinkTarget => null;
        /// <inheritdoc/>
        public FileAttributes Attributes => isFolder ? FileAttributes.Directory : FileAttributes.Normal;
        /// <inheritdoc/>
        public bool IsSpecialFile => false;
        /// <inheritdoc/>
        public bool IsBlockDevice => false;
        /// <inheritdoc/>
        public bool IsCharacterDevice => false;
        /// <inheritdoc/>
        public bool IsAlternateStream => false;
        /// <inheritdoc/>
        public string? HardlinkTargetId => null;

        /// <inheritdoc/>
        public async Task<Stream> OpenRead(CancellationToken cancellationToken)
        {
            if (isFolder)
                throw new InvalidOperationException("OpenRead can only be called on files");

            // The listed size is not passed on: a listing can be stale or report no
            // size at all, and the manager fails a download whose size differs.
            var file = await backendManager.GetFileUnencryptedWithPathAsync(relativePath, cancellationToken).ConfigureAwait(false);
            try
            {
                // The stream removes the temporary file when it is closed
                return Library.Utility.TempFileStream.Create(file);
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        /// <inheritdoc/>
        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>());

        /// <inheritdoc/>
        public async Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        {
            if (!isFolder)
                throw new InvalidOperationException("FileExists can only be called on folders");

            await foreach (var entry in Enumerate(cancellationToken).ConfigureAwait(false))
                if (!entry.IsFolder && entry.Path.EndsWith("/" + filename, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (!isFolder)
                throw new InvalidOperationException("Enumerate can only be called on folders");

            var items = (await backendManager.ListAsync(relativePath.Length == 0 ? null : relativePath, cancellationToken).ConfigureAwait(false)).ToList();

            // A listing that reports a name twice cannot be mirrored by name
            Library.Utility.Utility.GetUniqueItems(items.Where(x => !x.IsFolder).Select(x => x.Name ?? ""), nameComparer, out var duplicateNames);
            if (duplicateNames.Count > 0)
            {
                var message = string.Format("Found remote files reported as duplicates in the source, either the backend module is broken or you need to manually remove the extra copies.\nThe following files were found multiple times: {0}", string.Join(", ", duplicateNames));
                Logging.Log.WriteErrorMessage(LOGTAG, "DuplicateRemoteFiles", null, message);
                throw new RemoteListVerificationException(message, "DuplicateRemoteFiles");
            }

            foreach (var item in items)
            {
                // Several backends name a folder with a trailing slash
                var name = item.Name;
                if (item.IsFolder && name != null && name.Length > 1 && name[^1] == '/')
                    name = name[..^1];

                // The current and parent folder, and anything that is not a direct child
                if (string.IsNullOrWhiteSpace(name) || name == "." || name == ".." || name.Contains('/') || name.Contains('\\'))
                    continue;

                yield return new Entry(
                    backendManager,
                    nameComparer,
                    relativePath.Length == 0 ? name : relativePath + "/" + name,
                    item.IsFolder,
                    item.Size,
                    SyncHandler.AsUtc(item.LastModification));
            }
        }
    }
}
