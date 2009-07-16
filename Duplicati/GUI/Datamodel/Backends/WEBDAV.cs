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
    public class WEBDAV : IBackend
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";
        private const string INTEGRATED_AUTHENTICATION = "Integrated Authentication";
        private const string FORCE_DIGEST_AUTHENTICATION = "Force Digest Authentication";

        private Task m_owner;

        public WEBDAV(Task owner)
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
                int portn = 80;
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

        public bool IntegratedAuthentication
        {
            get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.BackendSettingsLookup[INTEGRATED_AUTHENTICATION] , false); }
            set { m_owner.BackendSettingsLookup[INTEGRATED_AUTHENTICATION] = value.ToString(); }
        }

        public bool ForceDigestAuthentication
        {
            get { return Duplicati.Library.Core.Utility.ParseBool(m_owner.BackendSettingsLookup[FORCE_DIGEST_AUTHENTICATION] , false);}
            set { m_owner.BackendSettingsLookup[FORCE_DIGEST_AUTHENTICATION] = value.ToString(); }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            return "webdav://" + this.Host + ":" + this.Port + "/" + this.Folder;
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            if (!string.IsNullOrEmpty(this.Username))
                options["ftp-username"] = this.Username;

            options["ftp-password"] = this.Password;
            if (IntegratedAuthentication)
                options["integrated-authentication"] = "";
            if (ForceDigestAuthentication)
                options["force-digest-authentication"] = "";
        }

        public string FriendlyName { get { return "WEBDAV host"; } }
        public string SystemName { get { return "webdav"; } }
        public void SetService() { m_owner.Service = this.SystemName; }

        #endregion
    }
}
