#region Disclaimer / License
// Copyright (C) 2021, The Duplicati Team
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
using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Parity.Strings
{
    internal static class Par2Parity
    {
        public static string Description { get { return LC.L(@"This module create parity for files following Par2 standard, using Reed-Solomon coding."); } }
        public static string DisplayName { get { return LC.L(@"Par2 parity provider, external"); } }
        public static string BlocksizesmallfileLong => LC.L(@"Use this option to set block size used for parity calculation of small files (smaller than 4 MB). The block size must be a multiply of 4.");
        public static string BlocksizesmallfileShort => LC.L(@"Set the block size for parity of small files (<4MB)");
        public static string BlocksizelargefileLong => LC.L(@"Use this option to set block size used for parity calculation of large files (larger than 4 MB). The block size must be a multiply of 4.");
        public static string BlocksizelargefileShort => LC.L(@"Set the block size for parity of large files (>=4MB)");
        public static string Par2programpathLong => LC.L(@"The path to the Par2 program. If not supplied, Duplicati will assume that the program ""par2"" is available in the system path.");
        public static string Par2programpathShort => LC.L(@"The path to Par2 program");
    }
}
