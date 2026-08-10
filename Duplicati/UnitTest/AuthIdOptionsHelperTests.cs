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

#nullable enable
using Duplicati.Library.Utility.Options;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Pins the OAuth login url that eight backends expose as their token url. The url is
/// currently built through the relaxed parser, and the exact string it produces is not
/// what System.UriBuilder would produce for the same input, so it is written down here
/// before anyone changes how it is built.
/// </summary>
public class AuthIdOptionsHelperTests : BasicSetupHelper
{
    [Test]
    [Category("UriUtility")]
    public static void TheServicePathIsReplacedByTheTypeQuery()
    {
        // Note there is no '/' between the host and the '?': the relaxed parser omits it
        // when the path is empty. System.UriBuilder would emit 'https://example.com/?...'
        // instead, which is equivalent per RFC 3986 but not the same string.
        Assert.AreEqual("https://example.com?type=googledrive",
            AuthIdOptionsHelper.GetOAuthLoginUrl("googledrive", "https://example.com/refresh"));
    }

    [Test]
    [Category("UriUtility")]
    public static void AnExistingQueryIsKept()
    {
        Assert.AreEqual("https://example.com?a=b&type=box.com",
            AuthIdOptionsHelper.GetOAuthLoginUrl("box.com", "https://example.com/refresh?a=b"));
    }

    [Test]
    [Category("UriUtility")]
    public static void ThePortIsKept()
    {
        Assert.AreEqual("https://example.com:8443?type=dropbox",
            AuthIdOptionsHelper.GetOAuthLoginUrl("dropbox", "https://example.com:8443/refresh"));
    }

    [Test]
    [Category("UriUtility")]
    public static void GetOAuthLoginUrlNewFormatsTheSameWay()
    {
        Assert.AreEqual("https://example.com?type=pcloud",
            AuthIdOptionsHelper.GetOAuthLoginUrlNew("pcloud", "https://example.com/refresh"));
    }

    [TestCase("jottacloud")]
    [TestCase("onedrivev2")]
    [Category("UriUtility")]
    public static void TheDefaultServiceIsUsedWhenNoUrlIsGiven(string module)
    {
        // The host is deliberately not asserted: both defaults are overridable through the
        // DUPLICATI_OAUTH_SERVICE environment variable, so only the shape is pinned.
        var url = AuthIdOptionsHelper.GetOAuthLoginUrl(module, null);
        Assert.IsTrue(url.StartsWith("https://"), $"Expected an https url, got '{url}'");
        Assert.IsTrue(url.EndsWith($"?type={module}"), $"Expected the type query at the end, got '{url}'");

        var urlNew = AuthIdOptionsHelper.GetOAuthLoginUrlNew(module, null);
        Assert.IsTrue(urlNew.StartsWith("https://"), $"Expected an https url, got '{urlNew}'");
        Assert.IsTrue(urlNew.EndsWith($"?type={module}"), $"Expected the type query at the end, got '{urlNew}'");
    }
}
