using Duplicati.Library.Common;
using System;

namespace Duplicati.Library.Utility
{
    public static class ThreadPrivilege
    {
        /// <summary>
        /// Executes action with backup privileges.
        /// </summary>
        /// <param name="action">Action to execute.</param>
        /// <remarks>
        /// On platforms that implement in-process security context changes,
        /// obtains additional privileges before executing the specified <paramref name="action"/>,
        /// and reverts the privileges afterwards.
        /// </remarks>
        public static void ExecuteWithBackupPrivileges(Action action)
        {
            if (Platform.IsClientWindows)
            {
                /*
                On Windows platforms, the "SeBackupPrivilege" privilege
                allows processes to bypass file and directory object permissions
                and read all objects for the purposes of backing up the system.
                Similarly, the "SeRestorePrivilege" privilege allows processes
                to write all objects for the purposes of backing up the system.

                The privileges are available to user accounts (and security groups)
                specified in the 'Back up files and directories' group policy
                (GPO => Computer Configuration => Windows Settings =>
                Security Settings => Local Policies => User Rights Assignment).
         
                By default, the 'Administrators' and 'Backup Operators' security
                groups are specified in the 'Back up files and directories' group
                policy.

                To take advantage of this functionality, configure the Duplicati
                service to use its own dedicated user account, and add this user
                account to the 'Backup Operators' security group.
                The process will be able to access ALL files on the system, without
                any other admin permissions.
                 */
                Win32.Privilege.RunWithPrivileges(action, Win32.Privilege.Backup, Win32.Privilege.Restore);
            }
            else
            {
                action();
            }
        }

    }
}
