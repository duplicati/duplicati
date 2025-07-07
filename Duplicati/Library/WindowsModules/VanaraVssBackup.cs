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

        foreach (var wanted in guids)
        {
            // Scan all metadata objects and compare their writer GUID
            bool found = _components
                .WriterMetadata
                .Select(GetWriterIdentity)   // (WriteId, WriterName)
                .Any(id => id.WriteId == wanted);

            if (!found)
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
        _components.StartSnapshotSet();
        _hasStartedSnapshotSet = true;
    }

    public void PrepareForBackup()
        => _components.PrepareForBackup().Wait((uint)MaxWaitTime.TotalMilliseconds).ThrowIfFailed();

    public void DoSnapshotSet()
        => _components.DoSnapshotSet().Wait((uint)MaxWaitTime.TotalMilliseconds).ThrowIfFailed();

    public bool IsVolumeSupported(string drive)
        => _components.IsVolumeSupported(Guid.Empty, drive);

    public Guid AddToSnapshotSet(string drive)
        => _components.AddToSnapshotSet(drive);

    public void BackupComplete()
    {
        _isBackupComplete = true;
        _components.BackupComplete();
    }

    public void DeleteSnapshot(Guid shadowId, bool forceDelete)
    {
        if (!_hasStartedSnapshotSet)
            return;
        _hasStartedSnapshotSet = false;
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
                    Name = writerName ?? string.Empty,
                    LogicalPath = ci.bstrLogicalPath ?? string.Empty,
                    Paths = GetPathsFromComponent(comp)
                };
            }
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
        if (_hasStartedSnapshotSet)
            DeleteSnapshot(Guid.Empty, true); // Delete all snapshots if any were created

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
