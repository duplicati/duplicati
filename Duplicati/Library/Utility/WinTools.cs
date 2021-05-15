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
using System.Runtime.InteropServices;

namespace Duplicati.Library.Utility
{
    public static class WinTools
    {
        /// <summary>
        /// Windows security privileges.
        /// Multiple privileges can be specified using bitwise addition.
        /// </summary>
        [Flags]
        public enum Privileges
        {
            /// <summary>
            /// No additional privileges.
            /// </summary>
            None = 0,

            /// <summary>
            /// SeBackupPrivilege privilege.
            /// </summary>
            SeBackupPrivilege = 1,

            /// <summary>
            /// SeRestorePrivilege privilege.
            /// </summary>
            SeRestorePrivilege = 2
        }


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
        /// On Windows platforms, tries to add specified privileges to the process
        /// before executing the specified <paramref name="action"/>.
        /// Executes the specified <paramref name="action"/> as-is if the additional process privilege cannot be added
        /// to the process security context (eg current user is not allowed to enable this privilege).
        /// </summary>
        /// <param name="privileges">Desired security privileges.</param>
        /// <param name="action">Action to execute.</param>
        /// <remarks>
        /// In Windows platforms, the <see cref="Privileges.SeBackupPrivilege"/>
        /// privilege allows processes to bypass file and directory object permissions and read all objects for the purposes of backing up the system.
        /// Similarly, the <see cref="Privileges.SeRestorePrivilege"/> 
        /// privilege allows processes to write all objects for the purposes of backing up the system.
        /// 
        /// The privileges are available to user accounts (and security groups) specified in the 
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
        public static void WithPrivileges(Privileges privileges, Action action)
        {
            if (Platform.IsClientWindows)
            {
                var addedPrivileges = ToggleProcessPrivileges(privileges, true);
                try
                {
                    action();
                }
                finally
                {
                    if (addedPrivileges != Privileges.None)
                    {
                        ToggleProcessPrivileges(addedPrivileges, false);
                    }
                }
            }
            else
            {
                action();
            }
        }

        /// <summary>
        /// Enables or disables one or more process privileges.
        /// </summary>
        /// <param name="privileges">Privileges to enable/disable.</param>
        /// <param name="enable">True to enable; false to disable.</param>
        /// <returns>Privileges that were successfully enabled/disabled.</returns>
        private static Privileges ToggleProcessPrivileges(Privileges privileges, bool enable)
        {
            Privileges result = Privileges.None;
            if (privileges == Privileges.None)
                return result;

            using (var safeHandle = GetPrivilegeAdjustProcessToken())
            {
                if (privileges.HasFlag(Privileges.SeBackupPrivilege))
                {
                    if (AdjustPrivilege(safeHandle.DangerousGetHandle(), Win32.SE_BACKUP_NAME, enable))
                        result |= Privileges.SeBackupPrivilege;
                }

                if (privileges.HasFlag(Privileges.SeRestorePrivilege))
                {
                    if (AdjustPrivilege(safeHandle.DangerousGetHandle(), Win32.SE_RESTORE_NAME, enable))
                        result |= Privileges.SeRestorePrivilege;
                }
            }

            return result;
        }


        /// <summary>
        /// Obtains a process access token that can be used to adjust process privileges.
        /// </summary>
        /// <returns></returns>
        private static SafeProcessHandle GetPrivilegeAdjustProcessToken()
        {
            bool retVal;
            using (var hproc = new SafeProcessHandle(Win32.GetCurrentProcess(), true))
            {
                IntPtr htok = IntPtr.Zero;
                retVal = Win32.OpenProcessToken(hproc.DangerousGetHandle(), Win32.TOKEN_ADJUST_PRIVILEGES | Win32.TOKEN_QUERY, ref htok);
                if (!retVal)
                {
                    return null;
                }
                return new SafeProcessHandle(htok, true);
            }
        }

        private static bool AdjustPrivilege(IntPtr processPointer, string privilege, bool enable)
        {
            bool retVal;
            Win32.TOKEN_PRIVILEGES tp;
            tp.Count = 1;
            tp.Luid = 0;
            tp.Attr = enable
                ? Win32.SE_PRIVILEGE_ENABLED
                : Win32.SE_PRIVILEGE_DISABLED;

            Win32.TOKEN_PRIVILEGES prev = new Win32.TOKEN_PRIVILEGES();

            retVal = Win32.LookupPrivilegeValue(null, privilege, ref tp.Luid);
            if (!retVal)
            {
                return false;
            }

            IntPtr retSize = IntPtr.Zero;
            retVal = Win32.AdjustTokenPrivileges(processPointer, false, ref tp, Marshal.SizeOf(prev), ref prev, ref retSize);

            // API call successful and previous value != current value (ie we actually enabled/disabled the privilege)
            return retVal && prev.Attr != tp.Attr;
        }

    }
}
