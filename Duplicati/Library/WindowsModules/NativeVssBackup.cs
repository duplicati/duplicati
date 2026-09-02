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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.WindowsModules.Vss;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Implementation of a snapshot provider using the VSS requestor COM API
/// via pure C# interop, without any third-party libraries.
/// Supports the full set of VSS stages, including writer metadata.
/// </summary>
[SupportedOSPlatform("windows")]
public class NativeVssBackup : ISnapshotProvider
{
    /// <summary>
    /// The tag used for logging messages
    /// </summary>
    private static readonly string LogTag = Log.LogTagFromType<NativeVssBackup>();

    /// <summary>
    /// The maximum time to wait for asynchronous VSS operations
    /// </summary>
    private readonly TimeSpan _maxWaitTime;

    /// <summary>
    /// The backup components interface
    /// </summary>
    private readonly IVssBackupComponents _components;

    /// <summary>
    /// Flag keeping track of whether writer metadata has been allocated
    /// </summary>
    private bool _hasAllocatedMetadata;

    /// <summary>
    /// Flag keeping track of whether a snapshot set has been started
    /// </summary>
    private bool _hasStartedSnapshotSet;

    /// <summary>
    /// Flag keeping track of whether the backup has been completed
    /// </summary>
    private bool _isBackupComplete;

    /// <summary>
    /// Flag keeping track of the disposed state
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Writer identities cached when the metadata was last gathered,
    /// so writers can be verified after the metadata has been freed
    /// </summary>
    private List<(Guid WriterId, string WriterName)>? _knownWriters;

    /// <summary>
    /// The snapshot ids added to the snapshot set, so they can be
    /// deleted individually if the set is torn down without an explicit delete
    /// </summary>
    private readonly List<Guid> _addedSnapshots = new List<Guid>();

    /// <summary>
    /// Creates a new instance of the provider
    /// </summary>
    /// <param name="maxWaitTime">The maximum time to wait for asynchronous VSS operations</param>
    public NativeVssBackup(TimeSpan maxWaitTime)
    {
        _maxWaitTime = maxWaitTime;
        _components = GetVssBackupComponents();
    }

    /// <inheritdoc/>
    public void EnableWriterClasses(Guid[] guids)
    {
        if (guids == null || guids.Length == 0)
            return;

        VssInteropUtility.ThrowIfFailed(
            _components.EnableWriterClasses(guids, (uint)guids.Length),
            nameof(EnableWriterClasses));
    }

    /// <inheritdoc/>
    public void DisableWriterClasses(Guid[] guids)
    {
        if (guids == null || guids.Length == 0)
            return;

        VssInteropUtility.ThrowIfFailed(
            _components.DisableWriterClasses(guids, (uint)guids.Length),
            nameof(DisableWriterClasses));
    }

    /// <inheritdoc/>
    public void GatherWriterMetadata()
    {
        if (_hasAllocatedMetadata)
            return;
        _hasAllocatedMetadata = true;
        VssInteropUtility.ThrowIfFailed(_components.GatherWriterMetadata(out var async), nameof(GatherWriterMetadata));
        VssInteropUtility.WaitAndCheck(async, (uint)_maxWaitTime.TotalMilliseconds, nameof(GatherWriterMetadata));

        // Cache the writer identities so VerifyWriters can be called
        // after the metadata has been freed with FreeWriterMetadata
        var knownWriters = new List<(Guid WriterId, string WriterName)>();
        foreach (var wm in EnumerateWriterMetadata())
        {
            try
            {
                knownWriters.Add(GetWriterIdentity(wm));
            }
            finally
            {
                VssInteropUtility.SafeRelease(wm);
            }
        }
        _knownWriters = knownWriters;
    }

    /// <inheritdoc/>
    public void FreeWriterMetadata()
    {
        if (!_hasAllocatedMetadata)
            return;
        _hasAllocatedMetadata = false;
        VssInteropUtility.ThrowIfFailed(_components.FreeWriterMetadata(), nameof(FreeWriterMetadata));
    }

    /// <inheritdoc/>
    public void VerifyWriters(Guid[] guids)
    {
        if (guids == null || guids.Length == 0)
            return;

        // Use the identities cached during GatherWriterMetadata, as the
        // metadata may have been freed before this method is called
        var knownWriters = _knownWriters
            ?? throw new InvalidOperationException("Writer metadata has not been gathered; call GatherWriterMetadata first.");

        foreach (var wanted in guids)
        {
            if (!knownWriters.Any(w => w.WriterId == wanted))
                throw new Exception($"Writer with GUID {wanted} was not added to the VSS writer set.");
        }
    }

    /// <summary>
    /// Stub for returning the snapshot info
    /// </summary>
    /// <param name="SnapshotDeviceObject">The path where the snapshot is located</param>
    private sealed record SnapshotInfo(string SnapshotDeviceObject) : ISnapshotInfo;

    /// <inheritdoc/>
    public ISnapshotInfo GetSnapshotProperties(Guid shadowId)
    {
        var prop = new VSS_SNAPSHOT_PROP();
        try
        {
            VssInteropUtility.ThrowIfFailed(_components.GetSnapshotProperties(shadowId, out prop), nameof(GetSnapshotProperties));

            var deviceObject = prop.m_pwszSnapshotDeviceObject == IntPtr.Zero
                ? null
                : Marshal.PtrToStringUni(prop.m_pwszSnapshotDeviceObject);

            if (string.IsNullOrEmpty(deviceObject))
                throw new InvalidOperationException($"No snapshot found for shadow ID {shadowId}.");

            return new SnapshotInfo(deviceObject!);
        }
        finally
        {
            // The strings in the structure are owned by VSS
            // and must be released with this function
            NativeMethods.VssFreeSnapshotProperties(ref prop);
        }
    }

    /// <inheritdoc/>
    public void StartSnapshotSet()
    {
        if (_hasStartedSnapshotSet)
            throw new InvalidOperationException("Snapshot set has already been started.");

        VssInteropUtility.ThrowIfFailed(_components.StartSnapshotSet(out var snapshotSetId), nameof(StartSnapshotSet));
        _hasStartedSnapshotSet = true;
        Log.WriteVerboseMessage(LogTag, "VssStartSnapshotSet", "Started snapshot set {0}", snapshotSetId);
    }

    /// <inheritdoc/>
    public void PrepareForBackup()
    {
        VssInteropUtility.ThrowIfFailed(_components.PrepareForBackup(out var async), nameof(PrepareForBackup));
        VssInteropUtility.WaitAndCheck(async, (uint)_maxWaitTime.TotalMilliseconds, nameof(PrepareForBackup));
    }

    /// <inheritdoc/>
    public void DoSnapshotSet()
    {
        VssInteropUtility.ThrowIfFailed(_components.DoSnapshotSet(out var async), nameof(DoSnapshotSet));
        VssInteropUtility.WaitAndCheck(async, (uint)_maxWaitTime.TotalMilliseconds, nameof(DoSnapshotSet));
        Log.WriteVerboseMessage(LogTag, "VssDoSnapshotSet", "Completed snapshot set");
    }

    /// <inheritdoc/>
    public bool IsVolumeSupported(string drive)
    {
        if (string.IsNullOrWhiteSpace(drive))
            return false;

        VssInteropUtility.ThrowIfFailed(_components.IsVolumeSupported(Guid.Empty, drive, out var supported), nameof(IsVolumeSupported));
        return supported;
    }

    /// <inheritdoc/>
    public Guid AddToSnapshotSet(string drive)
    {
        VssInteropUtility.ThrowIfFailed(_components.AddToSnapshotSet(drive, Guid.Empty, out var snapshotId), nameof(AddToSnapshotSet));
        _addedSnapshots.Add(snapshotId);
        Log.WriteVerboseMessage(LogTag, "VssAddToSnapshotSet", "Added {0} to snapshot set as {1}", drive, snapshotId);
        return snapshotId;
    }

    /// <inheritdoc/>
    public void BackupComplete()
    {
        if (_isBackupComplete)
            return;
        _isBackupComplete = true;

        // BackupComplete can be called without a snapshot set being created,
        // in which case it fails with VSS_E_BAD_STATE
        var hr = _components.BackupComplete(out var async);
        if (hr < 0)
        {
            Log.WriteVerboseMessage(LogTag, "VssBackupCompleteFailed", "BackupComplete returned HRESULT 0x{0:X8}", hr);
            return;
        }
        VssInteropUtility.WaitAndCheck(async, (uint)_maxWaitTime.TotalMilliseconds, nameof(BackupComplete));
    }

    /// <inheritdoc/>
    public void DeleteSnapshot(Guid shadowId, bool forceDelete)
    {
        if (!_hasStartedSnapshotSet)
            return;

        // VSS requires a valid snapshot id when deleting a single snapshot;
        // Guid.Empty is used as a request to delete all snapshots in the set
        if (shadowId == Guid.Empty)
        {
            foreach (var id in _addedSnapshots)
                DeleteSingleSnapshot(id, forceDelete);
            _addedSnapshots.Clear();
            return;
        }

        DeleteSingleSnapshot(shadowId, forceDelete);
        _addedSnapshots.Remove(shadowId);
    }

    /// <summary>
    /// Deletes a single snapshot, keeping the snapshot set flag set
    /// as other volumes in the set may still need to be deleted
    /// </summary>
    /// <param name="shadowId">The snapshot id to delete</param>
    /// <param name="forceDelete">Flag to choose force deletion</param>
    private void DeleteSingleSnapshot(Guid shadowId, bool forceDelete)
    {
        Log.WriteVerboseMessage(LogTag, "VssDeleteSnapshot", "Deleting snapshot {0}", shadowId);
        VssInteropUtility.ThrowIfFailed(
            _components.DeleteSnapshots(shadowId, VssObjectType.VSS_OBJECT_SNAPSHOT, forceDelete, out _, out _),
            nameof(DeleteSnapshot));
    }

    /// <inheritdoc/>
    public IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers)
    {
        // The metadata may have been released by an earlier FreeWriterMetadata
        // call; re-gather it for the duration of the parse if needed
        var gatheredHere = false;
        if (!_hasAllocatedMetadata)
        {
            GatherWriterMetadata();
            gatheredHere = true;
        }

        try
        {
            foreach (var result in ParseWriterMetaDataCore(writers))
                yield return result;
        }
        finally
        {
            if (gatheredHere)
                FreeWriterMetadata();
        }
    }

    /// <summary>
    /// Parses the writer metadata, requiring the metadata to be currently gathered
    /// </summary>
    /// <param name="writers">The writers to return information from</param>
    /// <returns>The writer metadata</returns>
    private IEnumerable<WriterMetaData> ParseWriterMetaDataCore(Guid[] writers)
    {
        foreach (var wm in EnumerateWriterMetadata())
        {
            // Get the writer identity (GUID and name)
            var (writerId, writerName) = GetWriterIdentity(wm);

            // Skip writers the caller didn't ask for (if a filter list was supplied)
            if (writers != null && writers.Length > 0 && !writers.Contains(writerId))
            {
                VssInteropUtility.SafeRelease(wm);
                continue;
            }

            var results = new List<WriterMetaData>();
            try
            {
                VssInteropUtility.ThrowIfFailed(wm.GetFileCounts(out _, out _, out var componentCount), nameof(IVssExamineWriterMetadata.GetFileCounts));

                // Emit one WriterMetaData per component the writer exposes
                for (uint i = 0; i < componentCount; i++)
                {
                    VssWMComponentWrapper? component = null;
                    IntPtr infoPtr = IntPtr.Zero;
                    try
                    {
                        VssInteropUtility.ThrowIfFailed(wm.GetComponent(i, out var componentPtr), nameof(IVssExamineWriterMetadata.GetComponent));
                        component = new VssWMComponentWrapper(componentPtr);

                        VssInteropUtility.ThrowIfFailed(component.GetComponentInfo(out infoPtr), nameof(VssWMComponentWrapper.GetComponentInfo));
                        var info = Marshal.PtrToStructure<VSS_COMPONENTINFO>(infoPtr);
                        var logicalPath = info.bstrLogicalPath == IntPtr.Zero ? string.Empty : Marshal.PtrToStringBSTR(info.bstrLogicalPath) ?? string.Empty;
                        var componentName = info.bstrComponentName == IntPtr.Zero ? string.Empty : Marshal.PtrToStringBSTR(info.bstrComponentName) ?? string.Empty;

                        results.Add(new WriterMetaData
                        {
                            Guid = writerId,
                            Name = componentName,
                            LogicalPath = logicalPath,
                            Paths = GetPathsFromComponent(component, info.cFileCount)
                        });
                    }
                    finally
                    {
                        if (component != null)
                        {
                            if (infoPtr != IntPtr.Zero)
                                component.FreeComponentInfo(infoPtr);
                            component.Dispose();
                        }
                    }
                }
            }
            finally
            {
                VssInteropUtility.SafeRelease(wm);
            }

            foreach (var result in results)
                yield return result;
        }
    }

    /// <summary>
    /// Returns the paths from the writer component
    /// </summary>
    /// <param name="component">The component to get the paths from</param>
    /// <param name="fileCount">The number of files in the component</param>
    /// <returns>The list of paths</returns>
    private List<string> GetPathsFromComponent(VssWMComponentWrapper component, uint fileCount)
    {
        var paths = new List<string>();

        for (uint i = 0; i < fileCount; i++)
        {
            VssWMFiledescWrapper? file = null;
            try
            {
                VssInteropUtility.ThrowIfFailed(component.GetFile(i, out var filePtr), nameof(VssWMComponentWrapper.GetFile));
                file = new VssWMFiledescWrapper(filePtr);

                var path = file.GetPath() ?? string.Empty;
                var filespec = file.GetFilespec() ?? string.Empty;

                if (path.Contains("*") || filespec.Contains("*"))
                {
                    if (Directory.Exists(Util.AppendDirSeparator(path)))
                        paths.Add(Util.AppendDirSeparator(path));
                }
                else
                {
                    var fileWithSpec = Path.Combine(path, filespec);
                    if (File.Exists(fileWithSpec))
                        paths.Add(fileWithSpec);
                }
            }
            finally
            {
                file?.Dispose();
            }
        }

        return paths;
    }

    /// <summary>
    /// Enumerates the writer metadata objects returned by GatherWriterMetadata
    /// </summary>
    /// <returns>The writer metadata objects; the caller must release each object</returns>
    private IEnumerable<IVssExamineWriterMetadata> EnumerateWriterMetadata()
    {
        VssInteropUtility.ThrowIfFailed(_components.GetWriterMetadataCount(out var count), nameof(IVssBackupComponents.GetWriterMetadataCount));

        for (uint i = 0; i < count; i++)
        {
            VssInteropUtility.ThrowIfFailed(_components.GetWriterMetadata(i, out _, out var metadata), nameof(IVssBackupComponents.GetWriterMetadata));
            yield return metadata;
        }
    }

    /// <summary>
    /// Gets the writer id and name from the metadata
    /// </summary>
    /// <param name="wm">The writer metadata</param>
    /// <returns>The writer id and name</returns>
    private static (Guid WriterId, string WriterName) GetWriterIdentity(IVssExamineWriterMetadata wm)
    {
        VssInteropUtility.ThrowIfFailed(
            wm.GetIdentity(out _, out var writerId, out var writerName, out _, out _),
            nameof(IVssExamineWriterMetadata.GetIdentity));
        return (writerId, writerName ?? string.Empty);
    }

    /// <summary>
    /// Creates and configures the backup components interface
    /// </summary>
    /// <returns>The configured interface</returns>
    private static IVssBackupComponents GetVssBackupComponents()
    {
        var comp = CreateVssBackupComponents();
        VssInteropUtility.ThrowIfFailed(comp.InitializeForBackup(null), nameof(IVssBackupComponents.InitializeForBackup));
        VssInteropUtility.ThrowIfFailed(comp.SetContext((int)VssSnapshotContext.VSS_CTX_BACKUP), nameof(IVssBackupComponents.SetContext));
        VssInteropUtility.ThrowIfFailed(comp.SetBackupState(false, true, VssBackupType.VSS_BT_FULL, false), nameof(IVssBackupComponents.SetBackupState));
        return comp;
    }

    /// <summary>
    /// Creates a new backup components instance
    /// </summary>
    /// <returns>The new instance</returns>
    private static IVssBackupComponents CreateVssBackupComponents()
    {
        VssInteropUtility.ThrowIfFailed(NativeMethods.CreateVssBackupComponentsInternal(out var comp), nameof(NativeMethods.CreateVssBackupComponentsInternal));
        return comp;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (_hasAllocatedMetadata)
                FreeWriterMetadata();
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LogTag, "VssFreeMetadataFailed", ex, "Failed to free VSS writer metadata");
        }

        try
        {
            if (_hasStartedSnapshotSet)
                DeleteSnapshot(Guid.Empty, true); // Delete all snapshots if any were created
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LogTag, "VssDeleteSnapshotFailed", ex, "Failed to delete VSS snapshots");
        }

        try
        {
            if (!_isBackupComplete)
                BackupComplete();
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LogTag, "VssDisposeFailed", ex, "Failed to complete VSS backup");
        }

        VssInteropUtility.SafeRelease(_components);
    }
}
