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
using System.Text;

namespace Duplicati.Datamodel.Backends
{
    public class FTP : IBackend
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string PASSIVE = "Passive";

        private Task m_owner;

        public FTP(Task owner)
        {
            m_owner = owner;
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

        public string Host
        {
            get { return m_owner.BackendSettingsLookup[HOST]; }
            set { m_owner.BackendSettingsLookup[HOST] = value; }
        }

        public int Port
        {
            get
            {
                int portn = 21;
                int.TryParse(m_owner.BackendSettingsLookup[PORT], out portn);
                return portn;
            }
            set { m_owner.BackendSettingsLookup[PORT] = value.ToString(); }
        }

        public string Folder
        {
            get { return m_owner.BackendSettingsLookup[FOLDER]; }
            set { m_owner.BackendSettingsLookup[FOLDER] = value; }
        }

        public bool Passive
        {
            get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.BackendSettingsLookup[PASSIVE], false); }
            set { m_owner.BackendSettingsLookup[PASSIVE] = value.ToString(); }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            return "ftp://" + this.Host + ":" +  this.Port + "/" + this.Folder;
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            options["ftp-username"] = this.Username;
            options["ftp-password"] = this.Password;
            if (this.Passive)
                options["ftp-passive"] = "";
            else
                options["ftp-regular"] = "";
        }

        public string FriendlyName { get { return "FTP host"; } }
        public string SystemName { get { return "ftp"; } }
        public void SetService() { m_owner.Service = this.SystemName; }

        #endregion
    }
}
