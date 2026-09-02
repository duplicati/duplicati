// Copyright (C) 2026, The Duplicati Team
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

using System;
using System.Collections.Specialized;
using System.Linq;
using System.Text;

#nullable enable

namespace Duplicati.Library.Utility
{
    // TODO: This class should be deleted.

    // It was introduced to make it simpler to give the backend url on the commandline,
    // and because the Mono implementation of System.Uri had some issues.
    // Since Mono is no longer used, the only problem is the commandline,
    // but it does not make sense to support "invalid" urls as that increases the complexity
    // of the code and potentially introduces ambiguity for the user.

    // The commandline is not the only problem, though. Several backends take a
    // case sensitive remote name out of the authority component:
    //
    //   box://MyBackups/Sub/   -> BoxBackend reads uri.HostAndPath as the remote path
    //   dropbox://MyBackups/   -> Dropbox reads UrlDecode(uri.HostAndPath)
    //   openstack://MyBucket   -> OpenStackStorage reads uri.Host as the container
    //
    // An RFC 3986 conformant parser lower-cases the authority, which destroys those
    // names. #7097 replaced this class with System.Uri and the Box.com tests failed on
    // all three platforms, plus CloudStack, with "The requested folder does not exist".
    // Replacing the parser therefore also means either changing that url convention or
    // taking the authority from the original string. TestHostAndPathKeepsTheFolderCase
    // in UriUtilityTests covers this so it fails in the normal unit test job rather than
    // only in the backend tests, which need credentials.

    /// <summary>
    /// Represents a relaxed parsing of a URL.
    /// The goal is to cover as many types of url's as possible,
    /// without being ambiguous.
    /// The major limitations is that an embedded username may not contain a :,
    /// and the password may not contain a @.
    /// </summary>
    public struct RelaxedUri
    {
        /// <summary>
        /// A very lax version of a URL parser
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex URL_PARSER = new System.Text.RegularExpressions.Regex(@"(?<scheme>[^:]+)://(((?<username>[^\:\?/]+)(\:(?<password>[^@\:\?/]*))?\@))?((?<hostname>(?:[^\[/\?\:][^/\?\:]*)|(?:\[[^\]]+\]))(\:(?<port>\d+))?)?((?<path>[^\?]*))?(\?(?<query>.+))?");

        /// <summary>
        /// Detects a Windows path
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex WINDOWS_PATH = new System.Text.RegularExpressions.Regex(@"^/?[a-zA-Z]:[\\/]");

        /// <summary>
        /// The URL scheme, e.g. http
        /// </summary>
        public readonly string Scheme;
        /// <summary>
        /// The server name, e.g. www.example.com
        /// </summary>
        public readonly string? Host;
        /// <summary>
        /// The server path, e.g. index.html.
        /// Note that the path does NOT have a leading /.
        /// </summary>
        public readonly string Path;
        /// <summary>
        /// The server port, e.g. 80, is -1 if using the default port
        /// </summary>
        public readonly int Port;
        /// <summary>
        /// The querystring, e.g. ?id=1
        /// </summary>
        public readonly string? Query;
        /// <summary>
        /// The username, if any
        /// </summary>
        public readonly string? Username;
        /// <summary>
        /// The password, if any
        /// </summary>
        public readonly string? Password;

        /// <summary>
        /// Whether the <see cref="Path"/> is already percent-encoded and must be
        /// emitted verbatim rather than encoded again during serialization.
        /// </summary>
        public readonly bool PathIsEncoded = false;

        /// <summary>
        /// The original URI.
        /// </summary>
        public readonly string OriginalUri;

        /// <summary>
        /// Cache for the query parameters.
        /// </summary>
        private NameValueCollection? m_queryParams;

        /// <summary>
        /// Gets the parameters in the query string
        /// </summary>
        /// <value>The query parameters.</value>
        public NameValueCollection QueryParameters
        {
            get
            {
                if (m_queryParams == null)
                {
                    if (Query == null)
                        m_queryParams = new NameValueCollection();
                    else
                        m_queryParams = UrlEncoding.ParseQueryString(Query, true);
                }

                return m_queryParams;
            }
        }

        // TODO: Maybe we should return EncodedNameValueCollection and 
        // DecodedNameValueCollection so the callers do not need to
        // keep track of the flag.

        /// <summary>
        /// Returns the query parameters with the values in their original URL encoding.
        /// </summary>
        public NameValueCollection GetEncodedQueryParameters()
            => UrlEncoding.ParseQueryString(Query ?? "", false);

        /// <summary>
        /// Gets the host and path.
        /// </summary>
        /// <value>The host and path.</value>
        public string HostAndPath
        {
            get
            {
                if (string.IsNullOrEmpty(Path))
                    return Host ?? "";
                else if (string.IsNullOrEmpty(Host))
                    return Path;
                else
                    return Host + (Path == null ? "" : "/" + Path);
            }
        }

        /// <summary>
        /// Gets the path and query.
        /// </summary>
        /// <value>The path and query.</value>
        public string PathAndQuery
        {
            get
            {
                return (Path ?? "") + (Query == null ? "" : "?" + Query);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Duplicati.Library.Utility.RelaxedUri"/> struct.
        /// </summary>
        /// <param name="url">The URL to parse</param>
        public RelaxedUri(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));

            m_queryParams = null;
            this.OriginalUri = url;

            // A file:// URL whose remainder is a Windows drive path (e.g. file://c:\@folder\)
            // must not go through the generic parser: an '@' or ':' in the path would be
            // misread as user:password@host. Treat it as a local path directly. This
            // generalizes the file://c:\test / file:///C:\test handling below so it also
            // works when the path contains '@' or other userinfo-confusing characters.
            // Decode first so the encoded forms (e.g. %40 for @, %5C for \) that ToString()
            // produces are recognized as the same Windows path and round-trip cleanly.
            if (url.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
            {
                // A query string may follow the path. Split it off before decoding,
                // following standard query string logic: only a raw '?' delimits the
                // query, an encoded %3F stays part of the path. A Windows path cannot
                // contain a '?', so the split is safe.
                var remainder = url.Substring("file://".Length);
                string? query = null;
                var queryIndex = remainder.IndexOf('?');
                if (queryIndex >= 0)
                {
                    query = remainder.Substring(queryIndex + 1);
                    remainder = remainder.Substring(0, queryIndex);
                }

                var candidate = UrlEncoding.UrlDecode(remainder);

                // also accept the correctly-encoded file:///C:\ triple-slash form
                if (candidate.StartsWith("/", StringComparison.Ordinal) && WINDOWS_PATH.IsMatch(candidate))
                    candidate = candidate.Substring(1);

                if (WINDOWS_PATH.IsMatch(candidate) && candidate.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0)
                {
                    this.Scheme = "file";
                    this.Host = null;
                    this.Path = System.IO.Path.GetFullPath(candidate);
                    this.Port = -1;
                    this.Query = query;
                    this.Username = null;
                    this.Password = null;
                    return;
                }
                // otherwise fall through to the generic parser (unchanged behavior)
            }

            var m = URL_PARSER.Match(url);
            if (!m.Success || m.Length != url.Length)
            {
                var path = url;
                if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                    path = path.Substring("file://".Length);

                path = UrlEncoding.UrlDecode(path);

                if (path.IndexOfAny(System.IO.Path.GetInvalidPathChars()) < 0)
                    try
                    {
                        var fp = System.IO.Path.GetFullPath(path);
                        this.Scheme = "file";
                        this.Host = null;
                        this.Path = fp;
                        this.Port = -1;
                        this.Query = null;
                        this.Username = null;
                        this.Password = null;
                        return;
                    }
                    catch
                    {
                    }
                throw new ArgumentException(Strings.Uri.UriParseError(Utility.StripUrlUserInfo(url)), nameof(url));
            }

            this.Scheme = m.Groups["scheme"].Value;
            var h = UrlEncoding.UrlDecode(m.Groups["hostname"].Success ? m.Groups["hostname"].Value : "");

            var p = UrlEncoding.UrlDecode(m.Groups["path"].Success ? m.Groups["path"].Value : "");
            if (m.Groups["hostname"].Success && p.StartsWith("/", StringComparison.Ordinal))
                p = p.Substring(1);

            // file://c:\test support
            if (h.Length == 1 && p.StartsWith(":", StringComparison.Ordinal))
            {
                p = h + p;
                if (p.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
                    throw new ArgumentException(Strings.Uri.UriParseError(Utility.StripUrlUserInfo(url)), nameof(url));
                p = System.IO.Path.GetFullPath(p);
                h = null;
            }

            // Support correctly encoded file:///C:\test
            if ((h == null || h.Length == 0) && Scheme == "file" && WINDOWS_PATH.IsMatch(p) && p.StartsWith("/", StringComparison.Ordinal))
                p = p.Substring(1);

            this.Host = h;
            this.Path = p;

            this.Query = m.Groups["query"].Success ? m.Groups["query"].Value : null;
            this.Username = m.Groups["username"].Success ? UrlEncoding.UrlDecode(m.Groups["username"].Value) : null;
            this.Password = m.Groups["password"].Success ? UrlEncoding.UrlDecode(m.Groups["password"].Value) : null;
            if (m.Groups["port"].Success)
                this.Port = int.Parse(m.Groups["port"].Value);
            else
                this.Port = -1;
        }

        /// <summary>
        /// Constructs a free-form URI from components
        /// </summary>
        /// <param name="scheme">The url scheme, e.g. http</param>
        /// <param name="host">The hostname, e.g. www.example.com</param>
        /// <param name="path">The path, e.g. index.html</param>
        /// <param name="query">The query string, e.g. id=1</param>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <param name="port">The port</param>
        /// <param name="pathIsEncoded">Whether the path is already percent-encoded</param>
        public RelaxedUri(string scheme, string? host, string? path = null, string? query = null, string? username = null, string? password = null, int port = -1, bool pathIsEncoded = false)
        {
            m_queryParams = null;
            Scheme = scheme;
            Host = host;
            Path = path ?? "";
            PathIsEncoded = pathIsEncoded;
            Query = query;
            Username = username;
            Password = password;
            Port = port;
            OriginalUri = AsString(scheme, host, path, query, username, password, port, pathIsEncoded);
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Utility.RelaxedUri"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the current <see cref="Duplicati.Library.Utility.RelaxedUri"/>.</returns>
        public override string ToString()
        {
            return AsString(Scheme, Host, Path, Query, Username, Password, Port, PathIsEncoded);
        }

        /// <summary>
        /// Throws an exception if the host name is missing.
        /// </summary>
        public void RequireHost()
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentException(Strings.Uri.NoHostname(Utility.StripUrlUserInfo(OriginalUri)));
        }

        /// <summary>
        /// Constructs an url-like string from components.
        /// </summary>
        /// <returns>An url-like string</returns>
        /// <param name="scheme">The url scheme, e.g. http</param>
        /// <param name="host">The hostname, e.g. www.example.com</param>
        /// <param name="path">The path, e.g. index.html</param>
        /// <param name="query">The query string, e.g. id=1</param>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <param name="port">The port</param>
        /// <param name="pathIsEncoded">Whether the path is already percent-encoded</param>
        private static string AsString(string scheme, string? host, string? path, string? query, string? username, string? password, int port, bool pathIsEncoded)
        {
            var s = scheme + "://";
            if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
            {

                s += UrlEncoding.UrlEncode(username ?? "");
                s += ":";
                s += UrlEncoding.UrlEncode(password ?? "");
                s += "@";
            }

            if (!string.IsNullOrEmpty(host))
            {
                // The parser keeps IPv6 literals bracketed; encoding the
                // brackets would make the host unreadable to System.Uri
                s += host.StartsWith("[", StringComparison.Ordinal) && host.EndsWith("]", StringComparison.Ordinal)
                    ? host
                    : UrlEncoding.UrlPathEncode(host);
                if (port != -1)
                    s += ":" + port.ToString();
            }

            if (!string.IsNullOrEmpty(path))
            {
                // Append the leading `/` to the Windows path for file:///c:\test
                if (!string.IsNullOrEmpty(host) || (WINDOWS_PATH.IsMatch(path) && !path.StartsWith("/")))
                    s += "/";

                s += pathIsEncoded
                    ? path
                    : string.Join('/', path.Split('/').Select(x => UrlEncoding.UrlPathEncode(x)));
            }
            if (!string.IsNullOrEmpty(query))
                s += "?" + query;

            return s;
        }

        /// <summary>
        /// Creates a new instance with another scheme
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="scheme">The new scheme to use</param>
        public RelaxedUri SetScheme(string scheme)
        {
            return new RelaxedUri(scheme, Host, Path, Query, Username, Password, Port, PathIsEncoded);
        }

        /// <summary>
        /// Creates a new instance with another path
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="path">The new path to use</param>
        public RelaxedUri SetPath(string? path)
        {
            return new RelaxedUri(Scheme, Host, path, Query, Username, Password, Port);
        }

        /// <summary>
        /// Creates a new instance with another path whose segments are already
        /// percent-encoded, so the path is emitted verbatim during serialization.
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="encodedPath">The new pre-encoded path to use</param>
        public RelaxedUri SetEncodedPath(string? encodedPath)
        {
            return new RelaxedUri(Scheme, Host, encodedPath, Query, Username, Password, Port, pathIsEncoded: true);
        }

        /// <summary>
        /// Creates a new instance with another query
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="query">The new query to use</param>
        public RelaxedUri SetQuery(string? query)
        {
            return new RelaxedUri(Scheme, Host, Path, query, Username, Password, Port, PathIsEncoded);
        }

        /// <summary>
        /// Creates a new instance with other credentials
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="username">The new username to use</param>
        /// <param name="password">The new password to use</param>
        public RelaxedUri SetCredentials(string? username, string? password)
        {
            return new RelaxedUri(Scheme, Host, Path, Query, username, password, Port, PathIsEncoded);
        }

        /// <summary>
        /// Builds a URL together using a base URL, a path and a query.
        /// </summary>
        /// <returns>The built together URL.</returns>
        /// <param name="url">Base URL, containing schema, host, port.</param>
        /// <param name="path">Base path.</param>
        /// <param name="query">A collection of name value pairs to be translated into a query string.</param>
        /// <param name="preEncoded">A value indicating if the query contains pre-encoded values</param>
        public static string UriBuilder(string url, string path, NameValueCollection? query, bool preEncoded)
        {
            // System.UriBuilder collapses host-less urls with schemes it does not
            // know ("s3://" becomes "s3:/"), so the url is reassembled from the
            // relaxed parts instead. The serializer adds the '/' between host and
            // path itself. A host-less url without a path puts the appended path in
            // the authority position ("s3://" + "sub" => "s3://sub"), while an
            // existing path stays absolute ("file:///a" + "b" => "file:///a/b").
            var uri = new RelaxedUri(url);
            var newPath = new UrlPath(uri.Path).Append(path).ToString();
            if (!string.IsNullOrEmpty(uri.Host) || string.IsNullOrEmpty(uri.Path))
                newPath = newPath.TrimStart('/');
            return uri
                .SetPath(newPath)
                .SetQuery(query != null ? EscapeUriQuery(UrlEncoding.BuildUriQuery(query, preEncoded)) : null)
                .ToString();
        }

        /// <summary>
        /// Builds a URL together using a base URL and a pre-encoded path.
        /// The path segments are assumed to already be percent-encoded (e.g. an object
        /// name whose '/' is encoded as %2F), so they are emitted verbatim rather than
        /// encoded again. The base URL's own path is still treated as decoded.
        /// </summary>
        /// <returns>The built together URL.</returns>
        /// <param name="url">Base URL, containing schema, host, port.</param>
        /// <param name="encodedPath">The pre-encoded path to append.</param>
        /// <param name="query">A collection of name value pairs to be translated into a query string.</param>
        /// <param name="preEncodedQuery">A value indicating if the query contains pre-encoded values</param>
        public static string UriBuilderWithEncodedPath(string url, string encodedPath, NameValueCollection? query, bool preEncodedQuery)
        {
            var uri = new RelaxedUri(url);
            var newPath = new UrlPath(uri.Path).Append(encodedPath).ToString();
            if (!string.IsNullOrEmpty(uri.Host) || string.IsNullOrEmpty(uri.Path))
                newPath = newPath.TrimStart('/');
            return uri
                .SetEncodedPath(newPath)
                .SetQuery(query != null ? EscapeUriQuery(UrlEncoding.BuildUriQuery(query, preEncodedQuery)) : null)
                .ToString();
        }

        /// <summary>
        /// Escapes a raw query string the way System.Uri does, without letting
        /// System.Uri normalize the rest of the url. A '#' is encoded so it stays
        /// in the query instead of becoming a fragment.
        /// </summary>
        /// <returns>The escaped query string</returns>
        /// <param name="query">The raw query string</param>
        private static string EscapeUriQuery(string query)
            => new System.Uri("http://localhost/?" + query.Replace("#", "%23")).Query.TrimStart('?');

        /// <summary>
        /// Grab path part of a URI.
        /// At the moment, simple implementation does not remove fragments.
        /// </summary>
        /// <returns>The path.</returns>
        /// <param name="url">URL.</param>
        public static string? ExtractPath(string url)
        {
            return new RelaxedUri(url).Path;
        }

        /// <summary>
        /// Builds a URL together using a base URL and path.
        /// </summary>
        /// <returns>The built together URL.</returns>
        /// <param name="url">Base URL, containing schema, host, port.</param>
        /// <param name="path">Base path.</param>
        public static string UriBuilder(string url, string path)
        {
            return UriBuilder(url, path, null, true);
        }
    }
}

