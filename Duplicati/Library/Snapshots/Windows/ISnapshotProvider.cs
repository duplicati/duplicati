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

namespace Duplicati.Library.Snapshots.Windows;

/// <summary>
/// Interface for the snapshot information
/// </summary>
internal interface ISnapshotInfo
{
    /// <summary>
    /// The path where the snapshot is located
    /// </summary>
    string SnapshotDeviceObject { get; }
}

/// <summary>
/// Interface for a snapshot provider
/// </summary>
internal interface ISnapshotProvider : IDisposable
{
    /// <summary>
    /// Explicitly enables the selected writer GUIDs
    /// </summary>
    /// <param name="guids">The GUIDs to enable</param>
    void EnableWriterClasses(Guid[] guids);
    /// <summary>
    /// Explicitly disable the selected writer GUIDs
    /// </summary>
    /// <param name="guids">The GUIDs to disable</param>
    void DisableWriterClasses(Guid[] guids);
    /// <summary>
    /// Gather writer metadata prior to the snapshot
    /// </summary>
    void GatherWriterMetadata();
    /// <summary>
    /// Release any allocated metadata
    /// </summary>
    void FreeWriterMetadata();
    /// <summary>
    /// Verify that the writes are enabled on the snapshot
    /// </summary>
    /// <param name="guids">The writer GUIDs to verify as enabled</param>
    void VerifyWriters(Guid[] guids);

    /// <summary>
    /// Gets information of a snapshot
    /// </summary>
    /// <param name="shadowId">The shadow copy id</param>
    /// <returns>The snapshot information</returns>
    ISnapshotInfo GetSnapshotProperties(Guid shadowId);

    /// <summary>
    /// Creates a snapshot
    /// </summary>
    void StartSnapshotSet();
    /// <summary>
    /// Starts the snapshot as a backup snapshot
    /// </summary>
    void PrepareForBackup();
    /// <summary>
    /// Finalizes the snapshot creation
    /// </summary>
    void DoSnapshotSet();
    /// <summary>
    /// Checks if the volume is supported by the snapshot
    /// </summary>
    /// <param name="drive">The drive to check</param>
    /// <returns><c>true</c> if the volume is supported; <c>false<c/> otherwise</returns>
    bool IsVolumeSupported(string drive);
    /// <summary>
    /// Adds a drive to the snapshot set
    /// </summary>
    /// <param name="drive">The drive to add</param>
    /// <returns>The shadow id</returns>
    Guid AddToSnapshotSet(string drive);

    /// <summary>
    /// Completes the snapshot after the backup has completed
    /// </summary>
    void BackupComplete();
    /// <summary>
    /// Releases all resources held by the snapshot
    /// </summary>
    /// <param name="shadowId">The snapshot shadow id</param>
    /// <param name="forceDelete">Flag to choose force deletion</param>
    void DeleteSnapshot(Guid shadowId, bool forceDelete);

    /// <summary>
    /// Parses write metadata
    /// </summary>
    /// <param name="writers">The writers to return information from</param>
    /// <returns>The writer metadata</returns>
    IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers);
}