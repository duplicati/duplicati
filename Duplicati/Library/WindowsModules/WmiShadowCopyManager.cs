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
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.Management.Infrastructure;

namespace Duplicati.Library.WindowsModules;

/// <summary>
/// A shadow copy manager using the WMI/CIM interface via Microsoft.Management.Infrastructure
/// </summary>
[SupportedOSPlatform("windows")]
internal class WmiShadowCopyManager : IDisposable
{
    /// <summary>
    /// The tag used for logging messages
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType<WmiShadowCopyManager>();

    /// <summary>
    /// The WMI namespace for shadow copy operations
    /// </summary>
    private const string CimNamespace = @"root\cimv2";

    /// <summary>
    /// The CIM class name for shadow copies
    /// </summary>
    private const string ShadowCopyClassName = "Win32_ShadowCopy";

    /// <summary>
    /// A single shadow copy
    /// </summary>
    /// <param name="shadowId">The shadow ID as a string</param>
    /// <param name="parsedId">The shadow ID as a GUID</param>
    /// <param name="originalDrive">The drive that the snapshot is for</param>
    /// <param name="mappedPath">The path that contains the snapshot</param>
    public class WmiShadowCopy(string shadowId, Guid parsedId, string originalDrive, string mappedPath) : IDisposable
    {
        /// <summary>
        /// Gets the shadow ID
        /// </summary>
        public string ShadowID { get; } = shadowId;
        /// <summary>
        /// Gets the shadow ID
        /// </summary>
        public Guid ParsedId { get; } = parsedId;
        /// <summary>
        /// Gets the drive that was originally mapped
        /// </summary>
        public string OriginalDrive { get; } = originalDrive;
        /// <summary>
        /// Gets the path where the snapshot is found
        /// </summary>
        public string MappedPath { get; } = mappedPath;

        /// <summary>
        /// Flag keeping track of the snapshot deletion state
        /// </summary>
        private bool _snapshotDeleted;

        /// <inheritdoc/>
        public void Dispose()
        {
            DeleteShadowCopy();
        }

        /// <summary>
        /// Deletes the shadow copy
        /// </summary>
        private void DeleteShadowCopy()
        {
            if (_snapshotDeleted)
                return;
            if (!string.IsNullOrEmpty(ShadowID))
            {
                _snapshotDeleted = true;
                Logging.Log.WriteVerboseMessage(LOGTAG, "DeleteShadowCopy", $"Deleting Shadow Copy: {ShadowID}");
                DeleteShadow(ShadowID);
            }
        }
    }

    /// <summary>
    /// The list of the currently registered shadow copies
    /// </summary>
    private List<WmiShadowCopy> _shadowCopies = new List<WmiShadowCopy>();

    /// <summary>
    /// Gets the list of the currently registered shadow copies
    /// </summary>
    public IEnumerable<WmiShadowCopy> ShadowCopies => _shadowCopies;

    /// <summary>
    /// Creates a new snapshot for the given drive
    /// </summary>
    /// <param name="drive">The drive to create the snapshot for</param>
    /// <returns>The created snapshot</returns>
    public WmiShadowCopy Add(string drive)
    {
        var shadowId = CreateShadowCopy(drive);
        if (string.IsNullOrEmpty(shadowId))
            throw new InvalidOperationException("Failed to create shadow copy");

        var shadowPath = GetShadowPath(shadowId);
        if (string.IsNullOrEmpty(shadowPath))
        {
            DeleteShadow(shadowId);
            throw new InvalidOperationException("Failed to get shadow copy path");
        }

        var snapshot = new WmiShadowCopy(shadowId, Guid.Parse(shadowId), drive, shadowPath);
        _shadowCopies.Add(snapshot);

        return snapshot;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var shadow in ShadowCopies)
        {
            shadow.Dispose();
        }
        _shadowCopies.Clear();
    }

    /// <summary>
    /// Creates a shadow copy by invoking the Win32_ShadowCopy.Create method
    /// </summary>
    /// <param name="drive">The drive to create the snapshot for</param>
    /// <returns>The shadow id</returns>
    private static string? CreateShadowCopy(string drive)
    {
        try
        {
            using var session = CimSession.Create(null);
            var parameters = new CimMethodParametersCollection
            {
                CimMethodParameter.Create("Volume", drive, CimType.String, CimFlags.In)
            };

            using var result = session.InvokeMethod(CimNamespace, ShadowCopyClassName, "Create", parameters);

            var returnValue = Convert.ToUInt32(result.ReturnValue?.Value);
            if (returnValue != 0)
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", null, "Failed to create shadow copy for {0}: ReturnValue={1}", drive, returnValue);
                return null;
            }

            var shadowId = result.OutParameters["ShadowID"]?.Value as string;
            if (string.IsNullOrEmpty(shadowId))
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", null, "Failed to create shadow copy for {0}: no ShadowID returned", drive);
                return null;
            }

            return shadowId;
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", ex, "Failed to create shadow copy for {0}", drive);
            return null;
        }
    }

    /// <summary>
    /// Gets the path where the shadow copy is mounted
    /// </summary>
    /// <param name="shadowId">The shadow copy id</param>
    /// <returns>The path where the copy is mounted</returns>
    private static string? GetShadowPath(string shadowId)
    {
        try
        {
            using var session = CimSession.Create(null);
            var query = $"SELECT DeviceObject FROM {ShadowCopyClassName} WHERE ID = '{EscapeWqlValue(shadowId)}'";
            var deviceObject = session.QueryInstances(CimNamespace, "WQL", query)
                .Select(x => x.CimInstanceProperties["DeviceObject"]?.Value as string)
                .FirstOrDefault(x => !string.IsNullOrEmpty(x));

            if (string.IsNullOrEmpty(deviceObject))
            {
                Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", null, "Failed to get shadow copy path for {0}", shadowId);
                return null;
            }

            return deviceObject;
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyFailed", ex, "Failed to get shadow copy path for {0}", shadowId);
            return null;
        }
    }

    /// <summary>
    /// Returns the drives that are vss enabled
    /// </summary>
    /// <returns>The set of drive letters that support VSS snapshots</returns>
    public static HashSet<string> GetVssCapableDrives()
    {
        var vssDrives = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var session = CimSession.Create(null);
            var volumes = session.QueryInstances(CimNamespace, "WQL", "SELECT DriveLetter, FileSystem FROM Win32_Volume WHERE DriveType = 3");

            foreach (var volume in volumes)
            {
                var driveLetter = volume.CimInstanceProperties["DriveLetter"]?.Value as string;
                var fileSystem = volume.CimInstanceProperties["FileSystem"]?.Value as string;

                if (!string.IsNullOrEmpty(driveLetter) && IsSnapshotFileSystem(fileSystem))
                    vssDrives.Add(driveLetter.Substring(0, 1));
            }
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyListFailed", ex, "Failed to list volumes");
        }

        return vssDrives;
    }

    /// <summary>
    /// Checks if the filesystem supports VSS snapshots
    /// </summary>
    /// <param name="fileSystem">The filesystem name</param>
    /// <returns>True if the filesystem supports VSS snapshots</returns>
    private static bool IsSnapshotFileSystem(string? fileSystem)
        => string.Equals(fileSystem, "NTFS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileSystem, "ReFS", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Delete a shadow copy
    /// </summary>
    /// <param name="shadowId">The shadow id</param>
    private static void DeleteShadow(string shadowId)
    {
        try
        {
            using var session = CimSession.Create(null);
            var query = $"SELECT * FROM {ShadowCopyClassName} WHERE ID = '{EscapeWqlValue(shadowId)}'";
            foreach (var instance in session.QueryInstances(CimNamespace, "WQL", query))
                session.DeleteInstance(instance);
        }
        catch (Exception ex)
        {
            Logging.Log.WriteErrorMessage(LOGTAG, "ShadowCopyDeleteFailed", ex, "Failed to delete shadow copy {0}", shadowId);
        }
    }

    /// <summary>
    /// Escapes a string value for safe inclusion in a WQL query
    /// </summary>
    /// <param name="value">The value to escape</param>
    /// <returns>The escaped value</returns>
    private static string EscapeWqlValue(string value)
        => value.Replace("\\", "\\\\").Replace("'", "\\'");
}
