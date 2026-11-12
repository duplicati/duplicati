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

        // A password in the user info of the URL (user:password@host) is masked too
        if (TryGetUserInfoPassword(urlstring, out var userInfo) && userInfo.Groups["password"].Length > 0)
            urlstring = userInfo.Groups["before"].Value + Connection.PASSWORD_PLACEHOLDER + urlstring.Substring(userInfo.Length);

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
    /// Removes the properties listed in protectedProperties and the password in the user info from the given URL,
    /// keeping the rest of the URL as it is written.
    /// </summary>
    /// <param name="urlstring">The URL to remove the properties from.</param>
    /// <param name="protectedProperties">The set of property names to remove (case-insensitive).</param>
    /// <returns>The URL without the sensitive properties.</returns>
    public static string Remove(string urlstring, IReadOnlySet<string> protectedProperties)
    {
        if (string.IsNullOrEmpty(urlstring))
            return urlstring;

        // user:password@host becomes user@host
        if (TryGetUserInfoPassword(urlstring, out var userInfo))
            urlstring = userInfo.Groups["before"].Value[..^1] + urlstring.Substring(userInfo.Length);

        if (!TrySplitQuery(urlstring, out var beforeQuery, out var parameters))
            return urlstring;

        var kept = parameters
            .Where(x => !protectedProperties.Contains(GetKey(x), StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (kept.Count == parameters.Length)
            return urlstring;

        return kept.Count == 0
            ? beforeQuery[..^1]
            : beforeQuery + string.Join("&", kept);
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

        var url = newUrl;

        // A masked password in the user info of the URL is restored from the previous URLs
        if (TryGetUserInfoPassword(url, out var userInfo) && Connection.IsPasswordPlaceholder(userInfo.Groups["password"].Value))
        {
            var password = GetPreviousPassword(previousUrls)
                ?? throw new InvalidOperationException("Missing previous value for the password in the URL.");
            url = userInfo.Groups["before"].Value + password + url.Substring(userInfo.Length);
        }

        if (!TrySplitQuery(url, out var beforeQuery, out var parameters))
            return url;

        // If any value of a key contains the mask, the entire set for that key is replaced from the previous URLs
        var maskedKeys = parameters
            .Where(x => Connection.IsPasswordPlaceholder(GetEncodedValue(x)))
            .Select(GetKey)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (maskedKeys.Count == 0)
            return url;

        var result = new List<string>(parameters.Length);
        var replacedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in parameters)
        {
            var key = GetKey(parameter);
            if (!maskedKeys.Contains(key))
                result.Add(parameter);
            // The previous entries go where the first entry for the key was, the other entries are dropped
            else if (replacedKeys.Add(key))
                result.AddRange(GetPreviousParameters(key, GetEncodedKey(parameter), previousUrls));
        }

        return beforeQuery + string.Join("&", result);
    }

    /// <summary>
    /// The name of the option that a backend also reads from the user info of its URL
    /// </summary>
    private const string AuthPasswordOption = "auth-password";

    /// <summary>
    /// Checks if a query key is the password option, with or without the leading dashes
    /// </summary>
    private static bool IsAuthPassword(string key)
        => string.Equals(key.TrimStart('-'), AuthPasswordOption, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the password for the user info of a URL in the previous URLs: the password in their user info,
    /// or else their <c>auth-password</c> parameter, as a UI may have moved it from one to the other.
    /// </summary>
    /// <param name="previousUrls">The previous URLs, used as a priority list.</param>
    /// <returns>The password as it is written in the previous URL, or null if there is none.</returns>
    private static string? GetPreviousPassword(IReadOnlyList<string>? previousUrls)
    {
        foreach (var previousUrl in previousUrls ?? [])
        {
            if (string.IsNullOrEmpty(previousUrl))
                continue;

            if (TryGetUserInfoPassword(previousUrl, out var userInfo))
            {
                var password = userInfo.Groups["password"].Value;
                if (password.Length > 0 && !Connection.IsPasswordPlaceholder(password))
                    return password;
            }

            if (TrySplitQuery(previousUrl, out _, out var parameters))
            {
                var password = parameters
                    .Where(x => IsAuthPassword(GetKey(x)))
                    .Select(GetEncodedValue)
                    .FirstOrDefault(x => !Connection.IsPasswordPlaceholder(x));
                if (password != null)
                    return password;
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the entries for a key in the previous URLs, as they are written there.
    /// </summary>
    /// <param name="key">The decoded key to find (case-insensitive).</param>
    /// <param name="encodedKey">The key as it is written in the new URL.</param>
    /// <param name="previousUrls">The previous URLs, used as a priority list.</param>
    /// <returns>The entries of the first URL that has a value for the key that is not masked.</returns>
    private static List<string> GetPreviousParameters(string key, string encodedKey, IReadOnlyList<string>? previousUrls)
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

        // The password may have been in the user info of the previous URL, and a UI moved it to the query
        if (previous.Count == 0 && IsAuthPassword(key))
        {
            var password = GetPreviousPassword(previousUrls);
            if (password != null)
                previous = [encodedKey + "=" + password];
        }

        if (previous.Count == 0)
            throw new InvalidOperationException($"Missing previous value for protected parameter '{key}'.");

        return previous;
    }

    /// <summary>
    /// Finds the password in the user info of a URL (user:password@host), as it is written.
    /// </summary>
    /// <param name="url">The URL to look in.</param>
    /// <param name="match">The match, with the part before the password in the group "before" and the password in the group "password".</param>
    /// <returns><c>true</c> if the URL has a password in its user info; <c>false</c> otherwise.</returns>
    private static bool TryGetUserInfoPassword(string url, out System.Text.RegularExpressions.Match match)
    {
        match = UserInfoPasswordRegex.Match(url);

        // The pattern follows the parser of RelaxedUri. A path that the parser does not read as a URL,
        // such as a Windows path with a ':' or '@' (file://c:\@folder), has no user info.
        return match.Success && new Library.Utility.RelaxedUri(url).Password != null;
    }

    /// <summary>
    /// Matches the password in the user info of a URL, as the parser of RelaxedUri reads it
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex UserInfoPasswordRegex =
        new(@"^(?<before>[^:]+://[^:\?/]+:)(?<password>[^@:\?/]*)(?=@)", System.Text.RegularExpressions.RegexOptions.Compiled);

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
