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
using System.Net;
using Duplicati.Library.Interface;

namespace Duplicati.Library.Backend
{
    public class CloudFiles : IBackend, IStreamingBackend
    {
        public const string AUTH_URL_US = "https://identity.api.rackspacecloud.com/auth";
        public const string AUTH_URL_UK = "https://lon.auth.api.rackspacecloud.com/v1.0";
        private const string DUMMY_HOSTNAME = "api.mosso.com";

        public static readonly KeyValuePair<string, string>[] KNOWN_CLOUDFILES_PROVIDERS = new KeyValuePair<string, string>[] {
            new KeyValuePair<string, string>("Rackspace US", AUTH_URL_US),
            new KeyValuePair<string, string>("Rackspace UK", AUTH_URL_UK),
        };


        private const int ITEM_LIST_LIMIT = 1000;
        private string m_username;
        private string m_password;
        private string m_path;

        private string m_storageUrl = null;
        private string m_authToken = null;
        private string m_authUrl;

        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        public CloudFiles()
        {
        }

        public CloudFiles(string url, Dictionary<string, string> options)
        {
            var uri = new Utility.Uri(url);
            
            if (options.ContainsKey("auth-username"))
                m_username = options["auth-username"];
            if (options.ContainsKey("auth-password"))
                m_password = options["auth-password"];

            if (options.ContainsKey("cloudfiles-username"))
                m_username = options["cloudfiles-username"];
            if (options.ContainsKey("cloudfiles-accesskey"))
                m_password = options["cloudfiles-accesskey"];
            
            if (!string.IsNullOrEmpty(uri.Username))
                m_username = uri.Username;
            if (!string.IsNullOrEmpty(uri.Password))
                m_password = uri.Password;

            if (string.IsNullOrEmpty(m_username))
                throw new UserInformationException(Strings.CloudFiles.NoUserIDError);
            if (string.IsNullOrEmpty(m_password))
                throw new UserInformationException(Strings.CloudFiles.NoAPIKeyError);

            //Fallback to the previous format
            if (url.Contains(DUMMY_HOSTNAME))
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
                    }
                }

                //We use the api.mosso.com hostname.
                //This allows the use of containers that have names that are not valid hostnames, 
                // such as container names with spaces in them
                if (u.Host.Equals(DUMMY_HOSTNAME))
                    m_path = Library.Utility.Uri.UrlDecode(u.PathAndQuery);
                else
                    m_path = u.Host + Library.Utility.Uri.UrlDecode(u.PathAndQuery);
            }
            else
            {
                m_path = uri.HostAndPath;
            }

            if (m_path.EndsWith("/"))
                m_path = m_path.Substring(0, m_path.Length - 1);
            if (!m_path.StartsWith("/"))
                m_path = "/" + m_path;

            if (!options.TryGetValue("cloudfiles-authentication-url", out m_authUrl))
                m_authUrl = Utility.Utility.ParseBoolOption(options, "cloudfiles-uk-account") ? AUTH_URL_UK : AUTH_URL_US;
        }

        #region IBackend Members

        public string DisplayName
        {
            get { return Strings.CloudFiles.DisplayName; }
        }

        public string ProtocolKey
        {
            get { return "cloudfiles"; }
        }

        public List<IFileEntry> List()
        {
            var files = new List<IFileEntry>();
            string extraUrl = "?format=xml&limit=" + ITEM_LIST_LIMIT.ToString();
            string markerUrl = "";

            bool repeat;

            do
            {
                var doc = new System.Xml.XmlDocument();

                var req = CreateRequest("", extraUrl + markerUrl);

                try
                {
                    var areq = new Utility.AsyncHttpRequest(req);
                    using (var resp = (HttpWebResponse)areq.GetResponse())
                    using (var s = areq.GetResponseStream())
                        doc.Load(s);
                }
                catch (WebException wex)
                {
                    if (markerUrl == "") //Only check on first itteration
                        if (wex.Response is HttpWebResponse && ((HttpWebResponse)wex.Response).StatusCode == HttpStatusCode.NotFound)
                            throw new FolderMissingException(wex);
                    
                    //Other error, just re-throw
                    throw;
                }

                System.Xml.XmlNodeList lst = doc.SelectNodes("container/object");

                //Perhaps the folder does not exist?
                //The response should be 404 from the server, but it is not :(
                if (lst.Count == 0 && markerUrl == "") //Only on first itteration
                {
                    try { CreateFolder(); }
                    catch { } //Ignore
                }

                string lastItemName = "";
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
                    markerUrl = "&marker=" + Library.Utility.Uri.UrlEncode(lastItemName);

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
            try
            {
                HttpWebRequest req = CreateRequest("/" + remotename, "");

                req.Method = "DELETE";
                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (HttpWebResponse resp = (HttpWebResponse)areq.GetResponse())
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new FileMissingException();

                    if ((int)resp.StatusCode >= 300)
                        throw new WebException(Strings.CloudFiles.FileDeleteError, null, WebExceptionStatus.ProtocolError, resp);
                    else
                        using (areq.GetResponseStream())
                        { }
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
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.CloudFiles.DescriptionAuthPasswordShort, Strings.CloudFiles.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.CloudFiles.DescriptionAuthUsernameShort, Strings.CloudFiles.DescriptionAuthUsernameLong),
                    new CommandLineArgument("cloudfiles-username", CommandLineArgument.ArgumentType.String, Strings.CloudFiles.DescriptionUsernameShort, Strings.CloudFiles.DescriptionUsernameLong, null, new string[] {"auth-username"} ),
                    new CommandLineArgument("cloudfiles-accesskey", CommandLineArgument.ArgumentType.Password, Strings.CloudFiles.DescriptionPasswordShort, Strings.CloudFiles.DescriptionPasswordLong, null, new string[] {"auth-password"}),
                    new CommandLineArgument("cloudfiles-uk-account", CommandLineArgument.ArgumentType.Boolean, Strings.CloudFiles.DescriptionUKAccountShort, Strings.CloudFiles.DescriptionUKAccountLong("cloudfiles-authentication-url", AUTH_URL_UK)),
                    new CommandLineArgument("cloudfiles-authentication-url", CommandLineArgument.ArgumentType.String, Strings.CloudFiles.DescriptionAuthenticationURLShort, Strings.CloudFiles.DescriptionAuthenticationURLLong_v2("cloudfiles-uk-account"), AUTH_URL_US),
                });
            }
        }

        public string Description
        {
            get { return Strings.CloudFiles.Description_v2; }
        }

        #endregion

        #region IBackend_v2 Members
        
        public void Test()
        {
            //The "Folder not found" is not detectable :(
            List();
        }

        public void CreateFolder()
        {
            HttpWebRequest createReq = CreateRequest("", "");
            createReq.Method = "PUT";
            Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(createReq);
            using (HttpWebResponse resp = (HttpWebResponse)areq.GetResponse())
            { }
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

        public void Get(string remotename, System.IO.Stream stream)
        {
            var req = CreateRequest("/" + remotename, "");
            req.Method = "GET";

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = areq.GetResponse())
            using (var s = areq.GetResponseStream())
            using (var mds = new Utility.MD5CalculatingStream(s))
            {
                string md5Hash = resp.Headers["ETag"];
                Utility.Utility.CopyStream(mds, stream, true, m_copybuffer);

                if (mds.GetFinalHashString().ToLower() != md5Hash.ToLower())
                    throw new Exception(Strings.CloudFiles.ETagVerificationError);
            }
        }

        public void Put(string remotename, System.IO.Stream stream)
        {
            HttpWebRequest req = CreateRequest("/" + remotename, "");
            req.Method = "PUT";
            req.ContentType = "application/octet-stream";

            try { req.ContentLength = stream.Length; }
            catch { }

            //If we can pre-calculate the MD5 hash before transmission, do so
            /*if (stream.CanSeek)
            {
                System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create();
                req.Headers["ETag"] = Core.Utility.ByteArrayAsHexString(md5.ComputeHash(stream)).ToLower();
                stream.Seek(0, System.IO.SeekOrigin.Begin);

                using (System.IO.Stream s = req.GetRequestStream())
                    Core.Utility.CopyStream(stream, s);

                //Reset the timeout to the default value of 100 seconds to 
                // avoid blocking the GetResponse() call
                req.Timeout = 100000;

                //The server handles the eTag verification for us, and gives an error if the hash was a mismatch
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    if ((int)resp.StatusCode >= 300)
                        throw new WebException(Strings.CloudFiles.FileUploadError, null, WebExceptionStatus.ProtocolError, resp);

            }
            else //Otherwise use a client-side calculation
            */
            //TODO: We cannot use the local MD5 calculation, because that could involve a throttled read,
            // and may invoke various events
            {
                string fileHash = null;

                long streamLen = -1;
                try { streamLen = stream.Length; }
                catch {}


                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.IO.Stream s = areq.GetRequestStream(streamLen))
                using (var mds = new Utility.MD5CalculatingStream(s))
                {
                    Utility.Utility.CopyStream(stream, mds, true, m_copybuffer);
                    fileHash = mds.GetFinalHashString();
                }

                string md5Hash = null;

                //We need to verify the eTag locally
                try
                {
                    using (HttpWebResponse resp = (HttpWebResponse)areq.GetResponse())
                        if ((int)resp.StatusCode >= 300)
                            throw new WebException(Strings.CloudFiles.FileUploadError, null, WebExceptionStatus.ProtocolError, resp);
                        else
                            md5Hash = resp.Headers["ETag"];
                }
                catch (WebException wex)
                {
                    //Catch 404 and turn it into a FolderNotFound error
                    if (wex.Response is HttpWebResponse && ((HttpWebResponse)wex.Response).StatusCode == HttpStatusCode.NotFound)
                        throw new FolderMissingException(wex);

                    //Other error, just re-throw
                    throw;
                }


                if (md5Hash == null || md5Hash.ToLower() != fileHash.ToLower())
                {
                    //Remove the broken file
                    try { Delete(remotename); }
                    catch { }

                    throw new Exception(Strings.CloudFiles.ETagVerificationError);
                }
            }
        }

        #endregion

        private HttpWebRequest CreateRequest(string remotename, string query)
        {
            //If this is the first call, get an authentication token
            if (string.IsNullOrEmpty(m_authToken) || string.IsNullOrEmpty(m_storageUrl))
            {
                HttpWebRequest authReq = (HttpWebRequest)HttpWebRequest.Create(m_authUrl);
                authReq.Headers.Add("X-Auth-User", m_username);
                authReq.Headers.Add("X-Auth-Key", m_password);
                authReq.Method = "GET";

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(authReq);
                using (WebResponse resp = areq.GetResponse())
                {
                    m_storageUrl = resp.Headers["X-Storage-Url"];
                    m_authToken = resp.Headers["X-Auth-Token"];
                }

                if (string.IsNullOrEmpty(m_authToken) || string.IsNullOrEmpty(m_storageUrl))
                    throw new Exception(Strings.CloudFiles.UnexpectedResponseError);
            }

            HttpWebRequest req = (HttpWebRequest)HttpWebRequest.Create(m_storageUrl + UrlEncode(m_path + remotename) + query);
            req.Headers.Add("X-Auth-Token", UrlEncode(m_authToken));

            req.UserAgent = "Duplicati CloudFiles Backend v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString();
            req.KeepAlive = false;
            req.PreAuthenticate = true;
            req.AllowWriteStreamBuffering = false;

            return req;
        }

        private string UrlEncode(string value)
        {
            return Library.Utility.Uri.UrlEncode(value).Replace("+", "%20").Replace("%2f", "/");
        }
    }
}
