using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;

using System.Globalization;

namespace Duplicati.Library.Snapshots
{

    //Original code: HVBackup Project
    public class HyperVUtility
    {
        private string _wmiQuery;
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
                            _hyperVMachines.Add((string)mObject[_vmIdField], (string)mObject["ElementName"]);

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
#region Wmi helpers
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
#endregion Wmi helpers






#region Recovery Virtual Machines
        public void CreateHyperVMachine()
        {
            ManagementObject sysMan = GetMsVM_VirtualSystemManagementService("MsVM_VirtualSystemManagementService", null);
        }
        
        /// <summary>
        /// Please refer to https://blogs.msdn.microsoft.com/sergeim/2008/06/03/prepare-vm-create-vm-programmatically-hyper-v-api-c-version/
        /// </summary>
        /// <param name="className"></param>
        /// <param name="where"></param>
        /// <returns></returns>
        private ManagementObject GetMsVM_VirtualSystemManagementService(string className, string where)
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

        /// <summary>
        /// Please refer to https://blogs.msdn.microsoft.com/sergeim/2008/06/03/prepare-vm-create-vm-programmatically-hyper-v-api-c-version/
        /// </summary>
        /// <param name="className"></param>
        /// <param name="where"></param>
        /// <returns></returns>
        public void CreateVm(string displayName)
        {
            string notes = "Created" + DateTime.Now;

            ManagementObject sysMan = GetMsVM_VirtualSystemManagementService();
            // Create VM with empty settings
            ManagementBaseObject definition = sysMan.InvokeMethod(Constants.DefineVirtualSystem, sysMan.GetMethodParameters(Constants.DefineVirtualSystem), null); //Empty set
            uint retCode = (uint)definition["returnvalue"];

            if (retCode != Constants.ERROR_SUCCESS)
                throw new InvalidOperationException("DefineVirtualSystem failed");
            
            // Obtain WMI root\virtualization:ComputerSystem object.
            // we will need "Name" of it, which is GUID
            
            string vmPath = definition["DefinedSystem"] as string;
            ManagementObject computerSystemTemplate = new ManagementObject(vmPath);
            string vmName = (string)computerSystemTemplate["name"];

            // this is GUID; will need to locate settings for this VM
            ManagementObject settings = GetMsvm_VirtualSystemSettingData(vmName);
            
            // Now, set settings of this MSVM_ComputerSystem as we like

            settings["elementname"] = displayName;
            settings["notes"] = notes;
            settings["BIOSGUID"] = new Guid();
            settings["BIOSSerialNumber"] = "1234567890";
            settings["BIOSNumLock"] = "true";

            // settings["…"] = …;
            // … set whatever you like; see list at
            //     http://msdn.microsoft.com/en-us/library/cc136944(VS.85).aspx

            settings.Put();
            
            // Now, set the settings which were build above to newly created ComputerSystem
            ManagementBaseObject inParams = sysMan.GetMethodParameters(Constants.ModifyVirtualSystem);
            string settingsText = settings.GetText(TextFormat.WmiDtd20);
            inParams["ComputerSystem"] = computerSystemTemplate;
            inParams["SystemSettingData"] = settingsText;
            ManagementBaseObject resultToCheck = sysMan.InvokeMethod(Constants.ModifyVirtualSystem, inParams, null);
            
            // Almost done – now apply the settings to newly created ComputerSystem
            ManagementObject settingsAsSet = (ManagementObject)resultToCheck["ModifiedSettingData"];
            
            // Optionally print settingsAsSet here
            
            Log(string.Format("Created: VM with name {0} and GUID name {1}", displayName, vmName));

        } // CreateVm

        private ManagementObject GetMsVM_VirtualSystemManagementService()
        {
            return GetWmiObject("MsVM_VirtualSystemManagementService", null);
        }

        private ManagementObject GetMsvm_VirtualSystemSettingData(string vmName)
        {
            return GetWmiObject("Msvm_VirtualSystemSettingData", string.Format("systemname = '{0}'", vmName));
        }

        #region Wmi Helpers

        private ManagementObject GetWmiObject(string classname, string where)
        {
            ManagementObjectCollection resultset = GetWmiObjects(classname, where);

            if (resultset.Count != 1)
                throw new InvalidOperationException(string.Format("Cannot locate {0} where {1}", classname, where));

            ManagementObjectCollection.ManagementObjectEnumerator en = resultset.GetEnumerator();
            en.MoveNext();
            ManagementObject result = en.Current as ManagementObject;

            if (result == null) throw new InvalidOperationException("Failure retrieving " +classname + " where " +where);

            return result;
        }

        private ManagementObjectCollection GetWmiObjects(string className, string where)
        {
            string query;
            ManagementScope scope = new ManagementScope(@"root\virtualization", null);
            if (where != null)
            {
                query = string.Format("select * from {0} where {1}", className, where);
            } else {
                query = string.Format("select * from {0}", className);
            }

            ManagementObjectSearcher searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
            ManagementObjectCollection resultset = searcher.Get();
            return resultset;
        }
        #endregion Wmi helpers

        private static void Log(string message, params object[] data)
        {
            Console.WriteLine(message, data);
        }

        private static void Oops(object sender, UnhandledExceptionEventArgs e)
        {
            Console.BackgroundColor = ConsoleColor.White;
            Console.ForegroundColor = ConsoleColor.Black;
            Exception ex = e.ExceptionObject as Exception;
            Log(ex.Message);
            Console.ResetColor();
            Log(ex.ToString());
        }

    } // class MainCreateVm

    class Constants
    {
        internal const string DefineVirtualSystem = "DefineVirtualSystem";
        internal const string ModifyVirtualSystem = "ModifyVirtualSystem";
        internal const uint ERROR_SUCCESS = 0;
        internal const uint ERROR_INV_ARGUMENTS = 87;
    }
}

