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

namespace FreshKeeper
{
    public class UpdateList
    {
        string m_signedhash = "";
        Update[] m_updates;

        public UpdateList()
        {
        }

        [System.Xml.Serialization.XmlAttribute()]
        public string SignedHash
        {
            get { return m_signedhash; }
            set { m_signedhash = value; }
        }

        [System.Xml.Serialization.XmlElement("Update")]
        public Update[] Updates
        {
            get { return m_updates; }
            set { m_updates = value; }
        }
    }
}
