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

using System;
using Duplicati.Library.Common;

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

            // return gpg4win if it exists
            var location32 = Library.Utility.RegistryUtility.GetDataByValueName(@"SOFTWARE\WOW6432Node\GnuPG", "Install Directory");
            var location64 = Library.Utility.RegistryUtility.GetDataByValueName(@"SOFTWARE\GnuPG", "Install Directory");
            var gpg4winLocation = string.IsNullOrEmpty(location64) ? location32 : location64;

            if (!string.IsNullOrEmpty(gpg4winLocation))
            {
                return System.IO.Path.Combine(gpg4winLocation, "bin", "gpg.exe");
            }

            // otherwise return our included win-tools
            var wintoolsPath = System.IO.Path.Combine(
                System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location),
                "win-tools", "gpg.exe");
            return string.IsNullOrEmpty(wintoolsPath) ? null : wintoolsPath;
        }
    }
}
