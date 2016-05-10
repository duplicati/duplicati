using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;

namespace Duplicati.Library.Snapshots
{

    //Original code: HVBackup Project
    public class HyperVUtility
    {
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
                ? string.Format("select * from {0} where {1}", className, where)
                : string.Format("select * from {0}", className);

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
                ManagementObject sysManService = GetMsVMObject(Constants.MsVM_VSMS, null);

                //Defining a VM with empty settings
                ManagementBaseObject hyperVM = sysManService.InvokeMethod(Constants.DefineVirtualSystem,
                    sysManService.GetMethodParameters(Constants.DefineVirtualSystem), null);

                if ((uint)hyperVM["returnvalue"] != Constants.ERROR_SUCCESS)
                    throw new InvalidOperationException("DefineVirtualSystem failed");

                ManagementObject hyperVMTemplate = new ManagementObject((string)hyperVM["DefinedSystem"]);

                // this is GUID; will need to locate settings for this VM and edit the settings
                ManagementObject hyperVMSettings = GetMsVMObject(Constants.MsVM_VSSD, string.Format("systemname = '{0}'", (string) hyperVMTemplate["name"]));
                hyperVMSettings["elementname"] = vmDisplayName;
                hyperVMSettings["notes"] = vmNotes;
                hyperVMSettings["BIOSGUID"] = new Guid();
                hyperVMSettings["BIOSNumLock"] = "true";
                hyperVMSettings["Description"] = "Hyper-V Machine restored from machine xxxx by Duplicati";
                hyperVMSettings.Put();

                ManagementBaseObject hyperVMParams = sysManService.GetMethodParameters(Constants.ModifyVirtualSystem);
                string settingsText = hyperVMSettings.GetText(TextFormat.WmiDtd20);
                hyperVMParams["ComputerSystem"] = hyperVMTemplate;
                hyperVMParams["SystemSettingData"] = settingsText;
                sysManService.InvokeMethod(Constants.ModifyVirtualSystem, hyperVMParams, null);
            }
            catch (Exception exception)
            {
                Console.WriteLine("Failed to create Hyper-V machine");
                throw new Exception(exception.ToString());
            }
        }

        class Constants
        {
            internal const string DefineVirtualSystem = "DefineVirtualSystem";
            internal const string ModifyVirtualSystem = "ModifyVirtualSystem";
            internal const string MsVM_VSMS = "MsVM_VirtualSystemManagementService";
            internal const string MsVM_VSSD = "MsVM_VirtualSystemSettingData";
            internal const uint ERROR_SUCCESS = 0;
        }
        #endregion 
    }
}

