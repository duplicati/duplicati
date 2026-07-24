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

#nullable enable

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// A compatibility wrapper that exposes the same surface as <see cref="LegacyUri"/> but
    /// chooses the parsing implementation at runtime.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default, URLs are parsed strictly with <see cref="System.Uri"/>. This rejects the
    /// ambiguous or malformed URLs that the legacy relaxed parser accepted, which avoids
    /// subtle bugs (for example an <c>@</c> in a local path being read as userinfo).
    /// </para>
    /// <para>
    /// Existing installations that rely on the old, relaxed parsing can opt back in by either
    /// setting the <c>DUPLICATI_LEGACY_URL_PARSING</c> environment variable to a truthy value,
    /// or by placing a file named <c>legacy-url-parsing.txt</c> in the binary folder (the same
    /// location as <c>insecure-permissions.txt</c>). When legacy parsing is enabled, parsing
    /// and round-tripping are delegated to <see cref="LegacyUri"/>, preserving the previous
    /// behavior exactly.
    /// </para>
    /// <para>
    /// The static helper methods (<see cref="UrlEncode"/>, <see cref="UrlDecode"/>,
    /// <see cref="ParseQueryString"/>, <see cref="BuildUriQuery"/>, <see cref="UriBuilder"/>
    /// and <see cref="ExtractPath"/>) always delegate to <see cref="LegacyUri"/> regardless of
    /// the parsing mode, since those are pure encoding/string utilities and do not depend on
    /// the URL parsing strategy.
    /// </para>
    /// </remarks>
    public readonly struct CompatUri
    {
        /// <summary>
        /// The environment variable that, when set to a truthy value, enables the legacy
        /// relaxed URL parser. Values <c>false</c>, <c>0</c>, <c>no</c> and <c>off</c> disable it;
        /// any other non-empty value (or an empty value, indicating the variable was set) enables it.
        /// </summary>
        public const string LegacyUrlParsingEnvVar = "DUPLICATI_LEGACY_URL_PARSING";

        /// <summary>
        /// The marker file that, when present in the binary folder, enables the legacy relaxed
        /// URL parser. Mirrors the <c>insecure-permissions.txt</c> opt-in mechanism.
        /// </summary>
        public const string LegacyUrlParsingMarkerFile = "legacy-url-parsing.txt";

        /// <summary>
        /// Cached result of the legacy-parsing opt-in check. The check only touches the
        /// environment and the filesystem once and is safe to cache for the process lifetime.
        /// </summary>
        private static readonly bool s_useLegacyParsing = DetectLegacyParsing();

        /// <summary>
        /// Gets a value indicating whether legacy URL parsing is enabled.
        /// </summary>
        public static bool UseLegacyParsing => s_useLegacyParsing;

        /// <summary>
        /// Detects whether the user has opted in to legacy URL parsing, either via the
        /// <see cref="LegacyUrlParsingEnvVar"/> environment variable or the
        /// <see cref="LegacyUrlParsingMarkerFile"/> marker file in the binary folder.
        /// </summary>
        /// <returns><c>true</c> if legacy parsing is enabled; otherwise <c>false</c>.</returns>
        private static bool DetectLegacyParsing()
        {
            var envValue = Environment.GetEnvironmentVariable(LegacyUrlParsingEnvVar);
            if (envValue != null)
                return ParseBool(envValue);

            var installfolder = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(installfolder))
            {
                var path = System.IO.Path.Combine(installfolder, LegacyUrlParsingMarkerFile);
                if (System.IO.File.Exists(path))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Parses a truthy string value. An empty or whitespace value is treated as enabled
        /// (the option was present); <c>false</c>, <c>0</c>, <c>no</c> and <c>off</c> disable it.
        /// </summary>
        /// <param name="value">The value to parse.</param>
        /// <returns><c>true</c> if the value is truthy; otherwise <c>false</c>.</returns>
        private static bool ParseBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            return !value.Equals("false", StringComparison.OrdinalIgnoreCase)
                   && !value.Equals("0", StringComparison.OrdinalIgnoreCase)
                   && !value.Equals("no", StringComparison.OrdinalIgnoreCase)
                   && !value.Equals("off", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The backing <see cref="LegacyUri"/> when legacy parsing is enabled, or the parsed
        /// components when using <see cref="System.Uri"/>.
        /// </summary>
        private readonly LegacyUri m_legacy;

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
        /// The original URI.
        /// </summary>
        public readonly string OriginalUri;

        /// <summary>
        /// Cache for the query parameters.
        /// </summary>
        private readonly NameValueCollection? m_queryParams;

        /// <summary>
        /// Gets the parameters in the query string
        /// </summary>
        /// <value>The query parameters.</value>
        public NameValueCollection QueryParameters
        {
            get
            {
                if (m_queryParams != null)
                    return m_queryParams;

                if (s_useLegacyParsing)
                    return m_legacy.QueryParameters;

                if (Query == null)
                    return new NameValueCollection();
                return ParseQueryString(Query);
            }
        }

        /// <summary>
        /// Gets the host and path.
        /// </summary>
        /// <value>The host and path.</value>
        public string HostAndPath
        {
            get
            {
                if (s_useLegacyParsing)
                    return m_legacy.HostAndPath;

                if (string.IsNullOrEmpty(Path))
                    return Host ?? "";
                if (string.IsNullOrEmpty(Host))
                    return Path;
                return Host + "/" + Path;
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
                if (s_useLegacyParsing)
                    return m_legacy.PathAndQuery;

                return (Path ?? "") + (Query == null ? "" : "?" + Query);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CompatUri"/> struct by parsing the
        /// given URL with the active parsing strategy.
        /// </summary>
        /// <param name="url">The URL to parse</param>
        public CompatUri(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentNullException(nameof(url));

            if (s_useLegacyParsing)
            {
                m_legacy = new LegacyUri(url);
                Scheme = m_legacy.Scheme;
                Host = m_legacy.Host;
                Path = m_legacy.Path;
                Port = m_legacy.Port;
                Query = m_legacy.Query;
                Username = m_legacy.Username;
                Password = m_legacy.Password;
                OriginalUri = m_legacy.OriginalUri;
                m_queryParams = null;
            }
            else
            {
                m_legacy = default;
                m_queryParams = null;
                OriginalUri = url;

                // Parse strictly with System.Uri. An invalid/malformed URL throws, which is
                // the intended behavior in non-legacy mode: it surfaces ambiguous URLs early
                // rather than silently guessing at their structure.
                if (!System.Uri.TryCreate(url, UriKind.Absolute, out var systemUri) || systemUri == null)
                    throw new ArgumentException(Strings.Uri.UriParseError(url), nameof(url));

                Scheme = systemUri.Scheme;
                Host = systemUri.Host.Length == 0 ? null : systemUri.Host;

                // Mirror the LegacyUri convention: strip the leading '/' only when a host is
                // present. For host-less URIs (e.g. file:///path), the leading '/' is part of
                // the absolute path and must be preserved so that round-tripping through
                // BuildUriString yields file:///path rather than file://path.
                var absolutePath = systemUri.IsAbsoluteUri ? systemUri.AbsolutePath : systemUri.OriginalString;
                Path = !string.IsNullOrEmpty(Host) && absolutePath.Length > 0 && absolutePath[0] == '/'
                    ? absolutePath.Substring(1)
                    : absolutePath;

                Port = systemUri.IsDefaultPort ? -1 : systemUri.Port;

                // System.Uri leaves the '?' out of Query, matching the LegacyUri convention
                Query = string.IsNullOrEmpty(systemUri.Query) ? null : systemUri.Query.TrimStart('?');

                var userInfo = systemUri.UserInfo;
                if (!string.IsNullOrEmpty(userInfo))
                {
                    var colon = userInfo.IndexOf(':');
                    if (colon >= 0)
                    {
                        Username = userInfo.Substring(0, colon);
                        Password = userInfo.Substring(colon + 1);
                    }
                    else
                    {
                        Username = userInfo;
                        Password = null;
                    }
                }
                else
                {
                    Username = null;
                    Password = null;
                }
            }
        }

        /// <summary>
        /// Constructs a free-form URI from components.
        /// </summary>
        /// <param name="scheme">The url scheme, e.g. http</param>
        /// <param name="host">The hostname, e.g. www.example.com</param>
        /// <param name="path">The path, e.g. index.html</param>
        /// <param name="query">The query string, e.g. id=1</param>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <param name="port">The port</param>
        public CompatUri(string scheme, string? host, string? path = null, string? query = null, string? username = null, string? password = null, int port = -1)
        {
            m_legacy = default;
            m_queryParams = null;
            Scheme = scheme;
            Host = host;
            Path = path ?? "";
            Query = query;
            Username = username;
            Password = password;
            Port = port;
            OriginalUri = AsString(scheme, host, path, query, username, password, port);
        }

        /// <summary>
        /// Returns a <see cref="System.String"/> that represents the current <see cref="CompatUri"/>.
        /// </summary>
        /// <returns>A <see cref="System.String"/> that represents the current <see cref="CompatUri"/>.</returns>
        public override string ToString()
        {
            if (s_useLegacyParsing)
                return m_legacy.ToString();

            return AsString(Scheme, Host, Path, Query, Username, Password, Port);
        }

        /// <summary>
        /// Throws an exception if the host name is missing.
        /// </summary>
        public void RequireHost()
        {
            if (string.IsNullOrEmpty(Host))
                throw new ArgumentException(Strings.Uri.NoHostname(OriginalUri));
        }

        /// <summary>
        /// Constructs an url-like string from components. This mirrors <see cref="LegacyUri"/>'s
        /// <c>AsString</c> so that round-tripping is consistent across parsing modes.
        /// </summary>
        /// <returns>An url-like string</returns>
        /// <param name="scheme">The url scheme, e.g. http</param>
        /// <param name="host">The hostname, e.g. www.example.com</param>
        /// <param name="path">The path, e.g. index.html</param>
        /// <param name="query">The query string, e.g. id=1</param>
        /// <param name="username">The username</param>
        /// <param name="password">The password</param>
        /// <param name="port">The port</param>
        private static string AsString(string scheme, string? host, string? path, string? query, string? username, string? password, int port)
            => LegacyUri.BuildUriString(scheme, host, path, query, username, password, port);

        /// <summary>
        /// Creates a new instance with another scheme
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="scheme">The new scheme to use</param>
        public CompatUri SetScheme(string scheme)
            => new CompatUri(scheme, Host, Path, Query, Username, Password, Port);

        /// <summary>
        /// Creates a new instance with another host
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="host">The new hostname to use</param>
        public CompatUri SetHost(string host)
            => new CompatUri(Scheme, host, Path, Query, Username, Password, Port);

        /// <summary>
        /// Creates a new instance with another path
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="path">The new path to use</param>
        public CompatUri SetPath(string? path)
            => new CompatUri(Scheme, Host, path, Query, Username, Password, Port);

        /// <summary>
        /// Creates a new instance with another query
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="query">The new query to use</param>
        public CompatUri SetQuery(string? query)
            => new CompatUri(Scheme, Host, Path, query, Username, Password, Port);

        /// <summary>
        /// Creates a new instance with other credentials
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="username">The new username to use</param>
        /// <param name="password">The new password to use</param>
        public CompatUri SetCredentials(string? username, string? password)
            => new CompatUri(Scheme, Host, Path, Query, username, password, Port);

        /// <summary>
        /// Creates a new instance with another port
        /// </summary>
        /// <returns>A new instance</returns>
        /// <param name="port">The new port to use</param>
        public CompatUri SetPort(int port)
            => new CompatUri(Scheme, Host, Path, Query, Username, Password, port);

        // The static encoding/query helpers always delegate to LegacyUri. These are pure
        // string utilities whose behavior must not change with the parsing mode.

        /// <summary>
        /// Encodes a URL path segment, like System.Web.HttpUtility.UrlPathEncode
        /// </summary>
        /// <returns>The encoded URL</returns>
        /// <param name="value">The URL fragment to encode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlPathEncode(string value, System.Text.Encoding? encoding = null)
            => LegacyUri.UrlPathEncode(value, encoding);

        /// <summary>
        /// Encodes a URL, like System.Web.HttpUtility.UrlEncode
        /// </summary>
        /// <returns>The encoded URL</returns>
        /// <param name="value">The URL fragment to encode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlEncode(string value, System.Text.Encoding? encoding = null, string spacevalue = "+")
            => LegacyUri.UrlEncode(value, encoding, spacevalue);

        /// <summary>
        /// Decodes a URL, like System.Web.HttpUtility.UrlDecode
        /// </summary>
        /// <returns>The decoded URL</returns>
        /// <param name="value">The URL fragment to decode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlDecode(string value, System.Text.Encoding? encoding = null)
            => LegacyUri.UrlDecode(value, encoding);

        /// <summary>
        /// Parses the query string.
        /// This is a local implementation of System.Web.HttpUtility.ParseQueryString, kept for consistent behavior.
        /// </summary>
        /// <returns>The parsed query string</returns>
        /// <param name="query">The query to parse</param>
        public static NameValueCollection ParseQueryString(string query)
            => LegacyUri.ParseQueryString(query);

        /// <summary>
        /// Parses the query string.
        /// This is a local implementation of System.Web.HttpUtility.ParseQueryString, kept for consistent behavior.
        /// </summary>
        /// <returns>The parsed query string</returns>
        /// <param name="query">The query to parse</param>
        /// <param name="decodeValues">Whether to the parameter values should be decoded or not.</param>
        public static NameValueCollection ParseQueryString(string query, bool decodeValues)
            => LegacyUri.ParseQueryString(query, decodeValues);

        /// <summary>
        /// Build the querystring to be used in a URL
        /// </summary>
        /// <returns>The generated querystring</returns>
        /// <param name="query">A collection of name value pairs to be translated into a query string</param>
        /// <param name="delimiter">The delimiter to separate key value pairs in the query string</param>
        public static string BuildUriQuery(NameValueCollection query, string delimiter)
            => LegacyUri.BuildUriQuery(query, delimiter);

        /// <summary>
        /// Build the querystring to be used in a URL
        /// </summary>
        /// <returns>The generated querystring</returns>
        /// <param name="query">A collection of name value pairs to be translated into a query string that is
        /// ampersand delimited.</param>
        public static string BuildUriQuery(NameValueCollection query)
            => LegacyUri.BuildUriQuery(query);

        /// <summary>
        /// Builds a URL together using a base URL, a path and a query.
        /// </summary>
        /// <returns>The built together URL.</returns>
        /// <param name="url">Base URL, containing schema, host, port.</param>
        /// <param name="path">Base path.</param>
        /// <param name="query">A collection of name value pairs to be translated into a query string.</param>
        public static string UriBuilder(string url, string path, NameValueCollection? query)
            => LegacyUri.UriBuilder(url, path, query);

        /// <summary>
        /// Builds a URL together using a base URL and path.
        /// </summary>
        /// <returns>The built together URL.</returns>
        /// <param name="url">Base URL, containing schema, host, port.</param>
        /// <param name="path">Base path.</param>
        public static string UriBuilder(string url, string path)
            => LegacyUri.UriBuilder(url, path);

        /// <summary>
        /// Grab path part of a URI.
        /// At the moment, simple implementation does not remove fragments.
        /// </summary>
        /// <returns>The path.</returns>
        /// <param name="url">URL.</param>
        public static string? ExtractPath(string url)
            => new CompatUri(url).Path;
    }
}
