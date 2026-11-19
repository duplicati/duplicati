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
/// Represents a partition info metadata file as a source entry.
/// The file lives inside the partition folder so that a restore selection
/// containing only this partition still carries the partition size and the
/// filesystem block size (geometry.json sits at the disk level and is not
/// part of such a selection). The restore provider treats it as a priority file.
/// </summary>
internal class PartitionInfoSourceEntry : DiskImageEntryBase
{
    private readonly PartitionInfoMetadata _metadata;

    public PartitionInfoSourceEntry(string parentPath, PartitionInfoMetadata metadata)
        : base(System.IO.Path.Combine(parentPath, PartitionInfoMetadata.FileName))
    {
        _metadata = metadata;
    }

    /// <inheritdoc />
    public override bool IsFolder => false;
    /// <inheritdoc />
    public override bool IsMetaEntry => false;
    /// <inheritdoc />
    public override long Size => Encoding.UTF8.GetByteCount(_metadata.ToJson());
    /// <summary>
    /// Gets the last modification time. Always the current time, so partition info
    /// is always backed up, as block sizes and partition sizes may change.
    /// </summary>
    /// <inheritdoc />
    public override DateTime LastModificationUtc => DateTime.UtcNow;

    public PartitionInfoMetadata Metadata => _metadata;

    public override Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        var json = _metadata.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);
        return Task.FromResult<Stream>(new MemoryStream(bytes));
    }

    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Partition info entries are leaf nodes - no children
        await Task.CompletedTask;
        yield break;
    }

    public override async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
    {
        var metadata = await base.GetMinorMetadata(cancellationToken);

        // Add partition info specific metadata
        metadata["diskimage:Type"] = "partitioninfo";
        metadata["partitioninfo:Version"] = _metadata.Version.ToString();

        if (_metadata.Filesystem != null)
        {
            metadata["filesystem:Type"] = _metadata.Filesystem.Type.ToString();
            metadata["filesystem:BlockSize"] = _metadata.Filesystem.BlockSize.ToString();
        }

        return metadata;
    }
}
