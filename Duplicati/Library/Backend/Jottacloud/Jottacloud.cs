#region Disclaimer / License
// Copyright (C) 2015, The Duplicati Team
// http://www.duplicati.com, info@duplicati.com
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
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class Jottacloud : IBackend, IStreamingBackend
    {
        public const string JFS_ROOT = "https://www.jottacloud.com/jfs";
        public const string API_VERSION = "2.2"; // Hard coded per October 2014
        public const string DateTimeFormat = "yyyy'-'MM'-'dd-'T'HH':'mm':'ssK";
        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        private System.Net.NetworkCredential m_userInfo;
        private string m_path;
        private string m_url;

        public Jottacloud()
        {
        }

        public Jottacloud(string url, Dictionary<string, string> options)
        {
            var u = new Utility.Uri(url);
            m_path = u.HostAndPath;
            if (string.IsNullOrEmpty(m_path))
                throw new UserInformationException(Strings.Jottacloud.NoPathError);
            if (!m_path.EndsWith("/"))
                m_path += "/";
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
            if (m_userInfo == null || string.IsNullOrEmpty(m_userInfo.UserName))
                throw new UserInformationException(Strings.Jottacloud.NoUsernameError);
            if (m_userInfo == null || string.IsNullOrEmpty(m_userInfo.Password))
                throw new UserInformationException(Strings.Jottacloud.NoPasswordError);

            m_url = JFS_ROOT + "/" + m_userInfo.UserName + "/Jotta/" + m_path; // Hard coding device name "Jotta"

            //Bugfix, see http://connect.microsoft.com/VisualStudio/feedback/details/695227/networkcredential-default-constructor-leaves-domain-null-leading-to-null-object-reference-exceptions-in-framework-code
            if (m_userInfo != null)
                m_userInfo.Domain = "";

        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.Jottacloud.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "jottacloud"; }
        }

        public List<IFileEntry> List()
        {
            var req = CreateRequest("");
            req.Method = System.Net.WebRequestMethods.Http.Get;
            try
            {
                var areq = new Utility.AsyncHttpRequest(req);
                var doc = new System.Xml.XmlDocument();
                using (var resp = (System.Net.HttpWebResponse)areq.GetResponse())
                {
                    int code = (int)resp.StatusCode;
                    if (code < 200 || code >= 300) //For some reason Mono does not throw this automatically
                        throw new System.Net.WebException(resp.StatusDescription, null, System.Net.WebExceptionStatus.ProtocolError, resp);
                    using (var rs = areq.GetResponseStream())
                        doc.Load(rs);
                }
                // Parse XML response. Note that root element can be "mountPoint" or "folder", but the content is very similar.
                List<IFileEntry> files = new List<IFileEntry>();
                foreach (System.Xml.XmlNode xFolder in doc.SelectNodes("//folders/folder"))
                {
                    // Folders have only name in Jottacloud file system.
                    FileEntry fe = new FileEntry(xFolder.Attributes["name"].Value);
                    fe.IsFolder = true;
                    files.Add(fe);
                }
                foreach (System.Xml.XmlNode xFile in doc.SelectNodes("//files/file"))
                {
                    string name = xFile.Attributes["name"].Value;
                    // Normal files have "currentRevision", incomplete or corrupt files have "latestRevision" or "revision" instead.
                    System.Xml.XmlNode xRevision = xFile.SelectSingleNode("currentRevision");
                    if (xRevision != null)
                    {
                        System.Xml.XmlNode xNode = xRevision.SelectSingleNode("size");
                        long size = 0;
                        if (xNode != null)
                        {
                            size = long.Parse(xNode.InnerText);
                        }
                        DateTime lastAccess = new DateTime();
                        DateTime lastModified = new DateTime();
                        xNode = xRevision.SelectSingleNode("modified"); // There is also a timestamp for "updated"?
                        if (xNode != null)
                        {
                            lastAccess = lastModified = DateTime.ParseExact(xNode.InnerText, DateTimeFormat, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        FileEntry fe = new FileEntry(name, size, lastAccess, lastModified);
                        files.Add(fe);
                    }
                }
                return files;
            }
            catch (System.Net.WebException wex)
            {
                if (wex.Response as System.Net.HttpWebResponse != null &&
                        ((wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.NotFound || (wex.Response as System.Net.HttpWebResponse).StatusCode == System.Net.HttpStatusCode.Conflict))
                    throw new Interface.FolderMissingException(Strings.Jottacloud.MissingFolderError(req.RequestUri.PathAndQuery, wex.Message), wex);

                throw;
            }
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
                if (wex.Response is System.Net.HttpWebResponse && ((System.Net.HttpWebResponse)wex.Response).StatusCode == System.Net.HttpStatusCode.NotFound)
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
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.Jottacloud.DescriptionAuthPasswordShort, Strings.Jottacloud.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.Jottacloud.DescriptionAuthUsernameShort, Strings.Jottacloud.DescriptionAuthUsernameLong),
                });
            }
        }

        public string Description
        {
            get { return Strings.Jottacloud.Description; }
        }

        public void Test()
        {
            List();
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
            req.Credentials = m_userInfo;
            //We need this under Mono for some reason,
            // and it appears some servers require this as well
            req.PreAuthenticate = true; 

            req.KeepAlive = false;
            req.UserAgent = "Duplicati Jottacloud Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            req.Headers.Add("X-JottaAPIVersion", API_VERSION);

            return req;
        }

        #region IStreamingBackend Members

        public void Put(string remotename, System.IO.Stream stream)
        {
            System.Net.HttpWebRequest req = CreateRequest(remotename);
            req.Method = System.Net.WebRequestMethods.Http.Put;
            req.ContentType = "application/octet-stream";
            try
            {
                try { req.ContentLength = stream.Length; }
                catch { }

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.IO.Stream s = areq.GetRequestStream())
                    Utility.Utility.CopyStream(stream, s, true, m_copybuffer);

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
                        throw new Interface.FolderMissingException(Strings.Jottacloud.MissingFolderError(req.RequestUri.PathAndQuery, wex.Message), wex);

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
                        throw new Interface.FolderMissingException(Strings.Jottacloud.MissingFolderError(req.RequestUri.PathAndQuery, wex.Message), wex);
                }

                throw;
            }
        }

        #endregion
    }
}
