#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";

        private Task m_owner;

        public File(Task owner)
        {
            m_owner = owner;
            if (m_owner.Extensions.FileTimeSeperator == null)
                m_owner.Extensions.FileTimeSeperator = "'";
        }

        public string DestinationFolder
        {
            get { return m_owner.BackendSettingsLookup[DESTINATION_FOLDER]; }
            set { m_owner.BackendSettingsLookup[DESTINATION_FOLDER] = value; }
        }

        public string Username
        {
            get { return m_owner.BackendSettingsLookup[USERNAME]; }
            set { m_owner.BackendSettingsLookup[USERNAME] = value; }
        }

        public string Password
        {
            get { return m_owner.BackendSettingsLookup[PASSWORD]; }
            set { m_owner.BackendSettingsLookup[PASSWORD] = value; }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            return "file://" + this.DestinationFolder;
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            if (!string.IsNullOrEmpty(this.Username))
                options["ftp-username"] = this.Username;
            if (!string.IsNullOrEmpty(this.Password))
                options["ftp-password"] = this.Password;
        }

        public string FriendlyName { get { return "External drive or folder"; } }
        public string SystemName { get { return "file"; } }
        public void SetService() { m_owner.Service = this.SystemName; }

        #endregion
    }
}
