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
using System.Net;

namespace Duplicati.Library.Backend
{
    public class CloudFiles : IBackend, IStreamingBackend
    {
        private const string AUTH_URL = "https://api.mosso.com/auth";
        private const int ITEM_LIST_LIMIT = 1000;
        private string m_username;
        private string m_password;
        private string m_path;
        Dictionary<string, string> m_options;


        public CloudFiles()
        {
        }

        public CloudFiles(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                if (u.UserInfo.IndexOf(":") >= 0)
                {
                    m_username = u.UserInfo.Substring(0, u.UserInfo.IndexOf(":"));
                    m_password = u.UserInfo.Substring(u.UserInfo.IndexOf(":") + 1);
                }
                else
                {
                    m_username = u.UserInfo;
                    if (options.ContainsKey("cloudfiles_accesskey"))
                        m_password = options["cloudfiles_accesskey"];
                    else if (options.ContainsKey("ftp-password"))
                        m_password = options["ftp-password"];
                }
            }
            else
            {
                if (options.ContainsKey("cloudfiles_username"))
                    m_username = options["cloudfiles_username"];
                if (options.ContainsKey("cloudfiles_accesskey"))
                    m_password = options["cloudfiles_accesskey"];
            }

            if (string.IsNullOrEmpty(m_username))
                throw new Exception("No CloudFiles userID given");
            if (string.IsNullOrEmpty(m_password))
                throw new Exception("No CloudFiles API Access Key given");

            m_options = options;
            m_path = u.Host + u.PathAndQuery;
            if (m_path.EndsWith("/"))
                m_path = m_path.Substring(0, m_path.Length - 1);
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return "CloudFiles"; }
        }

        public string ProtocolKey
        {
            get { return "cloudfiles:"; }
        }

        public List<FileEntry> List()
        {
            List<FileEntry> files = new List<FileEntry>();
            string storageToken = Guid.NewGuid().ToString();
            string extraUrl = "?format=xml&limit=" + ITEM_LIST_LIMIT.ToString();
            string markerUrl = "";

            bool repeat;

            do
            {
                System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
                HttpWebRequest req = CreateRequest(extraUrl + markerUrl);
                req.Headers.Add("X-Storage-Token", storageToken);
                using (WebResponse resp = req.GetResponse())
                using (System.IO.Stream s = resp.GetResponseStream())
                    doc.Load(s);

                string lastItemName = "";
                System.Xml.XmlNodeList lst = doc.SelectNodes("container/object");

                foreach (System.Xml.XmlNode n in lst)
                {
                    string name = n["name"].InnerText;
                    long size;
                    DateTime mod;

                    if (!long.TryParse(n["bytes"].InnerText, out size))
                        size = -1;
                    if (!DateTime.TryParse(n["last_modified"].InnerText, out mod))
                        mod = new DateTime();

                    lastItemName = name;
                    files.Add(new FileEntry(name, size, mod, mod));
                }

                repeat = lst.Count == ITEM_LIST_LIMIT;

                if (repeat)
                    markerUrl = "&marker=" + System.Web.HttpUtility.UrlEncode(lastItemName);

            } while (repeat);

            return files;
        }

        public void Put(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                Put(remotename, fs);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            HttpWebRequest req = CreateRequest("/" + remotename);

            req.Method = "DELETE";
            using (WebResponse resp = req.GetResponse())
            using (resp.GetResponseStream())
            { }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get 
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, "Supplies the password used to connect to the server", "The password used to connect to the server. This may also be supplied as the environment variable \"FTP_PASSWORD\"."),
                    new CommandLineArgument("cloudfiles_username", CommandLineArgument.ArgumentType.String, "Supplies the username used to authenticate with CloudFiles", "Supplies the username used to authenticate with CloudFiles."),
                    new CommandLineArgument("cloudfiles_accesskey", CommandLineArgument.ArgumentType.String, "Supplies the access key used to connect to the server", "Supplies the API Access Key used to authenticate with CloudFiles."),
                });
            }
        }

        public string Description
        {
            get { return "Supports connections to the CloudFiles backend. Allowed formats are \"cloudfiles://container/folder\" or \"cloudfiles://username:password@container/folder\"."; }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public void Put(string remotename, System.IO.Stream stream)
        {
            HttpWebRequest req = CreateRequest("/" + remotename);
            req.Method = "GET";

            using (WebResponse resp = req.GetResponse())
            using (System.IO.Stream s = resp.GetResponseStream())
            using (MD5CalculatingStream mds = new MD5CalculatingStream(s))
            {

                string md5Hash = resp.Headers["ETag"];
                Core.Utility.CopyStream(mds, stream);

                if (mds.GetFinalHashString() != md5Hash)
                    throw new Exception("MD5 Hash (ETag) verification failed");
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            HttpWebRequest req = CreateRequest("/" + remotename);
            req.Method = "PUT";

            try { req.ContentLength = stream.Length; }
            catch { }

            string fileHash = null;

            //TODO: It would be better if we knew the MD5 sum in advance,
            // so we could send it to the server along with the data
            using (System.IO.Stream s = req.GetRequestStream())
            using (MD5CalculatingStream mds = new MD5CalculatingStream(s))
            {
                Core.Utility.CopyStream(stream, mds);
                fileHash = mds.GetFinalHashString();
            }

            string md5Hash = null;

            using (WebResponse resp = req.GetResponse())
                md5Hash = resp.Headers["ETag"];

            if (md5Hash != fileHash)
            {
                //Remove the broken file
                try { Delete(remotename); }
                catch { }

                throw new Exception("MD5 Hash (ETag) verification failed");
            }
        }

        #endregion

        private HttpWebRequest CreateRequest(string remotename)
        {
            string storageUrl;
            string authToken;

            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(AUTH_URL);
            req.Headers.Add("X-Auth-User", m_username);
            req.Headers.Add("X-Auth-Key", m_password);
            req.Method = "GET";

            using (WebResponse resp = req.GetResponse())
            {
                storageUrl = resp.Headers["X-Storage-Url"];
                authToken = resp.Headers["X-Auth-Token"];
            }

            if (string.IsNullOrEmpty(authToken) || string.IsNullOrEmpty(storageUrl))
                throw new Exception("Unexpected CloudFiles response, perhaps the API has changed?");

            req = (HttpWebRequest)HttpWebRequest.Create(storageUrl + m_path + remotename);
            req.Headers.Add("X-Auth-Token", authToken);

            return req;

        }
    }
}
