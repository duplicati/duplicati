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

using System.Reflection;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

public class SharedRemoteOperationTests : BasicSetupHelper
{
    private static string? AppendAdditionalPath(string? url, string? additionalPath)
    {
        var method = typeof(WebserverCore.Endpoints.Shared.SharedRemoteOperation)
            .GetMethod("AppendAdditionalPath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method, "AppendAdditionalPath not found via reflection");
        return (string?)method!.Invoke(null, new object?[] { url, additionalPath });
    }

    [Test]
    [Category("RemoteOperation")]
    public void AppendAdditionalPathKeepsNonStandardSchemesIntact()
    {
        // System.UriBuilder collapses host-less urls with schemes it does not
        // know ("s3://" becomes "s3:/"); appending a path must keep them intact
        Assert.AreEqual("s3://bucket/prefix/sub", AppendAdditionalPath("s3://bucket/prefix", "sub"));
        Assert.AreEqual("s3://bucket/sub", AppendAdditionalPath("s3://bucket", "sub"));
        // A host-less url without a path puts the appended path in the authority position
        Assert.AreEqual("s3://sub?x=1", AppendAdditionalPath("s3://?x=1", "sub"));
        Assert.AreEqual("dropbox://sub?authid=x", AppendAdditionalPath("dropbox://?authid=x", "sub"));
        Assert.AreEqual("dropbox://folder/sub?authid=x", AppendAdditionalPath("dropbox://folder?authid=x", "sub"));
        // An existing absolute path keeps its leading slash
        Assert.AreEqual("file:///mnt/backup/sub", AppendAdditionalPath("file:///mnt/backup", "sub"));
        Assert.AreEqual("https://example.com/base/sub?x=1", AppendAdditionalPath("https://example.com/base?x=1", "sub"));
        // An IPv6 literal host must keep its brackets
        Assert.AreEqual("http://[::1]:8080/base/sub", AppendAdditionalPath("http://[::1]:8080/base", "sub"));
    }

    [Test]
    [Category("RemoteOperation")]
    public void AppendAdditionalPathPassesThroughWithoutPath()
    {
        Assert.AreEqual("s3://?x=1", AppendAdditionalPath("s3://?x=1", null));
        Assert.AreEqual("s3://?x=1", AppendAdditionalPath("s3://?x=1", "/"));
        Assert.IsNull(AppendAdditionalPath(null, "sub"));
    }
}
