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
using System.IO;
using System.Linq;

namespace Duplicati.Library.Utility.Power
{
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
            // AC power.  If none of the power supply devices are of type "Mains", then we 
            // are also on AC power.  See https://bugzilla.redhat.com/show_bug.cgi?id=644629.
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
