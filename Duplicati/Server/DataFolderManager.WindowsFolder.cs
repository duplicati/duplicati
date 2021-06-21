using Duplicati.Library.Common.IO;
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Duplicati.Server
{
    partial class DataFolderManager
    {
        private static class WindowsFolder
        {
            public static void EnsureDataFolderDirectory(string path)
            {
                if (!Library.Common.Platform.IsClientWindows)
                {
                    throw new System.InvalidOperationException();
                }
                if (SystemIO.IO_WIN.DirectoryExists(path))
                {
                    return;
                }

                try
                {
                    if (WinTools.Ntfs.IsPathEligibleForNtfsPermissions(path))
                    {
                        var userIdentity = WinTools.GetWindowsIdentity();

                        if (userIdentity.IsAuthenticated &&
                            !userIdentity.User.IsWellKnown(WellKnownSidType.AccountGuestSid))
                        {
                            SecurityIdentifier owner;

                            if (userIdentity.User.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
                                userIdentity.User.IsWellKnown(WellKnownSidType.NetworkServiceSid))
                            {
                                owner = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                            }
                            else if (userIdentity.Groups.OfType<SecurityIdentifier>().Any(g =>
                                g.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
                                g.IsWellKnown(WellKnownSidType.BuiltinBackupOperatorsSid)))
                            {
                                owner = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
                            }
                            else
                            {
                                owner = userIdentity.User;
                            }

                            CreateDataFolder(path, owner);
                            return;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "ConfigureDataFolderNtfsPermissions", ex, "Cannot configure NTFS permissions for new data folder at '{0}'.", path);
                }

                SystemIO.IO_WIN.DirectoryCreate(path);
            }

            private static void CreateDataFolder(string path, SecurityIdentifier owner)
            {
                var rules = new List<FileSystemAccessRule>(4);
                rules.Add(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

                if (!owner.IsWellKnown(WellKnownSidType.LocalSystemSid))
                {
                    rules.Add(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                }

                if (!owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid))
                {
                    rules.Add(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                        FileSystemRights.FullControl,
                        InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                }

                rules.Add(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinBackupOperatorsSid, null),
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));

                WinTools.Ntfs.DirectoryCreate(path, owner, rules.ToArray(), true);
            }

        }
    }
}
