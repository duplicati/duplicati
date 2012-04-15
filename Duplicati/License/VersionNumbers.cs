#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.License
{
    public static class VersionNumbers
    {
        public static string Version
        {
            get
            {
#if DEBUG
                string debug = " - DEBUG";
#else
                string debug = "";
#endif

                return VersionNumber + debug;
            }
        }

        private static string VersionNumber
        {
            get
            {
                Version v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;


                if (v == new Version(1, 0, 0, 183))
                    return "1.0";
                else if (v == new Version(1, 99, 0, 605))
                    return "1.2 beta 1";
                else if (v == new Version(1, 1, 99, 663))
                    return "1.2 beta 2";
                else if (v == new Version(1, 1, 99, 779))
                    return "1.2 RC";
                else if (v == new Version(1, 1, 99, 794))
                    return "1.2 Final";
                else if (v == new Version(1, 3, 0, 969))
                    return "1.3 Beta (r969)";
                else if (v == new Version(1, 3, 0, 1022))
                    return "1.3 Beta (r1022)";
                else if (v == new Version(1, 3, 0, 1047))
                    return "1.3 Beta (r1047)";
                else if (v == new Version(1, 3, 0, 1066))
                    return "1.3";
                else if (v == new Version(1, 3, 0, 1205))
                    return "1.3.1";
                else
                    return System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
        }
    }
}
