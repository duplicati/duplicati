// Copyright (C) 2025, The Duplicati Team
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
using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Duplicati.WindowsService
{
    [SupportedOSPlatform("windows")]
    public class ServiceInstaller
    {
        [DllImport("advapi32.dll", EntryPoint = "OpenSCManagerW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint dwAccess);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseServiceHandle(IntPtr hSCObject);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr OpenService(IntPtr hSCManager, string lpServiceName, uint dwDesiredAccess);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr CreateService(IntPtr hSCManager, string lpServiceName, string lpDisplayName, uint dwDesiredAccess, uint dwServiceType,
            uint dwStartType, uint dwErrorControl, string lpBinaryPathName, string lpLoadOrderGroup, string lpdwTagId, string lpDependencies, string lpServiceStartName,
            string lpPassword);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteService(IntPtr hService);

        private const int SERVICE_CONFIG_DESCRIPTION = 0x01;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ChangeServiceConfig2(IntPtr hService, int dwInfoLevel, [MarshalAs(UnmanagedType.Struct)] ref SERVICE_DESCRIPTION lpInfo);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SERVICE_DESCRIPTION
        {
            public string lpDescription;
        }

        private enum SC_STATUS_TYPE : int
        {
            SC_STATUS_PROCESS_INFO = 0
        }

        private const uint SERVICE_CONTROL_STOP = 0x1;
        private const uint SERVICE_CONTROL_CONTINUE = 0x3;
        private enum SERVICE_STATUS : uint
        {
            SERVICE_STOPPED = 0x00000001,
            SERVICE_START_PENDING = 0x00000002,
            SERVICE_STOP_PENDING = 0x00000003,
            SERVICE_RUNNING = 0x00000004,
            SERVICE_CONTINUE_PENDING = 0x00000005,
            SERVICE_PAUSE_PENDING = 0x00000006,
            SERVICE_PAUSED = 0x00000007,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SERVICE_STATUS_PROCESS
        {
            public uint dwServiceType;
            public uint dwCurrentState;
            public uint dwControlsAccepted;
            public uint dwWin32ExitCode;
            public uint dwServiceSpecificExitCode;
            public uint dwCheckPoint;
            public uint dwWaitHint;
            public uint dwProcessId;
            public uint dwServiceFlags;
        }


        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool StartServiceW(IntPtr hService, uint dwNumServiceArgs, string[] lpServiceArgVectors);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ControlService(IntPtr hService, uint dwControl, ref SERVICE_STATUS lpServiceStatus);

        [DllImport("advapi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryServiceStatusEx(IntPtr hService, SC_STATUS_TYPE lpServiceStatus, ref SERVICE_STATUS_PROCESS lpServiceStatusProcess, int cbBufSize, out uint pcbBytesNeeded);

        /// <summary>
        /// Access to the service. Before granting the requested access, the
        /// system checks the access token of the calling process.
        /// </summary>
        [Flags]
        private enum SERVICE_ACCESS : uint
        {
            /// <summary>
            /// Required to call the QueryServiceConfig and
            /// QueryServiceConfig2 functions to query the service configuration.
            /// </summary>
            SERVICE_QUERY_CONFIG = 0x00001,

            /// <summary>
            /// Required to call the ChangeServiceConfig or ChangeServiceConfig2 function
            /// to change the service configuration. Because this grants the caller
            /// the right to change the executable file that the system runs,
            /// it should be granted only to administrators.
            /// </summary>
            SERVICE_CHANGE_CONFIG = 0x00002,

            /// <summary>
            /// Required to call the QueryServiceStatusEx function to ask the service
            /// control manager about the status of the service.
            /// </summary>
            SERVICE_QUERY_STATUS = 0x00004,

            /// <summary>
            /// Required to call the EnumDependentServices function to enumerate all
            /// the services dependent on the service.
            /// </summary>
            SERVICE_ENUMERATE_DEPENDENTS = 0x00008,

            /// <summary>
            /// Required to call the StartService function to start the service.
            /// </summary>
            SERVICE_START = 0x00010,

            /// <summary>
            ///     Required to call the ControlService function to stop the service.
            /// </summary>
            SERVICE_STOP = 0x00020,

            /// <summary>
            /// Required to call the ControlService function to pause or continue
            /// the service.
            /// </summary>
            SERVICE_PAUSE_CONTINUE = 0x00040,

            /// <summary>
            /// Required to call the EnumDependentServices function to enumerate all
            /// the services dependent on the service.
            /// </summary>
            SERVICE_INTERROGATE = 0x00080,

            /// <summary>
            /// Required to call the ControlService function to specify a user-defined
            /// control code.
            /// </summary>
            SERVICE_USER_DEFINED_CONTROL = 0x00100,

            /// <summary>
            /// Includes STANDARD_RIGHTS_REQUIRED in addition to all access rights in this table.
            /// </summary>
            SERVICE_ALL_ACCESS = (ACCESS_MASK.STANDARD_RIGHTS_REQUIRED |
                SERVICE_QUERY_CONFIG |
                SERVICE_CHANGE_CONFIG |
                SERVICE_QUERY_STATUS |
                SERVICE_ENUMERATE_DEPENDENTS |
                SERVICE_START |
                SERVICE_STOP |
                SERVICE_PAUSE_CONTINUE |
                SERVICE_INTERROGATE |
                SERVICE_USER_DEFINED_CONTROL),

            GENERIC_READ = ACCESS_MASK.STANDARD_RIGHTS_READ |
                SERVICE_QUERY_CONFIG |
                SERVICE_QUERY_STATUS |
                SERVICE_INTERROGATE |
                SERVICE_ENUMERATE_DEPENDENTS,

            GENERIC_WRITE = ACCESS_MASK.STANDARD_RIGHTS_WRITE |
                SERVICE_CHANGE_CONFIG,

            GENERIC_EXECUTE = ACCESS_MASK.STANDARD_RIGHTS_EXECUTE |
                SERVICE_START |
                SERVICE_STOP |
                SERVICE_PAUSE_CONTINUE |
                SERVICE_USER_DEFINED_CONTROL,

            /// <summary>
            /// Required to call the QueryServiceObjectSecurity or
            /// SetServiceObjectSecurity function to access the SACL. The proper
            /// way to obtain this access is to enable the SE_SECURITY_NAME
            /// privilege in the caller's current access token, open the handle
            /// for ACCESS_SYSTEM_SECURITY access, and then disable the privilege.
            /// </summary>
            ACCESS_SYSTEM_SECURITY = ACCESS_MASK.ACCESS_SYSTEM_SECURITY,

            /// <summary>
            /// Required to call the DeleteService function to delete the service.
            /// </summary>
            DELETE = ACCESS_MASK.DELETE,

            /// <summary>
            /// Required to call the QueryServiceObjectSecurity function to query
            /// the security descriptor of the service object.
            /// </summary>
            READ_CONTROL = ACCESS_MASK.READ_CONTROL,

            /// <summary>
            /// Required to call the SetServiceObjectSecurity function to modify
            /// the Dacl member of the service object's security descriptor.
            /// </summary>
            WRITE_DAC = ACCESS_MASK.WRITE_DAC,

            /// <summary>
            /// Required to call the SetServiceObjectSecurity function to modify
            /// the Owner and Group members of the service object's security
            /// descriptor.
            /// </summary>
            WRITE_OWNER = ACCESS_MASK.WRITE_OWNER,
        }

        [Flags]
        private enum ACCESS_MASK : uint
        {
            DELETE = 0x00010000,
            READ_CONTROL = 0x00020000,
            WRITE_DAC = 0x00040000,
            WRITE_OWNER = 0x00080000,
            SYNCHRONIZE = 0x00100000,

            STANDARD_RIGHTS_REQUIRED = 0x000F0000,

            STANDARD_RIGHTS_READ = 0x00020000,
            STANDARD_RIGHTS_WRITE = 0x00020000,
            STANDARD_RIGHTS_EXECUTE = 0x00020000,

            STANDARD_RIGHTS_ALL = 0x001F0000,

            SPECIFIC_RIGHTS_ALL = 0x0000FFFF,

            ACCESS_SYSTEM_SECURITY = 0x01000000,

            MAXIMUM_ALLOWED = 0x02000000,

            GENERIC_READ = 0x80000000,
            GENERIC_WRITE = 0x40000000,
            GENERIC_EXECUTE = 0x20000000,
            GENERIC_ALL = 0x10000000,

            DESKTOP_READOBJECTS = 0x00000001,
            DESKTOP_CREATEWINDOW = 0x00000002,
            DESKTOP_CREATEMENU = 0x00000004,
            DESKTOP_HOOKCONTROL = 0x00000008,
            DESKTOP_JOURNALRECORD = 0x00000010,
            DESKTOP_JOURNALPLAYBACK = 0x00000020,
            DESKTOP_ENUMERATE = 0x00000040,
            DESKTOP_WRITEOBJECTS = 0x00000080,
            DESKTOP_SWITCHDESKTOP = 0x00000100,

            WINSTA_ENUMDESKTOPS = 0x00000001,
            WINSTA_READATTRIBUTES = 0x00000002,
            WINSTA_ACCESSCLIPBOARD = 0x00000004,
            WINSTA_CREATEDESKTOP = 0x00000008,
            WINSTA_WRITEATTRIBUTES = 0x00000010,
            WINSTA_ACCESSGLOBALATOMS = 0x00000020,
            WINSTA_EXITWINDOWS = 0x00000040,
            WINSTA_ENUMERATE = 0x00000100,
            WINSTA_READSCREEN = 0x00000200,

            WINSTA_ALL_ACCESS = 0x0000037F
        }

        /// <summary>
        /// Service types.
        /// </summary>
        [Flags]
        private enum SERVICE_TYPE : uint
        {
            /// <summary>
            /// Driver service.
            /// </summary>
            SERVICE_KERNEL_DRIVER = 0x00000001,

            /// <summary>
            /// File system driver service.
            /// </summary>
            SERVICE_FILE_SYSTEM_DRIVER = 0x00000002,

            /// <summary>
            /// Service that runs in its own process.
            /// </summary>
            SERVICE_WIN32_OWN_PROCESS = 0x00000010,

            /// <summary>
            /// Service that shares a process with one or more other services.
            /// </summary>
            SERVICE_WIN32_SHARE_PROCESS = 0x00000020,

            /// <summary>
            /// The service can interact with the desktop.
            /// </summary>
            SERVICE_INTERACTIVE_PROCESS = 0x00000100,
        }

        /// <summary>
        /// Service start options
        /// </summary>
        private enum SERVICE_START : uint
        {
            /// <summary>
            /// A device driver started by the system loader. This value is valid
            /// only for driver services.
            /// </summary>
            SERVICE_BOOT_START = 0x00000000,

            /// <summary>
            /// A device driver started by the IoInitSystem function. This value
            /// is valid only for driver services.
            /// </summary>
            SERVICE_SYSTEM_START = 0x00000001,

            /// <summary>
            /// A service started automatically by the service control manager
            /// during system startup. For more information, see Automatically
            /// Starting Services.
            /// </summary>        
            SERVICE_AUTO_START = 0x00000002,

            /// <summary>
            /// A service started by the service control manager when a process
            /// calls the StartService function. For more information, see
            /// Starting Services on Demand.
            /// </summary>
            SERVICE_DEMAND_START = 0x00000003,

            /// <summary>
            /// A service that cannot be started. Attempts to start the service
            /// result in the error code ERROR_SERVICE_DISABLED.
            /// </summary>
            SERVICE_DISABLED = 0x00000004,
        }

        /// <summary>
        /// Severity of the error, and action taken, if this service fails
        /// to start.
        /// </summary>
        private enum SERVICE_ERROR
        {
            /// <summary>
            /// The startup program ignores the error and continues the startup
            /// operation.
            /// </summary>
            SERVICE_ERROR_IGNORE = 0x00000000,

            /// <summary>
            /// The startup program logs the error in the event log but continues
            /// the startup operation.
            /// </summary>
            SERVICE_ERROR_NORMAL = 0x00000001,

            /// <summary>
            /// The startup program logs the error in the event log. If the
            /// last-known-good configuration is being started, the startup
            /// operation continues. Otherwise, the system is restarted with
            /// the last-known-good configuration.
            /// </summary>
            SERVICE_ERROR_SEVERE = 0x00000002,

            /// <summary>
            /// The startup program logs the error in the event log, if possible.
            /// If the last-known-good configuration is being started, the startup
            /// operation fails. Otherwise, the system is restarted with the
            /// last-known good configuration.
            /// </summary>
            SERVICE_ERROR_CRITICAL = 0x00000003,
        }

        [Flags]
        private enum SCM_ACCESS : uint
        {
            /// <summary>
            /// Required to connect to the service control manager.
            /// </summary>
            SC_MANAGER_CONNECT = 0x00001,

            /// <summary>
            /// Required to call the CreateService function to create a service
            /// object and add it to the database.
            /// </summary>
            SC_MANAGER_CREATE_SERVICE = 0x00002,

            /// <summary>
            /// Required to call the EnumServicesStatusEx function to list the
            /// services that are in the database.
            /// </summary>
            SC_MANAGER_ENUMERATE_SERVICE = 0x00004,

            /// <summary>
            /// Required to call the LockServiceDatabase function to acquire a
            /// lock on the database.
            /// </summary>
            SC_MANAGER_LOCK = 0x00008,

            /// <summary>
            /// Required to call the QueryServiceLockStatus function to retrieve
            /// the lock status information for the database.
            /// </summary>
            SC_MANAGER_QUERY_LOCK_STATUS = 0x00010,

            /// <summary>
            /// Required to call the NotifyBootConfigStatus function.
            /// </summary>
            SC_MANAGER_MODIFY_BOOT_CONFIG = 0x00020,

            /// <summary>
            /// Includes STANDARD_RIGHTS_REQUIRED, in addition to all access
            /// rights in this table.
            /// </summary>
            SC_MANAGER_ALL_ACCESS = ACCESS_MASK.STANDARD_RIGHTS_REQUIRED |
                SC_MANAGER_CONNECT |
                SC_MANAGER_CREATE_SERVICE |
                SC_MANAGER_ENUMERATE_SERVICE |
                SC_MANAGER_LOCK |
                SC_MANAGER_QUERY_LOCK_STATUS |
                SC_MANAGER_MODIFY_BOOT_CONFIG,

            GENERIC_READ = ACCESS_MASK.STANDARD_RIGHTS_READ |
                SC_MANAGER_ENUMERATE_SERVICE |
                SC_MANAGER_QUERY_LOCK_STATUS,

            GENERIC_WRITE = ACCESS_MASK.STANDARD_RIGHTS_WRITE |
                SC_MANAGER_CREATE_SERVICE |
                SC_MANAGER_MODIFY_BOOT_CONFIG,

            GENERIC_EXECUTE = ACCESS_MASK.STANDARD_RIGHTS_EXECUTE |
                SC_MANAGER_CONNECT | SC_MANAGER_LOCK,

            GENERIC_ALL = SC_MANAGER_ALL_ACCESS,
        }

        public static void InstallService(string ServiceName, string DisplayName, string Description, string Path)
        {
            var scMgrHandle = OpenSCManager(null, null, (uint)SCM_ACCESS.SC_MANAGER_ALL_ACCESS);

            try
            {
                if (scMgrHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during install service (OpenSCManager)");

                var serviceHandle = CreateService(scMgrHandle, ServiceName, DisplayName, (uint)SERVICE_ACCESS.SERVICE_ALL_ACCESS, (uint)SERVICE_TYPE.SERVICE_WIN32_OWN_PROCESS,
                    (uint)SERVICE_START.SERVICE_AUTO_START, (uint)SERVICE_ERROR.SERVICE_ERROR_NORMAL, Path, null, null, null, null, null);

                if (serviceHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during install service (CreateService)");

                var pinfo = new SERVICE_DESCRIPTION
                {
                    lpDescription = Description
                };

                var res = ChangeServiceConfig2(serviceHandle, SERVICE_CONFIG_DESCRIPTION, ref pinfo);
                if (!res)
                    System.Diagnostics.Trace.WriteLine($"Failed to set decription: {Marshal.GetLastWin32Error().ToString()}");

                CloseServiceHandle(serviceHandle);
            }
            finally
            {
                CloseServiceHandle(scMgrHandle);
            }
        }

        public static void StartService(string ServiceName)
        {
            var scMgrHandle = OpenSCManager(null, null, (uint)SCM_ACCESS.SC_MANAGER_ALL_ACCESS);

            try
            {
                if (scMgrHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during start service (OpenSCManager)");

                var serviceHandle = OpenService(scMgrHandle, ServiceName, (uint)SERVICE_ACCESS.SERVICE_ALL_ACCESS);

                if (serviceHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during start service (OpenService)");

                try
                {
                    var ssp = new SERVICE_STATUS_PROCESS();
                    var status = QueryServiceStatusEx(serviceHandle, SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO, ref ssp, Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out uint needed);
                    if (!status)
                        throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during start service (QueryServiceStatusEx)");

                    var runningStates = new[] {
                        (uint)SERVICE_STATUS.SERVICE_RUNNING ,
                        (uint)SERVICE_STATUS.SERVICE_CONTINUE_PENDING,
                        (uint)SERVICE_STATUS.SERVICE_START_PENDING,
                    };
                    if (Array.IndexOf(runningStates, ssp.dwCurrentState) >= 0)
                        return;

                    if (ssp.dwCurrentState == (uint)SERVICE_STATUS.SERVICE_STOP_PENDING || ssp.dwCurrentState == (uint)SERVICE_STATUS.SERVICE_PAUSE_PENDING)
                        throw new Exception("Service is in a pending state");

                    if (ssp.dwCurrentState == (uint)SERVICE_STATUS.SERVICE_PAUSED)
                    {
                        var sc_status = new SERVICE_STATUS();
                        if (!ControlService(serviceHandle, SERVICE_CONTROL_CONTINUE, ref sc_status))
                            throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during start service (ControlService)");
                    }
                    else
                    {
                        if (StartServiceW(serviceHandle, 0, null) == false)
                            throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during start service (StartServiceW)");
                    }
                }
                finally
                {
                    CloseServiceHandle(serviceHandle);
                }
            }
            finally
            {
                CloseServiceHandle(scMgrHandle);
            }
        }

        public static void StopService(string ServiceName)
        {
            var scMgrHandle = OpenSCManager(null, null, (uint)SCM_ACCESS.SC_MANAGER_ALL_ACCESS);

            try
            {
                if (scMgrHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during stop service (OpenSCManager)");

                var serviceHandle = OpenService(scMgrHandle, ServiceName, (uint)SERVICE_ACCESS.SERVICE_ALL_ACCESS);

                if (serviceHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during stop service (OpenService)");

                var ssp = new SERVICE_STATUS_PROCESS();
                var status = QueryServiceStatusEx(serviceHandle, SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO, ref ssp, Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out uint needed);
                if (!status)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during stop service (QueryServiceStatusEx)");

                if (ssp.dwCurrentState == (uint)SERVICE_STATUS.SERVICE_STOPPED)
                    return;

                if (ssp.dwCurrentState != (uint)SERVICE_STATUS.SERVICE_STOP_PENDING)
                {
                    var sc_status = new SERVICE_STATUS();
                    if (!ControlService(serviceHandle, SERVICE_CONTROL_STOP, ref sc_status))
                        throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during stop service (ControlService)");
                }

                while (ssp.dwCurrentState != (uint)SERVICE_STATUS.SERVICE_STOPPED)
                {
                    var dwWaitTime = (int)Math.Max(10000, Math.Min(1000, ssp.dwWaitHint / 10));
                    System.Threading.Thread.Sleep(dwWaitTime);
                    if (!QueryServiceStatusEx(serviceHandle, SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO, ref ssp, Marshal.SizeOf<SERVICE_STATUS_PROCESS>(), out needed))
                        throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during stop service (QueryServiceStatusEx)");
                }
            }
            finally
            {
                CloseServiceHandle(scMgrHandle);
            }
        }

        public static void DeleteService(string ServiceName)
        {
            var scMgrHandle = OpenSCManager(null, null, (uint)SCM_ACCESS.SC_MANAGER_ALL_ACCESS);

            try
            {
                if (scMgrHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during delete service (OpenSCManager)");

                var serviceHandle = OpenService(scMgrHandle, ServiceName, (uint)SERVICE_ACCESS.SERVICE_ALL_ACCESS);

                if (serviceHandle == IntPtr.Zero)
                    throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during delete service (OpenService)");

                try
                {
                    if (DeleteService(serviceHandle) == false)
                        throw new Exception($"Win32 error {Marshal.GetLastWin32Error().ToString()} during delete service (DeleteService)");
                }
                finally
                {
                    CloseServiceHandle(serviceHandle);
                }
            }
            finally
            {
                CloseServiceHandle(scMgrHandle);
            }
        }
    }
}
