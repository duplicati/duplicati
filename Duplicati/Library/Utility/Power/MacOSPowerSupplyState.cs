//  Copyright (C) 2017, The Duplicati Team
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

namespace Duplicati.Library.Utility.Power
{
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
                var psi = new System.Diagnostics.ProcessStartInfo("pmset", "-g batt");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;

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
                var psi = new System.Diagnostics.ProcessStartInfo("ioreg", "-n AppleSmartBattery -r");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;

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
