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
    public class SSH : IBackend
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string HOST = "Host";
        private const string FOLDER = "Folder";
        private const string PASWORDLESS = "Passwordless";
        private const string PORT = "Port";

        private Task m_owner;

        public SSH(Task owner)
        {
            m_owner = owner;
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

        public string Host
        {
            get { return m_owner.Settings[HOST]; }
            set { m_owner.Settings[HOST] = value; }
        }

        public int Port
        {
            get
            {
                string port = m_owner.Settings[PORT];
                int portn;
                if (!int.TryParse(port, out portn))
                    portn = 22;
                return portn;
            }
            set { m_owner.Settings[PORT] = value.ToString(); }
        }

        public string Folder
        {
            get { return m_owner.Settings[FOLDER]; }
            set { m_owner.Settings[FOLDER] = value; }
        }

        public bool Passwordless
        {
            get
            {
                bool v;
                if (bool.TryParse(m_owner.Settings[PASWORDLESS], out v))
                    return v;
                else
                    return false;
            }
            set { m_owner.Settings[PASWORDLESS] = value.ToString(); }
        }


        #region IBackend Members

        public string GetDestinationPath()
        {
            return "ssh://" + this.Username + "@" + this.Host + "/" + this.Folder;
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            if (!this.Passwordless)
                options.Add("ssh-askpass", "");
            else
                options["ftp_password"] = this.Password;

            if (!options.ContainsKey("ssh-options"))
                options["ssh-options"] = "";

            if (this.Port != 22)
                options["ssh-options"] += "-P " + this.Port;
        }

        public string FriendlyName { get { return "SSH host"; } }
        public string SystemName { get { return "ssh"; } }
        public void SetService() { m_owner.Service = this.SystemName; }

        #endregion
    }
}
