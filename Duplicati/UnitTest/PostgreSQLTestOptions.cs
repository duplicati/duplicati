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

using System.Collections.Generic;
using Duplicati.Library.SourceProvider.Builtin.PostgreSQL;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Gives the PostgreSQL providers their options the way the provider loaders do: the options in
/// the URL are added to the options of the operation, and win over them.
/// </summary>
internal static class PostgreSQLTestOptions
{
    /// <summary>
    /// The options a loader gives a provider for a URL
    /// </summary>
    public static Dictionary<string, string?> FromUrl(string url, Dictionary<string, string?> options)
    {
        var merged = new Dictionary<string, string?>(options);
        var query = new RelaxedUri(url).QueryParameters;
        foreach (var key in query.AllKeys)
            if (key != null)
                merged[key] = query[key];
        return merged;
    }

    /// <summary>Parses the connection options for a URL</summary>
    public static PostgreSQLOptions Parse(string url, Dictionary<string, string?> options)
        => PostgreSQLOptions.Parse(FromUrl(url, options));

    /// <summary>Parses the restore options for a URL</summary>
    public static PostgreSQLRestoreOptions ParseRestore(string url, Dictionary<string, string?> options)
        => PostgreSQLRestoreOptions.Parse(FromUrl(url, options));
}
