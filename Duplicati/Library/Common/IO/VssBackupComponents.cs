//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Alphaleonis.Win32.Vss;

namespace Duplicati.Library.Common.IO
{
    public class WriterMetaData
    {
        public string Name { get; set;  }
        public string LogicalPath { get; set; }
        public Guid Guid { get; set; }
        public List<string> Paths { get; set; }
    }

    public class VssBackupComponents : IDisposable
    {
        /// <summary>
        /// The tag used for logging messages
        /// </summary>
        public static readonly string LOGTAG = Logging.Log.LogTagFromType<VssBackupComponents>();


        private IVssBackupComponents _vssBackupComponents;

        /// <summary>
        /// The list of snapshot ids for each volume, key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private Dictionary<string, Guid> _volumes;

        /// <summary>
        /// The mapping of snapshot sources to their snapshot entries , key is the path root, eg C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private Dictionary<string, string> _volumeMap;

        /// <summary>
        /// Reverse mapping for speed up.
        /// </summary>
        private Dictionary<string, string> _volumeReverseMap;

        /// <summary>
        /// A list of mapped drives
        /// </summary>
        private List<DefineDosDevice> _mappedDrives;

        public VssBackupComponents()
        {
            _vssBackupComponents = VssBackupComponentsHelper.GetVssBackupComponents();
        }

        public void SetupWriters(Guid[] includedWriters, Guid[] excludedWriters)
        {
            if (includedWriters != null && includedWriters.Length > 0)
                _vssBackupComponents.EnableWriterClasses(includedWriters);

            if (excludedWriters != null && excludedWriters.Length > 0)
                _vssBackupComponents.DisableWriterClasses(excludedWriters);

            try
            {
                _vssBackupComponents.GatherWriterMetadata();
            }
            finally
            {
                _vssBackupComponents.FreeWriterMetadata();
            }

            if (includedWriters == null)
            {
                return;
            }

            // check if writers got enabled
            foreach (var writerGUID in includedWriters)
            {
                if (!_vssBackupComponents.WriterMetadata.Any(o => o.WriterId.Equals(writerGUID)))
                {
                    throw new Exception(string.Format("Writer with GUID {0} was not added to VSS writer set.", writerGUID.ToString()));
                }
            }
        }

        public void MapDrives()
        {
            _mappedDrives = new List<DefineDosDevice>();
            foreach (var k in new List<string>(_volumeMap.Keys))
            {
                try
                {
                    DefineDosDevice d = new DefineDosDevice(_volumeMap[k]);
                    _mappedDrives.Add(d);
                    _volumeMap[k] = Util.AppendDirSeparator(d.Drive);
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "SubstMappingfailed", ex, "Failed to map VSS path {0} to drive", k);
                }
            }
        }

        public void MapVolumesToSnapShots()
        {
            _volumeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _volumes)
            {
                _volumeMap.Add(kvp.Key, _vssBackupComponents.GetSnapshotProperties(kvp.Value).SnapshotDeviceObject);
            }

            _volumeReverseMap = _volumeMap.ToDictionary(x => x.Value, x => x.Key);
        }

        public Dictionary<string, string> SnapshotDeviceAndVolumes
        {
            get {
                return _volumeReverseMap;
            }
        }

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
                    var fileWithSpec = SystemIO.IO_WIN.PathCombine(file.Path, file.FileSpecification);
                    if (File.Exists(fileWithSpec))
                        paths.Add(fileWithSpec);
                }
            }
            return paths;

        }

        /// <summary>
        /// Enumerable to iterate over components of the given writers.
        /// Returns an WriterMetaData object containing GUID, component name, logical path and paths of the component.
        /// </summary>
        /// <returns>Anonymous object containing GUID, component name, logical path and paths of the component.</returns>
        /// <param name="writers">Writers.</param>
        public IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers)
        {
            // check if writers got enabled
            foreach (var writerGUID in writers)
            {
                var writer = _vssBackupComponents.WriterMetadata.First(o => o.WriterId.Equals(writerGUID));
                foreach (var component in writer.Components)
                {
                    yield return new WriterMetaData{ Guid = writerGUID,
                        Name = component.ComponentName,
                        LogicalPath = component.LogicalPath,
                        Paths = GetPathsFromComponent(component) };
                }
            }
        }


        public void InitShadowVolumes(IEnumerable<string> sources)
        {
            _vssBackupComponents.StartSnapshotSet();

            CheckSupportedVolumes(sources);

            //Make all writers aware that we are going to do the backup
            _vssBackupComponents.PrepareForBackup();

            //Create the shadow volumes
            _vssBackupComponents.DoSnapshotSet();
        }

        public string GetVolumeFromCache(string path)
        {
            if (!_volumeMap.TryGetValue(path, out var volumePath))
                throw new InvalidOperationException();

            return volumePath;
        }

        public void CheckSupportedVolumes(IEnumerable<string> sources)
        {
            //Figure out which volumes are in the set
            _volumes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sources)
            {
                var drive = SystemIO.IO_WIN.GetPathRoot(s);
                if (!_volumes.ContainsKey(drive))
                {
                    if (!_vssBackupComponents.IsVolumeSupported(drive))
                    {
                        throw new VssVolumeNotSupportedException(drive);
                    }

                    _volumes.Add(drive, _vssBackupComponents.AddToSnapshotSet(drive));
                }
            }
        }

        public void Dispose()
        {
            try
            {
                if (_mappedDrives != null)
                {
                    foreach (var d in _mappedDrives)
                    {
                        d.Dispose();
                    }

                    _mappedDrives = null;
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "MappedDriveCleanupError", ex, "Failed during VSS mapped drive unmapping");
            }

            try
            {
                _vssBackupComponents?.BackupComplete();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "VSSTerminateError", ex, "Failed to signal VSS completion");
            }

            try
            {
                if (_vssBackupComponents != null)
                {
                    foreach (var g in _volumes.Values)
                    {
                        try
                        {
                            _vssBackupComponents.DeleteSnapshot(g, false);
                        }
                        catch (Exception ex)
                        {
                            Logging.Log.WriteVerboseMessage(LOGTAG, "VSSSnapShotDeleteError", ex, "Failed to close VSS snapshot");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "VSSSnapShotDeleteCleanError", ex, "Failed during VSS esnapshot closing");
            }

            if (_vssBackupComponents != null)
            {
                _vssBackupComponents.Dispose();
                _vssBackupComponents = null;
            }
        }
    }


    public static class VssBackupComponentsHelper
    {
        public static IVssBackupComponents GetVssBackupComponents()
        {
            //Prepare the backup
            IVssBackupComponents vssBackupComponents = CreateVssBackupComponents();
            vssBackupComponents.InitializeForBackup(null);
            vssBackupComponents.SetContext(VssSnapshotContext.Backup);
            vssBackupComponents.SetBackupState(false, true, VssBackupType.Full, false);

            return vssBackupComponents;
        }

        public static IVssBackupComponents CreateVssBackupComponents()
        {
            // Substitute for calling VssUtils.LoadImplementation(), as we have the dlls outside the GAC
            var assemblyLocation = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (assemblyLocation == null)
                throw new InvalidOperationException();

            //Here we don't need a custom Path.Combine: we need unconditional access to alphaFS
            var alphadll = Path.Combine(assemblyLocation, "alphavss", VssUtils.GetPlatformSpecificAssemblyShortName() + ".dll");
            var vss = (IVssImplementation)System.Reflection.Assembly.LoadFile(alphadll).CreateInstance("Alphaleonis.Win32.Vss.VssImplementation");
            if (vss == null)
                throw new InvalidOperationException();

            return vss.CreateVssBackupComponents();
        }
    }
}
