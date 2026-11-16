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
using System.Collections.Generic;
using System.Linq;
using Duplicati.Server.Database;

#nullable enable

namespace Duplicati.Server;

/// <summary>
/// Helper class to mask and unmask sensitive query parameters in backup configs.
/// </summary>
/// <remarks>
/// Only the values of the masked parameters are changed. The rest of the URL is kept as it is written,
/// as a parse and rebuild would change parts that a backend may read differently, such as an encoded
/// separator in the path, a repeated parameter or a parameter without a value.
/// </remarks>
public static class QuerystringMasking
{
    /// <summary>
    /// Masks the values in the given URL for any properties listed in protectedProperties.
    /// </summary>
    /// <param name="urlstring">The URL to mask.</param>
    /// <param name="protectedProperties">The set of property names to mask (case-insensitive).</param>
    /// <returns>>The URL with sensitive properties masked.</returns>
    public static string Mask(string urlstring, IReadOnlySet<string> protectedProperties)
    {
        if (string.IsNullOrEmpty(urlstring))
            return urlstring;

        if (!TrySplitQuery(urlstring, out var beforeQuery, out var parameters))
            return urlstring;

        var modified = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var key = GetKey(parameters[i]);
            if (key.Length > 0 && protectedProperties.Contains(key, StringComparer.OrdinalIgnoreCase))
            {
                parameters[i] = GetEncodedKey(parameters[i]) + "=" + Connection.PASSWORD_PLACEHOLDER;
                modified = true;
            }
        }

        if (!modified) return urlstring;

        return beforeQuery + string.Join("&", parameters);
    }

    /// <summary>
    /// Unmasks sensitive properties in the <paramref name="newUrl"/> by copying values from the <paramref name="previousUrl"/>
    /// </summary>
    /// <param name="newUrl">The new URL which may contain masked properties.</param>
    /// <param name="previousUrl">The previous URL to copy unmasked values from.</param>
    /// <returns>The unmasked URL.</returns>
    public static string Unmask(string newUrl, string previousUrl)
        => Unmask(newUrl, string.IsNullOrEmpty(previousUrl) ? null : [previousUrl]);

    /// <summary>
    /// Unmasks sensitive properties in the <paramref name="newUrl"/> by copying values from the <paramref name="previousUrls"/>
    /// using the list as a priority list to find the value.
    /// </summary>
    /// <param name="newUrl">The new URL which may contain masked properties.</param>
    /// <param name="previousUrls">The list of previous URLs to copy unmasked values from, used as a priority list.</param>
    /// <returns>The unmasked URL.</returns>
    public static string Unmask(string newUrl, IReadOnlyList<string>? previousUrls)
    {
        if (string.IsNullOrEmpty(newUrl))
            throw new ArgumentException("newUrl is null or empty");

        if (!TrySplitQuery(newUrl, out var beforeQuery, out var parameters))
            return newUrl;

        // If any value of a key contains the mask, the entire set for that key is replaced from the previous URLs
        var maskedKeys = parameters
            .Where(x => Connection.IsPasswordPlaceholder(GetEncodedValue(x)))
            .Select(GetKey)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (maskedKeys.Count == 0)
            return newUrl;

        var result = new List<string>(parameters.Length);
        var replacedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            var key = GetKey(parameter);
            if (!maskedKeys.Contains(key))
                result.Add(parameter);
            // The previous entries go where the first entry for the key was, the other entries are dropped
            else if (replacedKeys.Add(key))
                result.AddRange(GetPreviousParameters(key, previousUrls));
        }

        return beforeQuery + string.Join("&", result);
    }

    /// <summary>
    /// Finds the entries for a key in the previous URLs, as they are written there.
    /// </summary>
    /// <param name="key">The decoded key to find (case-insensitive).</param>
    /// <param name="previousUrls">The previous URLs, used as a priority list.</param>
    /// <returns>The entries of the first URL that has a value for the key that is not masked.</returns>
    private static List<string> GetPreviousParameters(string key, IReadOnlyList<string>? previousUrls)
    {
        var previous = new List<string>();

        // Search through previous URLs in priority order
        if (previousUrls != null)
        {
            foreach (var previousUrl in previousUrls)
            {
                if (string.IsNullOrEmpty(previousUrl))
                    continue;

                previous = TrySplitQuery(previousUrl, out _, out var previousParameters)
                    ? previousParameters.Where(x => string.Equals(GetKey(x), key, StringComparison.OrdinalIgnoreCase)).ToList()
                    : [];

                if (previous.Any(x => !Connection.IsPasswordPlaceholder(GetEncodedValue(x))))
                    break;
            }
        }

        if (previous.Count == 0)
            throw new InvalidOperationException($"Missing previous value for protected parameter '{key}'.");

        return previous;
    }

    /// <summary>
    /// Splits a URL into the part up to and including the '?' and the query parameters, as they are written.
    /// </summary>
    /// <param name="url">The URL to split.</param>
    /// <param name="beforeQuery">The part of the URL before the query, including the '?'.</param>
    /// <param name="parameters">The query parameters, still encoded.</param>
    /// <returns><c>true</c> if the URL has a query; <c>false</c> otherwise.</returns>
    private static bool TrySplitQuery(string url, out string beforeQuery, out string[] parameters)
    {
        beforeQuery = url;
        parameters = [];

        // The URL is parsed only to find out if it has a query, and to reject URLs that cannot be parsed.
        // The query starts at the first raw '?', as no part before it may contain one.
        if (string.IsNullOrWhiteSpace(new Library.Utility.RelaxedUri(url).Query))
            return false;

        var index = url.IndexOf('?');
        if (index < 0)
            return false;

        beforeQuery = url.Substring(0, index + 1);
        parameters = url.Substring(index + 1).Split('&');
        return true;
    }

    /// <summary>
    /// Gets the key of a query parameter, as it is written.
    /// </summary>
    private static string GetEncodedKey(string parameter)
    {
        var index = parameter.IndexOf('=');
        return index < 0 ? parameter : parameter.Substring(0, index);
    }

    /// <summary>
    /// Gets the decoded key of a query parameter.
    /// </summary>
    private static string GetKey(string parameter)
        => Library.Utility.UrlEncoding.UrlDecode(GetEncodedKey(parameter));

    /// <summary>
    /// Gets the value of a query parameter, as it is written, or an empty string if it has none.
    /// </summary>
    private static string GetEncodedValue(string parameter)
    {
        var index = parameter.IndexOf('=');
        return index < 0 ? "" : parameter.Substring(index + 1);
    }
}
