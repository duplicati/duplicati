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
using System.Linq;
using System.Runtime.Versioning;

namespace Duplicati.Library.Utility.Power
{
    [SupportedOSPlatform("macOS")]
    public class MacOSPowerSupplyState : IPowerSupplyState
    {
        public PowerSupply.Source GetSource()
        {
            var src = GetSourcePmset();
            if (src == PowerSupply.Source.Unknown)
                src = GetSourceIoreg();

            return src;
        }

        /// <summary>
        /// Gets the current power source using `pmset`
        /// </summary>
        /// <returns>The power source according to pmset.</returns>
        private PowerSupply.Source GetSourcePmset()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("pmset", "-g batt")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardInput = false,
                    RedirectStandardError = false,
                    UseShellExecute = false
                };

                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(1000);
                if (pi.HasExited)
                {
                    var pmout = pi.StandardOutput.ReadToEnd().Trim();
                    if (pmout.IndexOf("'AC Power'", StringComparison.OrdinalIgnoreCase) >= 0)
                        return PowerSupply.Source.AC;
                    if (pmout.IndexOf("'Battery Power'", StringComparison.OrdinalIgnoreCase) >= 0)
                        return PowerSupply.Source.Battery;
                }
                else
                    pi.Kill();

                return PowerSupply.Source.Unknown;
            }
            catch
            {
                return PowerSupply.Source.Unknown;
            }
        }

        /// <summary>
        /// Gets the current power source using `ioreg`
        /// </summary>
        /// <returns>The power source according to ioreg.</returns>
        private PowerSupply.Source GetSourceIoreg()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("ioreg", "-n AppleSmartBattery -r")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    RedirectStandardInput = false,
                    UseShellExecute = false
                };

                var pi = System.Diagnostics.Process.Start(psi);
                pi.WaitForExit(1000);
                if (pi.HasExited)
                {
                    // Find:
                    // "ExternalConnected" = Yes
                    var ioreg = pi.StandardOutput.ReadToEnd()
                                  .Split(new string[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
                                  .Select(x => x.Trim())
                                  .Where(x => x.StartsWith("\"ExternalConnected\"", StringComparison.OrdinalIgnoreCase))
                                  .Select(x => x.Split(new char[] { '=' }, 2).LastOrDefault())
                                  .FirstOrDefault();

                    if (string.Equals(ioreg, "No", StringComparison.OrdinalIgnoreCase))
                        return PowerSupply.Source.Battery;

                    if (string.Equals(ioreg, "Yes", StringComparison.OrdinalIgnoreCase))
                        return PowerSupply.Source.AC;
                }
                else
                    pi.Kill();

                return PowerSupply.Source.Unknown;
            }
            catch
            {
                return PowerSupply.Source.Unknown;
            }
        }
    }
}
