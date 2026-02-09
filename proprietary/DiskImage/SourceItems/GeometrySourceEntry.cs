// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.DiskImage.SourceItems;

/// <summary>
/// Represents a consolidated geometry metadata file as a source entry.
/// This file stores all geometry information for disk, partition table, partitions, and filesystems.
/// </summary>
internal class GeometrySourceEntry : DiskImageEntryBase
{
    private readonly GeometryMetadata _metadata;

    public GeometrySourceEntry(string parentPath, GeometryMetadata metadata)
        : base(PathCombine(parentPath, "geometry.json"))
    {
        _metadata = metadata;
    }

    public override bool IsFolder => false;
    public override bool IsMetaEntry => true;
    public override long Size => Encoding.UTF8.GetByteCount(_metadata.ToJson());

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

    private static string PathCombine(string p1, string p2)
    {
        if (string.IsNullOrEmpty(p1)) return p2;
        if (string.IsNullOrEmpty(p2)) return p1;
        return p1.TrimEnd('/') + "/" + p2.TrimStart('/');
    }
}
