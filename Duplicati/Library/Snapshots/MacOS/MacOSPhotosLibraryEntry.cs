// Copyright (C) 2025, The Duplicati Team
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
using System.Runtime.Versioning;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Snapshots.MacOS;

[SupportedOSPlatform("macOS")]
internal sealed class MacOSPhotosLibraryEntry : ISourceProviderEntry
{
    private static readonly string LOGTAG = Log.LogTagFromType<MacOSPhotosLibraryEntry>();

    private readonly ISourceProviderEntry inner;
    private readonly MacOSPhotosLibrary photosLibrary;
    private readonly SemaphoreSlim cacheLock = new(1, 1);

    private IReadOnlyList<MacOSPhotoAssetEntry>? cachedEntries;
    private Dictionary<string, MacOSPhotoAssetEntry>? cachedEntriesByRelativePath;
    private readonly MacOSPhotosHandling photosHandling;

    internal MacOSPhotosLibraryEntry(ISourceProviderEntry entry, MacOSPhotosHandling photosHandling)
    {
        inner = entry;
        photosLibrary = new MacOSPhotosLibrary(entry.Path);
        this.photosHandling = photosHandling;
    }

    public bool IsFolder => inner.IsFolder;

    public bool IsMetaEntry => inner.IsMetaEntry;

    public bool IsRootEntry => inner.IsRootEntry;

    public DateTime CreatedUtc => inner.CreatedUtc;

    public DateTime LastModificationUtc => inner.LastModificationUtc;

    public string Path => inner.Path;

    public long Size => inner.Size;

    public bool IsSymlink => inner.IsSymlink;

    public string? SymlinkTarget => inner.SymlinkTarget;

    public FileAttributes Attributes => inner.Attributes;

    public Dictionary<string, string> MinorMetadata => inner.MinorMetadata;

    public bool IsBlockDevice => inner.IsBlockDevice;

    public bool IsCharacterDevice => inner.IsCharacterDevice;

    public bool IsAlternateStream => inner.IsAlternateStream;

    public string? HardlinkTargetId => inner.HardlinkTargetId;

    public Task<Stream> OpenRead(CancellationToken cancellationToken)
        => inner.OpenRead(cancellationToken);

    public Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken)
        => inner.OpenMetadataRead(cancellationToken);

    public async Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        await EnsureEntriesAsync(cancellationToken).ConfigureAwait(false);

        if (cachedEntriesByRelativePath == null)
            return false;

        var key = NormalizeRelativePath(filename);
        return cachedEntriesByRelativePath.ContainsKey(key);
    }

    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (photosHandling != MacOSPhotosHandling.PhotosOnly)
        {
            await foreach (var item in inner.Enumerate(cancellationToken))
                yield return item;
        }

        if (photosHandling != MacOSPhotosHandling.LibraryOnly)
        {
            await EnsureEntriesAsync(cancellationToken).ConfigureAwait(false);
            if (cachedEntries == null)
                yield break;

            // Wrap the assets into a virtual subfolder to avoid collisions with other files in the library
            yield return new MacOSPhotoSubFolder(this, MacOSPhotosLibrary.EXPORT_SUBFOLDER, cachedEntries);
        }
    }

    private async Task EnsureEntriesAsync(CancellationToken cancellationToken)
    {
        if (cachedEntries != null)
            return;

        await cacheLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (cachedEntries != null)
                return;

            var assets = await photosLibrary.GetAssetsAsync(cancellationToken).ConfigureAwait(false);
            var entries = assets.Select(asset => new MacOSPhotoAssetEntry(photosLibrary, inner.Path, asset)).ToList();

            cachedEntries = entries;
            cachedEntriesByRelativePath = entries.ToDictionary(x => NormalizeRelativePath(x.RelativePath), x => x, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            cacheLock.Release();
        }
    }

    private static string NormalizeRelativePath(string path)
        => path.Replace(System.IO.Path.AltDirectorySeparatorChar, System.IO.Path.DirectorySeparatorChar);
}
