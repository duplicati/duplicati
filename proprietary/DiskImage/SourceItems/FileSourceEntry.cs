// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Filesystem;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

internal class FileSourceEntry(string parentPath, IFilesystem filesystem, IFile file)
    : DiskImageEntryBase(System.IO.Path.Combine(parentPath, file.Path ?? file.Address?.ToString("X016") ?? "unknown"))
{
    public override bool IsFolder => file.IsDirectory;
    public override long Size => file.Size;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        return filesystem.OpenReadStreamAsync(file, cancellationToken);
    }

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
