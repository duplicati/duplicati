// Copyright (C) 2024, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class WEBDAV : IBackend, IStreamingBackend
    {
        private readonly System.Net.NetworkCredential m_userInfo;
        private readonly string m_url;
        private readonly string m_path;
        private readonly string m_sanitizedUrl;
        private readonly string m_reverseProtocolUrl;
        private readonly string m_rawurl;
        private readonly string m_rawurlPort;
        private readonly string m_dnsName;
        private readonly bool m_useIntegratedAuthentication = false;
        private readonly bool m_forceDigestAuthentication = false;
        private readonly bool m_useSSL = false;
        private readonly string m_debugPropfindFile = null;
        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        /// <summary>
        /// A list of files seen in the last List operation.
        /// It is used to detect a problem with IIS where a file is listed,
        /// but IIS responds 404 because the file mapping is incorrect.
        /// </summary>
        private List<string> m_filenamelist = null;

        // According to the WEBDAV standard, the "allprop" request should return all properties, however this seems to fail on some servers (box.net).
        // I've found this description: http://www.webdav.org/specs/rfc2518.html#METHOD_PROPFIND
        //  "An empty PROPFIND request body MUST be treated as a request for the names and values of all properties."
        //
        //private static readonly byte[] PROPFIND_BODY = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:allprop/></D:propfind>");
        private static readonly byte[] PROPFIND_BODY = new byte[0];

        public WEBDAV()
        {
        }

        public WEBDAV(string url, Dictionary<string, string> options)
        {
            var u = new Utility.Uri(url);
            u.RequireHost();
            m_dnsName = u.Host;

            if (!string.IsNullOrEmpty(u.Username))
            {
                m_userInfo = new System.Net.NetworkCredential();
                m_userInfo.UserName = u.Username;
                if (!string.IsNullOrEmpty(u.Password))
                    m_userInfo.Password = u.Password;
                else if (options.ContainsKey("auth-password"))
                    m_userInfo.Password = options["auth-password"];
            }
            else
            {
                if (options.ContainsKey("auth-username"))
                {
                    m_userInfo = new System.Net.NetworkCredential();
                    m_userInfo.UserName = options["auth-username"];
                    if (options.ContainsKey("auth-password"))
                        m_userInfo.Password = options["auth-password"];
                }
            }
            
            //Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
            if (m_userInfo != null)
                m_userInfo.Domain = "";

            m_useIntegratedAuthentication = Utility.Utility.ParseBoolOption(options, "integrated-authentication");
            m_forceDigestAuthentication = Utility.Utility.ParseBoolOption(options, "force-digest-authentication");
            m_useSSL = Utility.Utility.ParseBoolOption(options, "use-ssl");

            m_url = u.SetScheme(m_useSSL ? "https" : "http").SetCredentials(null, null).SetQuery(null).ToString();
            m_url = Util.AppendDirSeparator(m_url, "/");

            m_path = u.Path;
            if (!m_path.StartsWith("/", StringComparison.Ordinal))
                m_path = "/" + m_path;
            m_path = Util.AppendDirSeparator(m_path, "/");

            m_path = Library.Utility.Uri.UrlDecode(m_path);
            m_rawurl = new Utility.Uri(m_useSSL ? "https" : "http", u.Host, m_path).ToString();

            int port = u.Port;
            if (port <= 0)
                port = m_useSSL ? 443 : 80;

            m_rawurlPort = new Utility.Uri(m_useSSL ? "https" : "http", u.Host, m_path, null, null, null, port).ToString();
            m_sanitizedUrl = new Utility.Uri(m_useSSL ? "https" : "http", u.Host, m_path).ToString();
            m_reverseProtocolUrl = new Utility.Uri(m_useSSL ? "http" : "https", u.Host, m_path).ToString();
            options.TryGetValue("debug-propfind-file", out m_debugPropfindFile);
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.WEBDAV.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "webdav"; }
        }

        public IEnumerable<IFileEntry> List()
        {
            try
            {
                return this.ListWithouExceptionCatch();
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response as System.Net.HttpWebResponse != null &&
                        ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict))
                    throw new Interface.FolderMissingException(Strings.WEBDAV.MissingFolderError(m_path, wex.Message), wex);

                if (wex.Response as System.Net.HttpWebResponse != null && (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
                    throw new UserInformationException(Strings.WEBDAV.MethodNotAllowedError((wex.Response as System.Net.HttpWebResponse).StatusCode), "WebdavMethodNotAllowed", wex);

                throw;
            }
        }

        private IEnumerable<IFileEntry> ListWithouExceptionCatch()
        {
            var req = CreateRequest("");

            req.Method = "PROPFIND";
            req.Headers.Add("Depth", "1");
            req.ContentType = "text/xml";
            req.ContentLength = PROPFIND_BODY.Length;

            var areq = new Utility.AsyncHttpRequest(req);
            using (System.IO.Stream s = areq.GetRequestStream())
                s.Write(PROPFIND_BODY, 0, PROPFIND_BODY.Length);

            var doc = new System.Xml.XmlDocument();
            using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                if (!string.IsNullOrEmpty(m_debugPropfindFile))
                {
                    using (var rs = areq.GetResponseStream())
                    using (var fs = new System.IO.FileStream(m_debugPropfindFile, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None))
                        Utility.Utility.CopyStream(rs, fs, false, m_copybuffer);

                    doc.Load(m_debugPropfindFile);
                }
                else
                {
                    using (var rs = areq.GetResponseStream())
                        doc.Load(rs);
                }
            }

            System.Xml.XmlNamespaceManager nm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nm.AddNamespace("D", "DAV:");

            List<IFileEntry> files = new List<IFileEntry>();
            m_filenamelist = new List<string>();

            foreach (System.Xml.XmlNode n in doc.SelectNodes("D:multistatus/D:response/D:href", nm))
            {
                //IIS uses %20 for spaces and %2B for +
                //Apache uses %20 for spaces and + for +
                string name = Library.Utility.Uri.UrlDecode(n.InnerText.Replace("+", "%2B"));

                string cmp_path;

                //TODO: This list is getting ridiculous, should change to regexps

                if (name.StartsWith(m_url, StringComparison.Ordinal))
                    cmp_path = m_url;
                else if (name.StartsWith(m_rawurl, StringComparison.Ordinal))
                    cmp_path = m_rawurl;
                else if (name.StartsWith(m_rawurlPort, StringComparison.Ordinal))
                    cmp_path = m_rawurlPort;
                else if (name.StartsWith(m_path, StringComparison.Ordinal))
                    cmp_path = m_path;
                else if (name.StartsWith("/" + m_path, StringComparison.Ordinal))
                    cmp_path = "/" + m_path;
                else if (name.StartsWith(m_sanitizedUrl, StringComparison.Ordinal))
                    cmp_path = m_sanitizedUrl;
                else if (name.StartsWith(m_reverseProtocolUrl, StringComparison.Ordinal))
                    cmp_path = m_reverseProtocolUrl;
                else
                    continue;

                if (name.Length <= cmp_path.Length)
                    continue;

                name = name.Substring(cmp_path.Length);

                long size = -1;
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
                    else
                        isCollection = (stat.SelectSingleNode("D:resourcetype/D:collection", nm) != null);
                }

                FileEntry fe = new FileEntry(name, size, lastAccess, lastModified);
                fe.IsFolder = isCollection;
                files.Add(fe);
                m_filenamelist.Add(name);
            }
            
            return files;
        }

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken);
        }

        public void Get(string remotename, string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Get(remotename, fs);
        }

        public void Delete(string remotename)
        {
            try
            {
                System.Net.HttpWebRequest req = CreateRequest(remotename);
                req.Method = "DELETE";
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new FileMissingException();

                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                }
            } 
            catch (System.Net.WebException wex)
            {
                if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw;
            }
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get 
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.WEBDAV.DescriptionAuthPasswordShort, Strings.WEBDAV.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.WEBDAV.DescriptionAuthUsernameShort, Strings.WEBDAV.DescriptionAuthUsernameLong),
                    new CommandLineArgument("integrated-authentication", CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionIntegratedAuthenticationShort, Strings.WEBDAV.DescriptionIntegratedAuthenticationLong),
                    new CommandLineArgument("force-digest-authentication", CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionForceDigestShort, Strings.WEBDAV.DescriptionForceDigestLong),
                    new CommandLineArgument("use-ssl", CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionUseSSLShort, Strings.WEBDAV.DescriptionUseSSLLong),
                    new CommandLineArgument("debug-propfind-file", CommandLineArgument.ArgumentType.Path, Strings.WEBDAV.DescriptionDebugPropfindShort, Strings.WEBDAV.DescriptionDebugPropfindLong),
                });
            }
        }

        public string Description
        {
            get { return Strings.WEBDAV.Description; }
        }

        public string[] DNSName 
        {
            get { return new string[] { m_dnsName }; }
        }

        public void Test()
        {
            this.List();
        }

        public void CreateFolder()
        {
            System.Net.HttpWebRequest req = CreateRequest("");
            req.Method = System.Net.WebRequestMethods.Http.MkCol;
            req.KeepAlive = false;
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
            using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
            {
                int code = (int)resp.StatusCode;
                if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                    throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
            }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        private System.Net.HttpWebRequest CreateRequest(string remotename)
        {
            System.Net.HttpWebRequest req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(m_url + Library.Utility.Uri.UrlEncode(remotename).Replace("+", "%20"));
            if (m_useIntegratedAuthentication)
            {
                req.UseDefaultCredentials = true;
            }
            else if (m_forceDigestAuthentication)
            {
                System.Net.CredentialCache cred = new System.Net.CredentialCache();
                cred.Add(new Uri(m_url), "Digest", m_userInfo);
                req.Credentials = cred;
            }
            else
            {
                req.Credentials = m_userInfo;
                //We need this under Mono for some reason,
                // and it appears some servers require this as well
                req.PreAuthenticate = true; 
            }

            req.KeepAlive = false;
            req.UserAgent = "Duplicati WEBDAV Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

            return req;
        }

        #region IStreamingBackend Members

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            try
            {
                System.Net.HttpWebRequest req = CreateRequest(remotename);
                req.Method = System.Net.WebRequestMethods.Http.Put;
                req.ContentType = "application/octet-stream";

                try { req.ContentLength = stream.Length; }
                catch { }

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.IO.Stream s = areq.GetRequestStream())
                    await Utility.Utility.CopyStreamAsync(stream, s, true, cancelToken, m_copybuffer);

                using (System.Net.HttpWebResponse resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                }
            }
            catch (System.Net.WebException wex)
            {
                //Convert to better exception
                if (wex.Response as System.Net.HttpWebResponse != null)
                    if ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new Interface.FolderMissingException(Strings.WEBDAV.MissingFolderError(m_path, wex.Message), wex);

                throw;
            }
        }

        public void Get(string remotename, System.IO.Stream stream)
        {
            var req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Http.Get;

            try
            {
                var areq = new Utility.AsyncHttpRequest(req);
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);

                    using (var s = areq.GetResponseStream())
                        Utility.Utility.CopyStream(s, stream, true, m_copybuffer);
                }
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response as System.Net.HttpWebResponse != null)
                {
                    if ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict)
                        throw new Interface.FolderMissingException(Strings.WEBDAV.MissingFolderError(m_path, wex.Message), wex);

                    if
                    (
                        (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound
                        &&
                        m_filenamelist != null
                        &&
                        m_filenamelist.Contains(remotename)
                    )
                        throw new Exception(Strings.WEBDAV.SeenThenNotFoundError(m_path, remotename, System.IO.Path.GetExtension(remotename), wex.Message), wex);
                }

                throw;
            }
        }

        #endregion
    }
}
