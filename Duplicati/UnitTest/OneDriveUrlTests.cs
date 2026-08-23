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
using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The destination url names a folder in the drive, and every request the backend makes puts that
/// name inside the request url. Graph reads the path there percent-decoded, so what the folder is
/// called and what the url says are not the same string, and these cover the difference.
/// </summary>
[TestFixture]
public class OneDriveUrlTests
{
    /// <summary>
    /// What GetDrivePrefix answers for a default drive
    /// </summary>
    private const string Prefix = "/v1.0/me/drive";

    private static OneDrive Backend(string url)
        => new OneDrive(url, new Dictionary<string, string?> { ["authid"] = "test-authid" });

    /// <summary>
    /// A percent sign is part of the folder name, so it has to reach Graph encoded. Otherwise
    /// "a%20b" and "a b" are the same request and one of them is the wrong folder.
    /// </summary>
    [TestCase("onedrivev2://a%2520b", "/v1.0/me/drive/root:/a%2520b")]
    [TestCase("onedrivev2://a%20b", "/v1.0/me/drive/root:/a%20b")]
    // The url encoder spells a folder named "100%25done" as "100%2525done", and Graph decodes
    // the url once, so that is what has to arrive
    [TestCase("onedrivev2://100%2525done", "/v1.0/me/drive/root:/100%2525done")]
    [TestCase("onedrivev2://100%25done", "/v1.0/me/drive/root:/100%25done")]
    // A '#' would otherwise start a fragment and cut the rest of the url off
    [TestCase("onedrivev2://a%23b", "/v1.0/me/drive/root:/a%23b")]
    [TestCase("onedrivev2://M%C3%A4ppe", "/v1.0/me/drive/root:/M%C3%A4ppe")]
    // The separators stay separators
    [TestCase("onedrivev2://Backup/Sub", "/v1.0/me/drive/root:/Backup/Sub")]
    [TestCase("onedrivev2://Backup/", "/v1.0/me/drive/root:/Backup")]
    public void TheFolderNameReachesTheUrlEncoded(string url, string expected)
        => Assert.AreEqual(expected, Backend(url).RootItemUrl(Prefix), url);

    [Test]
    public void TwoDifferentFoldersDoNotShareAUrl()
    {
        // "a b" and "a%20b" are two folders. Reading the destination url twice made them the same
        // request, so one of them silently used the other's folder.
        var spelledAsSpace = Backend("onedrivev2://a%20b").RootItemUrl(Prefix);
        var spelledAsPercent = Backend("onedrivev2://a%2520b").RootItemUrl(Prefix);

        Assert.AreNotEqual(spelledAsSpace, spelledAsPercent,
            "A folder named \"a b\" and a folder named \"a%20b\" are addressed by the same url");
    }

    [Test]
    public void AnEncodedPlusIsAPlusAndNotASpace()
    {
        // Decoding the path twice turned "%2B" into "+" and then into a space. A plus written
        // literally still becomes a space: that happens in the shared parser, and this backend
        // cannot move to System.Uri because the first segment of its url is a folder name.
        Assert.AreEqual("/v1.0/me/drive/root:/My%2BFolder", Backend("onedrivev2://My%2BFolder").RootItemUrl(Prefix));
        Assert.AreEqual("/v1.0/me/drive/root:/My%20Folder", Backend("onedrivev2://My+Folder").RootItemUrl(Prefix));
    }

    [Test]
    public void TheNameOfARemoteFileIsUnchanged()
    {
        // The names Duplicati writes are made of characters that encode to themselves, so an
        // existing destination keeps asking for exactly what it asked for before.
        const string remotename = "duplicati-b7de2f19f7c8f4f0e9c4e6b2a1d3c5e7f.dblock.zip.aes";
        Assert.AreEqual($"/v1.0/me/drive/root:/Backup/{remotename}",
            Backend("onedrivev2://Backup").RootItemUrl(Prefix, remotename));
    }

    [Test]
    public void ARemoteNameIsEncodedToo()
    {
        Assert.AreEqual("/v1.0/me/drive/root:/Backup/a%23b", Backend("onedrivev2://Backup").RootItemUrl(Prefix, "a#b"));
    }
}
