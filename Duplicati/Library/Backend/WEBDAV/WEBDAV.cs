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
using Duplicati.Library.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend
{
    public class WEBDAV : IStreamingBackend
    {
        private record RequestResources : IDisposable
        {
            public RequestResources(HttpClient httpClient, HttpRequestMessage requestMessage)
            {
                HttpClient = httpClient;
                RequestMessage = requestMessage;
            }
            public HttpRequestMessage RequestMessage { get; init; }
            public HttpClient HttpClient { get; init; }

            public void Dispose()
            {
                try
                {
                    RequestMessage?.Dispose();
                }
                catch { }
                try
                {
                    HttpClient?.Dispose();
                }
                catch { }
            }
        }
        private readonly NetworkCredential m_userInfo;
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

        /// <summary>
        /// A list of files seen in the last List operation.
        /// It is used to detect a problem with IIS where a file is listed,
        /// but IIS responds 404 because the file mapping is incorrect.
        /// </summary>
        private HashSet<string> m_filenamelist = null;

        // According to the WEBDAV standard, the "allprop" request should return all properties, however this seems to fail on some servers (box.net).
        // I've found this description: http://www.webdav.org/specs/rfc2518.html#METHOD_PROPFIND
        //  "An empty PROPFIND request body MUST be treated as a request for the names and values of all properties."
        //
        //private static readonly byte[] PROPFIND_BODY = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:allprop/></D:propfind>");
        private static readonly byte[] PROPFIND_BODY = new byte[0];

        /// <summary>
        /// Option to accept any SSL certificate
        /// </summary>
        private readonly bool m_acceptAnyCertificate;

        /// <summary>
        /// Specific hashes to be accepted by the certificate validator
        /// </summary>
        private readonly string[] m_acceptSpecificCertificates;

        /// <summary> 
        /// The default timeout in seconds for List operations
        /// </summary> 
        private const int LIST_OPERATION_TIMEOUT_SECONDS = 600;

        /// <summary>
        /// The default timeout in seconds for Delete/CreateFolder operations
        /// </summary>
        private const int SHORT_OPERATION_TIMEOUT_SECONDS = 30;

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
                m_userInfo = new NetworkCredential();
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
                    m_userInfo = new NetworkCredential();
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

            m_path = Utility.Uri.UrlDecode(m_path);
            m_rawurl = new Utility.Uri(m_useSSL ? "https" : "http", u.Host, m_path).ToString();

            int port = u.Port;
            if (port <= 0)
                port = m_useSSL ? 443 : 80;

            m_rawurlPort = new Utility.Uri(m_useSSL ? "https" : "http", u.Host, m_path, null, null, null, port).ToString();
            m_sanitizedUrl = new Utility.Uri(m_useSSL ? "https" : "http", u.Host, m_path).ToString();
            m_reverseProtocolUrl = new Utility.Uri(m_useSSL ? "http" : "https", u.Host, m_path).ToString();
            m_acceptAnyCertificate = options.ContainsKey("accept-any-ssl-certificate") && Utility.Utility.ParseBoolOption(options, "accept-any-ssl-certificate");
            m_acceptSpecificCertificates = options.ContainsKey("accept-specified-ssl-hash") ? options["accept-specified-ssl-hash"].Split([",", ";"], StringSplitOptions.RemoveEmptyEntries) : null;
        }

        #region IBackend Members

        ///<inheritdoc/>
        public string DisplayName => Strings.WEBDAV.DisplayName;

        ///<inheritdoc/>
        public string ProtocolKey => "webdav";

        ///<inheritdoc/>
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            using var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(TimeSpan.FromSeconds(LIST_OPERATION_TIMEOUT_SECONDS));

            using var requestResources = CreateRequest(string.Empty, new HttpMethod("PROPFIND"));
            requestResources.RequestMessage.Headers.Add("Depth", "1");
            requestResources.RequestMessage.Content = new StreamContent(new MemoryStream(PROPFIND_BODY));
            requestResources.RequestMessage.Content.Headers.ContentLength = PROPFIND_BODY.Length;

            var doc = new System.Xml.XmlDocument();

            try
            {
                using var response = await requestResources.HttpClient.SendAsync(requestResources.RequestMessage, HttpCompletionOption.ResponseContentRead, timeoutToken.Token).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var stream = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
                doc.Load(stream);
            }
            catch (HttpRequestException wex)
            {
                if (wex.StatusCode == HttpStatusCode.NotFound || wex.StatusCode == HttpStatusCode.Conflict)
                    throw new FolderMissingException(Strings.WEBDAV.MissingFolderError(m_path, wex.Message), wex);

                if (wex.StatusCode == HttpStatusCode.MethodNotAllowed)
                    throw new UserInformationException(Strings.WEBDAV.MethodNotAllowedError((HttpStatusCode)wex.StatusCode), "WebdavMethodNotAllowed", wex);

                throw;
            }

            var nm = new System.Xml.XmlNamespaceManager(doc.NameTable);
            nm.AddNamespace("D", "DAV:");

            var filenamelist = new HashSet<string>();
            foreach (System.Xml.XmlNode n in doc.SelectNodes("D:multistatus/D:response/D:href", nm))
            {
                //IIS uses %20 for spaces and %2B for +
                //Apache uses %20 for spaces and + for +
                string name = Utility.Uri.UrlDecode(n.InnerText.Replace("+", "%2B"));

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
                        isCollection = stat.SelectSingleNode("D:resourcetype/D:collection", nm) != null;
                }

                var fe = new FileEntry(name, size, lastAccess, lastModified)
                {
                    IsFolder = isCollection
                };

                filenamelist.Add(name);
                yield return fe;
            }

            m_filenamelist = filenamelist;
        }

        ///<inheritdoc/>
        public async Task PutAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await using FileStream fs = File.OpenRead(filename);
            await PutAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        ///<inheritdoc/>
        public async Task GetAsync(string remotename, string filename, CancellationToken cancelToken)
        {
            await using var fs = File.Create(filename);
            await GetAsync(remotename, fs, cancelToken).ConfigureAwait(false);
        }

        ///<inheritdoc/>
        public async Task DeleteAsync(string remotename, CancellationToken cancelToken)
        {
            try
            {
                using var timeoutToken = new CancellationTokenSource();
                timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
                using var combinedTokens = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancelToken);

                using var requestResources = CreateRequest(remotename);
                requestResources.RequestMessage.Method = HttpMethod.Delete;

                var response = await requestResources.HttpClient.SendAsync(requestResources.RequestMessage, combinedTokens.Token).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

            }
            catch (HttpRequestException wex)
            {
                if (wex.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                throw;
            }
        }

        ///<inheritdoc/>
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
                    new CommandLineArgument("accept-any-ssl-certificate", CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionAcceptAnyCertificateShort, Strings.WEBDAV.DescriptionAcceptAnyCertificateLong),
                    new CommandLineArgument("accept-specified-ssl-hash", CommandLineArgument.ArgumentType.String, Strings.WEBDAV.DescriptionAcceptHashShort, Strings.WEBDAV.DescriptionAcceptHashLong2)
                 });
            }
        }

        ///<inheritdoc/>
        public string Description => Strings.WEBDAV.Description;

        ///<inheritdoc/>
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { m_dnsName });

        ///<inheritdoc/>
        public Task TestAsync(CancellationToken cancelToken)
            => this.TestListAsync(cancelToken);

        ///<inheritdoc/>
        public async Task CreateFolderAsync(CancellationToken cancelToken)
        {
            using var timeoutToken = new CancellationTokenSource();
            timeoutToken.CancelAfter(TimeSpan.FromSeconds(SHORT_OPERATION_TIMEOUT_SECONDS));
            using var combinedTokens = CancellationTokenSource.CreateLinkedTokenSource(timeoutToken.Token, cancelToken);

            using var requestResources = CreateRequest(string.Empty, new HttpMethod("MKCOL"));

            using var response = await requestResources.HttpClient.SendAsync(requestResources.RequestMessage, combinedTokens.Token).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion

        private RequestResources CreateRequest(string remotename, HttpMethod method = null)
        {
            HttpClient httpClient;
            HttpClientHandler httpHandler = new HttpClientHandler();
            HttpClientHelper.ConfigureHandlerCertificateValidator(httpHandler, m_acceptAnyCertificate, m_acceptSpecificCertificates);

            if (m_useIntegratedAuthentication)
            {
                httpHandler.UseDefaultCredentials = true;
                httpClient = HttpClientHelper.CreateClient(httpHandler);
            }
            else if (m_forceDigestAuthentication)
            {
                httpHandler.Credentials = new CredentialCache
                {
                    { new System.Uri(m_url), "Digest", m_userInfo }
                };
                httpClient = HttpClientHelper.CreateClient(httpHandler);
            }
            else
            {
                httpClient = HttpClientHelper.CreateClient(httpHandler);
                httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                    "Basic",
                    Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{m_userInfo.UserName}:{m_userInfo.Password}"))
                );
            }

            httpClient.Timeout = Timeout.InfiniteTimeSpan;

            var request = new HttpRequestMessage(HttpMethod.Get, $"{m_url}{Utility.Uri.UrlEncode(remotename).Replace("+", "%20")}");
            request.Headers.Add(HttpRequestHeader.UserAgent.ToString(), "Duplicati WEBDAV Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

            request.Headers.ConnectionClose = !m_useIntegratedAuthentication; // ConnectionClose is incompatible with integrated authentication

            if (method != null)
                request.Method = method;

            return new RequestResources(httpClient, request);

        }

        #region IStreamingBackend Members

        ///<inheritdoc/>
        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                using var requestResources = CreateRequest(remotename, HttpMethod.Put);

                requestResources.RequestMessage.Content = new StreamContent(stream);
                requestResources.RequestMessage.Content.Headers.ContentLength = stream.Length;
                requestResources.RequestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                requestResources.RequestMessage.Version = HttpVersion.Version11;

                using var response = await requestResources.HttpClient.SendAsync(requestResources.RequestMessage, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException wex)
            {
                if (wex.StatusCode == HttpStatusCode.Conflict || wex.StatusCode == HttpStatusCode.NotFound)
                    throw new FolderMissingException(Strings.WEBDAV.MissingFolderError(m_path, wex.Message), wex);

                throw;
            }
        }

        ///<inheritdoc/>
        public async Task GetAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                using var requestResources = CreateRequest(remotename, HttpMethod.Get);

                await requestResources.HttpClient.DownloadFile(requestResources.RequestMessage, stream, null, cancelToken).ConfigureAwait(false);
            }
            catch (HttpRequestException wex)
            {
                if (wex.StatusCode == HttpStatusCode.Conflict)
                    throw new FolderMissingException(Strings.WEBDAV.MissingFolderError(m_path, wex.Message), wex);

                if
                (
                    wex.StatusCode == HttpStatusCode.NotFound
                    &&
                    m_filenamelist != null
                    &&
                    m_filenamelist.Contains(remotename)
                )
                    throw new Exception(Strings.WEBDAV.SeenThenNotFoundError(m_path, remotename, Path.GetExtension(remotename), wex.Message), wex);

                if (wex.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);

                throw;
            }
        }

        #endregion
    }
}