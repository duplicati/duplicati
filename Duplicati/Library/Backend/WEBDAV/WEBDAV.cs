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
using Duplicati.Library.Utility.Options;
using System.Net;
using System.Runtime.CompilerServices;

namespace Duplicati.Library.Backend
{
    public class WEBDAV : IStreamingBackend
    {
        /// <summary>
        /// The credentials to use for the connection
        /// </summary>
        private readonly NetworkCredential? m_userInfo;
        /// <summary>
        /// The URL to connect to
        /// </summary>
        private readonly string m_url;
        /// <summary>
        /// The path to use for all operations
        /// </summary>
        private readonly string m_path;
        /// <summary>
        /// The sanitized URL to use for listing
        /// </summary>
        private readonly string m_sanitizedUrl;
        /// <summary>
        /// The reverse protocol URL to use for listing
        /// </summary>
        private readonly string m_reverseProtocolUrl;
        /// <summary>
        /// The raw URL to use for listing
        /// </summary>
        private readonly string m_rawurl;
        /// <summary>
        /// The raw URL with port to use for listing
        /// </summary>
        private readonly string m_rawurlPort;
        /// <summary>
        /// The DNS name used for the connection
        /// </summary>
        private readonly string m_dnsName;
        /// <summary>
        /// Flag to indicate if integrated authentication should be used
        /// </summary>
        private readonly bool m_useIntegratedAuthentication = false;
        /// <summary>
        /// Flag to indicate if digest authentication should be used
        /// </summary>
        private readonly bool m_forceDigestAuthentication = false;
        /// <summary>
        /// The timeouts to use for the operations
        /// </summary>
        private readonly TimeoutOptionsHelper.Timeouts m_timeouts;
        /// <summary>
        /// The options for the SSL certificate validation
        /// </summary>
        private readonly SslOptionsHelper.SslCertificateOptions m_certificateOptions;

        /// <summary>
        /// A list of files seen in the last List operation.
        /// It is used to detect a problem with IIS where a file is listed,
        /// but IIS responds 404 because the file mapping is incorrect.
        /// </summary>
        private HashSet<string>? m_filenamelist = null;

        // According to the WEBDAV standard, the "allprop" request should return all properties, however this seems to fail on some servers (box.net).
        // I've found this description: http://www.webdav.org/specs/rfc2518.html#METHOD_PROPFIND
        //  "An empty PROPFIND request body MUST be treated as a request for the names and values of all properties."
        //
        //private static readonly byte[] PROPFIND_BODY = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:allprop/></D:propfind>");
        private static readonly byte[] PROPFIND_BODY = new byte[0];

        /// <summary>
        /// The HTTP client to use for all operations
        /// </summary>
        private HttpClient? m_httpClient = null;

        public WEBDAV()
        {
            m_url = string.Empty;
            m_path = string.Empty;
            m_sanitizedUrl = string.Empty;
            m_reverseProtocolUrl = string.Empty;
            m_rawurl = string.Empty;
            m_rawurlPort = string.Empty;
            m_dnsName = string.Empty;
            m_certificateOptions = null!;
            m_timeouts = null!;
        }

        public WEBDAV(string url, Dictionary<string, string?> options)
        {
            var u = new Utility.Uri(url);
            u.RequireHost();
            m_dnsName = u.Host;
            var auth = AuthOptionsHelper.Parse(options, u);
            if (auth.HasUsername)
            {
                m_userInfo = new NetworkCredential() { Domain = "" };
                m_userInfo.UserName = auth.Username;
                if (auth.HasPassword)
                    m_userInfo.Password = auth.Password;
            }

            m_certificateOptions = SslOptionsHelper.Parse(options);
            m_useIntegratedAuthentication = Utility.Utility.ParseBoolOption(options, "integrated-authentication");
            m_forceDigestAuthentication = Utility.Utility.ParseBoolOption(options, "force-digest-authentication");

            // Explicitly support setups with no username and password
            if (m_forceDigestAuthentication && m_userInfo == null)
                throw new UserInformationException(Strings.WEBDAV.UsernameRequired, "UsernameRequired");

            m_url = u.SetScheme(m_certificateOptions.UseSSL ? "https" : "http").SetCredentials(null, null).SetQuery(null).ToString();
            m_url = Util.AppendDirSeparator(m_url, "/");

            m_path = u.Path;
            if (!m_path.StartsWith("/", StringComparison.Ordinal))
                m_path = "/" + m_path;
            m_path = Util.AppendDirSeparator(m_path, "/");

            m_path = Utility.Uri.UrlDecode(m_path);
            m_rawurl = new Utility.Uri(m_certificateOptions.UseSSL ? "https" : "http", u.Host, m_path).ToString();

            int port = u.Port;
            if (port <= 0)
                port = m_certificateOptions.UseSSL ? 443 : 80;

            m_rawurlPort = new Utility.Uri(m_certificateOptions.UseSSL ? "https" : "http", u.Host, m_path, null, null, null, port).ToString();
            m_sanitizedUrl = new Utility.Uri(m_certificateOptions.UseSSL ? "https" : "http", u.Host, m_path).ToString();
            m_reverseProtocolUrl = new Utility.Uri(m_certificateOptions.UseSSL ? "http" : "https", u.Host, m_path).ToString();
            m_timeouts = TimeoutOptionsHelper.Parse(options);
        }

        /// <summary>
        /// Gets the HTTP client to use for all operations
        /// </summary>
        /// <returns>The HTTP client to use</returns>
        private HttpClient GetHttpClient()
        {
            if (m_httpClient == null)
            {
                var httpHandler = m_certificateOptions.CreateHandler();
                if (m_useIntegratedAuthentication)
                {
                    httpHandler.UseDefaultCredentials = true;
                }
                else if (m_forceDigestAuthentication)
                {
                    httpHandler.Credentials = new CredentialCache
                    {
                        { new System.Uri(m_url), "Digest", m_userInfo! }
                    };
                }

                m_httpClient = HttpClientHelper.CreateClient(httpHandler);
                m_httpClient.Timeout = Timeout.InfiniteTimeSpan;

                if (!m_useIntegratedAuthentication && !m_forceDigestAuthentication && m_userInfo != null)
                {
                    m_httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Basic",
                        Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes($"{m_userInfo.UserName}:{m_userInfo.Password}"))
                    );
                }
            }

            return m_httpClient;
        }

        #region IBackend Members

        ///<inheritdoc/>
        public string DisplayName => Strings.WEBDAV.DisplayName;

        ///<inheritdoc/>
        public string ProtocolKey => "webdav";

        ///<inheritdoc/>
        public async IAsyncEnumerable<IFileEntry> ListAsync([EnumeratorCancellation] CancellationToken cancelToken)
        {
            using var request = CreateRequest(string.Empty, new HttpMethod("PROPFIND"));
            request.Headers.Add("Depth", "1");
            request.Content = new StreamContent(new MemoryStream(PROPFIND_BODY));
            request.Content.Headers.ContentLength = PROPFIND_BODY.Length;

            var doc = new System.Xml.XmlDocument();

            try
            {
                using var response = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, ct =>
                    GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                ).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                using var sourceStream = await Utility.Utility.WithTimeout(m_timeouts.ReadWriteTimeout, cancelToken, ct =>
                    response.Content.ReadAsStreamAsync(ct)
                ).ConfigureAwait(false);

                using var timeoutStream = sourceStream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, false);
                doc.Load(timeoutStream);
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
            var lst = doc.SelectNodes("D:multistatus/D:response/D:href", nm);
            if (lst == null)
            {
                m_filenamelist = filenamelist;
                yield break;
            }

            foreach (System.Xml.XmlNode n in lst)
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

                System.Xml.XmlNode? stat = n.ParentNode?.SelectSingleNode("D:propstat/D:prop", nm);
                if (stat != null)
                {
                    System.Xml.XmlNode? s = stat.SelectSingleNode("D:getcontentlength", nm);
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
                await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
                {
                    using var request = CreateRequest(remotename);
                    request.Method = HttpMethod.Delete;

                    var response = await GetHttpClient().SendAsync(request, ct).ConfigureAwait(false);

                    response.EnsureSuccessStatusCode();
                }).ConfigureAwait(false);

            }
            catch (HttpRequestException wex)
            {
                if (wex.StatusCode == HttpStatusCode.NotFound)
                    throw new FileMissingException(wex);
                throw;
            }
        }

        ///<inheritdoc/>
        public IList<ICommandLineArgument> SupportedCommands =>
        [
            .. AuthOptionsHelper.GetOptions(),
            new CommandLineArgument("integrated-authentication", CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionIntegratedAuthenticationShort, Strings.WEBDAV.DescriptionIntegratedAuthenticationLong),
            new CommandLineArgument("force-digest-authentication", CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionForceDigestShort, Strings.WEBDAV.DescriptionForceDigestLong),
            .. SslOptionsHelper.GetOptions(),
            .. TimeoutOptionsHelper.GetOptions()
        ];

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
            await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, async ct =>
            {
                using var request = CreateRequest(string.Empty, new HttpMethod("MKCOL"));
                using var response = await GetHttpClient().SendAsync(request, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
            }).ConfigureAwait(false);
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {

        }

        #endregion

        private HttpRequestMessage CreateRequest(string remotename, HttpMethod? method = null)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{m_url}{Utility.Uri.UrlEncode(remotename).Replace("+", "%20")}");
            request.Headers.Add(HttpRequestHeader.UserAgent.ToString(), "Duplicati WEBDAV Client v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);

            request.Headers.ConnectionClose = !m_useIntegratedAuthentication; // ConnectionClose is incompatible with integrated authentication

            if (method != null)
                request.Method = method;

            return request;

        }

        #region IStreamingBackend Members

        ///<inheritdoc/>
        public async Task PutAsync(string remotename, Stream stream, CancellationToken cancelToken)
        {
            try
            {
                using var request = CreateRequest(remotename, HttpMethod.Put);
                using var timeoutStream = stream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, false);
                request.Content = new StreamContent(timeoutStream);
                request.Content.Headers.ContentLength = stream.Length;
                request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                request.Version = HttpVersion.Version11;

                using var response = await GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);

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
                using var request = CreateRequest(remotename, HttpMethod.Get);
                using var timeoutStream = stream.ObserveWriteTimeout(m_timeouts.ReadWriteTimeout, false);
                await GetHttpClient().DownloadFile(request, timeoutStream, null, cancelToken).ConfigureAwait(false);
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