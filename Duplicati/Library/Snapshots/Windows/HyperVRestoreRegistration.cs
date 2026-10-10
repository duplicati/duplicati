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
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Serialization;

namespace Duplicati.Library.Snapshots.Windows
{
    /// <summary>
    /// Registers restored Hyper-V virtual machines with Hyper-V, using the
    /// planned virtual machine methods of the WMI v2 namespace
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class HyperVRestoreRegistration : IDisposable
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<HyperVRestoreRegistration>();

        /// <summary>
        /// The WMI namespace with the planned virtual machine methods
        /// </summary>
        private const string WMI_NAMESPACE = @"root\virtualization\v2";

        /// <summary>
        /// The return value of a method that continues as a job
        /// </summary>
        private const uint METHOD_JOB_STARTED = 4096;

        /// <summary>
        /// The job state of a job that has completed successfully
        /// </summary>
        private const ushort JOB_STATE_COMPLETED = 7;

        /// <summary>
        /// The job states of a job that has stopped without completing
        /// </summary>
        private static readonly ushort[] JOB_STATES_FAILED = [8, 9, 10];

        /// <summary>
        /// The resource type of a virtual hard disk in a storage allocation setting
        /// </summary>
        private const ushort RESOURCE_TYPE_LOGICAL_DISK = 31;

        /// <summary>
        /// The CIM session
        /// </summary>
        private readonly CimSession _session;

        /// <summary>
        /// The serializer used for the embedded instances the WMI methods take
        /// </summary>
        private readonly CimSerializer _serializer;

        /// <summary>
        /// Creates a new instance
        /// </summary>
        public HyperVRestoreRegistration()
        {
            _session = CimSession.Create(null);
            _serializer = CimSerializer.Create();
        }

        /// <summary>
        /// Checks whether a virtual machine with the given ID is registered with Hyper-V
        /// </summary>
        /// <param name="vmId">The virtual machine ID</param>
        /// <returns>True if the virtual machine is registered</returns>
        public bool IsRegistered(Guid vmId)
            => _session.QueryInstances(WMI_NAMESPACE, "WQL", $"SELECT * FROM Msvm_ComputerSystem WHERE Name = '{vmId}'").Any();

        /// <summary>
        /// Gets the parent of a differencing virtual hard disk
        /// </summary>
        /// <param name="path">The path to the virtual hard disk</param>
        /// <returns>The path to the parent disk, or <c>null</c> if the disk is not a differencing disk</returns>
        public string? GetParentDisk(string path)
        {
            using var result = InvokeMethod(GetImageManagementService(), "GetVirtualHardDiskSettingData", [
                CimMethodParameter.Create("Path", path, CimType.String, CimFlags.In)
            ], $"read the settings of {path}");

            if (result.OutParameters["SettingData"]?.Value is not string settingData)
                return null;

            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(settingData);
            var parent = doc.SelectSingleNode("//PROPERTY[@NAME = 'ParentPath']/VALUE/child::text()")?.Value;
            return string.IsNullOrWhiteSpace(parent) ? null : parent;
        }

        /// <summary>
        /// Sets the parent of a differencing virtual hard disk
        /// </summary>
        /// <param name="childPath">The path to the differencing disk</param>
        /// <param name="parentPath">The path to the new parent disk</param>
        public void SetParentDisk(string childPath, string parentPath)
        {
            using var _ = InvokeMethod(GetImageManagementService(), "SetParentVirtualHardDisk", [
                CimMethodParameter.Create("ChildPath", childPath, CimType.String, CimFlags.In),
                CimMethodParameter.Create("ParentPath", parentPath, CimType.String, CimFlags.In),
                CimMethodParameter.Create("LeafPath", null, CimType.String, CimFlags.In),
                CimMethodParameter.Create("IgnoreIDMismatch", false, CimType.Boolean, CimFlags.In),
            ], $"set the parent of {childPath} to {parentPath}");
        }

        /// <summary>
        /// Imports a virtual machine from its configuration file and registers it with Hyper-V
        /// </summary>
        /// <param name="configurationFile">The virtual machine configuration file (<c>.vmcx</c> or <c>.xml</c>)</param>
        /// <param name="snapshotFolder">The folder with the checkpoint configurations</param>
        /// <param name="generateNewId">True to import the machine as a copy with a new ID</param>
        /// <param name="diskPathMap">The virtual hard disk paths to replace in the configuration, keyed by the path in the configuration</param>
        /// <param name="nameSuffix">The text to add to the name of the machine, or <c>null</c> to keep its name</param>
        /// <returns>The ID and the name of the registered virtual machine</returns>
        public (string Id, string Name) Import(string configurationFile, string snapshotFolder, bool generateNewId, IReadOnlyDictionary<string, string> diskPathMap, string? nameSuffix)
        {
            var managementService = _session.EnumerateInstances(WMI_NAMESPACE, "Msvm_VirtualSystemManagementService").First();

            CimInstance planned;
            using (var imported = InvokeMethod(managementService, "ImportSystemDefinition", [
                CimMethodParameter.Create("SystemDefinitionFile", configurationFile, CimType.String, CimFlags.In),
                CimMethodParameter.Create("SnapshotFolder", snapshotFolder, CimType.String, CimFlags.In),
                CimMethodParameter.Create("GenerateNewSystemIdentifier", generateNewId, CimType.Boolean, CimFlags.In),
            ], $"import {configurationFile}"))
            {
                planned = imported.OutParameters["ImportedSystem"]?.Value as CimInstance
                    ?? throw new InvalidOperationException($"Hyper-V did not return the imported machine for {configurationFile}");
            }

            try
            {
                var settings = _session.EnumerateAssociatedInstances(WMI_NAMESPACE, planned, "Msvm_SettingsDefineState", "Msvm_VirtualSystemSettingData", null, null).First();
                var originalName = settings.CimInstanceProperties["ElementName"]?.Value as string ?? string.Empty;
                var name = originalName + (nameSuffix ?? string.Empty);

                // Point the disks at the restored files, and disconnect the network
                // adapters from switches that do not exist on this host
                var changed = new List<CimInstance>();
                foreach (var disk in _session.EnumerateAssociatedInstances(WMI_NAMESPACE, settings, null, "Msvm_StorageAllocationSettingData", null, null))
                {
                    if (Convert.ToUInt16(disk.CimInstanceProperties["ResourceType"]?.Value) != RESOURCE_TYPE_LOGICAL_DISK)
                        continue;

                    var hostResource = disk.CimInstanceProperties["HostResource"]?.Value as string[];
                    if (hostResource == null || hostResource.Length == 0 || !diskPathMap.TryGetValue(hostResource[0], out var mapped))
                        continue;

                    disk.CimInstanceProperties["HostResource"].Value = new[] { mapped };
                    changed.Add(disk);
                }

                var switches = _session.EnumerateInstances(WMI_NAMESPACE, "Msvm_VirtualEthernetSwitch")
                    .Select(x => x.CimInstanceProperties["Name"]?.Value as string)
                    .Where(x => x != null)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var port in _session.EnumerateAssociatedInstances(WMI_NAMESPACE, settings, null, "Msvm_EthernetPortAllocationSettingData", null, null))
                {
                    var hostResource = port.CimInstanceProperties["HostResource"]?.Value as string[];
                    if (hostResource == null || hostResource.Length == 0)
                        continue;

                    var switchName = Regex.Match(hostResource[0], "Name=\"(?<name>[^\"]+)\"").Groups["name"].Value;
                    if (switches.Contains(switchName))
                        continue;

                    Logging.Log.WriteWarningMessage(LOGTAG, "HyperVSwitchMissing", null, "The virtual switch of a network adapter of \"{0}\" does not exist on this host, so the adapter is disconnected", name);
                    port.CimInstanceProperties["HostResource"].Value = Array.Empty<string>();
                    changed.Add(port);
                }

                if (changed.Count > 0)
                {
                    using var _ = InvokeMethod(managementService, "ModifyResourceSettings", [
                        CimMethodParameter.Create("ResourceSettings", changed.Select(Embed).ToArray(), CimType.StringArray, CimFlags.In)
                    ], $"update the disks and network adapters of \"{name}\"");
                }

                if (!string.IsNullOrEmpty(nameSuffix))
                {
                    settings.CimInstanceProperties["ElementName"].Value = name;
                    using var _ = InvokeMethod(managementService, "ModifySystemSettings", [
                        CimMethodParameter.Create("SystemSettings", Embed(settings), CimType.String, CimFlags.In)
                    ], $"rename \"{originalName}\" to \"{name}\"");
                }

                using var realized = InvokeMethod(managementService, "RealizePlannedSystem", [
                    CimMethodParameter.Create("PlannedSystem", planned, CimType.Reference, CimFlags.In)
                ], $"register \"{name}\"");

                var id = (realized.OutParameters["ResultingSystem"]?.Value as CimInstance)?.CimInstanceProperties["Name"]?.Value as string ?? string.Empty;
                return (id, name);
            }
            catch
            {
                // Do not leave a half-imported machine behind
                try
                {
                    using var _ = InvokeMethod(managementService, "DestroySystem", [
                        CimMethodParameter.Create("AffectedSystem", planned, CimType.Reference, CimFlags.In)
                    ], "remove the planned machine");
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "HyperVPlannedSystemCleanupFailed", ex, "Failed to remove the planned machine after a failed import");
                }

                throw;
            }
        }

        /// <summary>
        /// Gets the image management service
        /// </summary>
        private CimInstance GetImageManagementService()
            => _session.EnumerateInstances(WMI_NAMESPACE, "Msvm_ImageManagementService").First();

        /// <summary>
        /// Serializes an instance to the embedded instance format the WMI methods take
        /// </summary>
        private string Embed(CimInstance instance)
            => Encoding.Unicode.GetString(_serializer.Serialize(instance, InstanceSerializationOptions.None));

        /// <summary>
        /// Invokes a WMI method and waits for the job it starts to complete
        /// </summary>
        /// <param name="target">The instance to invoke the method on</param>
        /// <param name="method">The method name</param>
        /// <param name="parameters">The method parameters</param>
        /// <param name="description">A description of the operation, for error messages</param>
        /// <returns>The method result</returns>
        private CimMethodResult InvokeMethod(CimInstance target, string method, CimMethodParameter[] parameters, string description)
        {
            var collection = new CimMethodParametersCollection();
            foreach (var parameter in parameters)
                collection.Add(parameter);

            var result = _session.InvokeMethod(WMI_NAMESPACE, target, method, collection);
            var returnValue = Convert.ToUInt32(result.ReturnValue?.Value);
            if (returnValue == METHOD_JOB_STARTED)
            {
                var job = result.OutParameters["Job"]?.Value as CimInstance
                    ?? throw new InvalidOperationException($"Failed to {description}: Hyper-V started a job but did not return it");
                WaitForJob(job, description);
            }
            else if (returnValue != 0)
            {
                result.Dispose();
                throw new InvalidOperationException($"Failed to {description}: Hyper-V returned {returnValue}");
            }

            return result;
        }

        /// <summary>
        /// Waits for a WMI job to complete
        /// </summary>
        /// <param name="job">The job</param>
        /// <param name="description">A description of the operation, for error messages</param>
        private void WaitForJob(CimInstance job, string description)
        {
            while (true)
            {
                using var current = _session.GetInstance(WMI_NAMESPACE, job);
                var state = Convert.ToUInt16(current.CimInstanceProperties["JobState"]?.Value);
                if (state == JOB_STATE_COMPLETED)
                    return;

                if (JOB_STATES_FAILED.Contains(state))
                    throw new InvalidOperationException($"Failed to {description}: {current.CimInstanceProperties["ErrorDescription"]?.Value}");

                Thread.Sleep(500);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _serializer.Dispose();
            _session.Dispose();
        }
    }
}
