using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Backend.WEBDAV
{
    public class WEBDAV : IBackend, IStreamingBackend
    {
        private System.Net.NetworkCredential m_userInfo;
        private string m_url;
        Dictionary<string, string> m_options;
        private bool m_useIntegratedAuthentication = false;
        private bool m_forceDigestAuthentication = false;

        private static readonly byte[] PROPFIND = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:allprop/></D:propfind>");

        public WEBDAV()
        {
        }

        public WEBDAV(string url, Dictionary<string, string> options)
        {
            Uri u = new Uri(url);

            if (!string.IsNullOrEmpty(u.UserInfo))
            {
                m_userInfo = new System.Net.NetworkCredential();
                if (u.UserInfo.IndexOf(":") >= 0)
                {
                    m_userInfo.UserName = u.UserInfo.Substring(0, u.UserInfo.IndexOf(":"));
                    m_userInfo.Password = u.UserInfo.Substring(u.UserInfo.IndexOf(":") + 1);
                }
                else
                {
                    m_userInfo.UserName = u.UserInfo;
                    if (options.ContainsKey("ftp-password"))
                        m_userInfo.Password = options["ftp-password"];
                }
            }

            m_options = options;
            m_useIntegratedAuthentication = m_options.ContainsKey("integrated-authentication");
            m_forceDigestAuthentication = m_options.ContainsKey("force-digest-authentication");
            m_url = "http" + url.Substring(u.Scheme.Length);
            if (!m_url.EndsWith("/"))
                m_url += "/";
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return "WEBDAV"; }
        }

        public string ProtocolKey
        {
            get { return "webdav"; }
        }

        public List<FileEntry> List()
        {
            System.Net.HttpWebRequest req = CreateRequest("");

            req.Method = "PROPFIND";
            req.Headers.Add("Depth", "1");
            req.ContentType = "text/xml";
            req.ContentLength = PROPFIND.Length;

            using (System.IO.Stream s = req.GetRequestStream())
                s.Write(PROPFIND, 0, PROPFIND.Length);

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            using (System.Net.WebResponse resp = req.GetResponse())
                doc.Load(resp.GetResponseStream());

            System.Xml.XmlNamespaceManager nm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nm.AddNamespace("D", "DAV:");

            List<FileEntry> files = new List<FileEntry>();

            foreach (System.Xml.XmlNode n in doc.SelectNodes("D:multistatus/D:response/D:href", nm))
            {
                string name = n.InnerText;

                if (name.Length <= m_url.Length)
                    continue;

                name = System.Web.HttpUtility.UrlDecode(name.Substring(m_url.Length));

                long size = 0;
                DateTime lastAccess = new DateTime();
                DateTime lastModified = new DateTime();
                bool isCollection = false;

                System.Xml.XmlNode stat = n.ParentNode.SelectSingleNode("D:propstat/D:prop", nm);
                if (stat != null)
                {
                    System.Xml.XmlNode s = stat.SelectSingleNode("D:getcontentlength", nm);
                    if (s != null)
                        size = long.Parse(s.InnerText);
                    s = stat.SelectSingleNode("D:getlastmodified", nm);
                    if (s != null)
                        try
                        {
                            //Not important if this succeeds
                            lastAccess = lastModified = DateTime.Parse(s.InnerText, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        catch { }

                    s = stat.SelectSingleNode("D:iscollection", nm);
                    if (s != null)
                        isCollection = s.InnerText.Trim() == "1";
                }

                FileEntry fe = new FileEntry(name, size, lastAccess, lastModified);
                fe.IsFolder = isCollection;
                files.Add(fe);
            }

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
            System.Net.HttpWebRequest req = CreateRequest(remotename);
            req.Method = "DELETE";
            using (req.GetResponse())
            { }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get 
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("ftp-password", CommandLineArgument.ArgumentType.String, "Supplies the password used to connect to the server", "The password used to connect to the server. This may also be supplied as the environment variable \"FTP_PASSWORD\"."),
                    new CommandLineArgument("integrated-authentication", CommandLineArgument.ArgumentType.Boolean, "Use windows integrated authentication to connect to the server", "If the server and client both supports integrated authentication, this option enables that authentication method. This is likely only avalible with windows servers and clients."),
                    new CommandLineArgument("force-digest-authentication", CommandLineArgument.ArgumentType.Boolean, "Force the use of the HTTP Digest authentication method", "Using the HTTP Digest authentication method allows the user to authenticate with the server, without sending the password in clear. However, a man-in-the-middle attack is easy, because the HTTP protocol specifies a fallback to Basic authentication, which will make the client send the password to the attacker. Using this flag, the client doest not accept this, and always uses Digest authentication or fails to connect."),
                });
            }
        }

        public string Description
        {
            get { return "Supports connections to a WEBDAV enabled web server"; }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            //throw new Exception("The method or operation is not implemented.");
        }

        #endregion

        private System.Net.HttpWebRequest CreateRequest(string remotename)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(m_url + System.Web.HttpUtility.UrlEncode(remotename));
            if (m_useIntegratedAuthentication)
                req.UseDefaultCredentials = true;
            else if (m_forceDigestAuthentication)
            {
                System.Net.CredentialCache cred = new System.Net.CredentialCache();
                cred.Add(new Uri(m_url), "Digest", m_userInfo);
                req.Credentials = cred;
            }
            else
                req.Credentials = m_userInfo;
            req.KeepAlive = false;
            req.UserAgent = "Duplicati WEBDAV Client";

            return req;
        }

        #region IStreamingBackend Members

        public bool SupportsStreaming
        {
            get { return true; }
        }

        public void Put(string remotename, System.IO.Stream stream)
        {
            System.Net.HttpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Http.Put;
            req.ContentType = "application/binary";

            try { req.ContentLength = stream.Length; }
            catch { }

            using (System.IO.Stream s = req.GetRequestStream())
                Core.Utility.CopyStream(stream, s);

            using (req.GetResponse())
            { }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            System.Net.HttpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Http.Get;

            using (System.Net.WebResponse resp = req.GetResponse())
            using (System.IO.Stream s = resp.GetResponseStream())
                Core.Utility.CopyStream(s, stream);
        }

        #endregion
    }
}
