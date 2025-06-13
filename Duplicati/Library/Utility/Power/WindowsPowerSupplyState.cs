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
using System.Runtime.Versioning;

namespace Duplicati.Library.Utility.Power
{
    [SupportedOSPlatform("windows")]
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
            { }

            return PowerSupply.Source.Unknown;

        }
    }
}
