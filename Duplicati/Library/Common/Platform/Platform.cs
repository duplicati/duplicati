using System.Runtime.InteropServices;
//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Runtime.InteropServices;

namespace Duplicati.Library.Common
{
    public static class Platform
    {
        /// <value>
        /// Gets or sets a value indicating if the client is Linux/Unix based
        /// </value>
        public static readonly bool IsClientPosix;


        /// <summary>
        /// Gets a value indicating if the client is Windows based
        /// </summary>
        public static readonly bool IsClientWindows;


        /// <value>
        /// Gets or sets a value indicating if the client is running OSX
        /// </value>
        public static readonly bool IsClientOSX;

        static Platform()
        {
            IsClientOSX = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
            IsClientPosix = IsClientOSX || RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
            IsClientWindows = !IsClientPosix;
        }

        /// <value>
        /// Gets the output of "uname -a" on Linux, or null on Windows
        /// </value>
        public static string UnameAll
        {
            get
            {
                if (!IsClientPosix)
                    return null;

                return _RetrieveUname(true);

            }
        }

        private static string _RetrieveUname(bool showAll)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("uname", showAll ? "-a" : null)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(5000);
                if (pi.HasExited)
                    return pi.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
                return null;
            }

            return null;
        }

    }
}
