#region Disclaimer / License
// Copyright (C) 2019, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using Duplicati.Library.Common;
using Microsoft.Win32.SafeHandles;
using System;

namespace Duplicati.Library.Utility
{
    public static class WinTools
    {
        public static string GetWindowsGpgExePath()
        {
            if (!Platform.IsClientWindows)
            {
                return null;
            }

            try
            {
                // return gpg4win if it exists
                var location32 = Library.Utility.RegistryUtility.GetDataByValueName(@"SOFTWARE\WOW6432Node\GnuPG", "Install Directory");
                var location64 = Library.Utility.RegistryUtility.GetDataByValueName(@"SOFTWARE\GnuPG", "Install Directory");
                var gpg4winLocation = string.IsNullOrEmpty(location64) ? location32 : location64;

                if (!string.IsNullOrEmpty(gpg4winLocation))
                {
                    return System.IO.Path.Combine(gpg4winLocation, "bin", "gpg.exe");
                }
            }
            catch
            {
                // NOOP
            }

            // otherwise return our included win-tools
            var wintoolsPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "win-tools", "gpg.exe");
            return string.IsNullOrEmpty(wintoolsPath) ? null : wintoolsPath;
        }


        /// <summary>
        /// On Windows platforms, tries to add <see cref="Win32.SE_BACKUP_NAME"/> privilege to the process
        /// before executing the specified <paramref name="action"/>.
        /// Executes the specified <paramref name="action"/> as-is if the additional process privilege cannot be added
        /// to the process security context (eg current user is not allowed to enable this privilege).
        /// </summary>
        /// <param name="action">Action to execute.</param>
        /// <remarks>
        /// In Windows platforms, the <see cref="Win32.SE_BACKUP_NAME"/>
        /// privilege allows users to bypass file and directory object permissions for the purposes of backing up the system.
        /// 
        /// The privilege is available to user accounts (and security groups) specified in the 
        /// 'Back up files and directories' group policy (GPO =&gt; Computer Configuration =&gt;
        /// Windows Settings =&gt; Security Settings =&gt; Local Policies =&gt; User Rights Assignment).
        /// 
        /// By default, the 'Administrators' and 'Backup Operators' security groups are specified
        /// in the 'Back up files and directories' group policy.
        /// 
        /// To take advantage of this functionality, configure the Duplicati service to use its own dedicated user account,
        /// and add this user account to the 'Backup Operators' security group. The process will be able to access ALL files on the system,
        /// without any other admin permissions.
        /// </remarks>
        public static void WithBackupPrivileges(Action action)
        {
            if (Platform.IsClientWindows)
            {
                var addedPrivilege = AddPrivilege(Win32.SE_BACKUP_NAME);
                try
                {
                    action();
                }
                finally
                {
                    if (addedPrivilege)
                    {
                        RemovePrivilege(Win32.SE_BACKUP_NAME);
                    }
                }
            }
            else
            {
                action();
            }
        }


        private static bool AddPrivilege(string privilege)
        {
            bool retVal;
            Win32.TOKEN_PRIVILEGES tp;
            using (var hproc = new SafeProcessHandle(Win32.GetCurrentProcess(), true))
            {
                IntPtr htok = IntPtr.Zero;
                retVal = Win32.OpenProcessToken(hproc.DangerousGetHandle(), Win32.TOKEN_ADJUST_PRIVILEGES | Win32.TOKEN_QUERY, ref htok);
                if (!retVal)
                {
                    return false;
                }
                using (var safeTokenHandle = new SafeProcessHandle(htok, true))
                {
                    tp.Count = 1;
                    tp.Luid = 0;
                    tp.Attr = Win32.SE_PRIVILEGE_ENABLED;

                    retVal = Win32.LookupPrivilegeValue(null, privilege, ref tp.Luid);
                    if (!retVal)
                    {
                        return false;
                    }
                    retVal = Win32.AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    return retVal;
                }
            }
        }
        private static bool RemovePrivilege(string privilege)
        {
            bool retVal;
            Win32.TOKEN_PRIVILEGES tp;
            using (var hproc = new SafeProcessHandle(Win32.GetCurrentProcess(), true))
            {
                IntPtr htok = IntPtr.Zero;
                retVal = Win32.OpenProcessToken(hproc.DangerousGetHandle(), Win32.TOKEN_ADJUST_PRIVILEGES | Win32.TOKEN_QUERY, ref htok);
                if (!retVal)
                {
                    return false;
                }
                using (var safeTokenHandle = new SafeProcessHandle(htok, true))
                {
                    tp.Count = 1;
                    tp.Luid = 0;
                    tp.Attr = Win32.SE_PRIVILEGE_DISABLED;

                    retVal = Win32.LookupPrivilegeValue(null, privilege, ref tp.Luid);
                    if (!retVal)
                    {
                        return false;
                    }
                    retVal = Win32.AdjustTokenPrivileges(htok, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
                    return retVal;
                }
            }
        }

    }
}
