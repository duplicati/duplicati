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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;

namespace Duplicati.Library.Snapshots.Windows;

/// <summary>
/// Snapshot provider based on WMIC
/// </summary>
[SupportedOSPlatform("windows")]
internal class WmicVssBackup : ISnapshotProvider
{    
    /// <summary>
    /// The manager that contains the actual interface to WMIC
    /// </summary>
    private WmicShadowCopyManager _manager = new WmicShadowCopyManager();

    /// <summary>
    /// Gets the VSS enabled drives
    /// </summary>
    private Lazy<HashSet<string>> _vssEnabledDrives = new Lazy<HashSet<string>>(() => WmicShadowCopyManager.GetVssCapableDrivesViaVssadmin());

    /// <inheritdoc/>
    public Guid AddToSnapshotSet(string drive)
        => _manager.Add(drive).ParsedId;

    /// <inheritdoc/>
    public void BackupComplete()
    {
    }

    /// <inheritdoc/>
    public void DeleteSnapshot(Guid shadowId, bool forceDelete)
    {
        foreach (var x in _manager.ShadowCopies.Where(x => x.ParsedId == shadowId))
            x.Dispose();
    }

    /// <inheritdoc/>
    public void DisableWriterClasses(Guid[] guids)
    {
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _manager.Dispose();
    }

    /// <inheritdoc/>
    public void DoSnapshotSet()
    {
    }

    /// <inheritdoc/>
    public void EnableWriterClasses(Guid[] guids)
    {
    }

    /// <inheritdoc/>
    public void FreeWriterMetadata()
    {
    }

    /// <inheritdoc/>
    public void GatherWriterMetadata()
    {
    }

    /// <summary>
    /// Stub for returning the snapshot info
    /// </summary>
    /// <param name="SnapshotDeviceObject">The path where the snapshot is located</param>
    private sealed record SnapshotInfo (string SnapshotDeviceObject) : ISnapshotInfo;

    /// <inheritdoc/>
    public ISnapshotInfo GetSnapshotProperties(Guid shadowId)
        => new SnapshotInfo(_manager.ShadowCopies.First(x => x.ParsedId == shadowId).MappedPath);

    /// <inheritdoc/>
    public bool IsVolumeSupported(string drive)
    {
        if (string.IsNullOrWhiteSpace(drive))
            return false;

        return _vssEnabledDrives.Value.Contains(drive.Substring(0, 1));
    }

    /// <inheritdoc/>
    public IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers)
        => Enumerable.Empty<WriterMetaData>();

    /// <inheritdoc/>
    public void PrepareForBackup()
    {
    }

    /// <inheritdoc/>
    public void StartSnapshotSet()
    {
    }

    /// <inheritdoc/>
    public void VerifyWriters(Guid[] guids)
    {
    }
}