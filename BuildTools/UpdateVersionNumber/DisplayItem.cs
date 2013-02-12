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

namespace UpdateVersionNumber
{
    public class DisplayItem
    {
        public DisplayItem(string file, Version version)
        {
            m_file = file;
            m_version = version;
        }

        private string m_file;

        public string File
        {
            get { return m_file; }
            set { m_file = value; }
        }
        private Version m_version;

        public Version Version
        {
            get { return m_version; }
            set { m_version = value; }
        }

        public override string ToString()
        {
            return m_file + " (" + m_version.ToString() + ")";
        }
    }
}
