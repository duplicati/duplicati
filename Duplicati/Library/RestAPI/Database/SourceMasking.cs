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
using System.Linq;
using Duplicati.Server.Database;

#nullable enable

namespace Duplicati.Server;

/// <summary>
/// Helper class to mask and unmask sensitive information in source URLs.
/// </summary>
public static class SourceMasking
{
    private const string SPECIAL_SOURCE_PREFIX = "@";
    private const string SOURCE_SEPARATOR = "|";

    /// <summary>
    /// Checks if the source string is in the special format @/path|url://...
    /// </summary>
    public static bool IsSpecialSource(string source)
    {
        if (string.IsNullOrEmpty(source))
            return false;

        return source.StartsWith(SPECIAL_SOURCE_PREFIX) && source.Contains(SOURCE_SEPARATOR);
    }

    /// <summary>
    /// Extracts the path prefix portion (e.g., "@/path") from a special source.
    /// </summary>
    public static string ExtractPathPrefix(string source)
    {
        if (!IsSpecialSource(source))
            throw new ArgumentException("Not a special source", nameof(source));

        var separatorIndex = source.IndexOf(SOURCE_SEPARATOR, StringComparison.Ordinal);
        return source.Substring(0, separatorIndex);
    }

    /// <summary>
    /// Extracts the URL portion from a special source.
    /// </summary>
    public static string ExtractUrl(string source)
    {
        if (!IsSpecialSource(source))
            throw new ArgumentException("Not a special source", nameof(source));

        var separatorIndex = source.IndexOf(SOURCE_SEPARATOR, StringComparison.Ordinal);
        return source.Substring(separatorIndex + 1);
    }

    /// <summary>
    /// Replaces the URL portion of a special source with a new URL.
    /// </summary>
    public static string ReplaceUrl(string source, string newUrl)
    {
        if (!IsSpecialSource(source))
            throw new ArgumentException("Not a special source", nameof(source));

        string prefix = ExtractPathPrefix(source);
        return $"{prefix}{SOURCE_SEPARATOR}{newUrl}";
    }

    /// <summary>
    /// Checks if a URL contains the password placeholder.
    /// </summary>
    public static bool IsMaskedUrl(string url)
    {
        if (string.IsNullOrEmpty(url))
            return false;

        // The placeholder is "**********" (10 asterisks)
        // But we should check if it's actually used as a value
        return url.Contains(Connection.PASSWORD_PLACEHOLDER);
    }

    /// <summary>
    /// Masks a single source if it is a special source.
    /// </summary>
    public static string MaskSource(string source, IReadOnlySet<string> protectedNames)
    {
        if (!IsSpecialSource(source))
            return source;

        string url = ExtractUrl(source);
        string maskedUrl = QuerystringMasking.Mask(url, protectedNames);
        return ReplaceUrl(source, maskedUrl);
    }

    /// <summary>
    /// Masks an array of sources.
    /// </summary>
    public static string[] MaskSources(string[] sources, IReadOnlySet<string> protectedNames)
    {
        if (sources == null || sources.Length == 0)
            return sources ?? Array.Empty<string>();

        return sources.Select(s => MaskSource(s, protectedNames)).ToArray();
    }

    /// <summary>
    /// Unmasks sources by restoring masked values from the previous configuration.
    /// Uses the path prefix as a unique identifier to match sources.
    /// </summary>
    public static string[] UnmaskSources(string[] newSources, string[] previousSources)
    {
        if (newSources == null || newSources.Length == 0)
            return newSources ?? Array.Empty<string>();

        // Build a lookup from path prefix to full source for previous sources
        var previousLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in previousSources ?? Array.Empty<string>())
        {
            if (IsSpecialSource(source))
            {
                var pathPrefix = ExtractPathPrefix(source);
                previousLookup[pathPrefix] = source;
            }
        }

        // Process new sources
        var result = new string[newSources.Length];
        for (var i = 0; i < newSources.Length; i++)
        {
            var newSource = newSources[i];

            if (!IsSpecialSource(newSource))
            {
                // Regular source, keep as-is
                result[i] = newSource;
                continue;
            }

            var pathPrefix = ExtractPathPrefix(newSource);
            var urlPart = ExtractUrl(newSource);

            if (!IsMaskedUrl(urlPart))
            {
                // URL is not masked (new source with real URL), keep as-is
                result[i] = newSource;
                continue;
            }

            // URL is masked, need to restore from previous
            if (previousLookup.TryGetValue(pathPrefix, out var previousSource))
            {
                // Found matching previous source, restore the URL
                var previousUrl = ExtractUrl(previousSource);
                // We use QuerystringMasking.Unmask to handle the actual unmasking logic
                // This ensures we only replace the masked parts, though typically for sources
                // we might be replacing the whole URL if it was fully masked, but Unmask handles partials too.
                // However, the requirement says "restore the URL from the previous backup's corresponding source".
                // If the user changed non-sensitive parts of the URL but kept the mask, Unmask handles that.
                var unmaskedUrl = QuerystringMasking.Unmask(urlPart, previousUrl);
                result[i] = ReplaceUrl(newSource, unmaskedUrl);
            }
            else if (previousLookup.Count == 1)
            {
                // Fallback: If there is only one special source in the previous configuration,
                // use it even if the path prefix doesn't match. This handles cases where
                // the user renamed the source path but kept the URL (and thus the mask).
                var previousUrl = ExtractUrl(previousLookup.Values.First());
                var unmaskedUrl = QuerystringMasking.Unmask(urlPart, previousUrl);
                result[i] = ReplaceUrl(newSource, unmaskedUrl);
            }
            else
            {
                // No matching previous source found - this is an error
                // A masked URL was submitted but we have no original to restore from
                throw new InvalidOperationException(
                    $"Cannot unmask source '{pathPrefix}' because it did not exist in the previous configuration.");
            }
        }

        return result;
    }
}
