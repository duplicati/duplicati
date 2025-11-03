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
        /// The integrated authentication option name
        /// </summary>
        private const string INTEGRATED_AUTHENTICATION_OPTION = "integrated-authentication";
        /// <summary>
        /// The force digest authentication option name
        /// </summary>
        private const string FORCE_DIGEST_AUTHENTICATION_OPTION = "force-digest-authentication";
        /// <summary>
        /// The debug propfind file option name
        /// </summary>
        private const string DEBUG_PROPFIND_FILE_OPTION = "debug-propfind-file";
        /// <summary>
        /// The use extended propfind option name
        /// </summary>
        private const string USE_EXTENDED_PROPFIND_OPTION = "use-extended-propfind";
        /// <summary>
        /// The use legacy propfind parsing option name
        /// </summary>
        private const string USE_LEGACY_PROPFIND_PARSING_OPTION = "use-legacy-parsing";

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
        /// Flag indicating if the legacy PROPFIND parsing should be used
        /// </summary>
        private readonly bool m_useLegacyParsing;
        /// <summary>
        /// The base URI that represents the configured WebDAV root
        /// </summary>
        private readonly System.Uri m_baseUri;
        /// <summary>
        /// Known path prefixes used to interpret PROPFIND href responses
        /// </summary>
        private readonly List<string> m_propfindPathPrefixes;

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
        private static readonly byte[] PROPFIND_BODY_EXT = System.Text.Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><D:propfind xmlns:D=\"DAV:\"><D:allprop/></D:propfind>");
        private static readonly byte[] PROPFIND_BODY_EMPTY = new byte[0];

        /// <summary>
        /// The HTTP client to use for all operations
        /// </summary>
        private HttpClient? m_httpClient = null;

        /// <summary>
        /// If set, the PROPFIND response will be written to this file for debugging purposes.
        /// </summary>
        private readonly string? m_debugPropfindFile;

        /// <summary>
        /// Flag to indicate if extended PROPFIND should be used.
        /// </summary>
        private readonly bool m_useExtendedPropfind;

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
            m_useLegacyParsing = false;
            m_baseUri = null!;
            m_propfindPathPrefixes = null!;
        }

        public WEBDAV(string url, Dictionary<string, string?> options)
        {
            var u = new Utility.Uri(url);
            u.RequireHost();
            m_dnsName = u.Host ?? "";
            var auth = AuthOptionsHelper.Parse(options, u);
            if (auth.HasUsername)
            {
                m_userInfo = new NetworkCredential() { Domain = "" };
                m_userInfo.UserName = auth.Username;
                if (auth.HasPassword)
                    m_userInfo.Password = auth.Password;
            }

            m_certificateOptions = SslOptionsHelper.Parse(options);
            m_useIntegratedAuthentication = Utility.Utility.ParseBoolOption(options, INTEGRATED_AUTHENTICATION_OPTION);
            m_forceDigestAuthentication = Utility.Utility.ParseBoolOption(options, FORCE_DIGEST_AUTHENTICATION_OPTION);
            m_useExtendedPropfind = Utility.Utility.ParseBoolOption(options, USE_EXTENDED_PROPFIND_OPTION);
            m_useLegacyParsing = Utility.Utility.ParseBoolOption(options, USE_LEGACY_PROPFIND_PARSING_OPTION);
            m_debugPropfindFile = options.GetValueOrDefault(DEBUG_PROPFIND_FILE_OPTION);

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

            m_baseUri = new System.Uri(m_url, System.UriKind.Absolute);
            m_propfindPathPrefixes = BuildPropfindPrefixes();
        }

        private List<string> BuildPropfindPrefixes()
        {
            var prefixes = new HashSet<string>(StringComparer.Ordinal);

            AddPropfindPrefix(prefixes, "/");
            AddPropfindPrefix(prefixes, m_baseUri.AbsolutePath);
            AddPropfindPrefix(prefixes, m_path);
            AddPropfindPrefixFromUri(prefixes, m_url);
            AddPropfindPrefixFromUri(prefixes, m_rawurl);
            AddPropfindPrefixFromUri(prefixes, m_rawurlPort);
            AddPropfindPrefixFromUri(prefixes, m_sanitizedUrl);
            AddPropfindPrefixFromUri(prefixes, m_reverseProtocolUrl);

            var ordered = new List<string>(prefixes);
            ordered.Sort((left, right) => right.Length.CompareTo(left.Length));
            return ordered;
        }

        private static void AddPropfindPrefixFromUri(HashSet<string> prefixes, string? uri)
        {
            if (string.IsNullOrWhiteSpace(uri))
                return;

            if (System.Uri.TryCreate(uri, System.UriKind.Absolute, out var parsed))
                AddPropfindPrefix(prefixes, parsed.AbsolutePath);
        }

        private static void AddPropfindPrefix(HashSet<string> prefixes, string? path)
        {
            if (path == null)
                return;

            var normalized = Utility.Uri.UrlDecode(path.Replace("+", "%2B"));

            if (!normalized.StartsWith("/", StringComparison.Ordinal))
                normalized = "/" + normalized;

            if (!normalized.EndsWith("/", StringComparison.Ordinal))
                normalized += "/";

            prefixes.Add(normalized);
        }

        private string? ExtractNameFromPropfindHref(string hrefValue)
        {
            if (string.IsNullOrWhiteSpace(hrefValue))
                return null;

            var trimmed = hrefValue.Trim();

            if (!System.Uri.TryCreate(trimmed, System.UriKind.Absolute, out var hrefUri))
            {
                if (System.Uri.TryCreate(trimmed, System.UriKind.Relative, out var relativeUri))
                {
                    hrefUri = new System.Uri(m_baseUri, relativeUri);
                }
                else
                {
                    var decodedValue = Utility.Uri.UrlDecode(trimmed.Replace("+", "%2B"));
                    return ExtractRelativeFromDecodedPath(decodedValue);
                }
            }

            var decodedPath = Utility.Uri.UrlDecode(hrefUri.AbsolutePath.Replace("+", "%2B"));
            var name = ExtractRelativeFromDecodedPath(decodedPath);
            if (!string.IsNullOrEmpty(name))
                return name;

            var decodedHref = Utility.Uri.UrlDecode(trimmed.Replace("+", "%2B"));
            return ExtractRelativeFromDecodedPath(decodedHref);
        }

        private string? ExtractRelativeFromDecodedPath(string decodedPath)
        {
            if (string.IsNullOrEmpty(decodedPath))
                return null;

            var comparisonPath = decodedPath.StartsWith("/", StringComparison.Ordinal)
                ? decodedPath
                : "/" + decodedPath;

            foreach (var prefix in m_propfindPathPrefixes)
            {
                if (comparisonPath.Equals(prefix, StringComparison.Ordinal))
                    return null;

                if (comparisonPath.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var remainder = comparisonPath.Substring(prefix.Length);
                    return remainder.Length == 0 ? null : remainder;
                }

                var trimmedPrefix = prefix.EndsWith("/", StringComparison.Ordinal)
                    ? prefix.Substring(0, prefix.Length - 1)
                    : prefix;

                if (trimmedPrefix.Length > 0)
                {
                    if (comparisonPath.Equals(trimmedPrefix, StringComparison.Ordinal))
                        return null;

                    if (comparisonPath.StartsWith(trimmedPrefix + "/", StringComparison.Ordinal))
                    {
                        var remainder = comparisonPath.Substring(trimmedPrefix.Length + 1);
                        return remainder.Length == 0 ? null : remainder;
                    }
                }
            }

            if (comparisonPath.StartsWith("/", StringComparison.Ordinal))
            {
                var remainder = comparisonPath.Substring(1);
                return remainder.Length == 0 ? null : remainder;
            }

            return comparisonPath.Length == 0 ? null : comparisonPath;
        }

        private string? ExtractNameFromPropfindHrefLegacy(string hrefValue)
        {
            if (string.IsNullOrWhiteSpace(hrefValue))
                return null;

            string name = Utility.Uri.UrlDecode(hrefValue.Replace("+", "%2B"));

            string cmp_path;

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
                return null;

            if (name.Length <= cmp_path.Length)
                return null;

            return name.Substring(cmp_path.Length);
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
            var body = m_useExtendedPropfind ? PROPFIND_BODY_EXT : PROPFIND_BODY_EMPTY;
            using var request = CreateRequest(string.Empty, new HttpMethod("PROPFIND"));
            request.Headers.Add("Depth", "1");
            request.Content = new StreamContent(new MemoryStream(body));
            request.Content.Headers.ContentLength = body.Length;

            var doc = new System.Xml.XmlDocument();

            try
            {
                using var response = await Utility.Utility.WithTimeout(m_timeouts.ListTimeout, cancelToken, ct =>
                    GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseContentRead, ct)
                ).ConfigureAwait(false);

                response.EnsureSuccessStatusCode();

                using var sourceStream = await Utility.Utility.WithTimeout(m_timeouts.ShortTimeout, cancelToken, ct =>
                    response.Content.ReadAsStreamAsync(ct)
                ).ConfigureAwait(false);

                using var timeoutStream = sourceStream.ObserveReadTimeout(m_timeouts.ReadWriteTimeout, true);
                if (!string.IsNullOrWhiteSpace(m_debugPropfindFile))
                {
                    // Write the response to a file for debugging purposes
                    using (var debugStream = File.Create(m_debugPropfindFile))
                        await timeoutStream.CopyToAsync(debugStream, cancelToken).ConfigureAwait(false);

                    doc.Load(m_debugPropfindFile);
                }
                else
                {
                    doc.Load(timeoutStream);
                }
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

            var lst = doc.SelectNodes("D:multistatus/D:response/D:href", nm);
            if (lst == null)
            {
                m_filenamelist = new();
                yield break;
            }

            var entries = new List<IFileEntry>();
            foreach (var n in lst.Cast<System.Xml.XmlNode>())
            {
                var name = m_useLegacyParsing
                    ? ExtractNameFromPropfindHrefLegacy(n.InnerText)
                    : ExtractNameFromPropfindHref(n.InnerText) ?? ExtractNameFromPropfindHrefLegacy(n.InnerText);

                if (string.IsNullOrEmpty(name))
                    continue;

                var size = -1L;
                var lastAccess = new DateTime();
                var lastModified = new DateTime();
                var isCollection = false;

                var stat = n.ParentNode?.SelectSingleNode("D:propstat/D:prop", nm);
                if (stat != null)
                {
                    var s = stat.SelectSingleNode("D:getcontentlength", nm);
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

                entries.Add(fe);
            }

            m_filenamelist = entries.Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries)
            {
                if (cancelToken.IsCancellationRequested)
                    yield break;

                yield return entry;
            }
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
            new CommandLineArgument(INTEGRATED_AUTHENTICATION_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionIntegratedAuthenticationShort, Strings.WEBDAV.DescriptionIntegratedAuthenticationLong),
            new CommandLineArgument(FORCE_DIGEST_AUTHENTICATION_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionForceDigestShort, Strings.WEBDAV.DescriptionForceDigestLong),
            new CommandLineArgument(DEBUG_PROPFIND_FILE_OPTION, CommandLineArgument.ArgumentType.Path, Strings.WEBDAV.DescriptionDebugPropfindShort, Strings.WEBDAV.DescriptionDebugPropfindLong),
            new CommandLineArgument(USE_EXTENDED_PROPFIND_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionUseExtendedPropfindShort, Strings.WEBDAV.DescriptionUseExtendedPropfindLong),
            new CommandLineArgument(USE_LEGACY_PROPFIND_PARSING_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.WEBDAV.DescriptionUseLegacyPropfindParsingShort, Strings.WEBDAV.DescriptionUseLegacyPropfindParsingLong, "false"),
            .. SslOptionsHelper.GetOptions(),
            .. TimeoutOptionsHelper.GetOptions()
        ];

        ///<inheritdoc/>
        public string Description => Strings.WEBDAV.Description;

        ///<inheritdoc/>
        public Task<string[]> GetDNSNamesAsync(CancellationToken cancelToken) => Task.FromResult(new[] { m_dnsName });

        ///<inheritdoc/>
        public Task TestAsync(CancellationToken cancelToken)
            => this.TestReadWritePermissionsAsync(cancelToken);

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