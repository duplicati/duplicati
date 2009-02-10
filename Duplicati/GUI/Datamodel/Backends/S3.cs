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
    public class S3 : IBackend
    {
        private const string ACCESS_ID = "AccessID";
        private const string ACCESS_KEY = "AccessKey";
        private const string BUCKET_NAME = "Bucketname";
        private const string EUROBUCKET = "UseEuroBucket";
        private const string SUBDOMAIN = "UseSubdomainStrategy";
        private const string SERVER_URL = "ServerUrl";
        private const string PREFIX = "Prefix";

        private const string S3_PATH = "s3.amazonaws.com";

        private Task m_owner;

        public S3(Task owner)
        {
            m_owner = owner;
        }

        public string AccessID
        {
            get { return m_owner.Settings[ACCESS_ID]; }
            set { m_owner.Settings[ACCESS_ID] = value; }
        }

        public string AccessKey
        {
            get { return m_owner.Settings[ACCESS_KEY]; }
            set { m_owner.Settings[ACCESS_KEY] = value; }
        }

        public string BucketName
        {
            get { return m_owner.Settings[BUCKET_NAME]; }
            set { m_owner.Settings[BUCKET_NAME] = value; }
        }

        public string ServerUrl
        {
            get { return m_owner.Settings[SERVER_URL]; }
            set { m_owner.Settings[SERVER_URL] = value; }
        }

        public string Prefix
        {
            get { return m_owner.Settings[PREFIX]; }
            set { m_owner.Settings[PREFIX] = value; }
        }

        public bool UseEuroBucket
        {
            get 
            {
                bool v;
                if (bool.TryParse(m_owner.Settings[EUROBUCKET], out v))
                    return v;
                else
                    return false;
            }
            set { m_owner.Settings[EUROBUCKET] = value.ToString(); }
        }

        public bool UseSubdomainStrategy
        {
            get
            {
                bool v;
                if (bool.TryParse(m_owner.Settings[SUBDOMAIN], out v))
                    return v;
                else
                    return false;
            }
            set { m_owner.Settings[SUBDOMAIN] = value.ToString(); }
        }

        #region IBackend Members

        public string GetDestinationPath()
        {
            string host = this.ServerUrl;
            if (string.IsNullOrEmpty(host))
            {
                if (this.UseEuroBucket || this.UseSubdomainStrategy)
                    host = this.BucketName + "." +  S3_PATH;
                else
                    host = S3_PATH;
            }

            if (this.UseEuroBucket || this.UseSubdomainStrategy)
                return "s3://" + host + "/" + (string.IsNullOrEmpty(this.Prefix) ? "" : this.Prefix);
            else
                return "s3://" + host + "/" + this.BucketName + (string.IsNullOrEmpty(this.Prefix) ? "" : "/" + this.Prefix);

        }

        public void GetOptions(Dictionary<string, string> options)
        {
            options["aws_access_key_id"] = this.AccessID;
            options["aws_secret_access_key"] = this.AccessKey;
        }

        public string FriendlyName { get { return "Amazon S3"; } }
        public string SystemName { get { return "s3"; } }
        public void SetService() { m_owner.Service = this.SystemName; }

        #endregion
    }
}
