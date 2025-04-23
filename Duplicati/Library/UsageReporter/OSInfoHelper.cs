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
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.UsageReporter
{
    /// <summary>
    /// Helper class for extracting operating system info
    /// </summary>
    public static class OSInfoHelper
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
                var psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true, // Suppress error messages
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

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

        private static string CachedPlatformString = null;

        /// <summary>
        /// Gets a single string, identifying the OS of the current platform
        /// The output should not contain any machine identifiers, only OS info.
        /// The information is cached, so only the first call will invoke an external process.
        /// </summary>
        public static string PlatformString
        {
            get
            {
                if (CachedPlatformString != null)
                    return CachedPlatformString;

                if (OperatingSystem.IsMacOS())
                {
                    var m = RunProgramAndReadOutput("sw_vers", null);
                    if (m != null)
                    {
                        var lines = m.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        var product = lines.Where(x => x.Trim().StartsWith("ProductName:", StringComparison.Ordinal)).Select(x => x.Trim().Substring("ProductName:".Length).Trim()).FirstOrDefault();
                        var version = lines.Where(x => x.Trim().StartsWith("ProductVersion:", StringComparison.Ordinal)).Select(x => x.Trim().Substring("ProductVersion:".Length).Trim()).FirstOrDefault();
                        var build = lines.Where(x => x.Trim().StartsWith("BuildVersion:", StringComparison.Ordinal)).Select(x => x.Trim().Substring("BuildVersion:".Length).Trim()).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(product))
                            return CachedPlatformString = string.Format("{0} {1} {2}", product, version, build);
                    }
                }

                if (OperatingSystem.IsLinux())
                {
                    // Manual extraction, this grabs the distro identification files directly
                    try
                    {
                        var keys = new List<Tuple<string, string>>();
                        foreach (var fn in System.IO.Directory.GetFiles("/etc/", "*-release"))
                        {
                            var fi = new System.IO.FileInfo(fn);
                            if (fi.Exists && fi.Length < 1024 * 10)
                            {
                                try
                                {
                                    keys.AddRange(
                                        System.IO.File.ReadAllLines(fi.FullName)
                                                      .Select(x =>
                                                        {
                                                            var items = (x ?? string.Empty).Split(new char[] { '=' }, 2).Select(y => (y ?? string.Empty).Trim('"')).ToArray();
                                                            if (items.Length != 2 || items.Any(y => string.IsNullOrWhiteSpace(y)))
                                                                return null;

                                                            return new Tuple<string, string>(items[0], items[1]);
                                                        })
                                                    .Where(x => x != null)
                                    );
                                }
                                catch { }

                            }
                        }

                        var primary = keys.FirstOrDefault(x => string.Equals(x.Item1, "PRETTY_NAME", StringComparison.OrdinalIgnoreCase));
                        if (primary != null)
                            return primary.Item2;

                        var name = keys.FirstOrDefault(x => string.Equals(x.Item1, "NAME", StringComparison.OrdinalIgnoreCase));
                        var version = keys.FirstOrDefault(x => string.Equals(x.Item1, "VERSION", StringComparison.OrdinalIgnoreCase));

                        name = name ?? keys.FirstOrDefault(x => string.Equals(x.Item1, "DISTRIB_ID", StringComparison.OrdinalIgnoreCase));
                        name = name ?? keys.FirstOrDefault(x => string.Equals(x.Item1, "ID", StringComparison.OrdinalIgnoreCase));

                        version = version ?? keys.FirstOrDefault(x => string.Equals(x.Item1, "DISTRIB_RELEASE", StringComparison.OrdinalIgnoreCase));
                        version = version ?? keys.FirstOrDefault(x => string.Equals(x.Item1, "VERSION_ID", StringComparison.OrdinalIgnoreCase));

                        if (name != null && version != null)
                            return CachedPlatformString = string.Format("{0} {1}", name.Item2, version.Item2);
                        if (name != null)
                            return CachedPlatformString = name.Item2;
                    }
                    catch
                    {
                    }

                    // This works on debian based distros, but emits a warning to stderr
                    var m = RunProgramAndReadOutput("lsb_release", "-a");
                    if (m != null)
                    {
                        var lines = m.Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
                        var line = lines.Where(x => x.Trim().StartsWith("Description:", StringComparison.Ordinal)).Select(x => x.Trim().Substring("Description:".Length).Trim()).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(line))
                            return CachedPlatformString = line;
                    }
                }

                // This should work on all Linux/BSD based systems
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    return CachedPlatformString = RunProgramAndReadOutput("uname", "-srvmp");

                if (OperatingSystem.IsWindows())
                    return CachedPlatformString = RunProgramAndReadOutput("cmd", "/c ver");

                return CachedPlatformString = "Unknown";
            }
        }
    }
}

