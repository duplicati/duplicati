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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Microsoft.Management.Infrastructure;

namespace Duplicati.Library.Snapshots.Windows
{
    public class HyperVGuest : IEquatable<HyperVGuest>
    {
        public string Name { get; }
        public Guid ID { get; }
        public List<string>? DataPaths { get; }

        public HyperVGuest(string Name, Guid ID, List<string>? DataPaths)
        {
            this.Name = Name;
            this.ID = ID;
            this.DataPaths = DataPaths;
        }

        bool IEquatable<HyperVGuest>.Equals(HyperVGuest? other)
        {
            return other is not null && ID.Equals(other.ID);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public override bool Equals(object? obj)
        {
            HyperVGuest? guest = obj as HyperVGuest;
            if (guest != null)
            {
                return Equals(guest);
            }

            return false;
        }

        public static bool operator ==(HyperVGuest? guest1, HyperVGuest? guest2)
        {
            if (object.ReferenceEquals(guest1, guest2)) return true;
            if (object.ReferenceEquals(guest1, null)) return false;
            if (object.ReferenceEquals(guest2, null)) return false;

            return guest1.Equals(guest2);
        }

        public static bool operator !=(HyperVGuest? guest1, HyperVGuest? guest2)
        {
            if (object.ReferenceEquals(guest1, guest2)) return false;
            if (object.ReferenceEquals(guest1, null)) return true;
            if (object.ReferenceEquals(guest2, null)) return true;

            return !guest1.Equals(guest2);
        }
    }

    /// <summary>
    /// Interface for Hyper-V utility to query Hyper-V guests and their paths
    /// </summary>
    public interface IHyperVUtility
    {
        /// <summary>
        /// The Hyper-V VSS Writer Guid
        /// </summary>
        Guid HyperVWriterGuid { get; }
        /// <summary>
        /// Hyper-V is supported only on Windows platform
        /// </summary>
        bool IsHyperVInstalled { get; }
        /// <summary>
        /// Hyper-V writer is supported only on Server version of Windows
        /// </summary>
        bool IsVSSWriterSupported { get; }

        /// <summary>
        /// Enumerated Hyper-V guests
        /// </summary>
        List<HyperVGuest> Guests { get; }

        /// <summary>
        /// Query Hyper-V for all Virtual Machines info
        /// </summary>
        /// <param name="bIncludePaths">Specify if returned data should contain VM paths</param>
        /// <param name="provider">The provider to use for VSS</param>
        /// <returns>List of Hyper-V Machines</returns>
        void QueryHyperVGuestsInfo(WindowsSnapshotProvider provider, bool bIncludePaths = false);
    }

    [SupportedOSPlatform("windows")]
    public class HyperVUtility : IHyperVUtility, IDisposable
    {
        /// <summary>
        /// The tag used for logging
        /// </summary>
        private static readonly string LOGTAG = Logging.Log.LogTagFromType<HyperVUtility>();

        /// <summary>
        /// The System.IO abstraction for the Windows platform
        /// </summary>
        private static readonly ISystemIO IO_WIN = SystemIO.IO_OS;

        /// <summary>
        /// The CIM session used for all WMI queries
        /// </summary>
        private readonly CimSession _cimSession = null!;
        /// <summary>
        /// The WMI namespace used for Hyper-V queries
        /// </summary>
        private readonly string _wmiNamespace = null!;
        private readonly string _vmIdField = null!;
        private readonly bool _wmiv2Namespace;
        /// <summary>
        /// The Hyper-V VSS Writer Guid
        /// </summary>
        internal static readonly Guid _HyperVWriterGuid = new Guid("66841cd4-6ded-4f4b-8f17-fd23f8ddc3de");
        /// <summary>
        /// The Hyper-V VSS Writer Guid
        /// </summary>
        public Guid HyperVWriterGuid => _HyperVWriterGuid;
        /// <summary>
        /// Hyper-V is supported only on Windows platform
        /// </summary>
        public bool IsHyperVInstalled { get; }
        /// <summary>
        /// Hyper-V writer is supported only on Server version of Windows
        /// </summary>
        public bool IsVSSWriterSupported { get; }

        /// <summary>
        /// Enumerated Hyper-V guests
        /// </summary>
        public List<HyperVGuest> Guests { get; }

        public HyperVUtility()
        {
            Guests = new List<HyperVGuest>();

            if (!OperatingSystem.IsWindows())
            {
                IsHyperVInstalled = false;
                IsVSSWriterSupported = false;
                return;
            }

            //Set the namespace depending off host OS
            _wmiv2Namespace = OperatingSystem.IsWindowsVersionAtLeast(6, 2);

            _cimSession = CimSession.Create(null);
            //Set the namespace to use in WMI. V2 for Server 2012 or newer.
            _wmiNamespace = _wmiv2Namespace
                ? @"root\virtualization\v2"
                : @"root\virtualization";
            //Set the VM ID Selector Field for the WMI Query
            _vmIdField = _wmiv2Namespace ? "VirtualSystemIdentifier" : "SystemName";

            Logging.Log.WriteProfilingMessage(LOGTAG, "WMISelect", "Using WMI provider {0}", _wmiNamespace);

            IsVSSWriterSupported = _cimSession.QueryInstances(@"root\cimv2", "WQL", "SELECT ProductType FROM Win32_OperatingSystem")
                    .Select(o => Convert.ToUInt32(o.CimInstanceProperties["ProductType"]?.Value))
                    .First() != 1;

            try
            {
                IsHyperVInstalled = _cimSession.EnumerateClasses(_wmiNamespace)
                    .Any(c => c.CimSystemProperties.ClassName.StartsWith("Msvm_", StringComparison.Ordinal));
            }
            catch { IsHyperVInstalled = false; }

            if (!IsHyperVInstalled)
                Logging.Log.WriteInformationMessage(LOGTAG, "NoHyperVFound", "Cannot open WMI provider {0}. Hyper-V is probably not installed.", _wmiNamespace);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            _cimSession?.Dispose();
        }

        /// <summary>
        /// Safely gets a string value from a CIM instance property
        /// </summary>
        /// <param name="instance">The CIM instance</param>
        /// <param name="propertyName">The property name</param>
        /// <returns>The string value, or null if the property is missing or not a string</returns>
        private static string? GetStringProperty(CimInstance instance, string propertyName)
            => instance.CimInstanceProperties[propertyName]?.Value as string;

        /// <summary>
        /// Query Hyper-V for all Virtual Machines info
        /// </summary>
        /// <param name="bIncludePaths">Specify if returned data should contain VM paths</param>
        /// <param name="provider">The provider to use for VSS</param>
        /// <returns>List of Hyper-V Machines</returns>
        public void QueryHyperVGuestsInfo(WindowsSnapshotProvider provider, bool bIncludePaths = false)
        {
            if (!IsHyperVInstalled)
                return;

            Guests.Clear();
            var wmiQuery = _wmiv2Namespace
                ? "SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'"
                : "SELECT * FROM Msvm_VirtualSystemSettingData WHERE SettingType = 3";

            var vmSettings = _cimSession.QueryInstances(_wmiNamespace, "WQL", wmiQuery).ToList();

            if (IsVSSWriterSupported)
            {
                if (bIncludePaths)
                {
                    foreach (var o in GetAllVMsPathsVSS(provider))
                    {
                        foreach (var mObject in vmSettings)
                        {
                            if (GetStringProperty(mObject, _vmIdField) == o.Name)
                            {
                                Guests.Add(new HyperVGuest(GetStringProperty(mObject, "ElementName")!, new Guid(GetStringProperty(mObject, _vmIdField)!), o.Paths));
                            }
                        }
                    }
                }
                else
                {
                    foreach (var mObject in vmSettings)
                        Guests.Add(new HyperVGuest(GetStringProperty(mObject, "ElementName")!, new Guid(GetStringProperty(mObject, _vmIdField)!), null));
                }
            }
            else
            {
                foreach (var mObject in vmSettings)
                {
                    Guests.Add(new HyperVGuest(GetStringProperty(mObject, "ElementName")!, new Guid(GetStringProperty(mObject, _vmIdField)!), bIncludePaths ?
                        GetVMVhdPathsWMI(GetStringProperty(mObject, _vmIdField)!)
                            .Union(GetVMConfigPathsWMI(GetStringProperty(mObject, _vmIdField)!))
                            .ToList()
                            .ConvertAll(m => m[0].ToString().ToUpperInvariant() + m.Substring(1))
                            .Distinct(Utility.Utility.ClientFilenameStringComparer)
                            .OrderBy(a => a).ToList() : null));
                }
            }
        }

        /// <summary>
        /// For all Hyper-V guests it enumerate all associated paths using VSS data
        /// </summary>
        /// <returns>A collection of VMs and paths</returns>
        private static IEnumerable<WriterMetaData> GetAllVMsPathsVSS(WindowsSnapshotProvider provider)
        {
            using (var vssBackupComponents = new SnapshotManager(provider, SnapshotManager.WriterMetadataQueryTimeout))
            {
                var writerGUIDS = new[] { _HyperVWriterGuid };

                try
                {
                    vssBackupComponents.SetupWriters(writerGUIDS, null);
                }
                catch (Exception)
                {
                    throw new Interface.UserInformationException("Microsoft Hyper-V VSS Writer not found - cannot backup Hyper-V machines.", "NoHyperVVssWriter");
                }
                foreach (var o in vssBackupComponents.ParseWriterMetaData(writerGUIDS))
                {
                    yield return o;
                }
            }
        }

        /// <summary>
        /// For given Hyper-V guest it enumerate all associated configuration files using WMI data
        /// </summary>
        /// <param name="vmID">ID of VM to get paths for</param>
        /// <returns>A collection of configuration paths</returns>
        private List<string> GetVMConfigPathsWMI(string vmID)
        {
            var result = new List<string>();
            string path;
            var wmiQuery = _wmiv2Namespace
                ? string.Format("select * from Msvm_VirtualSystemSettingData where {0}='{1}'", _vmIdField, vmID)
                : string.Format("select * from Msvm_VirtualSystemGlobalSettingData where {0}='{1}'", _vmIdField, vmID);

            var mObject1 = _cimSession.QueryInstances(_wmiNamespace, "WQL", wmiQuery).First();
            if (_wmiv2Namespace)
            {
                path = IO_WIN.PathCombine(GetStringProperty(mObject1, "ConfigurationDataRoot")!, GetStringProperty(mObject1, "ConfigurationFile")!);
                if (File.Exists(path))
                    result.Add(path);

                var snaps = _cimSession.QueryInstances(_wmiNamespace, "WQL", string.Format(
                    "SELECT * FROM Msvm_VirtualSystemSettingData where VirtualSystemType='Microsoft:Hyper-V:Snapshot:Realized' and {0}='{1}'",
                    _vmIdField, vmID));

                foreach (var snap in snaps)
                {
                    path = IO_WIN.PathCombine(GetStringProperty(snap, "ConfigurationDataRoot")!, GetStringProperty(snap, "ConfigurationFile")!);
                    if (File.Exists(path))
                        result.Add(path);
                    path = Util.AppendDirSeparator(IO_WIN.PathCombine(GetStringProperty(snap, "ConfigurationDataRoot")!, GetStringProperty(snap, "SuspendDataRoot")!));
                    if (Directory.Exists(path))
                        result.Add(path);
                }
            }
            else
            {
                path = IO_WIN.PathCombine(GetStringProperty(mObject1, "ExternalDataRoot")!, "Virtual Machines", vmID + ".xml");
                if (File.Exists(path))
                    result.Add(path);
                path = Util.AppendDirSeparator(IO_WIN.PathCombine(GetStringProperty(mObject1, "ExternalDataRoot")!, "Virtual Machines", vmID));
                if (Directory.Exists(path))
                    result.Add(path);

                var snapsIDs = _cimSession.QueryInstances(_wmiNamespace, "WQL", string.Format(
                    "SELECT InstanceID FROM Msvm_VirtualSystemSettingData where SettingType=5 and {0}='{1}'",
                    _vmIdField, vmID)).Select(o => GetStringProperty(o, "InstanceID")).OfType<string>().ToList();

                foreach (var snapID in snapsIDs)
                {
                    path = IO_WIN.PathCombine(GetStringProperty(mObject1, "SnapshotDataRoot")!, "Snapshots", snapID.Replace("Microsoft:", "") + ".xml");
                    if (File.Exists(path))
                        result.Add(path);
                    path = Util.AppendDirSeparator(IO_WIN.PathCombine(GetStringProperty(mObject1, "SnapshotDataRoot")!, "Snapshots", snapID.Replace("Microsoft:", "")));
                    if (Directory.Exists(path))
                        result.Add(path);
                }
            }

            return result;
        }

        /// <summary>
        /// For given Hyper-V guest it enumerate all associated VHD files using WMI data
        /// </summary>
        /// <param name="vmID">ID of VM to get paths for</param>
        /// <returns>A collection of VHD paths</returns>
        private List<string> GetVMVhdPathsWMI(string vmID)
        {
            var result = new List<string>();
            var vm = _cimSession.QueryInstances(_wmiNamespace, "WQL", string.Format("select * from Msvm_ComputerSystem where Name = '{0}'", vmID)).First();

            foreach (var sysSettings in _cimSession.EnumerateAssociatedInstances(_wmiNamespace, vm, null, "MsVM_VirtualSystemSettingData", null, null))
            {
                var resourceClassName = _wmiv2Namespace ? "MsVM_StorageAllocationSettingData" : "MsVM_ResourceAllocationSettingData";
                var systemObjCollection = _cimSession.EnumerateAssociatedInstances(_wmiNamespace, sysSettings, null, resourceClassName, null, null);

                List<string> tempvhd;

                if (_wmiv2Namespace)
                    tempvhd = (from systemBaseObj in systemObjCollection
                               where ((UInt16)systemBaseObj.CimInstanceProperties["ResourceType"]?.Value! == 31
                                       && GetStringProperty(systemBaseObj, "ResourceSubType") == "Microsoft:Hyper-V:Virtual Hard Disk")
                               select ((string[])systemBaseObj.CimInstanceProperties["HostResource"]?.Value!)[0]).ToList();
                else
                    tempvhd = (from systemBaseObj in systemObjCollection
                               where ((UInt16)systemBaseObj.CimInstanceProperties["ResourceType"]?.Value! == 21
                                       && GetStringProperty(systemBaseObj, "ResourceSubType") == "Microsoft Virtual Hard Disk")
                               select ((string[])systemBaseObj.CimInstanceProperties["Connection"]?.Value!)[0]).ToList();

                foreach (var vhd in tempvhd)
                {
                    if (File.Exists(vhd))
                    {
                        result.Add(vhd);
                    }
                    else
                    {
                        Logging.Log.WriteWarningMessage(LOGTAG, "HyperVInvalidVhd", null, "Invalid VHD file detected, file does not exist: {0}", vhd);
                    }
                }
            }

            var imgMan = _cimSession.EnumerateInstances(_wmiNamespace, "MsVM_ImageManagementService").First();
            var parentPaths = new List<string>();

            foreach (var vhdPath in result)
            {
                var inParams = new CimMethodParametersCollection
                {
                    CimMethodParameter.Create("Path", vhdPath, CimType.String, CimFlags.In)
                };

                using var outParams = _cimSession.InvokeMethod(imgMan, _wmiv2Namespace ? "GetVirtualHardDiskSettingData" : "GetVirtualHardDiskInfo", inParams);
                var propertyValue = outParams?.OutParameters[_wmiv2Namespace ? "SettingData" : "Info"]?.Value as string;

                if (propertyValue != null)
                {
                    var doc = new System.Xml.XmlDocument();
                    doc.LoadXml(propertyValue);
                    var node = doc.SelectSingleNode("//PROPERTY[@NAME = 'ParentPath']/VALUE/child::text()");

                    if (node != null && File.Exists(node.Value))
                        parentPaths.Add(node.Value!);
                }
            }

            result.AddRange(parentPaths);

            return result;
        }
    }
}
