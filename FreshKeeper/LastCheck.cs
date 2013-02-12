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
    /// <summary>
    /// A class that contains information about the most recent update check
    /// </summary>
    public class LastCheck
    {
        private Version m_version;
        private DateTime m_time;

        /// <summary>
        /// Constructs the default LastCheck instance
        /// </summary>
        public LastCheck()
        {
            m_version = new Version(0, 0);
            m_time = new DateTime(0);
        }

        /// <summary>
        /// The time the last update check was performed
        /// </summary>
        public DateTime Time
        {
            get { return m_time; }
            set { m_time = value; }
        }

        /// <summary>
        /// The last version the user was notified of
        /// </summary>
        public Version Version
        {
            get { return m_version; }
            set { m_version = value; }
        }
    }
}
