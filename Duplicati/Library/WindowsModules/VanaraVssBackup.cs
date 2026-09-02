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
using System.Runtime.Versioning;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Vanara.PInvoke.VssApi;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// Implementation of a snapshot using the Vanara VSS bindings
/// </summary>

[SupportedOSPlatform("windows")]
public class VanaraVssBackup : ISnapshotProvider
{
    private static readonly TimeSpan MaxWaitTime = TimeSpan.FromMinutes(1);
    private static readonly string LogTag = Log.LogTagFromType<VanaraVssBackup>();
    private IVssBackupComponents _components;
    private bool _hasAllocatedMetadata;
    private bool _hasStartedSnapshotSet;
    private bool _isBackupComplete;

    /// <summary>
    /// Writer identities cached when the metadata was last gathered,
    /// so writers can be verified after the metadata has been freed
    /// </summary>
    private List<(Guid WriteId, string WriterName)>? _knownWriters;

    /// <summary>
    /// The snapshot ids added to the snapshot set, so they can be
    /// deleted individually if the set is torn down without an explicit delete
    /// </summary>
    private readonly List<Guid> _addedSnapshots = new List<Guid>();
    public VanaraVssBackup()
    {
        _components = GetVssBackupComponents();
    }

    public void EnableWriterClasses(Guid[] guids)
        => _components.EnableWriterClasses(guids);

    public void DisableWriterClasses(Guid[] guids)
        => _components.DisableWriterClasses(guids);

    public void GatherWriterMetadata()
    {
        if (_hasAllocatedMetadata)
            return;
        _hasAllocatedMetadata = true;
        _components.GatherWriterMetadata().Wait((uint)MaxWaitTime.TotalMilliseconds).ThrowIfFailed();

        // Cache the writer identities so VerifyWriters can be called
        // after the metadata has been freed with FreeWriterMetadata
        _knownWriters = _components.WriterMetadata.Select(GetWriterIdentity).ToList();
    }

    public void FreeWriterMetadata()
    {
        if (!_hasAllocatedMetadata)
            return;
        _hasAllocatedMetadata = false;
        _components.FreeWriterMetadata();
    }

    public void VerifyWriters(Guid[] guids)
    {
        if (guids == null || guids.Length == 0)
            return;                     // nothing to verify

        // Use the identities cached during GatherWriterMetadata, as the
        // metadata may have been freed before this method is called
        var knownWriters = _knownWriters
            ?? throw new InvalidOperationException("Writer metadata has not been gathered; call GatherWriterMetadata first.");

        foreach (var wanted in guids)
        {
            if (!knownWriters.Any(w => w.WriteId == wanted))
                throw new Exception(
                    $"Writer with GUID {wanted} was not added to the VSS writer set.");
        }
    }
    private sealed record SnapshotInfo(string SnapshotDeviceObject) : ISnapshotInfo;

    public ISnapshotInfo GetSnapshotProperties(Guid shadowId)
    {
        var str = _components.GetSnapshotProperties(shadowId).m_pwszSnapshotDeviceObject;
        if (string.IsNullOrEmpty(str))
            throw new InvalidOperationException($"No snapshot found for shadow ID {shadowId}.");
        return new SnapshotInfo(str!);
    }

    public void StartSnapshotSet()
    {
        if (_hasStartedSnapshotSet)
            throw new InvalidOperationException("Snapshot set has already been started.");
        var snapshotSetId = _components.StartSnapshotSet();
        _hasStartedSnapshotSet = true;
        Log.WriteVerboseMessage(LogTag, "VssStartSnapshotSet", "Started snapshot set {0}", snapshotSetId);
    }

    public void PrepareForBackup()
        => _components.PrepareForBackup().Wait((uint)MaxWaitTime.TotalMilliseconds).ThrowIfFailed();

    public void DoSnapshotSet()
    {
        _components.DoSnapshotSet().Wait((uint)MaxWaitTime.TotalMilliseconds).ThrowIfFailed();
        Log.WriteVerboseMessage(LogTag, "VssDoSnapshotSet", "Completed snapshot set");
    }

    public bool IsVolumeSupported(string drive)
        => _components.IsVolumeSupported(Guid.Empty, drive);

    public Guid AddToSnapshotSet(string drive)
    {
        var snapshotId = _components.AddToSnapshotSet(drive);
        _addedSnapshots.Add(snapshotId);
        Log.WriteVerboseMessage(LogTag, "VssAddToSnapshotSet", "Added {0} to snapshot set as {1}", drive, snapshotId);
        return snapshotId;
    }

    public void BackupComplete()
    {
        _isBackupComplete = true;
        _components.BackupComplete();
    }

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
        _components.DeleteSnapshots(shadowId, VSS_OBJECT_TYPE.VSS_OBJECT_SNAPSHOT, forceDelete, out _, out _).ThrowIfFailed();
    }

    private static IVssBackupComponents GetVssBackupComponents()
    {
        var comp = CreateVssBackupComponents();
        comp.InitializeForBackup(null);
        comp.SetContext(VSS_SNAPSHOT_CONTEXT.VSS_CTX_BACKUP);
        comp.SetBackupState(false, true, VSS_BACKUP_TYPE.VSS_BT_FULL, false);
        return comp;
    }

    private static IVssBackupComponents CreateVssBackupComponents()
    {
        VssFactory.CreateVssBackupComponents(out var comp).ThrowIfFailed();
        return comp;
    }

    private static (Guid WriteId, string WriterName) GetWriterIdentity(IVssExamineWriterMetadata wm)
    {
        // GetIdentity order:
        //   0 = instanceId   (ignore)
        //   1 = writerId     (the GUID we’re after)
        //   2 = writerName   (friendly name)
        //   3 = instanceName (ignore)
        //   4 = usage        (ignore)
        //   5 = source       (ignore)
        wm.GetIdentity(out Guid _, out Guid writerId, out string writerName, out _, out _, out _);
        return (writerId, writerName ?? string.Empty);
    }

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
            // Enumerate all writer metadata objects returned by PrepareForBackup / GatherWriterMetadata
            foreach (var wm in _components.WriterMetadata)
            {
                // Get the writer identity (GUID and name)
                var (writerId, writerName) = GetWriterIdentity(wm);

                // Skip writers the caller didn't ask for (if a filter list was supplied)
                if (writers != null && writers.Length > 0 && !writers.Contains(writerId))
                    continue;

                // Emit one WriterMetaData per component the writer exposes
                foreach (var comp in wm.Components)
                {
                    var ci = comp.GetComponentInfo();

                    yield return new WriterMetaData
                    {
                        Guid = writerId,
                        Name = ci.bstrComponentName ?? string.Empty,
                        LogicalPath = ci.bstrLogicalPath ?? string.Empty,
                        Paths = GetPathsFromComponent(comp)
                    };
                }
            }
        }
        finally
        {
            if (gatheredHere)
                FreeWriterMetadata();
        }
    }

    private List<string> GetPathsFromComponent(IVssWMComponent component)
    {
        var paths = new List<string>();

        foreach (var file in component.Files)
        {
            if (file.Path.Contains("*"))
            {
                if (Directory.Exists(Util.AppendDirSeparator(file.Path)))
                    paths.Add(Util.AppendDirSeparator(file.Path));
            }
            else
            {
                var fileWithSpec = Path.Combine(file.Path, file.FileSpec);
                if (File.Exists(fileWithSpec))
                    paths.Add(fileWithSpec);
            }
        }
        return paths;
    }

    public void Dispose()
    {
        if (_hasAllocatedMetadata)
            FreeWriterMetadata();

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
                _components?.BackupComplete();
        }
        catch (Exception ex)
        {
            Log.WriteVerboseMessage(LogTag, "VssDisposeFailed", ex, "Failed to complete VSS backup");
        }

    }
}
