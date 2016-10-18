//  Copyright (C) 2015, The Duplicati Team
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
using System.Linq;

namespace Duplicati.Library.UsageReporter
{
    /// <summary>
    /// Helper class for extracting operating system info
    /// </summary>
    internal static class OSInfoHelper
    {
        /// <summary>
        /// Runs a commandline program and returns stdout as a string.
        /// </summary>
        /// <returns>The stdout value.</returns>
        /// <param name="cmd">The command to run.</param>
        /// <param name="args">The commandline arguments.</param>
        private static string RunProgramAndReadOutput(string cmd, string args)
        {
            System.Diagnostics.Process pi = null;
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(cmd, args);
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true; // Suppress error messages
                psi.UseShellExecute = false;

                pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(5000);
                if (pi.HasExited)
                    return pi.StandardOutput.ReadToEnd().Trim();
            }
            catch
            {
            }
            finally
            {
                if (pi != null && !pi.HasExited)
                    try { pi.Kill(); }
                    catch { }
            }

            return null;   
        }

        /// <summary>
        /// Gets a single string, identifying the OS of the current platform
        /// The output should not contain any machine identifiers, only OS info
        /// </summary>
        /// <value>The platform.</value>
        public static string PlatformString
        {
            get
            {
                if (!Utility.Utility.IsClientLinux)
                {
                    return Environment.OSVersion.ToString();
                }
                else if (Utility.Utility.IsClientOSX)
                {
                    var m = RunProgramAndReadOutput("sw_vers", null);
                    if (m != null)
                    {
                        var lines = m.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        var product = lines.Where(x => x.Trim().StartsWith("ProductName:")).Select(x => x.Trim().Substring("ProductName:".Length).Trim()).FirstOrDefault();
                        var version = lines.Where(x => x.Trim().StartsWith("ProductVersion:")).Select(x => x.Trim().Substring("ProductVersion:".Length).Trim()).FirstOrDefault();
                        var build = lines.Where(x => x.Trim().StartsWith("BuildVersion:")).Select(x => x.Trim().Substring("BuildVersion:".Length).Trim()).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(product))
                            return string.Format("{0} {1} {2}", product, version, build);
                    }
                       
                    return RunProgramAndReadOutput("uname", "-srvmp");
                }
                else
                {
                    var m = RunProgramAndReadOutput("lsb_release", "-a");
                    if (m != null)
                    {
                        var lines = m.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        var line = lines.Where(x => x.Trim().StartsWith("Description:")).Select(x => x.Trim().Substring("Description:".Length).Trim()).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(line))
                            return line;
                    }

                    return RunProgramAndReadOutput("uname", "-srvmpio");

                }

                return null;
            }
        }    
    }
}

