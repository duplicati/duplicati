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

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage;

/// <summary>
/// Restore provider for disk images that writes to an image file instead of a
/// physical disk, e.g. <c>diskimage-file:///images/server01.img</c>. The file is a
/// raw (dd-style) image, or a dynamic VHD or VHDX file, chosen by the file
/// extension or the <c>diskimage-restore-image-format</c> option.
/// A whole-disk backup is restored to a new image the size of the source disk.
/// When the target URL points to a partition within an existing image (e.g.
/// <c>diskimage-file:///images/server01.img/part_GPT_1</c>), the data is restored
/// into that partition. The restore logic is shared with <see cref="RestoreProvider"/>.
/// </summary>
public sealed class ImageFileRestoreProvider : IRestoreDestinationProviderModule, IDisposable
{
    /// <summary>
    /// The restore provider that performs the restore, or <c>null</c> when this
    /// instance is only used for loading metadata about the provider.
    /// </summary>
    private readonly RestoreProvider? _provider;

    /// <summary>
    /// Default constructor for the restore provider.
    /// Only used for loading metadata about the provider.
    /// </summary>
    public ImageFileRestoreProvider()
    {
        _provider = null;
    }

    /// <summary>
    /// Constructs the ImageFileRestoreProvider with the given URL and options.
    /// </summary>
    /// <param name="url">The destination URL for the restore operation</param>
    /// <param name="options">The options for the restore operation</param>
    public ImageFileRestoreProvider(string url, Dictionary<string, string?> options)
    {
        _provider = new RestoreProvider(url, options, RestoreTargetKind.ImageFile);
    }

    /// <summary>
    /// Gets the restore provider that performs the restore.
    /// </summary>
    private RestoreProvider Provider => _provider ?? throw new InvalidOperationException("The provider was created for metadata only.");

    /// <inheritdoc />
    public string Key => OptionsHelper.ImageFileModuleKey;

    /// <inheritdoc />
    public string DisplayName => Strings.ImageFileRestoreProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.ImageFileRestoreProviderDescription;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.ImageFileSupportedCommands;

    /// <inheritdoc />
    public string TargetDestination => Provider.TargetDestination;

    /// <inheritdoc />
    public Task Initialize(CancellationToken cancel)
        => Provider.Initialize(cancel);

    /// <inheritdoc />
    public Task Test(CancellationToken cancellationToken)
        => Provider.Test(cancellationToken);

    /// <inheritdoc />
    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancel)
        => Provider.CreateFolderIfNotExists(path, cancel);

    /// <inheritdoc />
    public Task<bool> FileExists(string path, CancellationToken cancel)
        => Provider.FileExists(path, cancel);

    /// <inheritdoc />
    public Task<Stream> OpenWrite(string path, CancellationToken cancel)
        => Provider.OpenWrite(path, cancel);

    /// <inheritdoc />
    public Task<Stream> OpenRead(string path, CancellationToken cancel)
        => Provider.OpenRead(path, cancel);

    /// <inheritdoc />
    public Task<Stream> OpenReadWrite(string path, CancellationToken cancel)
        => Provider.OpenReadWrite(path, cancel);

    /// <inheritdoc />
    public Task<long> GetFileLength(string path, CancellationToken cancel)
        => Provider.GetFileLength(path, cancel);

    /// <inheritdoc />
    public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancel)
        => Provider.HasReadOnlyAttribute(path, cancel);

    /// <inheritdoc />
    public Task ClearReadOnlyAttribute(string path, CancellationToken cancel)
        => Provider.ClearReadOnlyAttribute(path, cancel);

    /// <inheritdoc />
    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool restoreSymlinkMetadata, bool restorePermissions, bool hasRestoredEntriesBelow, CancellationToken cancel)
        => Provider.WriteMetadata(path, metadata, restoreSymlinkMetadata, restorePermissions, hasRestoredEntriesBelow, cancel);

    /// <inheritdoc />
    public Task DeleteFolder(string path, CancellationToken cancel)
        => Provider.DeleteFolder(path, cancel);

    /// <inheritdoc />
    public Task DeleteFile(string path, CancellationToken cancel)
        => Provider.DeleteFile(path, cancel);

    /// <inheritdoc />
    public IList<string> GetPriorityFiles()
        => Provider.GetPriorityFiles();

    /// <inheritdoc />
    public Task Finalize(Action<double>? progressCallback, bool isComplete, CancellationToken cancel)
        => Provider.Finalize(progressCallback, isComplete, cancel);

    /// <inheritdoc />
    public void Dispose()
        => _provider?.Dispose();
}
