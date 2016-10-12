using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace Duplicati.Library.Snapshots
{
    public class HyperVGuest
    {
        public string Name { get; }
        public string ID { get; }
        public List<string> DataPaths { get; }

        public HyperVGuest(string Name, string ID, List<string> DataPaths)
        {
            this.Name = Name;
            this.ID = ID;
            this.DataPaths = DataPaths;
        }
    }

    public class HyperVUtility
    {
        private readonly ManagementScope _wmiScope;
        private readonly string _vmIdField;
        private readonly string _wmiHost = "localhost";
        private readonly bool _wmiv2Namespace;
        public bool IsHyperVInstalled { get; }

        public HyperVUtility()
        {
            if (!Library.Utility.Utility.IsClientWindows)
            {
                IsHyperVInstalled = false;
                Logging.Log.WriteMessage("Hyper-V Guests are supported only on Windows.", Logging.LogMessageType.Information);
                return;
            }

            //Set the namespace depending off host OS
            _wmiv2Namespace = Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 2;

            //Set the scope to use in WMI. V2 for Server 2012 or newer.
            _wmiScope = _wmiv2Namespace
                ? new ManagementScope(string.Format("\\\\{0}\\root\\virtualization\\v2", _wmiHost))
                : new ManagementScope(string.Format("\\\\{0}\\root\\virtualization", _wmiHost));
            //Set the VM ID Selector Field for the WMI Query
            _vmIdField = _wmiv2Namespace ? "VirtualSystemIdentifier" : "SystemName";

            Logging.Log.WriteMessage(string.Format("Using WMI provider {0}", _wmiScope.Path), Logging.LogMessageType.Profiling);
            
            try
            {
                var classesCount = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(
                        "SELECT * FROM meta_class")).Get().OfType<ManagementObject>().Count();

                IsHyperVInstalled = classesCount > 0;
            }
            catch { IsHyperVInstalled = false; }

            if (!IsHyperVInstalled)
                Logging.Log.WriteMessage(string.Format("Cannot open WMI provider {0}. Hyper-V is probably not installed.", _wmiScope.Path), Logging.LogMessageType.Information);
        }

        /// <summary>
        /// We query the Hyper-V for all requested Virtual Machines
        /// </summary>
        /// <returns>List of Hyper-V Machines</returns>
        public List<HyperVGuest> GetHyperVGuests()
        {
            var hyperVMachines = new List<HyperVGuest>();

            if(!IsHyperVInstalled)
                return hyperVMachines;

            var wmiQuery = _wmiv2Namespace
                ? "SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'"
                : "SELECT * FROM Msvm_VirtualSystemSettingData WHERE SettingType = 3";

            using (var moCollection = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(wmiQuery)).Get())
                foreach (var mObject in moCollection)
                {
                    var paths = GetAllVmVhdPaths((string)mObject[_vmIdField]);
                    paths.AddRange(GetAllVmConfigPaths((string)mObject[_vmIdField]));

                    hyperVMachines.Add(new HyperVGuest((string)mObject["ElementName"], (string)mObject[_vmIdField], paths));
                }

            return hyperVMachines;
        }

        /// <summary>
        /// For given Hyper-V guest it enumerate all associated configuration files
        /// </summary>
        /// <param name="query"></param>
        /// <returns>A collection of configuration paths</returns>
        private List<string> GetAllVmConfigPaths(string vmID)
        {
            var result = new List<string>();
            string path;
            var wmiQuery = _wmiv2Namespace
                ? string.Format("select * from Msvm_VirtualSystemSettingData where {0}='{1}'", _vmIdField, vmID)
                : string.Format("select * from Msvm_VirtualSystemGlobalSettingData where {0}='{1}'", _vmIdField, vmID);

            using (var mObject1 = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(wmiQuery)).Get().Cast<ManagementObject>().First())
                if (_wmiv2Namespace)
                {
                    path = Path.Combine((string)mObject1["ConfigurationDataRoot"], (string)mObject1["ConfigurationFile"]);
                    if (File.Exists(path))
                        result.Add(path);

                    using (var snaps = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(string.Format(
                        "SELECT * FROM Msvm_VirtualSystemSettingData where VirtualSystemType='Microsoft:Hyper-V:Snapshot:Realized' and {0}='{1}'",
                        _vmIdField, vmID))).Get())
                    {
                        foreach (var snap in snaps)
                        {
                            path = Path.Combine((string)snap["ConfigurationDataRoot"], (string)snap["ConfigurationFile"]);
                            if (File.Exists(path))
                                result.Add(path);
                            path = Utility.Utility.AppendDirSeparator(Path.Combine((string)snap["ConfigurationDataRoot"], (string)snap["SuspendDataRoot"]));
                            if (Directory.Exists(path))
                                result.Add(path);
                        }
                    }
                }
                else
                {
                    path = Path.Combine((string)mObject1["ExternalDataRoot"], "Virtual Machines", vmID + ".xml");
                    if (File.Exists(path))
                        result.Add(path);
                    path = Utility.Utility.AppendDirSeparator(Path.Combine((string)mObject1["ExternalDataRoot"], "Virtual Machines", vmID));
                    if (Directory.Exists(path))
                        result.Add(path);

                    var snapsIDs = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(string.Format(
                        "SELECT * FROM Msvm_VirtualSystemSettingData where SettingType=5 and {0}='{1}'",
                        _vmIdField, vmID))).Get().OfType<ManagementObject>().Select(o => (string)o.GetPropertyValue("InstanceID")).ToList();

                    foreach (var snapID in snapsIDs)
                    {
                        path = Path.Combine((string)mObject1["SnapshotDataRoot"], "Snapshots", snapID.Replace("Microsoft:", "") + ".xml");
                        if (File.Exists(path))
                            result.Add(path);
                        path = Utility.Utility.AppendDirSeparator(Path.Combine((string)mObject1["SnapshotDataRoot"], "Snapshots", snapID.Replace("Microsoft:", "")));
                        if (Directory.Exists(path))
                            result.Add(path);
                    }
                }

            return result.Distinct(Utility.Utility.ClientFilenameStringComparer).ToList();
        }

        /// <summary>
        /// For given Hyper-V guest it enumerate all associated VHD files
        /// </summary>
        /// <param name="query"></param>
        /// <returns>A collection of VHD paths</returns>
        private List<string> GetAllVmVhdPaths(string vmID)
        {
            var result = new List<string>();
            using (var vm = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(string.Format("select * from Msvm_ComputerSystem where Name = '{0}'", vmID)))
                .Get().OfType<ManagementObject>().First())
            {
                foreach (var sysSettings in vm.GetRelated("MsVM_VirtualSystemSettingData"))
                    using (var systemObjCollection = ((ManagementObject)sysSettings).GetRelated(_wmiv2Namespace ? "MsVM_StorageAllocationSettingData" : "MsVM_ResourceAllocationSettingData"))
                    {
                        List<string> tempvhd;

                        if (_wmiv2Namespace)
                            tempvhd = (from ManagementBaseObject systemBaseObj in systemObjCollection
                                       where ((UInt16)systemBaseObj["ResourceType"] == 31
                                               && (string)systemBaseObj["ResourceSubType"] == "Microsoft:Hyper-V:Virtual Hard Disk")
                                       select ((string[])systemBaseObj["HostResource"])[0]).ToList();
                        else
                            tempvhd = (from ManagementBaseObject systemBaseObj in systemObjCollection
                                       where ((UInt16)systemBaseObj["ResourceType"] == 21
                                               && (string)systemBaseObj["ResourceSubType"] == "Microsoft Virtual Hard Disk")
                                       select ((string[])systemBaseObj["Connection"])[0]).ToList();

                        foreach (var vhd in tempvhd)
                            result.Add(vhd);
                    }
            }

            using (var imgMan = new ManagementObjectSearcher(_wmiScope, new ObjectQuery("select * from MsVM_ImageManagementService")).Get().OfType<ManagementObject>().First())
            {
                var ParentPaths = new List<string>();
                var inParams = imgMan.GetMethodParameters(_wmiv2Namespace ? "GetVirtualHardDiskSettingData" : "GetVirtualHardDiskInfo");

                foreach (var vhdPath in result)
                {
                    inParams["Path"] = vhdPath;
                    using (var outParams = imgMan.InvokeMethod(_wmiv2Namespace ? "GetVirtualHardDiskSettingData" : "GetVirtualHardDiskInfo", inParams, null))
                    {
                        if (outParams != null)
                        {
                            var doc = new System.Xml.XmlDocument();
                            doc.LoadXml((string)outParams[_wmiv2Namespace ? "SettingData" : "Info"]);
                            var node = doc.SelectSingleNode("//PROPERTY[@NAME = 'ParentPath']/VALUE/child::text()");

                            if (node != null && File.Exists(node.Value))
                                ParentPaths.Add(node.Value);
                        }
                    }
                }

                result.AddRange(ParentPaths);
            }

            return result.Distinct(Utility.Utility.ClientFilenameStringComparer).ToList();
        }
    }
}