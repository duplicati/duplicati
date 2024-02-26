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
using System.Reflection;

namespace Duplicati.Library.Utility.Power
{
    public class WindowsPowerSupplyState : IPowerSupplyState
    {
        public PowerSupply.Source GetSource()
        {
            try
            {
                // Using reflection to allow building on non-Windows
                // PowerLineStatus status = System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus;
                var powerstatus = System.Type.GetType("System.Windows.Forms.SystemInformation, System.Windows.Forms")
                    .GetProperty("PowerStatus", BindingFlags.Public | BindingFlags.Static)
                    .GetValue(null);

                var status = powerstatus.GetType()
                    .GetProperty("PowerLineStatus", BindingFlags.Public | BindingFlags.Instance)
                    .GetValue(powerstatus)
                    .ToString();

                    
                if (string.Equals(status, "Online", System.StringComparison.OrdinalIgnoreCase))
                {
                    return PowerSupply.Source.AC;
                }
                if (string.Equals(status == "Offline", System.StringComparison.OrdinalIgnoreCase))
                {
                    return PowerSupply.Source.Battery;
                }
            }
            catch
            { }

            return PowerSupply.Source.Unknown;

        }
    }
}
