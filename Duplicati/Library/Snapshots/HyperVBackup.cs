using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Text;

namespace Duplicati.Library.Snapshots
{

    //Original code: HVBackup Project
    public class HyperVBackup
    {
        private string _wmiQuery;
        private readonly string _wmiScope;
        private readonly string _vmIdField;
        private readonly string _wmiHost = "localhost"; //We could backup remote Hyper-V Machines in the future
        private readonly bool _wmiv2Namespace;

        public List<string> RequestedVmNames = new List<string>();
        readonly IDictionary<string, string> _hyperVMachines = new Dictionary<string, string>();

        public HyperVBackup()
        {
            //Set the namespace depending off host OS
            _wmiv2Namespace = Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 2;
            //Set the scope to use in MWI. V2 for Server 2012 or newer.
            _wmiScope = _wmiv2Namespace ? $"\\\\{_wmiHost}\\root\\virtualization\\v2"
                : $"\\\\{_wmiHost}\\root\\virtualization";
            //Set the VM ID Selector Field for the WMI Query
            _vmIdField = _wmiv2Namespace ? "VirtualSystemIdentifier" : "SystemName";

        }

        public HyperVBackup(List<string> reqVmNames)
        {
            _wmiv2Namespace = Environment.OSVersion.Version.Major >= 6 && Environment.OSVersion.Version.Minor >= 2;
            _wmiScope = _wmiv2Namespace ? $"\\\\{_wmiHost}\\root\\virtualization\\v2"
                : $"\\\\{_wmiHost}\\root\\virtualization";
            _vmIdField = _wmiv2Namespace ? "VirtualSystemIdentifier" : "SystemName";
            //Store requested VM's
            RequestedVmNames = reqVmNames;
        }

        public IDictionary<string, string> GetHyperVMachines()
        {
            //Set the WMI Query according the WMI Namespace
            SetWmiQueryVmidField();
            //We MOSearcher to search for the Hyper-V Machines
            using (ManagementObjectSearcher moSearcher = new ManagementObjectSearcher(new ManagementScope(_wmiScope), new ObjectQuery(_wmiQuery)))
            {
                using (var moCollection = moSearcher.Get())
                    foreach (var mObject in moCollection)
                        using (mObject)
                            _hyperVMachines.Add((string)mObject[_vmIdField], (string)mObject["ElementName"]);
            }

            return _hyperVMachines;
        }

        private void SetWmiQueryVmidField()
        {
            _wmiQuery = _wmiv2Namespace ? "SELECT VirtualSystemIdentifier, ElementName FROM Msvm_VirtualSystemSettingData WHERE VirtualSystemType = 'Microsoft:Hyper-V:System:Realized'" 
                : "SELECT SystemName, ElementName FROM Msvm_VirtualSystemSettingData WHERE SettingType = 3";

            //Temporary Stringbuilder to build up the VM selectors
            var sb = new StringBuilder();

            if (RequestedVmNames == null || !RequestedVmNames.Any()) return;
            foreach (var vmName in RequestedVmNames)
            {
                if (sb.Length > 0)
                    sb.Append(" OR ");
                sb.Append($"ElementName = \'{vmName.Replace("'", "''")}\'");
            }
            _wmiQuery += $" AND ({sb})";
        }
        //private string GetWMIScope(string host = "localhost")
        //{
        //    //string scopeFormatStr;
        //    //if (IsNameSpaceWMIV2)
        //    //    scopeFormatStr = "\\\\{0}\\root\\virtualization\\v2";
        //    //else
        //    //    scopeFormatStr = "\\\\{0}\\root\\virtualization";
        //    //return (string.Format(scopeFormatStr, host));
        //}
    }
}
