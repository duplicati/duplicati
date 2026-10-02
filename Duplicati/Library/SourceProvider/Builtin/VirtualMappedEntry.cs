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

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.Builtin;

/// <summary>
/// Wraps a snapshot-backed filesystem entry and exposes it below a virtual folder.
/// A data path is placed directly in the virtual folder under the name assigned by
/// <see cref="VirtualSourcePath.AssignNames"/>, and the entries inside a data path
/// folder keep their path relative to it.
/// All file operations are delegated to the wrapped entry.
/// <para>
/// The metadata of every mapped entry records the local path it was read from
/// (<c>&lt;prefix&gt;orig-path</c>), together with the metadata that identifies
/// the virtual item it belongs to, so a restore can find the original location
/// of a single file without parsing the virtual path.
/// </para>
/// </summary>
internal sealed class VirtualMappedEntry : ISourceProviderEntry
{
    /// <summary>
    /// The wrapped snapshot entry
    /// </summary>
    private readonly ISourceProviderEntry _inner;

    /// <summary>
    /// The metadata key prefix, e.g. <c>hyperv:</c>
    /// </summary>
    private readonly string _metadataPrefix;

    /// <summary>
    /// The metadata version written to the entries
    /// </summary>
    private readonly string _metadataVersion;

    /// <summary>
    /// The metadata identifying the virtual item the entry belongs to
    /// </summary>
    private readonly IReadOnlyDictionary<string, string?> _itemMetadata;

    /// <summary>
    /// Creates a new mapped entry
    /// </summary>
    /// <param name="inner">The snapshot-backed entry to wrap</param>
    /// <param name="path">The virtual path of the entry</param>
    /// <param name="metadataPrefix">The metadata key prefix, e.g. <c>hyperv:</c></param>
    /// <param name="metadataVersion">The metadata version written to the entries</param>
    /// <param name="itemMetadata">The metadata identifying the virtual item the entry belongs to</param>
    private VirtualMappedEntry(ISourceProviderEntry inner, string path, string metadataPrefix, string metadataVersion, IReadOnlyDictionary<string, string?> itemMetadata)
    {
        _inner = inner;
        _metadataPrefix = metadataPrefix;
        _metadataVersion = metadataVersion;
        _itemMetadata = itemMetadata;
        Path = inner.IsFolder ? Util.AppendDirSeparator(path) : path;
    }

    /// <summary>
    /// Maps the data paths of a virtual item into its virtual folder
    /// </summary>
    /// <param name="virtualFolder">The virtual folder of the item</param>
    /// <param name="dataPaths">The snapshot-backed entries of the item's data paths</param>
    /// <param name="metadataPrefix">The metadata key prefix, e.g. <c>hyperv:</c></param>
    /// <param name="metadataVersion">The metadata version written to the entries</param>
    /// <param name="itemMetadata">The metadata identifying the virtual item</param>
    /// <returns>The mapped entries, in the same order as the data paths</returns>
    public static List<VirtualMappedEntry> MapDataPaths(string virtualFolder, IReadOnlyList<ISourceProviderEntry> dataPaths, string metadataPrefix, string metadataVersion, IReadOnlyDictionary<string, string?> itemMetadata)
    {
        var names = VirtualSourcePath.AssignNames(dataPaths.Select(x => (x.Path, x.IsFolder)).ToList());
        var folder = Util.AppendDirSeparator(virtualFolder);

        return dataPaths
            .Select((x, i) => new VirtualMappedEntry(x, folder + names[i], metadataPrefix, metadataVersion, itemMetadata))
            .ToList();
    }

    /// <inheritdoc />
    public bool IsFolder => _inner.IsFolder;

    /// <inheritdoc />
    public bool IsMetaEntry => _inner.IsMetaEntry;

    /// <inheritdoc />
    public bool IsRootEntry => false;

    /// <inheritdoc />
    public DateTime CreatedUtc => _inner.CreatedUtc;

    /// <inheritdoc />
    public DateTime LastModificationUtc => _inner.LastModificationUtc;

    /// <inheritdoc />
    public string Path { get; }

    /// <inheritdoc />
    public long Size => _inner.Size;

    /// <inheritdoc />
    public bool IsSymlink => _inner.IsSymlink;

    /// <inheritdoc />
    public string? SymlinkTarget => _inner.SymlinkTarget;

    /// <inheritdoc />
    public FileAttributes Attributes => _inner.Attributes;

    /// <inheritdoc />
    public bool IsBlockDevice => _inner.IsBlockDevice;

    /// <inheritdoc />
    public bool IsCharacterDevice => _inner.IsCharacterDevice;

    /// <inheritdoc />
    public bool IsAlternateStream => _inner.IsAlternateStream;

    /// <inheritdoc />
    public string? HardlinkTargetId => _inner.HardlinkTargetId;

    /// <inheritdoc />
    public Task<Stream> OpenRead(CancellationToken cancellationToken)
        => _inner.OpenRead(cancellationToken);

    /// <inheritdoc />
    public async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await _inner.GetMinorMetadata(cancellationToken).ConfigureAwait(false) ?? [];
        foreach (var kv in _itemMetadata)
            metadata[kv.Key] = kv.Value;

        metadata[_metadataPrefix + "v"] = _metadataVersion;
        metadata[_metadataPrefix + "Type"] = IsFolder ? "Folder" : "File";
        metadata[_metadataPrefix + VirtualSourcePath.ORIGINAL_PATH_KEY] = _inner.Path;
        return metadata;
    }

    /// <inheritdoc />
    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
        => _inner.FileExists(filename, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var innerFolder = Util.AppendDirSeparator(_inner.Path);
        await foreach (var child in _inner.Enumerate(cancellationToken).ConfigureAwait(false))
        {
            var relativePath = child.Path.StartsWith(innerFolder, StringComparison.OrdinalIgnoreCase)
                ? child.Path.Substring(innerFolder.Length).TrimEnd(System.IO.Path.DirectorySeparatorChar)
                : VirtualSourcePath.GetDataPathName(child.Path);

            yield return new VirtualMappedEntry(child, Path + relativePath, _metadataPrefix, _metadataVersion, _itemMetadata);
        }
    }
}
