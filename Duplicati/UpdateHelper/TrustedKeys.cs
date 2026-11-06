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
using System.IO;

namespace Duplicati.UpdateHelper;

/// <summary>
/// The keys that may sign update manifests: the keys built into the helper, or the OEM
/// key file in the install folder when present, mirroring the server's AutoUpdateSettings
/// </summary>
internal static class TrustedKeys
{
    /// <summary>
    /// The embedded resource holding the built-in keys, one RSAKeyValue XML string per line
    /// </summary>
    public const string EmbeddedResourceName = "AutoUpdateSignKeys.txt";
    /// <summary>
    /// The OEM key file that replaces the built-in keys, located in the install folder
    /// </summary>
    public const string OemKeyFilename = "oem-update-key.txt";

    /// <summary>
    /// Loads the trusted keys for an installation
    /// </summary>
    /// <param name="log">The log</param>
    /// <param name="installDir">The install folder, searched for the OEM key file</param>
    /// <returns>The keys</returns>
    public static IReadOnlyList<RsaPublicKey> Load(HelperLog log, string installDir)
    {
        var text = ReadEmbedded();

        if (!string.IsNullOrWhiteSpace(installDir))
        {
            var oem = Path.Combine(installDir, OemKeyFilename);
            if (File.Exists(oem))
            {
                log.Write($"Using the signing keys from {oem}");
                text = File.ReadAllText(oem);
            }
        }

        return Parse(text);
    }

    /// <summary>
    /// Parses keys from text, one RSAKeyValue XML string per line; the whole text is also tried as one key
    /// </summary>
    /// <param name="text">The text</param>
    /// <returns>The keys found</returns>
    public static IReadOnlyList<RsaPublicKey> Parse(string? text)
    {
        var keys = new List<RsaPublicKey>();
        if (string.IsNullOrWhiteSpace(text))
            return keys;

        var candidates = new List<string> { text.Trim() };
        candidates.AddRange(text.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        foreach (var c in candidates)
        {
            var key = RsaPublicKey.ParseXml(c);
            if (key != null && !keys.Exists(k => k.Matches(key)))
                keys.Add(key);
        }

        return keys;
    }

    private static string? ReadEmbedded()
    {
        try
        {
            using var rs = typeof(TrustedKeys).Assembly.GetManifestResourceStream(EmbeddedResourceName);
            if (rs == null)
                return null;
            using var rd = new StreamReader(rs);
            return rd.ReadToEnd();
        }
        catch
        {
            return null;
        }
    }
}
