using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
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
            internal const string MsVM_VHDI = "MsVM_VirtualHardDiskInfo";
            internal const string MsVM_IMS = "MsVM_ImageManagementService";
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
            ManagementObjectCollection moCollection = GetWmiObjects(WmiQueryHyperVMachines());
            foreach (ManagementBaseObject mObject in moCollection)
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
            string wmiQuery = _wmiv2Namespace
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
            ManagementObjectSearcher moSearcher = new ManagementObjectSearcher(_wmiScope, new ObjectQuery(query));
            ManagementObjectCollection resultset = moSearcher.Get();
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
            var query = @where != null
                ? $"select * from {className} where {@where}"
                : $"select * from {className}";

            ManagementObjectCollection resultset = GetWmiObjects(query);

            if (resultset.Count != 1)
                throw new InvalidOperationException(string.Format("Cannot locate {0} where {1}", className, where));

            try
            {
                ManagementObjectCollection.ManagementObjectEnumerator en = resultset.GetEnumerator();
                en.MoveNext();
                return en.Current as ManagementObject;
            }
            catch (Exception)
            {
                throw new InvalidOperationException(string.Format("Failure retrieving {0} where {1}", className, where));
            }
        }
        #endregion





#region Recovery Virtual Machines

        /// <summary>
        /// Please refer to https://blogs.msdn.microsoft.com/sergeim/2008/06/03/prepare-vm-create-vm-programmatically-hyper-v-api-c-version/
        /// Creation of a Hyper-V Machine
        /// </summary>
        /// <param name="vmDisplayName"></param>
        /// <param name="vmNotes"></param>
        public void CreateHyperVMachine(string vmDisplayName, string vmNotes)
        {
            try
            {
                ManagementObject sysManService = GetMsVMObject(WmiClasses.MsVM_VSMS, null);

                //Defining a VM with empty settings
                ManagementBaseObject hyperVM = sysManService.InvokeMethod(Methods.DefineVirtualSystem,
                    sysManService.GetMethodParameters(Methods.DefineVirtualSystem), null);

                if ((uint) hyperVM["ReturnValue"] != ReturnCode.ERROR_SUCCESS)
                    throw new InvalidOperationException(
                        $"DefineVirtualSystem failed. ReturnValue: {(uint) hyperVM["ReturnValue"]}");

                ManagementObject hyperVMTemplate = new ManagementObject((string) hyperVM["DefinedSystem"]);

                // this is GUID; will need to locate settings for this VM and edit the settings
                ManagementObject hyperVMSettings = GetMsVMObject(WmiClasses.MsVM_VSSD,
                    string.Format("systemname = '{0}'", (string) hyperVMTemplate["name"]));
                hyperVMSettings["elementname"] = vmDisplayName;
                hyperVMSettings["notes"] = vmNotes;
                hyperVMSettings["BIOSGUID"] = new Guid();
                hyperVMSettings["BIOSNumLock"] = "true";
                hyperVMSettings["Description"] = "Hyper-V Machine restored from machine xxxx by Duplicati";
                hyperVMSettings.Put();

                ManagementBaseObject hyperVMParams = sysManService.GetMethodParameters(Methods.ModifyVirtualSystem);
                string settingsText = hyperVMSettings.GetText(TextFormat.WmiDtd20);
                hyperVMParams["ComputerSystem"] = hyperVMTemplate;
                hyperVMParams["SystemSettingData"] = settingsText;
                sysManService.InvokeMethod(Methods.ModifyVirtualSystem, hyperVMParams, null);
            }
            catch (Exception exception)
            {
                Console.WriteLine("Failed to create Hyper-V machine");
                throw new Exception(exception.ToString());
            }
        }

        #endregion

            #region Merging VHD

        public void MergeVhd()
        {
            var vhdPath = GetVhdPathFromXml(GetXml("C:\\Users\\Administrator\\Desktop\\TESTMerge\\Snapshots\\TESTTS-VDP2-CLNT\\Virtual Machines\\75201246-AD9D-4D6F-8788-B2EF77EF6A2A.xml"));

            List<string> vhdPaths = GetVhdParentPaths(vhdPath);

            ManagementObject imgMan = GetMsVMObject(WmiClasses.MsVM_IMS, null);
            ManagementBaseObject inParams = imgMan.GetMethodParameters(Methods.MergeVirtualHardDisk);
            inParams["DestinationPath"] = vhdPaths.Last();
            inParams["SourcePath"] = vhdPaths.First();
            ManagementBaseObject outParams = imgMan.InvokeMethod(Methods.MergeVirtualHardDisk, inParams, null);
            if (outParams != null && (uint)outParams["ReturnValue"] == ReturnCode.ERROR_JOBSTARTED)
            {
                var result = JobCompleted(outParams, _wmiScope)
                    ? $"{inParams["SourcePath"]} was merged successfully."
                    : $"{inParams["SourcePath"]} failed merging.";

                Console.WriteLine(result);
            }
        }
        public static bool JobCompleted(ManagementBaseObject outParams, ManagementScope scope)
        {
            bool jobCompleted = true;
            
            //Retrieve msvc_StorageJob path. This is a full wmi path
            string JobPath = (string)outParams["Job"];
            ManagementObject Job = new ManagementObject(scope, new ManagementPath(JobPath), null);
            //Try to get storage job information
            Job.Get();
            while ((UInt16)Job["JobState"] == JobState.Starting
                || (UInt16)Job["JobState"] == JobState.Running)
            {
                Console.WriteLine("In progress... {0}% completed.", Job["PercentComplete"]);
                System.Threading.Thread.Sleep(1000);
                Job.Get();
            }

            //Figure out if job failed
            UInt16 jobState = (UInt16)Job["JobState"];
            if (jobState != JobState.Completed)
            {
                UInt16 jobErrorCode = (UInt16)Job["ErrorCode"];
                Console.WriteLine("Error Code:{0}", jobErrorCode);
                Console.WriteLine("ErrorDescription: {0}", (string)Job["ErrorDescription"]);
                jobCompleted = false;
            }
            return jobCompleted;
        }

        private List<string> GetVhdParentPaths(string vhdPath)
        {
            List<string> ParentPaths = new List<string>() {vhdPath};
            ManagementObject imgMan = GetMsVMObject(WmiClasses.MsVM_IMS, null);
            ManagementBaseObject inParams = imgMan.GetMethodParameters(Methods.GetVirtualHardDiskInfo);
            inParams["Path"] = vhdPath;

            while (true)
            { 
                ManagementBaseObject outParams = imgMan.InvokeMethod(Methods.GetVirtualHardDiskInfo, inParams, null);

                if (outParams != null && (uint)outParams["ReturnValue"] != ReturnCode.ERROR_SUCCESS)
                    throw new InvalidOperationException($"GetVirtualHardDiskInfo failed. ReturnValue: {(uint)outParams["ReturnValue"]}");

                string xpath = "//PROPERTY[@NAME = 'ParentPath']/VALUE/child::text()";
                XmlDocument doc = new XmlDocument();
                if (outParams != null) doc.LoadXml((string)outParams["Info"]);
                XmlNode node = doc.SelectSingleNode(xpath);
                if (node != null)
                {
                    ParentPaths.Add(node.Value);
                    inParams["Path"] = ParentPaths[ParentPaths.Count - 1];
                    Console.WriteLine(node.Value);
                } else break;
            }

            imgMan.Dispose();
            inParams.Dispose();
            return ParentPaths;
        }

        #endregion

        #region XML Reader Helpers
        /// <summary>
        /// Copies the xml to a temp folder so that we can load the xml
        /// </summary>
        /// <param name="filepath"></param>
        /// <returns></returns>
        private XDocument GetXml(string filepath)
        {
            string tempFilepath = $"{Path.GetTempPath()}{Path.GetFileName(filepath)}";
            File.Copy(filepath, tempFilepath);
            XDocument xmlDocument = XDocument.Load(tempFilepath);
            File.Delete(tempFilepath);
            return xmlDocument;
        }

        /// <summary>
        /// This will retrieve the respected active VHD of the machine
        /// </summary>
        /// <param name="xmlDocument"></param>
        /// <returns></returns>
        private string GetVhdPathFromXml(XDocument xmlDocument)
        {
            return
                xmlDocument.Descendants("type")
                    .Where(x => x.Value == "VHD")
                    .Select(x => x.Parent)
                    .FirstOrDefault()
                    .Element("pathname")
                    .Value;
        }

#endregion
    }
}

