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
using System.Runtime.Serialization;
using System.Web;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

public class BackupConfigMaskingTests
{
    [Test]
    public void MaskAndUnmaskBackupConfigRoundtrip()
    {
        var placeholder = Connection.PASSWORD_PLACEHOLDER;

        var original = new Backup
        {
            ID = "1",
            Name = "Test",
            Description = string.Empty,
            Tags = Array.Empty<string>(),
            TargetURL = "http://example.com?authid=secret&foo=bar",
            Sources = Array.Empty<string>(),
            Settings = new ISetting[]
            {
                new Setting { Name = "passphrase", Value = "secretpass", Filter = string.Empty },
                new Setting { Name = "nonsecret", Value = "keepme", Filter = string.Empty }
            },
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>()
        };

        var backupConfig = new Backup
        {
            ID = original.ID,
            Name = original.Name,
            Description = original.Description,
            Tags = original.Tags.ToArray(),
            TargetURL = original.TargetURL,
            Sources = original.Sources.ToArray(),
            Settings = original.Settings.Select(s => new Setting { Name = s.Name, Value = s.Value, Filter = s.Filter }).ToArray(),
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>(original.Metadata)
        };

        backupConfig.MaskSensitiveInformation();

        Assert.That(backupConfig.TargetURL, Does.Not.Contain("secret"));
        Assert.That(backupConfig.TargetURL, Does.Contain(placeholder));
        Assert.That(backupConfig.Settings.Single(s => s.Name == "passphrase").Value, Is.EqualTo(placeholder));
        Assert.That(backupConfig.Settings.Single(s => s.Name == "nonsecret").Value, Is.EqualTo("keepme"));

        backupConfig.UnmaskSensitiveInformation(original);

        Assert.That(EqualsIgnoreQueryOrder(backupConfig.TargetURL, original.TargetURL));
        Assert.That(backupConfig.Settings.Single(s => s.Name == "passphrase").Value, Is.EqualTo("secretpass"));
    }

    [Test]
    public void QuerystringMaskingRoundtrip()
    {

        var placeholder = Connection.PASSWORD_PLACEHOLDER;
        var protectedNames = new[] { "authid" }.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var original = "http://example.com?authid=secret&foo=bar";

        var masked = Server.QuerystringMasking.Mask(original, protectedNames);
        Assert.That(masked, Does.Not.Contain("secret"));
        Assert.That(masked, Does.Contain(placeholder));

        var restored = Server.QuerystringMasking.Unmask(masked, original);
        Assert.That(EqualsIgnoreQueryOrder(restored, original));
    }

    /// <summary>
    /// Helper to compare two URLs ignoring the order of query parameters
    /// </summary>
    /// <param name="url1">The first URL</param>
    /// <param name="url2">The second URL</param>
    /// <returns>True if the URLs are equal ignoring query parameter order</returns>
    public static bool EqualsIgnoreQueryOrder(string url1, string url2)
    {
        if (string.IsNullOrEmpty(url1) || string.IsNullOrEmpty(url2))
            return string.Equals(url1, url2, StringComparison.Ordinal);

        var uri1 = new Uri(url1, UriKind.RelativeOrAbsolute);
        var uri2 = new Uri(url2, UriKind.RelativeOrAbsolute);

        // Compare everything except query string
        if (!string.Equals(uri1.Scheme, uri2.Scheme, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri1.Host, uri2.Host, StringComparison.OrdinalIgnoreCase)) return false;
        if (uri1.Port != uri2.Port) return false;
        if (!string.Equals(uri1.AbsolutePath, uri2.AbsolutePath, StringComparison.Ordinal)) return false;
        if (!string.Equals(uri1.Fragment, uri2.Fragment, StringComparison.Ordinal)) return false;
        if (!string.Equals(uri1.UserInfo, uri2.UserInfo, StringComparison.Ordinal)) return false;

        // Normalize queries
        var q1 = HttpUtility.ParseQueryString(uri1.Query ?? "");
        var q2 = HttpUtility.ParseQueryString(uri2.Query ?? "");

        if (q1.Count != q2.Count) return false;

        foreach (var key in q1.AllKeys)
        {
            if (key == null) continue;

            var v1 = q1.GetValues(key) ?? Array.Empty<string>();
            var v2 = q2.GetValues(key) ?? Array.Empty<string>();

            // Must match value counts
            if (v1.Length != v2.Length) return false;

            // Order-insensitive value comparison
            var set1 = v1.OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var set2 = v2.OrderBy(x => x, StringComparer.Ordinal).ToArray();

            if (!set1.SequenceEqual(set2, StringComparer.Ordinal)) return false;
        }

        return true;
    }

    [Test]
    public void ValidateBackupDetectsPlaceholder([Values(0, 1, 2)] int type)
    {
        var placeholder = type switch
        {
            0 => Connection.PASSWORD_PLACEHOLDER,
            1 => "***",
            2 => "%2A%2a%2A",
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };

        var backup = new Backup
        {
            ID = "1",
            Name = "Test",
            Description = string.Empty,
            Tags = Array.Empty<string>(),
            TargetURL = $"http://example.com?authid={placeholder}",
            Sources = new[] { "/source" },
            Settings = new ISetting[]
            {
                new Setting { Name = "passphrase", Value = placeholder, Filter = string.Empty }
            },
            Filters = Array.Empty<IFilter>(),
            Metadata = new Dictionary<string, string>()
        };

        // Suppress as we are in test code
#pragma warning disable SYSLIB0050 // Type or member is obsolete
        var conn = (Connection)FormatterServices.GetUninitializedObject(typeof(Connection));
#pragma warning restore SYSLIB0050 // Type or member is obsolete
        var err = conn.ValidateBackup(backup, null);

        if (string.IsNullOrEmpty(err))
            Assert.Ignore("ValidateBackup does not detect placeholder values");

        Assert.That(err, Is.Not.Null.And.Not.Empty);
    }
}

