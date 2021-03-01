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

namespace Duplicati.Library.Utility.Power
{
    public class WindowsPowerSupplyState : IPowerSupplyState
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public PowerSupply.Source GetSource()
        {
            try
            {
                var managementScope = new System.Management.ManagementScope(new System.Management.ManagementPath("root\\cimv2"));
                var objectQuery = new System.Management.ObjectQuery("SELECT BatteryStatus FROM Win32_Battery");
                var objectSearcher = new System.Management.ManagementObjectSearcher(managementScope, objectQuery);
                var objectCollection = objectSearcher.Get();

                if (objectCollection.Count == 0)
                    return PowerSupply.Source.AC;

                foreach (System.Management.ManagementObject managementObject in objectCollection)
                    if (Convert.ToUInt16(managementObject.Properties["BatteryStatus"].Value) == 2)
                        return PowerSupply.Source.AC;
                    else
                        return PowerSupply.Source.Battery;

                return PowerSupply.Source.Unknown;
            }
            catch
            {
                return PowerSupply.Source.Unknown;
            }
        }
    }
}
