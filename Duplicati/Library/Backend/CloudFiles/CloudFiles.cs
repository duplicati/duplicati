#region Disclaimer / License
using System.Net.Http.Headers;
using System.Linq;
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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.CloudFiles
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class CloudFiles : IBackend, IBackendPagination
    {
        public const string AUTH_URL_US = "https://identity.api.rackspacecloud.com/auth";
        public const string AUTH_URL_UK = "https://lon.auth.api.rackspacecloud.com/v1.0";
        private const string DUMMY_HOSTNAME = "api.mosso.com";

        private const int ITEM_LIST_LIMIT = 1000;
        private readonly string m_username;
        private readonly string m_password;
        private readonly string m_path;

        private string m_storageUrl = null;
        private string m_authToken = null;
        private readonly string m_authUrl;
        
        private readonly HttpClient m_client = new HttpClient(
            new HttpClientHandler() { 
                PreAuthenticate = true
            } 
        );

        private readonly byte[] m_copybuffer = new byte[Duplicati.Library.Utility.Utility.DEFAULT_BUFFER_SIZE];

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
        public CloudFiles()
        {
        }

        // ReSharper disable once UnusedMember.Global
        // This constructor is needed by the BackendLoader.
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
                throw new UserInformationException(Strings.CloudFiles.NoUserIDError, "CloudFilesNoUserID");
            if (string.IsNullOrEmpty(m_password))
                throw new UserInformationException(Strings.CloudFiles.NoAPIKeyError, "CloudFilesNoApiKey");

            //Fallback to the previous format
            if (url.Contains(DUMMY_HOSTNAME))
            {
                Uri u = new Uri(url);

                if (!string.IsNullOrEmpty(u.UserInfo))
                {
                    if (u.UserInfo.IndexOf(":", StringComparison.Ordinal) >= 0)
                    {
                        m_username = u.UserInfo.Substring(0, u.UserInfo.IndexOf(":", StringComparison.Ordinal));
                        m_password = u.UserInfo.Substring(u.UserInfo.IndexOf(":", StringComparison.Ordinal) + 1);
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

            if (m_path.EndsWith("/", StringComparison.Ordinal))
                m_path = m_path.Substring(0, m_path.Length - 1);
            if (!m_path.StartsWith("/", StringComparison.Ordinal))
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

        public Task<IList<IFileEntry>> ListAsync(CancellationToken cancelToken)
            => this.CondensePaginatedListAsync(cancelToken);

        public async IAsyncEnumerable<IFileEntry> ListEnumerableAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancelToken)
        {
            string extraUrl = "?format=xml&limit=" + ITEM_LIST_LIMIT.ToString();
            string markerUrl = "";

            bool repeat;

            do
            {
                var doc = new System.Xml.XmlDocument();

                var req = await CreateRequestAsync("", extraUrl + markerUrl, cancelToken);

                try
                {
                    using (var resp = await m_client.SendAsync(req, cancelToken))
                    using (var s = await resp.Content.ReadAsStreamAsync())
                        doc.Load(s);
                }
                catch (WebException wex)
                {
                    if (markerUrl == "") //Only check on first iteration
                        if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                            throw new FolderMissingException(wex);
                    
                    //Other error, just re-throw
                    throw;
                }

                System.Xml.XmlNodeList lst = doc.SelectNodes("container/object");

                //Perhaps the folder does not exist?
                //The response should be 404 from the server, but it is not :(
                if (lst.Count == 0 && markerUrl == "") //Only on first iteration
                {
                    try { await CreateFolderAsync(cancelToken); }
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
                    yield return new FileEntry(name, size, mod, mod);
                }

                repeat = lst.Count == ITEM_LIST_LIMIT;

                if (repeat)
                    markerUrl = "&marker=" + Library.Utility.Uri.UrlEncode(lastItemName);

            } while (repeat);
        }

        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var req = await CreateRequestAsync("/" + remotename, "", cancelToken);

                req.Method = HttpMethod.Delete;
                using (var resp = await m_client.SendAsync(req, cancelToken))
                {
                    if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                        throw new FileMissingException();

                    if ((int)resp.StatusCode >= 300)
                        throw new WebException(Strings.CloudFiles.FileDeleteError, WebExceptionStatus.ProtocolError);
                    else
                        using (await resp.Content.ReadAsStreamAsync())
                        { }
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
        
        public Task TestAsync(CancellationToken cancelToken)
            //The "Folder not found" is not detectable :(
            => this.TestListAsync(cancelToken);

        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var createReq = await CreateRequestAsync("", "", cancelToken);
            createReq.Method = HttpMethod.Put;
            using (var resp = await m_client.SendAsync(createReq, cancelToken))
            { }
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public string[] DNSName
        {
            get { return new string[] { new Uri(m_authUrl).Host, string.IsNullOrWhiteSpace(m_storageUrl) ? null : new Uri(m_storageUrl).Host }; }
        }

        public bool SupportsStreaming => true;

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var req = await CreateRequestAsync("/" + remotename, "", cancelToken);
            req.Method = HttpMethod.Get;

            using (var resp = await m_client.SendAsync(req, cancelToken))
            using (var s = await resp.Content.ReadAsStreamAsync())
            using (var mds = new Utility.MD5CalculatingStream(s))
            {
                string md5Hash = resp.Headers.GetValues("ETag").FirstOrDefault();
                await Utility.Utility.CopyStreamAsync(mds, stream, true, cancelToken, m_copybuffer);

                if (!String.Equals(mds.GetFinalHashString(), md5Hash, StringComparison.OrdinalIgnoreCase))
                    throw new Exception(Strings.CloudFiles.ETagVerificationError);
            }
        }

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var req = await CreateRequestAsync("/" + remotename, "", cancelToken);
            req.Method = HttpMethod.Put;            
            using (var mds = new Utility.MD5CalculatingStream(stream))
            using (var body = new StreamContent(mds))
            {
                body.Headers.ContentType = MediaTypeHeaderValue.Parse("application/octet-stream");

                try { body.Headers.ContentLength = stream.Length; }
                catch { }

                req.Content = body;

                //TODO: We cannot use the local MD5 calculation, because that could involve a throttled read,
                // and may invoke various events
                string md5Hash = null;

                //We need to verify the eTag locally
                try
                {
                    using (var resp = await m_client.SendAsync(req))
                        if ((int)resp.StatusCode >= 300)
                            throw new WebException(Strings.CloudFiles.FileUploadError, WebExceptionStatus.ProtocolError);
                        else
                            md5Hash = resp.Headers.GetValues("ETag").FirstOrDefault();
                }
                catch (WebException wex)
                {
                    //Catch 404 and turn it into a FolderNotFound error
                    if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                        throw new FolderMissingException(wex);

                    //Other error, just re-throw
                    throw;
                }

                var fileHash = mds.GetFinalHashString();
                if (md5Hash == null || !String.Equals(md5Hash, fileHash, StringComparison.OrdinalIgnoreCase))
                {
                    //Remove the broken file
                    try { await DeleteAsync(remotename, cancelToken); }
                    catch { }

                    throw new Exception(Strings.CloudFiles.ETagVerificationError);
                }
            }
        }

        #endregion

        private async Task<HttpRequestMessage> CreateRequestAsync(string remotename, string query, CancellationToken cancelToken)
        {
            //If this is the first call, get an authentication token
            if (string.IsNullOrEmpty(m_authToken) || string.IsNullOrEmpty(m_storageUrl))
            {
                var authReq = new HttpRequestMessage(HttpMethod.Get, m_authUrl);
                authReq.Headers.Add("X-Auth-User", m_username);
                authReq.Headers.Add("X-Auth-Key", m_password);

                using (var resp = await m_client.SendAsync(authReq, cancelToken))
                {
                    m_storageUrl = resp.Headers.GetValues("X-Storage-Url").FirstOrDefault();
                    m_authToken = resp.Headers.GetValues("X-Auth-Token").FirstOrDefault();
                }

                if (string.IsNullOrEmpty(m_authToken) || string.IsNullOrEmpty(m_storageUrl))
                    throw new Exception(Strings.CloudFiles.UnexpectedResponseError);
            }

            var req = new HttpRequestMessage(HttpMethod.Get, m_storageUrl + UrlEncode(m_path + remotename) + query);
            req.Headers.Add("X-Auth-Token", UrlEncode(m_authToken));

            req.Headers.UserAgent.ParseAdd("Duplicati CloudFiles Backend v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            req.Headers.ConnectionClose = true;
            // TODO-DNC: Not supported, but no longer required? 
            //req.Headers.AllowWriteStreamBuffering = false;

            return req;
        }

        private string UrlEncode(string value)
        {
            return Library.Utility.Uri.UrlEncode(value).Replace("+", "%20").Replace("%2f", "/");
        }
    }
}
