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

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Disk;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Base implementation of <see cref="IPartition"/> that provides common properties
/// and shared <see cref="OpenReadAsync"/> and <see cref="OpenWriteAsync"/> logic.
/// </summary>
internal class BasePartition : IPartition
{
    /// <inheritdoc />
    public int PartitionNumber { get; init; }

    /// <inheritdoc />
    public PartitionType Type { get; init; }

    /// <inheritdoc />
    public required IPartitionTable PartitionTable { get; init; }

    /// <inheritdoc />
    public long StartOffset { get; init; }

    /// <inheritdoc />
    public long Size { get; init; }

    /// <inheritdoc />
    public string? Name { get; init; }

    /// <inheritdoc />
    public FileSystemType FilesystemType { get; init; }

    /// <inheritdoc />
    public Guid? VolumeGuid { get; init; }

    /// <summary>
    /// Gets the raw disk reference. May be null if the partition was reconstructed from metadata.
    /// </summary>
    public required IRawDisk? RawDisk { get; init; }

    /// <summary>
    /// Gets the starting LBA (for GPT/MBR partitions).
    /// </summary>
    public long StartingLba { get; init; }

    /// <summary>
    /// Gets the ending LBA (for GPT/MBR partitions).
    /// </summary>
    public long EndingLba { get; init; }

    /// <summary>
    /// Gets the partition attributes (for GPT partitions).
    /// </summary>
    public long Attributes { get; init; }

    /// <inheritdoc />
    public Task<Stream> OpenReadAsync(CancellationToken cancellationToken)
    {
        if (RawDisk == null)
            throw new InvalidOperationException("RawDisk not available.");
        return RawDisk.ReadBytesAsync(StartOffset, (int)Math.Min(Size, int.MaxValue), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Stream> OpenWriteAsync(CancellationToken cancellationToken)
    {
        if (RawDisk == null)
            throw new InvalidOperationException("RawDisk not available.");
        return Task.FromResult<Stream>(new PartitionWriteStream(RawDisk, StartOffset, Size));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources to dispose
    }
}
