using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace Duplicati.Library.Snapshots
{

    //Original code: HVBackup Project
    public class HyperVUtility
    {
        private static class WmiClasses
        {
            internal const string MsVM_VSMS = "MsVM_VirtualSystemManagementService";
            internal const string MsVM_VSSD = "MsVM_VirtualSystemSettingData";
            internal const string MsVM_IMS = "MsVM_ImageManagementService";
            internal const string MsVM_RASD = "MsVM_ResourceAllocationSettingData";
        }

        private static class Methods
        {
            internal const string DefineVirtualSystem = "DefineVirtualSystem";
            internal const string ModifyVirtualSystem = "ModifyVirtualSystem";
            internal const string GetVirtualHardDiskInfo = "GetVirtualHardDiskInfo";
            internal const string MergeVirtualHardDisk = "MergeVirtualHardDisk";
        }

        private static class ReturnCode
        {
            internal const UInt16 ERROR_SUCCESS = 0;
            internal const UInt16 ERROR_JOBSTARTED = 4096;
        }

        private static class JobState
        {
            internal const UInt16 Starting = 3;
            internal const UInt16 Running = 4;
            internal const UInt16 Completed = 7;
        }

        private readonly ManagementScope _wmiScope;
        private readonly string _vmIdField;
        private readonly string _wmiHost = "localhost"; //We could backup remote Hyper-V Machines in the future
        private readonly bool _wmiv2Namespace;

        public List<string> RequestedVmNames = new List<string>();
        readonly IDictionary<string, string> _hyperVMachines = new Dictionary<string, string>();

        public HyperVUtility()
        {
            //Set the namespace depending off host OS
            _wmiv2Namespace = Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 2;
            //Set the scope to use in MWI. V2 for Server 2012 or newer.
            _wmiScope = _wmiv2Namespace
                ? new ManagementScope(string.Format("\\\\{0}\\root\\virtualization\\v2", _wmiHost))
                : new ManagementScope(string.Format("\\\\{0}\\root\\virtualization", _wmiHost));
            //Set the VM ID Selector Field for the WMI Query
            _vmIdField = _wmiv2Namespace ? "VirtualSystemIdentifier" : "SystemName";

        }

        public HyperVUtility(List<string> reqVmNames)
        {
            _wmiv2Namespace = Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 2;
            _wmiScope = _wmiv2Namespace
                ? new ManagementScope(string.Format("\\\\{0}\\root\\virtualization\\v2", _wmiHost))
                : new ManagementScope(string.Format("\\\\{0}\\root\\virtualization", _wmiHost));
            _vmIdField = _wmiv2Namespace ? "VirtualSystemIdentifier" : "SystemName";
            //Store requested VM's
            RequestedVmNames = reqVmNames;
        }

        /// <summary>
        /// We query the Hyper-V for all requested Virtual Machines
        /// </summary>
        /// <returns>List of Hyper-V Machines</returns>
        public IDictionary<string, string> GetHyperVMachines()
        {
            var moCollection = GetWmiObjects(WmiQueryHyperVMachines());
            foreach (var mObject in moCollection)
                using (mObject)
                    _hyperVMachines.Add((string) mObject[_vmIdField], (string) mObject["ElementName"]);

            return _hyperVMachines;
        }

        /// <summary>
        /// We build up the wmi-query to retrieve all Virtual Machines
        /// </summary>
        /// <returns>WMI query</returns>
        private string WmiQueryHyperVMachines()
        {
            var wmiQuery = _wmiv2Namespace
                ? "SELECT VirtualSystemIdentifier, ElementName FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'"
                : "SELECT SystemName, ElementName FROM Msvm_VirtualSystemSettingData WHERE SettingType = 3";

            //Temporary Stringbuilder to build up the VM selectors
            var sb = new StringBuilder();

            if (RequestedVmNames == null || !RequestedVmNames.Any()) return null;
            foreach (var vmName in RequestedVmNames)
            {
                if (sb.Length > 0)
                    sb.Append(" OR ");
                sb.Append(string.Format("ElementName = \'{0}\'", vmName.Replace("'", "''")));
            }
            wmiQuery += string.Format(" AND ({0})", sb);

            return wmiQuery;
        }

        #region Management Object helpers
        /// <summary>
        /// Executes the WMI Query
        /// </summary>
        /// <param name="query"></param>
        /// <returns>A collection of results</returns>
        private ManagementObjectCollection GetWmiObjects(string query)
        {
            var moSearcher = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(query));
            var resultset = moSearcher.Get();
            return resultset;
        }

        /// <summary>
        /// Queries and gets a Management Object
        /// </summary>
        /// <param name="className"></param>
        /// <param name="where"></param>
        /// <returns></returns>
        private ManagementObject GetMsVMObject(string className, string where)
        {
            var query = where != null
                ? string.Format("select * from {0} where {1}", className, where)
                : string.Format("select * from {0}", className);

            var resultset = GetWmiObjects(query);

            if (resultset.Count != 1)
                throw new InvalidOperationException(string.Format("Cannot locate {0} where {1}", className, where));

            try
            {
                foreach (var instance in resultset)
                    return instance as ManagementObject;
                return null;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(string.Format("Failure retrieving {0} where {1}", className, where), ex);
            }
        }

        private ManagementObject GetTargetVmObject(string vmElementName)
        {
            var query = string.Format("select * from Msvm_ComputerSystem where ElementName = '{0}'", vmElementName);
            var computers = GetWmiObjects(query);

            foreach (var instance in computers)
                return (ManagementObject)instance;

            return null;
        }

        # endregion Management Object helpers



        #region Recovery Virtual Machines

        /// <summary>
        /// Please refer to https://blogs.msdn.microsoft.com/sergeim/2008/06/03/prepare-vm-create-vm-programmatically-hyper-v-api-c-version/
        /// Creation of a Hyper-V Machine
        /// </summary>
        /// <param name="vmDisplayName"></param>
        /// <param name="vmNotes"></param>
        public void CreateHyperVMachine(string vmDisplayName, string vmNotes)
        {
            var sysManService = GetMsVMObject(WmiClasses.MsVM_VSMS, null);

            //Defining a VM with empty settings
            var hyperVM = sysManService.InvokeMethod(Methods.DefineVirtualSystem,
                sysManService.GetMethodParameters(Methods.DefineVirtualSystem), null);

            if ((uint) hyperVM["ReturnValue"] != ReturnCode.ERROR_SUCCESS)
                throw new InvalidOperationException(
                    $"DefineVirtualSystem failed. ReturnValue: {(uint) hyperVM["ReturnValue"]}");

            var hyperVMTemplate = new ManagementObject((string) hyperVM["DefinedSystem"]);

            // this is GUID; will need to locate settings for this VM and edit the settings
            var hyperVMSettings = GetMsVMObject(WmiClasses.MsVM_VSSD,
                string.Format("systemname = '{0}'", (string) hyperVMTemplate["name"]));
            hyperVMSettings["elementname"] = vmDisplayName;
            hyperVMSettings["notes"] = vmNotes;
            hyperVMSettings["BIOSGUID"] = new Guid();
            hyperVMSettings["BIOSNumLock"] = "true";
            hyperVMSettings["Description"] = "Hyper-V Machine restored from machine xxxx by Duplicati";
            hyperVMSettings.Put();

            var hyperVMParams = sysManService.GetMethodParameters(Methods.ModifyVirtualSystem);
            var settingsText = hyperVMSettings.GetText(TextFormat.WmiDtd20);
            hyperVMParams["ComputerSystem"] = hyperVMTemplate;
            hyperVMParams["SystemSettingData"] = settingsText;
            sysManService.InvokeMethod(Methods.ModifyVirtualSystem, hyperVMParams, null);
        }

        #endregion

        #region Merging VHD
        public void MergeVhd(List<string> VmNames)
        {
            var vhdPaths = VmNames.SelectMany(x => GetAllVmVhdPaths(x)).ToArray();

            var imgMan = GetMsVMObject(WmiClasses.MsVM_IMS, null);
            var inParams = imgMan.GetMethodParameters(Methods.MergeVirtualHardDisk);
            inParams["DestinationPath"] = vhdPaths.First();
            inParams["SourcePath"] = vhdPaths.Last();
            var outParams = imgMan.InvokeMethod(Methods.MergeVirtualHardDisk, inParams, null);
            if (outParams != null && (uint)outParams["ReturnValue"] == ReturnCode.ERROR_JOBSTARTED)
            {
                JobCompleted(outParams, _wmiScope);
                /*var result = JobCompleted(outParams, _wmiScope)
                    ? $"{inParams["SourcePath"]} was merged successfully."
                    : $"{inParams["SourcePath"]} failed merging.";
                */
            }

            //Remove the snapshots since they are no longer valid.
            RemoveSnapshotTree();
        }

        private void RemoveSnapshotTree()
        {
            GetTargetVmObject("merge");
        }

        public static bool JobCompleted(ManagementBaseObject outParams, ManagementScope scope)
        {
            bool jobCompleted = true;
            
            //Retrieve msvc_StorageJob path. This is a full wmi path
            var JobPath = (string)outParams["Job"];
            var Job = new ManagementObject(scope, new ManagementPath(JobPath), null);
            //Try to get storage job information
            Job.Get();
            while ((UInt16)Job["JobState"] == JobState.Starting
                || (UInt16)Job["JobState"] == JobState.Running)
            {
                Logging.Log.WriteMessage(string.Format("HyperV in progress... {0}% completed.", Job["PercentComplete"]), Logging.LogMessageType.Information);
                System.Threading.Thread.Sleep(1000);
                Job.Get();
            }

            //Figure out if job failed
            var jobState = (UInt16)Job["JobState"];
            if (jobState != JobState.Completed)
            {
                var jobErrorCode = (UInt16)Job["ErrorCode"];
                Logging.Log.WriteMessage(string.Format("HyperV Error, code: {0}, message: {1}", jobErrorCode, Job["ErrorDescription"]), Logging.LogMessageType.Error);
                jobCompleted = false;
            }
            return jobCompleted;
        }

        private List<string> GetAllVmVhdPaths(string vmName)
        {

            var ParentPaths = new List<string>();
            var vm = GetTargetVmObject(vmName);
            var imgMan = GetMsVMObject(WmiClasses.MsVM_IMS, null);

            foreach (var sysSettings in vm.GetRelated(WmiClasses.MsVM_VSSD))
            {
                var systemObjCollection = ((ManagementObject)sysSettings).GetRelated(WmiClasses.MsVM_RASD);
                var tempvhd = from ManagementBaseObject systemBaseObj in systemObjCollection
                    where ((UInt16) systemBaseObj["ResourceType"] == 21
                            && (string) systemBaseObj["ResourceSubType"] == "Microsoft Virtual Hard Disk")
                    select systemBaseObj;
                ParentPaths.Add(((IEnumerable<object>) tempvhd.First()["Connection"]).First().ToString());
            }

            var result = new List<string>() {ParentPaths.First()};

            var inParams = imgMan.GetMethodParameters(Methods.GetVirtualHardDiskInfo);
            foreach (var vhdPath in ParentPaths)
            {
                inParams["Path"] = vhdPath;
                var outParams = imgMan.InvokeMethod(Methods.GetVirtualHardDiskInfo, inParams,
                    null);

                if (outParams != null)
                {
                    var doc = new XmlDocument();
                    doc.LoadXml((string) outParams["Info"]);
                    var node = doc.SelectSingleNode("//PROPERTY[@NAME = 'ParentPath']/VALUE/child::text()");
                    if (node == null)
                    {
                        if (result.Contains(vhdPath)) continue;
                        result.Insert(0, vhdPath);
                    }
                    if (!result.Contains(vhdPath))
                        result.Add(vhdPath);
                    if (result.Contains(vhdPath))
                        result.Insert(result.IndexOf(vhdPath), node.Value);
                }
            }
            return result;
        }

        #endregion
    }
}

