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

internal class FilesystemSourceEntry(string parentPath, IFilesystem filesystem)
    : DiskImageEntryBase(PathCombine(parentPath, $"fs_{filesystem.Type}{System.IO.Path.DirectorySeparatorChar}"))
{
    public override bool IsFolder => true;

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var file in filesystem.ListFilesAsync(cancellationToken))
        {
            yield return new FileSourceEntry(this.Path, filesystem, file);
        }
    }

    private static string PathCombine(string p1, string p2)
    {
        if (string.IsNullOrEmpty(p1)) return p2;
        if (string.IsNullOrEmpty(p2)) return p1;
        return p1.TrimEnd('/') + "/" + p2.TrimStart('/');
    }
}
