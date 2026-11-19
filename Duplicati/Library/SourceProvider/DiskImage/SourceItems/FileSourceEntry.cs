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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

/// <summary>
/// Represents a file within a filesystem as a source entry for backup operations.
/// </summary>
internal class FileSourceEntry(string parentPath, IFilesystem filesystem, IFile file)
    : DiskImageEntryBase(System.IO.Path.Combine(parentPath, file.Address?.ToString("X016") ?? file.Path ?? "unknown"))
{
    /// <inheritdoc />
    public override bool IsFolder => file.IsDirectory;

    /// <inheritdoc />
    public override long Size => file.Size;

    /// If the file doesn't report a last modified time, use the current time as a fallback to always back it up.
    /// <inheritdoc />
    public override DateTime LastModificationUtc => file.LastModified ?? DateTime.UtcNow;

    /// <inheritdoc />
    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        return filesystem.OpenReadStreamAsync(file, cancellationToken);
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (IsFolder)
        {
            await foreach (var child in filesystem.ListFilesAsync(file, cancellationToken))
            {
                yield return new FileSourceEntry(this.Path, filesystem, child);
            }
        }
    }

    /// <inheritdoc />
    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add file/block-level metadata
        metadata["diskimage:Type"] = "block";
        metadata["file:Path"] = file.Path;
        metadata["file:Size"] = file.Size.ToString();
        metadata["file:IsDirectory"] = file.IsDirectory.ToString();
        metadata["filesystem:Type"] = filesystem.Type.ToString();

        if (file.Address.HasValue)
            metadata["block:Address"] = file.Address.Value.ToString();

        return metadata;
    }

}
