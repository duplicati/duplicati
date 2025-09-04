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

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Duplicati.Server.Database;

#nullable enable

namespace Duplicati.Server;

/// <summary>
/// Helper class to mask and unmask sensitive query parameters in backup configs.
/// </summary>
public static class QuerystringMasking
{
    /// <summary>
    /// Masks the values in the given URL for any properties listed in protectedProperties.
    /// </summary>
    /// <param name="url">The URL to mask.</param>
    /// <param name="protectedProperties">The set of property names to mask (case-insensitive).</param>
    /// <returns>>The URL with sensitive properties masked.</returns>
    public static string Mask(string urlstring, IReadOnlySet<string> protectedProperties)
    {
        if (string.IsNullOrEmpty(urlstring))
            return urlstring;

        var url = new Library.Utility.Uri(urlstring);
        if (string.IsNullOrWhiteSpace(url.Query))
            return urlstring;

        var modified = false;
        var query = Library.Utility.Uri.ParseQueryString(url.Query, false);
        foreach (var k in query.AllKeys)
        {
            if (k is null) continue;
            if (protectedProperties.Contains(k, StringComparer.OrdinalIgnoreCase))
            {
                query[k] = Connection.PASSWORD_PLACEHOLDER;
                modified = true;
            }
        }

        if (!modified) return urlstring;

        url = url.SetQuery(Library.Utility.Uri.BuildUriQuery(query));
        return url.ToString();
    }

    /// <summary>
    /// Unmasks sensitive properties in the <paramref name="newUrl"/> by copying values from the <paramref name="previousUrl"/>
    /// </summary>
    /// <param name="newUrl">The new URL which may contain masked properties.</param>
    /// <param name="previousUrl">The previous URL to copy unmasked values from.</param>
    /// <returns>The unmasked URL.</returns>
    public static string Unmask(string newUrl, string previousUrl)
    {
        if (string.IsNullOrEmpty(newUrl))
            throw new ArgumentException("newUrl is null or empty");
        if (string.IsNullOrEmpty(previousUrl))
            throw new ArgumentException("previousUrl is null or empty");

        var newUb = new Library.Utility.Uri(newUrl);
        if (string.IsNullOrWhiteSpace(newUb.Query))
            return newUrl;
        var prevUb = new Library.Utility.Uri(previousUrl);

        var newQuery = Library.Utility.Uri.ParseQueryString(newUb.Query, false);
        var prevQuery = Library.Utility.Uri.ParseQueryString(prevUb.Query ?? "", false);

        var modified = false;
        foreach (var key in newQuery.AllKeys)
        {
            if (key is null) continue;

            // Read values from the new URL for this key
            var newValues = GetValuesCaseInsensitive(newQuery, key);
            if (newValues is null || newValues.Length == 0) continue;

            // If any value is the mask, replace the entire set for that key from the previous URL
            if (newValues.Any(v => string.Equals(v, Connection.PASSWORD_PLACEHOLDER, StringComparison.Ordinal)))
            {
                var prevValues = GetValuesCaseInsensitive(prevQuery, key);
                if (prevValues is null || prevValues.Length == 0)
                    throw new InvalidOperationException($"Missing previous value for protected parameter '{key}'.");

                // Remove existing entries for this key (case-insensitive) and add prior values
                RemoveCaseInsensitive(newQuery, key);
                foreach (var pv in prevValues)
                    newQuery.Add(key, pv);

                modified = true;
            }
        }

        if (!modified)
            return newUrl;

        newUb = newUb.SetQuery(Library.Utility.Uri.BuildUriQuery(newQuery));

        return newUb.ToString();

        static string[]? GetValuesCaseInsensitive(NameValueCollection nvc, string key)
        {
            foreach (var k in nvc.AllKeys)
                if (k != null && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return nvc.GetValues(k);
            return null;
        }

        static void RemoveCaseInsensitive(NameValueCollection nvc, string key)
        {
            string? match = nvc.AllKeys?.FirstOrDefault(k => k != null && string.Equals(k, key, StringComparison.OrdinalIgnoreCase));
            if (match != null) nvc.Remove(match);
        }
    }
}
