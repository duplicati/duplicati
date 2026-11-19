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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

/// <summary>
/// Represents a consolidated geometry metadata file as a source entry.
/// This file stores all geometry information for disk, partition table, partitions, and filesystems.
/// </summary>
internal class GeometrySourceEntry : DiskImageEntryBase
{
    private readonly GeometryMetadata _metadata;

    public GeometrySourceEntry(string parentPath, GeometryMetadata metadata)
        : base(System.IO.Path.Combine(parentPath, GeometryMetadata.FileName))
    {
        _metadata = metadata;
    }

    /// <inheritdoc />
    public override bool IsFolder => false;
    /// <inheritdoc />
    public override bool IsMetaEntry => false;
    /// <inheritdoc />
    public override long Size => Encoding.UTF8.GetByteCount(_metadata.ToJson());
    /// Geometry should always be backed up, as this captures re-partitionings.
    /// <inheritdoc />
    public override DateTime LastModificationUtc => DateTime.UtcNow;

    public GeometryMetadata Metadata => _metadata;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = _metadata.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Geometry entries are leaf nodes - no children
        await Task.CompletedTask;
        yield break;
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add geometry-specific metadata
        metadata["diskimage:Type"] = "geometry";
        metadata["geometry:Version"] = _metadata.Version.ToString();

        // Add disk info if available
        if (_metadata.Disk != null)
        {
            metadata["disk:DevicePath"] = _metadata.Disk.DevicePath;
            metadata["disk:Size"] = _metadata.Disk.Size.ToString();
            metadata["disk:SectorSize"] = _metadata.Disk.SectorSize.ToString();
            metadata["disk:Sectors"] = _metadata.Disk.Sectors.ToString();
            metadata["disk:PartitionTableType"] = _metadata.Disk.TableType.ToString();
        }

        // Add partition table info if available
        if (_metadata.PartitionTable != null)
        {
            metadata["partition_table:Type"] = _metadata.PartitionTable.Type.ToString();
            metadata["partition_table:Size"] = _metadata.PartitionTable.Size.ToString();
        }

        // Add partition count
        if (_metadata.Partitions != null)
        {
            metadata["partitions:Count"] = _metadata.Partitions.Count.ToString();
        }

        // Add filesystem count
        if (_metadata.Filesystems != null)
        {
            metadata["filesystems:Count"] = _metadata.Filesystems.Count.ToString();
        }

        return metadata;
    }

}
