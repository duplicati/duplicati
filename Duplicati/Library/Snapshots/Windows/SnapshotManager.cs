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
using System.Linq;
using System.Runtime.Versioning;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Snapshots.Windows
{
    /// <summary>
    /// The manager for the Windows snapshot
    /// </summary>
    [SupportedOSPlatform("windows")]
    public class SnapshotManager : IDisposable
    {
        /// <summary>
        /// The tag used for logging messages
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<SnapshotManager>();

        /// <summary>
        /// The VSS operation timeout used for short-lived writer metadata queries,
        /// such as enumerating MSSQL databases or Hyper-V guests, where waiting
        /// for the full provider default would stall the caller for too long
        /// </summary>
        public static readonly TimeSpan WriterMetadataQueryTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The default time to wait as a time string
        /// </summary>
        public const string DefaultMaxWaitTime = "10m";

        /// <summary>
        /// The snapshot implementation
        /// </summary>
        private ISnapshotProvider _snapshotProvider;

        /// <summary>
        /// The list of snapshot ids for each volume, key is the path root, e.g. C:\.
        /// The dictionary is case insensitive
        /// </summary>
        private Dictionary<string, Guid> _volumes;

        /// <summary>
        /// The mapping of snapshot sources to their snapshot entries , key is the path root, e.g. C:\.
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

        /// <summary>
        /// Creates a new snapshot manager
        /// </summary>
        /// <param name="provider">The provider to use</param>
        /// <param name="vssTimeout">The maximum time to wait for asynchronous VSS operations</param>
        public SnapshotManager(WindowsSnapshotProvider provider, TimeSpan vssTimeout)
        {
            _snapshotProvider = WindowsShimLoader.GetSnapshotProvider(provider, vssTimeout);
        }

        /// <summary>
        /// Setup writers on the snapshot
        /// </summary>
        /// <param name="includedWriters">The explicitly included writers</param>
        /// <param name="excludedWriters">The explicitly excluded writers</param>
        public void SetupWriters(Guid[] includedWriters, Guid[] excludedWriters)
        {
            if (includedWriters != null && includedWriters.Length > 0)
                _snapshotProvider.EnableWriterClasses(includedWriters);

            if (excludedWriters != null && excludedWriters.Length > 0)
                _snapshotProvider.DisableWriterClasses(excludedWriters);

            _snapshotProvider.GatherWriterMetadata();

            if (includedWriters == null)
            {
                return;
            }

            // check if writers got enabled
            _snapshotProvider.VerifyWriters(includedWriters);
        }

        /// <summary>
        /// Maps drives from snapshot paths
        /// </summary>
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

        /// <summary>
        /// Makes a map of the snapshots for easy lookup
        /// </summary>
        public void MapVolumesToSnapShots()
        {
            _volumeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in _volumes)
            {
                // The snapshot path has the format of "\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1"
                // Attempting to get attributes on this path will fail, so we need to append a double backslash
                // Strangely, the double backslash is not needed when acessing a subfolder, here a single backslash is enough,
                // but double backslash works for both cases due to path normalization.
                _volumeMap.Add(kvp.Key, Util.AppendDirSeparator(_snapshotProvider.GetSnapshotProperties(kvp.Value).SnapshotDeviceObject) + "\\");
            }

            _volumeReverseMap = _volumeMap.ToDictionary(x => x.Value, x => x.Key);
        }

        /// <summary>
        /// Returns the map of snapshot path and logical path
        /// </summary>
        public Dictionary<string, string> SnapshotDeviceAndVolumes
            => _volumeReverseMap;

        /// <summary>
        /// Enumerable to iterate over components of the given writers.
        /// Returns an WriterMetaData object containing GUID, component name, logical path and paths of the component.
        /// </summary>
        /// <returns>Anonymous object containing GUID, component name, logical path and paths of the component.</returns>
        /// <param name="writers">Writers.</param>
        public IEnumerable<WriterMetaData> ParseWriterMetaData(Guid[] writers)
            => _snapshotProvider.ParseWriterMetaData(writers);

        /// <summary>
        /// Prepares the snapshot and notifies all writers
        /// </summary>
        /// <param name="sources">The soruces to include</param>
        public void InitShadowVolumes(IEnumerable<string> sources)
        {
            // The writer metadata is no longer needed once writers have been
            // verified, and GatherWriterMetadata can only be called once per
            // backup components object, so free it before creating snapshots
            _snapshotProvider.FreeWriterMetadata();

            _snapshotProvider.StartSnapshotSet();

            CheckAndAddSupportedVolumes(sources);

            //Make all writers aware that we are going to do the backup
            _snapshotProvider.PrepareForBackup();

            //Create the shadow volumes
            _snapshotProvider.DoSnapshotSet();
        }

        /// <summary>
        /// Gets the snapshot path from the local path
        /// </summary>
        /// <param name="path">The local path</param>
        /// <returns>The snapshot path</returns>
        public string GetVolumeFromCache(string path)
        {
            if (!_volumeMap.TryGetValue(path, out var volumePath))
                throw new InvalidOperationException();

            return volumePath;
        }

        /// <summary>
        /// Checks if the volumes can be added and creates snapshots as needed
        /// </summary>
        /// <param name="sources">The sources to create snapshots for</param>
        private void CheckAndAddSupportedVolumes(IEnumerable<string> sources)
        {
            //Figure out which volumes are in the set
            _volumes = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in sources)
            {
                var drive = SystemIO.IO_OS.GetPathRoot(s);
                if (!_volumes.ContainsKey(drive))
                {
                    if (!_snapshotProvider.IsVolumeSupported(drive))
                        throw new ArgumentException($"Drive not supported for snapshot: {drive}");

                    _volumes.Add(drive, _snapshotProvider.AddToSnapshotSet(drive));
                }
            }
        }

        /// <inheritdoc/>
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
                _snapshotProvider?.FreeWriterMetadata();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "VSSFreeMetadataError", ex, "Failed to free VSS writer metadata");
            }

            try
            {
                _snapshotProvider?.BackupComplete();
            }
            catch (Exception ex)
            {
                Logging.Log.WriteVerboseMessage(LOGTAG, "VSSTerminateError", ex, "Failed to signal VSS completion");
            }

            try
            {
                if (_snapshotProvider != null)
                {
                    foreach (var g in _volumes.Values)
                    {
                        try
                        {
                            _snapshotProvider.DeleteSnapshot(g, false);
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
                Logging.Log.WriteVerboseMessage(LOGTAG, "VSSSnapShotDeleteCleanError", ex, "Failed during VSS snapshot closing");
            }

            if (_snapshotProvider != null)
            {
                _snapshotProvider.Dispose();
                _snapshotProvider = null;
            }
        }
    }
}
