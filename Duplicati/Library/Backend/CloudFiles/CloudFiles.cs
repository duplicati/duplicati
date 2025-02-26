// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    // ReSharper disable once UnusedMember.Global
    // This class is instantiated dynamically in the BackendLoader.
    public class CloudFiles : IBackend, IStreamingBackend
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
                var u = new System.Uri(url);

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

        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            var extraUrl = "?format=xml&limit=" + ITEM_LIST_LIMIT.ToString();
            var markerUrl = "";

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
                    try { await CreateFolderAsync(cancelToken).ConfigureAwait(false); }
                    catch { } //Ignore
                }

                var lastItemName = "";
                foreach (System.Xml.XmlNode n in lst)
                {
                    var name = n["name"].InnerText;
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

        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                await PutAsync(remotename, fs, cancelToken);
        }

        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        public Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                var req = CreateRequest("/" + remotename, "");

                req.Method = "DELETE";
                var areq = new Utility.AsyncHttpRequest(req);
                using (var resp = (HttpWebResponse)areq.GetResponse())
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
                if (wex.Response is HttpWebResponse response && response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                else
                    throw;
            }

            return Task.CompletedTask;
        }

        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>([
                    new CommandLineArgument("auth-password", CommandLineArgument.ArgumentType.Password, Strings.CloudFiles.DescriptionAuthPasswordShort, Strings.CloudFiles.DescriptionAuthPasswordLong),
                    new CommandLineArgument("auth-username", CommandLineArgument.ArgumentType.String, Strings.CloudFiles.DescriptionAuthUsernameShort, Strings.CloudFiles.DescriptionAuthUsernameLong),
                    new CommandLineArgument("cloudfiles-username", CommandLineArgument.ArgumentType.String, Strings.CloudFiles.DescriptionUsernameShort, Strings.CloudFiles.DescriptionUsernameLong, null, new string[] {"auth-username"} ),
                    new CommandLineArgument("cloudfiles-accesskey", CommandLineArgument.ArgumentType.Password, Strings.CloudFiles.DescriptionPasswordShort, Strings.CloudFiles.DescriptionPasswordLong, null, new string[] {"auth-password"}),
                    new CommandLineArgument("cloudfiles-uk-account", CommandLineArgument.ArgumentType.Boolean, Strings.CloudFiles.DescriptionUKAccountShort, Strings.CloudFiles.DescriptionUKAccountLong("cloudfiles-authentication-url", AUTH_URL_UK)),
                    new CommandLineArgument("cloudfiles-authentication-url", CommandLineArgument.ArgumentType.String, Strings.CloudFiles.DescriptionAuthenticationURLShort, Strings.CloudFiles.DescriptionAuthenticationURLLong_v2("cloudfiles-uk-account"), AUTH_URL_US),
                ]);
            }
        }

        public string Description
        {
            get { return Strings.CloudFiles.Description_v2; }
        }

        #endregion

        #region IBackend_v2 Members

        public Task TestAsync(CancellationToken cancelToken) =>
            //The "Folder not found" is not detectable :(
            this.TestListAsync(cancelToken);

        public Task CreateFolderAsync(CancellationToken cancelToken)
        {
            var createReq = CreateRequest("", "");
            createReq.Method = "PUT";
            var areq = new Utility.AsyncHttpRequest(createReq);
            using (var resp = (HttpWebResponse)areq.GetResponse())
            { }

            return Task.CompletedTask;
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
        }

        #endregion

        #region IStreamingBackend Members

        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(
            new string[] {
                new System.Uri(m_authUrl).Host,
                string.IsNullOrWhiteSpace(m_storageUrl) ? null : new System.Uri(m_storageUrl).Host
            }
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray()
        );

        public async Task GetAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
        {
            var req = CreateRequest("/" + remotename, "");
            req.Method = "GET";

            var areq = new Utility.AsyncHttpRequest(req);
            using (var resp = areq.GetResponse())
            using (var s = areq.GetResponseStream())
            using (var hasher = MD5.Create())
            using (var mds = new Utility.HashCalculatingStream(s, hasher))
            {
                string md5Hash = resp.Headers["ETag"];
                await Utility.Utility.CopyStreamAsync(mds, stream, true, cancelToken).ConfigureAwait(false);

                if (!String.Equals(mds.GetFinalHashString(), md5Hash, StringComparison.OrdinalIgnoreCase))
                    throw new Exception(Strings.CloudFiles.ETagVerificationError);
            }
        }

        public async Task PutAsync(string remotename, System.IO.Stream stream, CancellationToken cancelToken)
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
                req.Headers["ETag"] = Core.Utility.ByteArrayAsHexString(md5.ComputeHash(stream)).ToLower(System.Globalization.CultureInfo.InvariantCulture);
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
                catch { }

                Utility.AsyncHttpRequest areq = new Utility.AsyncHttpRequest(req);
                using (System.IO.Stream s = areq.GetRequestStream(streamLen))
                using (var hasher = MD5.Create())
                using (var mds = new Utility.HashCalculatingStream(s, hasher))
                {
                    await Utility.Utility.CopyStreamAsync(stream, mds, tryRewindSource: true, cancelToken: cancelToken);
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
                    if (wex.Response is HttpWebResponse response && response.StatusCode == HttpStatusCode.NotFound)
                        throw new FolderMissingException(wex);

                    //Other error, just re-throw
                    throw;
                }


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

        private HttpWebRequest CreateRequest(string remotename, string query)
        {
            //If this is the first call, get an authentication token
            if (string.IsNullOrEmpty(m_authToken) || string.IsNullOrEmpty(m_storageUrl))
            {
                var authReq = (HttpWebRequest)HttpWebRequest.Create(m_authUrl);
                authReq.Headers.Add("X-Auth-User", m_username);
                authReq.Headers.Add("X-Auth-Key", m_password);
                authReq.Method = "GET";

                var areq = new Utility.AsyncHttpRequest(authReq);
                using (var resp = areq.GetResponse())
                {
                    m_storageUrl = resp.Headers["X-Storage-Url"];
                    m_authToken = resp.Headers["X-Auth-Token"];
                }

                if (string.IsNullOrEmpty(m_authToken) || string.IsNullOrEmpty(m_storageUrl))
                    throw new Exception(Strings.CloudFiles.UnexpectedResponseError);
            }

            var req = (HttpWebRequest)HttpWebRequest.Create(m_storageUrl + UrlEncode(m_path + remotename) + query);
            req.Headers.Add("X-Auth-Token", UrlEncode(m_authToken));

            req.UserAgent = "Duplicati CloudFiles Backend v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
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
