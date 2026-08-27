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
    /// <summary>
    /// Encoding and query string helpers for urls.
    /// These do not parse urls, and are kept apart from the url parsing so the
    /// parser can be replaced without touching the many callers that only need
    /// to encode a value or build a query string.
    /// </summary>
    public static class UrlEncoding
    {
        /// <summary>
        /// The regular expression that matches %20 type values in a querystring
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex RE_ESCAPECHAR = new System.Text.RegularExpressions.Regex(@"[^0-9a-zA-Z\-_.]", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Encodes a URL, like System.Web.HttpUtility.UrlEncode
        /// </summary>
        /// <returns>The encoded URL</returns>
        /// <param name="value">The URL fragment to encode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlPathEncode(string value, System.Text.Encoding? encoding = null)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            return UrlEncode(value, encoding, "%20");
        }

        /// <summary>
        /// Encodes a URL, like System.Web.HttpUtility.UrlEncode
        /// </summary>
        /// <returns>The encoded URL</returns>
        /// <param name="value">The URL fragment to encode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlEncode(string value, System.Text.Encoding? encoding = null, string spacevalue = "+")
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            encoding = encoding ?? System.Text.Encoding.UTF8;

            var encoder = encoding.GetEncoder();
            var inbuf = new char[1];
            var outbuf = new byte[4];

            return RE_ESCAPECHAR.Replace(value, (m) =>
            {
                if (m.Value == " ")
                    return spacevalue;

                inbuf[0] = m.Value[0];

                try
                {
                    var len = encoder.GetBytes(inbuf, 0, 1, outbuf, 0, true);
                    return "%" + BitConverter.ToString(outbuf, 0, len).Replace("-", "%");
                }
                catch
                {
                }

                //Fallback
                return m.Value;
            });

        }

        /// <summary>
        /// The regular expression that matches %20 type values in a querystring
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex RE_NUMBER = new System.Text.RegularExpressions.Regex(@"(\%(?<number>([0-9]|[a-f]|[A-F]){2}))|(\+)|(\%u(?<unicode>([0-9]|[a-f]|[A-F]){4}))", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Decodes the path part of a URL, where a "+" is the character itself rather than a
        /// space. Only a query string spells a space that way, so decoding a path with
        /// <see cref="UrlDecode"/> turns a folder named "a+b" into one named "a b".
        /// </summary>
        /// <returns>The decoded path</returns>
        /// <param name="value">The path to decode</param>
        /// <param name="encoding">The encoding to use</param>
        public static string UrlPathDecode(string value, System.Text.Encoding? encoding = null)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            return UrlDecode(value, encoding, plusIsSpace: false);
        }

        /// <summary>
        /// Decodes a URL, like System.Web.HttpUtility.UrlDecode
        /// </summary>
        /// <returns>The decoded URL</returns>
        /// <param name="value">The URL fragment to decode</param>
        /// <param name="encoding">The encoding to use</param>
        /// <param name="plusIsSpace">True to read "+" as a space, as a query string spells it</param>
        public static string UrlDecode(string value, System.Text.Encoding? encoding = null, bool plusIsSpace = true)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            encoding = encoding ?? System.Text.Encoding.UTF8;

            var decoder = encoding.GetDecoder();
            var inbuf = new byte[8];
            var outbuf = new char[8];

            return RE_NUMBER.Replace(value, (m) =>
            {
                if (m.Value == "+")
                    return plusIsSpace ? " " : "+";

                try
                {
                    // The four digits of a %uXXXX escape are a UTF-16 code unit,
                    // not bytes, so they must not go through the byte decoder
                    var unicode = m.Groups["unicode"];
                    if (unicode.Success)
                        return ((char)Convert.ToUInt16(unicode.Value, 16)).ToString();

                    var hex = m.Groups["number"].Value;
                    var bytelen = hex.Length / 2;
                    Utility.HexStringAsByteArray(hex, inbuf);
                    var c = decoder.GetChars(inbuf, 0, bytelen, outbuf, 0);
                    return new string(outbuf, 0, c);
                }
                catch
                {
                }

                //Fallback
                return m.Value;
            });

        }

        /// <summary>
        /// The regular expression that matches a=b type values in a querystring
        /// </summary>
        private static readonly System.Text.RegularExpressions.Regex RE_URLPARAM = new System.Text.RegularExpressions.Regex(@"(?<key>[^\=\&]+)(\=(?<value>[^\&]*))?", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>
        /// Parses the query string.
        /// This is a local implementation of System.Web.HttpUtility.ParseQueryString, kept for consistent behavior (originally added due to Mono limitations)
        /// </summary>
        /// <returns>The parsed query string</returns>
        /// <param name="query">The query to parse</param>
        /// <param name="decodeValues">Whether to the parameter values should be decoded or not.</param>
        public static NameValueCollection ParseQueryString(string query, bool decodeValues)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));
            if (query.StartsWith("?", StringComparison.Ordinal))
                query = query.Substring(1);
            if (string.IsNullOrEmpty(query))
                return new NameValueCollection(StringComparer.OrdinalIgnoreCase);

            var result = new NameValueCollection(StringComparer.OrdinalIgnoreCase);
            foreach (System.Text.RegularExpressions.Match m in RE_URLPARAM.Matches(query))
            {
                string value = m.Groups["value"].Success ? m.Groups["value"].Value : "";
                if (decodeValues)
                {
                    value = UrlDecode(value);
                }

                result.Add(UrlDecode(m.Groups["key"].Value), value);
            }

            return result;
        }

        /// <summary>
        /// Build the querystring to be used in a URL
        /// </summary>
        /// <returns>The generated querystring</returns>
        /// <param name="query">A collection of name value pairs to be translated into a query string</param>
        /// <param name="delimiter">The delimiter to separate key value pairs in the query string</param>
        internal static string BuildUriQuery(NameValueCollection query, string delimiter)
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            StringBuilder builder = new StringBuilder();
            foreach (var key in query.Cast<string>().Where(key => !string.IsNullOrEmpty(query[key])))
            {
                builder.Append(builder.Length == 0 ? string.Empty : delimiter)
                       .Append(key)
                       .Append("=")
                       .Append(query[key]);
            }

            return builder.ToString();
        }

        /// <summary>
        /// Build the querystring to be used in a URL and encodes all parameters
        /// </summary>
        /// <returns>The generated querystring</returns>
        /// <param name="query">A collection of name value pairs to be translated into a query string that is
        /// ampersand delimited.</param>
        /// <param name="preEncoded">A value indicating if the values are already encoded</param>
        public static string BuildUriQuery(NameValueCollection query, bool preEncoded)
        {
            if (preEncoded)
                return BuildUriQuery(query, "&");

            var qp = new NameValueCollection();
            foreach (var k in query.AllKeys)
            {
                var v = query.Get(k);
                qp[k] = string.IsNullOrEmpty(v)
                    ? v
                    : UrlEncode(v);
            }

            return BuildUriQuery(qp, "&");
        }
    }
}
