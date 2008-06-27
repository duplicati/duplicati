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

        private Task m_owner;

        public FTP(Task owner)
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
                    portn = 21;
                return portn;
            }
            set { m_owner.Settings[PORT] = value.ToString(); }
        }

        public string Folder
        {
            get { return m_owner.Settings[FOLDER]; }
            set { m_owner.Settings[FOLDER] = value; }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            return "ftp://" + this.Username + "@" + this.Host + "/" + this.Folder;
        }

        public void GetExtraSettings(List<string> args, System.Collections.Specialized.StringDictionary env)
        {
            env["FTP_PASSWORD"] = this.Password;
        }

        #endregion
    }
}
