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

using Duplicati.Library.Common;

namespace Duplicati.Library.Utility.Power
{
    public static class PowerSupply
    {
        public enum Source
        {
            AC,
            Battery,
            Unknown
        }

        public static Source GetSource()
        {
            IPowerSupplyState state;

            // Since IsClientLinux returns true when on Mac OS X, we need to check IsClientOSX first.
            if (System.OperatingSystem.IsMacOS())
            {
                state = new MacOSPowerSupplyState();
            }
            else if (System.OperatingSystem.IsLinux())
            {
                state = new LinuxPowerSupplyState();
            }
            else if (System.OperatingSystem.IsWindows())
            {
                state = new WindowsPowerSupplyState();
            }
            else
            {
                state = new DefaultPowerSupplyState();
            }

            return state.GetSource();
        }
    }
}
