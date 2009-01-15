#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
using System.Collections.Specialized;
using System.Text;

namespace Duplicati.Datamodel.Backends
{
    public class File : IBackend
    {
        private const string DESTINATION_FOLDER = "Destination";
        private const string TIME_SEPARATOR = "TimeSeparator";

        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";

        private Task m_owner;

        public File(Task owner)
        {
            m_owner = owner;
            if (this.TimeSeparator == null)
                this.TimeSeparator = "'";
        }

        public string DestinationFolder
        {
            get { return m_owner.Settings[DESTINATION_FOLDER]; }
            set { m_owner.Settings[DESTINATION_FOLDER] = value; }
        }

        public string TimeSeparator
        {
            get { return m_owner.Settings[TIME_SEPARATOR]; }
            set { m_owner.Settings[TIME_SEPARATOR] = value; }
        }

        public string Username
        {
            get { return m_owner.Settings[USERNAME]; }
            set { m_owner.Settings[USERNAME] = value; }
        }

        public string Password
        {
            get { return m_owner.Settings[PASSWORD]; }
            set { m_owner.Settings[PASSWORD] = value; }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            if (!string.IsNullOrEmpty(this.Username))
                return "file://" + this.Username + "@" + this.DestinationFolder;
            else
                return "file://" + this.DestinationFolder;
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            options["time-separator"] = this.TimeSeparator;
            if (!string.IsNullOrEmpty(this.Password))
                options["ftp_password"] = this.Password;
        }

        public string FriendlyName { get { return "External drive or folder"; } }
        public string SystemName { get { return "file"; } }
        public void SetService() { m_owner.Service = this.SystemName; }

        #endregion
    }
}
