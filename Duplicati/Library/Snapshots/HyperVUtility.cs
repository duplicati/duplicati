using Alphaleonis.Win32.Vss;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;

namespace Duplicati.Library.Snapshots
{
    public class HyperVGuest : IEquatable<HyperVGuest>
    {
        public string Name { get; }
        public Guid ID { get; }
        public List<string> DataPaths { get; }

        public HyperVGuest(string Name, Guid ID, List<string> DataPaths)
        {
            this.Name = Name;
            this.ID = ID;
            this.DataPaths = DataPaths;
        }

        bool IEquatable<HyperVGuest>.Equals(HyperVGuest other)
        {
            return ID.Equals(other.ID);
        }

        public override int GetHashCode()
        {
            return ID.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            HyperVGuest guest = obj as HyperVGuest;
            if (guest != null)
            {
                return Equals(guest);
            }
            else
            {
                return false;
            }
        }

        public static bool operator ==(HyperVGuest guest1, HyperVGuest guest2)
        {
            if (object.ReferenceEquals(guest1, guest2)) return true;
            if (object.ReferenceEquals(guest1, null)) return false;
            if (object.ReferenceEquals(guest2, null)) return false;

            return guest1.Equals(guest2);
        }

        public static bool operator !=(HyperVGuest guest1, HyperVGuest guest2)
        {
            if (object.ReferenceEquals(guest1, guest2)) return false;
            if (object.ReferenceEquals(guest1, null)) return true;
            if (object.ReferenceEquals(guest2, null)) return true;

            return !guest1.Equals(guest2);
        }
    }

    public class HyperVUtility
    {
        private readonly ManagementScope _wmiScope;
        private readonly string _vmIdField;
        private readonly string _wmiHost = "localhost";
        private readonly bool _wmiv2Namespace;
        /// <summary>
        /// The Hyper-V VSS Writer Guid
        /// </summary>
        public static readonly Guid HyperVWriterGuid = new Guid("66841cd4-6ded-4f4b-8f17-fd23f8ddc3de");
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
        public List<HyperVGuest> Guests { get { return m_Guests; } }
        private List<HyperVGuest> m_Guests;

        public HyperVUtility()
        {
            m_Guests = new List<HyperVGuest>();

            if (!Utility.Utility.IsClientWindows)
            {
                IsHyperVInstalled = false;
                IsVSSWriterSupported = false;
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

            IsVSSWriterSupported = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem")
                    .Get().OfType<ManagementObject>()
                    .Select(o => (uint)o.GetPropertyValue("ProductType")).First() != 1;

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
        /// Query Hyper-V for all Virtual Machines info
        /// </summary>
        /// <param name="bIncludePaths">Specify if returned data should contain VM paths</param>
        /// <returns>List of Hyper-V Machines</returns>
        public void QueryHyperVGuestsInfo(bool bIncludePaths = false)
        {
            if (!IsHyperVInstalled)
                return;

            m_Guests.Clear();
            var wmiQuery = _wmiv2Namespace
                ? "SELECT * FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'"
                : "SELECT * FROM Msvm_VirtualSystemSettingData WHERE SettingType = 3";

            if (IsVSSWriterSupported)
                using (var moCollection = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(wmiQuery)).Get())
                    foreach (var mObject in moCollection)
                        m_Guests.Add(new HyperVGuest((string)mObject["ElementName"], new Guid((string)mObject[_vmIdField]), bIncludePaths ? GetAllVMsPathsVSS()[(string)mObject[_vmIdField]] : null));
            else
                using (var moCollection = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(wmiQuery)).Get())
                    foreach (var mObject in moCollection)
                        m_Guests.Add(new HyperVGuest((string)mObject["ElementName"], new Guid((string)mObject[_vmIdField]), bIncludePaths ?
                            GetVMVhdPathsWMI((string)mObject[_vmIdField])
                                .Union(GetVMConfigPathsWMI((string)mObject[_vmIdField]))
                                .Distinct(Utility.Utility.ClientFilenameStringComparer)
                                .OrderBy(a => a).ToList() : null));
        }

        /// <summary>
        /// For all Hyper-V guests it enumerate all associated paths using VSS data
        /// </summary>
        /// <returns>A collection of VMs and paths</returns>
        private Dictionary<string, List<string>> GetAllVMsPathsVSS()
        {
            var ret = new Dictionary<string, List<string>>();

            //Substitute for calling VssUtils.LoadImplementation(), as we have the dlls outside the GAC
            string alphadir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "alphavss");
            string alphadll = Path.Combine(alphadir, VssUtils.GetPlatformSpecificAssemblyShortName() + ".dll");
            IVssImplementation vss = (IVssImplementation)System.Reflection.Assembly.LoadFile(alphadll).CreateInstance("Alphaleonis.Win32.Vss.VssImplementation");

            using (var m_backup = vss.CreateVssBackupComponents())
            {
                m_backup.InitializeForBackup(null);
                m_backup.SetContext(VssSnapshotContext.Backup);
                m_backup.SetBackupState(false, true, VssBackupType.Full, false);
                m_backup.EnableWriterClasses(new Guid[] { HyperVWriterGuid });

                try
                {
                    m_backup.GatherWriterMetadata();
                    var writerMetaData = m_backup.WriterMetadata.FirstOrDefault(o => o.WriterId.Equals(HyperVWriterGuid));

                    if (writerMetaData == null)
                        throw new Duplicati.Library.Interface.UserInformationException("Microsoft Hyper-V VSS Writer not found - cannot backup Hyper-V machines.");

                    foreach (var component in writerMetaData.Components)
                    {
                        var paths = new List<string>();

                        foreach (var file in component.Files)
                            if (file.FileSpecification.Contains("*"))
                            {
                                if (Directory.Exists(Utility.Utility.AppendDirSeparator(file.Path)))
                                    paths.Add(Utility.Utility.AppendDirSeparator(file.Path));
                            }
                            else
                            {
                                if (File.Exists(Path.Combine(file.Path, file.FileSpecification)))
                                    paths.Add(Path.Combine(file.Path, file.FileSpecification));
                            }

                        ret.Add(component.ComponentName, paths.Distinct(Utility.Utility.ClientFilenameStringComparer).OrderBy(a => a).ToList());
                    }
                }
                finally
                {
                    m_backup.FreeWriterMetadata();
                }
            }

            return ret;
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

            return result;
        }
    }
}