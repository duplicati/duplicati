#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
        public static readonly string TAG;
        public static readonly string VERSION_NAME;
        
        static VersionNumbers()
        {
            string tag = "";
            try
            {
                using (var rd = new System.IO.StreamReader(System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(typeof(VersionNumbers), "VersionTag.txt")))
                    tag = rd.ReadToEnd();
            }
            catch
            {
            }
            
            if (string.IsNullOrWhiteSpace(tag))
                tag = "";
            
            TAG = tag.Trim();

            var v = VersionNumber;
            if (!string.IsNullOrWhiteSpace(TAG))
                v = " - " + TAG;
#if DEBUG
            v = " - debug";
#endif
            VERSION_NAME = v;

        }
        
        public static string Version
        {
            get
            {
                return VERSION_NAME;
            }
        }

        private static string VersionNumber
        {
            get
            {
                return Duplicati.Library.Utility.Utility.getExecutingAssembly().GetName().Version.ToString();
            }
        }
    }
}
