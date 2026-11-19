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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.DynamicLoader;

/// <summary>
/// Wraps a backend and appends a suffix to the name of each remote file,
/// for storage that only accepts some file types.
/// The names Duplicati sees are unchanged: the suffix is added when a file is written or read,
/// and removed when the files are listed. Files without the suffix are not listed.
/// </summary>
public sealed class RemoteFilenameSuffixBackend :
    IWrappingBackend,
    IStreamingBackend,
    IResumeEnabledBackend,
    IQuotaEnabledBackend,
    ILockingBackend,
    IRenameEnabledBackend,
    IFolderEnabledBackend,
    IFolderDeleteEnabledBackend,
    IFileInfoEnabledBackend,
    IModificationTimeEnabledBackend,
    ILinkEnabledBackend
{
    /// <summary>
    /// The option that sets the suffix
    /// </summary>
    public const string OptionName = "remote-filename-suffix";

    /// <summary>
    /// The wrapped backend
    /// </summary>
    private readonly IBackend m_backend;

    /// <summary>
    /// The suffix that is appended to the remote names
    /// </summary>
    private readonly string m_suffix;

    /// <summary>
    /// Creates a backend that appends a suffix to the remote names of another backend
    /// </summary>
    /// <param name="backend">The backend to wrap</param>
    /// <param name="suffix">The suffix to append</param>
    public RemoteFilenameSuffixBackend(IBackend backend, string suffix)
    {
        if (string.IsNullOrEmpty(suffix))
            throw new ArgumentException("The suffix must not be empty", nameof(suffix));
        if (suffix.IndexOfAny(['/', '\\']) >= 0)
            throw new UserInformationException(Strings.DynamicLoader.RemoteFilenameSuffixHasPathSeparator(OptionName, suffix), "RemoteFilenameSuffixHasPathSeparator");

        m_backend = backend;
        m_suffix = suffix;
    }

    /// <inheritdoc/>
    public IBackend WrappedBackend => m_backend;

    /// <summary>
    /// Gets the wrapped backend as an optional interface, or throws if it does not support it.
    /// Callers check with <see cref="BackendCapabilityExtensions.TryGetCapability{T}"/> first.
    /// </summary>
    private T Inner<T>() where T : class, IBackend
        => m_backend.TryGetCapability<T>(out var capability)
            ? capability
            : throw new NotSupportedException($"The backend {m_backend.ProtocolKey} does not implement {typeof(T).Name}");

    /// <summary>
    /// Gets the remote name of a file
    /// </summary>
    private string ToRemote(string name) => name + m_suffix;

    /// <summary>
    /// Translates an entry from the wrapped backend: a file with the suffix loses it,
    /// a file without it is not one of ours and gives null, a folder is unchanged.
    /// </summary>
    private IFileEntry? FromRemote(IFileEntry entry)
    {
        if (entry.IsFolder)
            return entry;
        if (!entry.Name.EndsWith(m_suffix, StringComparison.Ordinal) || entry.Name.Length == m_suffix.Length)
            return null;
        return new RenamedFileEntry(entry, entry.Name.Substring(0, entry.Name.Length - m_suffix.Length));
    }

    /// <summary>
    /// Translates the entries listed by the wrapped backend
    /// </summary>
    private async IAsyncEnumerable<IFileEntry> FromRemote(IAsyncEnumerable<IFileEntry> entries, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var entry in entries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var translated = FromRemote(entry);
            if (translated != null)
                yield return translated;
        }
    }

    /// <inheritdoc/>
    public IList<ICommandLineArgument> SupportedCommands => m_backend.SupportedCommands;

    /// <inheritdoc/>
    public string DisplayName => m_backend.DisplayName;

    /// <inheritdoc/>
    public string ProtocolKey => m_backend.ProtocolKey;

    /// <inheritdoc/>
    public string Description => m_backend.Description;

    /// <inheritdoc/>
    public IAsyncEnumerable<IFileEntry> ListAsync(CancellationToken cancellationToken)
        => FromRemote(m_backend.ListAsync(cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task PutAsync(string remotename, string filename, CancellationToken cancellationToken)
        => m_backend.PutAsync(ToRemote(remotename), filename, cancellationToken);

    /// <inheritdoc/>
    public Task GetAsync(string remotename, string filename, CancellationToken cancellationToken)
        => m_backend.GetAsync(ToRemote(remotename), filename, cancellationToken);

    /// <inheritdoc/>
    public Task DeleteAsync(string remotename, CancellationToken cancellationToken)
        => m_backend.DeleteAsync(ToRemote(remotename), cancellationToken);

    /// <inheritdoc/>
    public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken)
        => m_backend.GetDNSNamesAsync(cancelToken);

    /// <inheritdoc/>
    public Task TestAsync(bool alsoWrite, CancellationToken cancellationToken)
        => m_backend.TestAsync(alsoWrite, cancellationToken);

    /// <inheritdoc/>
    public Task CreateFolderAsync(CancellationToken cancellationToken)
        => m_backend.CreateFolderAsync(cancellationToken);

    /// <inheritdoc/>
    public bool SupportsStreaming
        => m_backend.TryGetCapability<IStreamingBackend>(out var streaming) && streaming.SupportsStreaming;

    /// <inheritdoc/>
    public Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        => Inner<IStreamingBackend>().PutAsync(ToRemote(remotename), stream, cancelToken);

    /// <inheritdoc/>
    public Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        => Inner<IStreamingBackend>().GetAsync(ToRemote(remotename), stream, cancelToken);

    /// <inheritdoc/>
    public Task<(Stream Stream, string ETag)> GetResumableStreamAsync(string remotename, long offset, string? etag, CancellationToken cancelToken)
        => Inner<IResumeEnabledBackend>().GetResumableStreamAsync(ToRemote(remotename), offset, etag, cancelToken);

    /// <inheritdoc/>
    public Task<IQuotaInfo?> GetQuotaInfoAsync(CancellationToken cancelToken)
        => Inner<IQuotaEnabledBackend>().GetQuotaInfoAsync(cancelToken);

    /// <inheritdoc/>
    public Task<DateTime?> GetObjectLockUntilAsync(string remotename, CancellationToken cancellationToken)
        => Inner<ILockingBackend>().GetObjectLockUntilAsync(ToRemote(remotename), cancellationToken);

    /// <inheritdoc/>
    public Task SetObjectLockUntilAsync(string remotename, DateTime lockUntilUtc, CancellationToken cancellationToken)
        => Inner<ILockingBackend>().SetObjectLockUntilAsync(ToRemote(remotename), lockUntilUtc, cancellationToken);

    /// <inheritdoc/>
    public Task RenameAsync(string oldname, string newname, CancellationToken cancellationToken)
        => Inner<IRenameEnabledBackend>().RenameAsync(ToRemote(oldname), ToRemote(newname), cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<IFileEntry> ListAsync(string? path, CancellationToken cancellationToken)
        => FromRemote(Inner<IFolderEnabledBackend>().ListAsync(path, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public async Task<IFileEntry?> GetEntryAsync(string path, CancellationToken cancellationToken)
    {
        var folderBackend = Inner<IFolderEnabledBackend>();
        var file = await folderBackend.GetEntryAsync(ToRemote(path), cancellationToken).ConfigureAwait(false);
        if (file != null && !file.IsFolder)
            return FromRemote(file);

        // A folder has no suffix
        var folder = await folderBackend.GetEntryAsync(path, cancellationToken).ConfigureAwait(false);
        return folder != null && folder.IsFolder ? folder : null;
    }

    /// <inheritdoc/>
    public bool HasCaseSensitivePaths => Inner<IFolderEnabledBackend>().HasCaseSensitivePaths;

    /// <inheritdoc/>
    public Task DeleteFolderRecursiveAsync(string path, CancellationToken cancellationToken)
        => Inner<IFolderDeleteEnabledBackend>().DeleteFolderRecursiveAsync(path, cancellationToken);

    /// <inheritdoc/>
    public async Task<IFileEntry?> GetFileInfoAsync(string remotename, CancellationToken cancelToken)
    {
        var entry = await Inner<IFileInfoEnabledBackend>().GetFileInfoAsync(ToRemote(remotename), cancelToken).ConfigureAwait(false);
        return entry == null ? null : FromRemote(entry);
    }

    /// <inheritdoc/>
    public Task PutAsync(string remotename, Stream stream, DateTime lastModifiedUtc, CancellationToken cancelToken)
        => Inner<IModificationTimeEnabledBackend>().PutAsync(ToRemote(remotename), stream, lastModifiedUtc, cancelToken);

    /// <inheritdoc/>
    public Task CreateHardLinkAsync(string existingName, string targetName, CancellationToken cancellationToken)
        => Inner<ILinkEnabledBackend>().CreateHardLinkAsync(ToRemote(existingName), ToRemote(targetName), cancellationToken);

    /// <inheritdoc/>
    public void Dispose() => m_backend.Dispose();

    /// <summary>
    /// A file entry with another name
    /// </summary>
    private sealed class RenamedFileEntry(IFileEntry entry, string name) : IFileEntry
    {
        /// <inheritdoc/>
        public bool IsFolder => entry.IsFolder;
        /// <inheritdoc/>
        public DateTime LastAccess => entry.LastAccess;
        /// <inheritdoc/>
        public DateTime LastModification => entry.LastModification;
        /// <inheritdoc/>
        public DateTime Created => entry.Created;
        /// <inheritdoc/>
        public string Name => name;
        /// <inheritdoc/>
        public long Size => entry.Size;
        /// <inheritdoc/>
        public bool IsArchived => entry.IsArchived;
    }
}
