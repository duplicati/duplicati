// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Partition;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

internal class DiskSourceEntry(SourceProvider provider, IRawDisk disk)
    : DiskImageEntryBase(provider.MountedPath)
{
    public override bool IsFolder => true;
    public override bool IsRootEntry => true;
    public override long Size => disk.Size;

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var table = await PartitionTableFactory.CreateAsync(disk, cancellationToken);
        if (table != null)
        {
            await foreach (var partition in table.EnumeratePartitions(cancellationToken))
            {
                yield return new PartitionSourceEntry(this.Path, partition);
            }
        }
    }
}
