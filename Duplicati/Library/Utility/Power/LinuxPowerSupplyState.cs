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
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace Duplicati.Library.Utility.Power
{
    [SupportedOSPlatform("linux")]
    public class LinuxPowerSupplyState : IPowerSupplyState
    {
        private readonly string sysfsPath = Path.Combine("/", "sys", "class", "power_supply");

        public PowerSupply.Source GetSource()
        {
            if (this.IsAC())
            {
                return PowerSupply.Source.AC;
            }
            if (this.IsBattery())
            {
                return PowerSupply.Source.Battery;
            }

            return PowerSupply.Source.Unknown;
        }

        private bool IsAC()
        {
            // If any of the power supply devices of type "Mains" are online, then we are on 
            // AC power. If none of the power supply devices are of type "Mains", then we 
            // are also on AC power. See https://bugzilla.redhat.com/show_bug.cgi?id=644629.
            bool reply = false;
            bool haveMains = false;

            try
            {
                foreach (string source in Directory.GetDirectories(this.sysfsPath))
                {
                    if (!reply)
                    {
                        string sourceType = File.ReadLines(Path.Combine(source, "type")).FirstOrDefault();
                        if (String.Equals(sourceType, "Mains", StringComparison.Ordinal))
                        {
                            haveMains = true;

                            string isOnline = File.ReadLines(Path.Combine(source, "online")).FirstOrDefault();
                            reply = String.Equals(isOnline, "1", StringComparison.Ordinal);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return reply || !haveMains;
        }

        private bool IsBattery()
        {
            // If there is at least one power supply device of type "Mains", and all "Mains"
            // devices are offline, then we are on battery power.
            bool allOffline = true;
            bool haveMains = false;

            try
            {
                foreach (string source in Directory.GetDirectories(this.sysfsPath))
                {
                    if (allOffline)
                    {
                        string sourceType = File.ReadLines(Path.Combine(source, "type")).FirstOrDefault();
                        if (String.Equals(sourceType, "Mains", StringComparison.Ordinal))
                        {
                            haveMains = true;

                            string isOnline = File.ReadLines(Path.Combine(source, "online")).FirstOrDefault();
                            allOffline &= String.Equals(isOnline, "0", StringComparison.Ordinal);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return haveMains && allOffline;
        }
    }
}
