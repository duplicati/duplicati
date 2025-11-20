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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots.MacOS;

[SupportedOSPlatform("macOS")]
internal sealed class MacOSPhotoAssetEntry : ISourceProviderEntry
{
    private readonly MacOSPhotosLibrary library;
    private readonly string libraryRootPath;
    private readonly MacOSPhotoAsset asset;
    private readonly Dictionary<string, string> metadata;

    public MacOSPhotoAssetEntry(MacOSPhotosLibrary library, string rootPath, MacOSPhotoAsset asset)
    {
        this.library = library ?? throw new ArgumentNullException(nameof(library));
        libraryRootPath = rootPath ?? throw new ArgumentNullException(nameof(rootPath));
        this.asset = asset ?? throw new ArgumentNullException(nameof(asset));

        metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LocalIdentifier"] = asset.Identifier,
            ["OriginalFileName"] = asset.FileName
        };

        if (!string.IsNullOrEmpty(asset.UniformTypeIdentifier))
            metadata["UniformTypeIdentifier"] = asset.UniformTypeIdentifier!;

        metadata["MediaType"] = asset.MediaType.ToString();
    }

    public string RelativePath => asset.RelativePath;

    public bool IsFolder => false;

    public bool IsMetaEntry => false;

    public bool IsRootEntry => false;

    public DateTime CreatedUtc => asset.CreatedUtc ?? asset.ModifiedUtc ?? DateTime.UnixEpoch;

    public DateTime LastModificationUtc => asset.ModifiedUtc ?? asset.CreatedUtc ?? DateTime.UnixEpoch;

    public string Path => System.IO.Path.Combine(libraryRootPath, asset.RelativePath);

    public long Size => asset.Size ?? -1;

    public bool IsSymlink => false;

    public string? SymlinkTarget => null;

    public FileAttributes Attributes => FileAttributes.Normal;

    public Dictionary<string, string> MinorMetadata => metadata;

    public bool IsBlockDevice => false;

    public bool IsCharacterDevice => false;

    public bool IsAlternateStream => false;

    public string? HardlinkTargetId => null;

    public async Task<Stream> OpenRead(CancellationToken cancellationToken)
        => await library.OpenAssetStreamAsync(asset, cancellationToken).ConfigureAwait(false);

    public Task<Stream?> OpenMetadataRead(CancellationToken cancellationToken)
        => Task.FromResult<Stream?>(new MemoryStream(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(metadata)));

    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => Task.FromResult(false);

    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
        => AsyncEnumerable.Empty<ISourceProviderEntry>();
}
