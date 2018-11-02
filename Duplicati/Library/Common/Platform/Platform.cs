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
namespace Duplicati.Library.Common
{
    public static class Platform
    {
        /// <value>
        /// Gets or sets a value indicating if the client is Linux/Unix based
        /// </value>
        public static bool IsClientLinux => Environment.OSVersion.Platform == PlatformID.Unix || Environment.OSVersion.Platform == PlatformID.MacOSX;

        /// <summary>
        /// Gets a value indicating if the client is Windows based
        /// </summary>
        public static bool IsClientWindows => !IsClientLinux;


        private static string UNAME;

        /// <value>
        /// Gets or sets a value indicating if the client is running OSX
        /// </value>
        public static bool IsClientOSX
        {
            get
            {
                // Sadly, Mono returns Unix when running on OSX
                //return Environment.OSVersion.Platform == PlatformID.MacOSX;

                if (!IsClientLinux)
                    return false;

                try
                {
                    if (UNAME == null)
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("uname")
                        {
                            RedirectStandardOutput = true,
                            RedirectStandardInput = false,
                            RedirectStandardError = false,
                            UseShellExecute = false
                        };

                        var pi = System.Diagnostics.Process.Start(psi);
                        pi.WaitForExit(5000);
                        if (pi.HasExited)
                            UNAME = pi.StandardOutput.ReadToEnd().Trim();
                    }
                }
                catch
                {
                }

                return "Darwin".Equals(UNAME);

            }
        }
        /// <value>
        /// Gets the output of "uname -a" on Linux, or null on Windows
        /// </value>
        public static string UnameAll
        {
            get
            {
                if (!IsClientLinux)
                    return null;

                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo("uname", "-a")
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
                    // ignored
                }

                return null;
            }
        }
    }
}
