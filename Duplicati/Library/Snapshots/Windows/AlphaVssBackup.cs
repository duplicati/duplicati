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
using Alphaleonis.Win32.Vss;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Snapshots.Windows;

/// <summary>
/// Implementation of a snapshot, using AlphaVSS
/// </summary>
internal class AlphaVssBackup : ISnapshotProvider
{
    /// <summary>
    /// The implementation that is being wrapped
    /// </summary>
    private IVssBackupComponents _components;
    /// <summary>
    /// Creates a new snapshot implementation
    /// </summary>
    /// <param name="components">The components to use</param>
    private AlphaVssBackup(IVssBackupComponents components)
    {
        _components = components;
    }

    /// <inheritdoc/>
    public void EnableWriterClasses(Guid[] guids)
        => _components.EnableWriterClasses(guids);
    
    /// <inheritdoc/>
    public void DisableWriterClasses(Guid[] guids)
        => _components.DisableWriterClasses(guids);

    /// <inheritdoc/>
    public void GatherWriterMetadata()
        => _components.GatherWriterMetadata();
    /// <inheritdoc/>
    public void FreeWriterMetadata()
        => _components.FreeWriterMetadata();
    
    /// <inheritdoc/>
    public void VerifyWriters(Guid[] guids)
    {
        foreach (var writerGUID in guids)
        {
            if (!_components.WriterMetadata.Any(o => o.WriterId.Equals(writerGUID)))
            {
                throw new Exception(string.Format("Writer with GUID {0} was not added to VSS writer set.", writerGUID.ToString()));
            }
        }
    }

    /// <summary>
    /// Stub for returning the snapshot info
    /// </summary>
    /// <param name="SnapshotDeviceObject">The path where the snapshot is located</param>
    private sealed record SnapshotInfo(string SnapshotDeviceObject) : ISnapshotInfo;

    /// <inheritdoc/>
    public ISnapshotInfo GetSnapshotProperties(Guid shadowId)
        => new SnapshotInfo(_components.GetSnapshotProperties(shadowId).SnapshotDeviceObject);

    /// <inheritdoc/>
    public void StartSnapshotSet()
        => _components.StartSnapshotSet();

    /// <inheritdoc/>
    public void PrepareForBackup()
        => _components.PrepareForBackup();

    /// <inheritdoc/>
    public void DoSnapshotSet()
        => _components.DoSnapshotSet();

    /// <inheritdoc/>
    public bool IsVolumeSupported(string drive)
        => _components.IsVolumeSupported(drive);
    
    /// <inheritdoc/>
    public Guid AddToSnapshotSet(string drive)
        => _components.AddToSnapshotSet(drive);

    /// <inheritdoc/>
    public void BackupComplete()
        => _components.BackupComplete();

    /// <inheritdoc/>
    public void DeleteSnapshot(Guid shadowId, bool forceDelete)
        => _components.DeleteSnapshot(shadowId, forceDelete);

    /// <inheritdoc/>
    public static ISnapshotProvider Create()
        => new AlphaVssBackup(GetVssBackupComponents());

    /// <summary>
    /// Creates a new AlphaVssBackupComponents instance and adds the context
    /// </summary>
    /// <returns>The configured instance</returns>
    private static IVssBackupComponents GetVssBackupComponents()
    {
        //Prepare the backup
        IVssBackupComponents vssBackupComponents = CreateVssBackupComponents();
        vssBackupComponents.InitializeForBackup(null);
        vssBackupComponents.SetContext(VssSnapshotContext.Backup);
        vssBackupComponents.SetBackupState(false, true, VssBackupType.Full, false);

        return vssBackupComponents;
    }

    /// <summary>
    /// Loads the AlphaVSS implementation for the current OS
    /// </summary>
    /// <returns>The implementation</returns>
    private static IVssBackupComponents CreateVssBackupComponents()
    {
        var vss = VssFactoryProvider.Default.GetVssFactory();
        if (vss == null)
            throw new InvalidOperationException();

        return vss.CreateVssBackupComponents();
    }

    /// <inheritdoc/>
    public IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers)
    {
        // check if writers got enabled
        foreach (var writerGUID in writers)
        {
            var writer = _components.WriterMetadata.First(o => o.WriterId.Equals(writerGUID));
            foreach (var component in writer.Components)
            {
                yield return new WriterMetaData
                {
                    Guid = writerGUID,
                    Name = component.ComponentName,
                    LogicalPath = component.LogicalPath,
                    Paths = GetPathsFromComponent(component)
                };
            }
        }    
    }

    /// <summary>
    /// Returns the paths from the writer metadata
    /// </summary>
    /// <param name="component">The component to get the paths from</param>
    /// <returns>The list of paths</returns>
    private List<string> GetPathsFromComponent(IVssWMComponent component)
    {
        var paths = new List<string>();

        foreach (var file in component.Files)
        {
            if (file.FileSpecification.Contains("*"))
            {
                if (Directory.Exists(Util.AppendDirSeparator(file.Path)))
                    paths.Add(Util.AppendDirSeparator(file.Path));
            }
            else
            {
                var fileWithSpec = SystemIO.IO_OS.PathCombine(file.Path, file.FileSpecification);
                if (File.Exists(fileWithSpec))
                    paths.Add(fileWithSpec);
            }
        }
        return paths;

    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _components.Dispose();
    }
}