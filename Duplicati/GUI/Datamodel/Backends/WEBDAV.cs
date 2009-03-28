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
                int portn = 80;
                int.TryParse(m_owner.Settings[PORT], out portn);
                return portn;
            }
            set { m_owner.Settings[PORT] = value.ToString(); }
        }

        public string Folder
        {
            get { return m_owner.Settings[FOLDER]; }
            set { m_owner.Settings[FOLDER] = value; }
        }

        public bool IntegratedAuthentication
        {
            get
            {
                bool res = false;
                if (!bool.TryParse(m_owner.Settings[INTEGRATED_AUTHENTICATION], out res))
                    return false;
                else
                    return res;
            }
            set { m_owner.Settings[INTEGRATED_AUTHENTICATION] = value.ToString(); }
        }

        public bool ForceDigestAuthentication
        {
            get
            {
                bool res = false;
                if (!bool.TryParse(m_owner.Settings[FORCE_DIGEST_AUTHENTICATION], out res))
                    return false;
                else
                    return res;
            }
            set { m_owner.Settings[FORCE_DIGEST_AUTHENTICATION] = value.ToString(); }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            if (string.IsNullOrEmpty(this.Username))
                return "webdav://" + this.Host + ":" + this.Port + "/" + this.Folder;
            else
                return "webdav://" + this.Username + "@" + this.Host + ":" +  this.Port + "/" + this.Folder;
        }

        public void GetOptions(Dictionary<string, string> options)
        {
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
