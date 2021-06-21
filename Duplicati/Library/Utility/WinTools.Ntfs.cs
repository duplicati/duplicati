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
using Duplicati.Library.Common.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

using AlphaFsDirectory = Alphaleonis.Win32.Filesystem.Directory;

namespace Duplicati.Library.Utility
{
    public static partial class WinTools
    {
        public static class Ntfs
        {
            public static void DirectoryCreate(string path, IdentityReference owner, FileSystemAccessRule[] rules, bool removeAllInheritedRules)
            {
                var ds = new DirectorySecurity();

                if (removeAllInheritedRules)
                {
                    ds.SetAccessRuleProtection(true, false);
                }

                if (owner != null)
                {
                    ds.SetOwner(owner);
                }

                foreach (var rule in rules)
                {
                    ds.AddAccessRule(rule);
                }
                AlphaFsDirectory.CreateDirectory(SystemIOWindowsBase.PrefixWithUNC(path), ds);
            }
            public static void DirectorySetAccessControl(string path, IdentityReference owner, FileSystemAccessRule[] rules, bool removeAllInheritedRules)
            {
                var ds = new DirectorySecurity();

                if (removeAllInheritedRules)
                {
                    ds.SetAccessRuleProtection(true, false);
                }

                if (owner != null)
                {
                    ds.SetOwner(owner);
                }

                foreach (var rule in rules)
                {
                    ds.AddAccessRule(rule);
                }

                AlphaFsDirectory.SetAccessControl(SystemIOWindowsBase.PrefixWithUNC(path), ds);
            }

            public static bool IsPathEligibleForNtfsPermissions(string path)
            {
                if (!Platform.IsClientWindows)
                {
                    return false;
                }

                var pathRoot = AlphaFsDirectory.GetDirectoryRoot(path);
                var driveInfo = Alphaleonis.Win32.Filesystem.DriveInfo.GetDrives()
                    .FirstOrDefault(d =>
                        string.Equals(d.RootDirectory.FullName, pathRoot, System.StringComparison.OrdinalIgnoreCase));

                if (driveInfo != null &&
                    (driveInfo.DriveType == System.IO.DriveType.Fixed ||
                    driveInfo.DriveType == System.IO.DriveType.Removable ||
                    driveInfo.DriveType == System.IO.DriveType.Network) &&
                    driveInfo.DriveFormat == "NTFS")
                {
                    return true;
                }

                return false;
            }

        }

    }
}
