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

using System.Net;
using Duplicati.WebserverCore.Middlewares;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the access rules guarding the folder status endpoints
/// </summary>
[TestFixture]
public class FolderStatusAccessFilterTests
{
    private static readonly IPAddress Remote = IPAddress.Parse("192.168.1.20");

    [Test]
    [Category("FolderStatus")]
    public void AuthenticatedCallerIsAllowedFromAnywhere()
    {
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(true, Remote, true));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(true, Remote, false));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(true, null, false));
    }

    [Test]
    [Category("FolderStatus")]
    public void LoopbackCallerIsAllowedWhenServiceEnabled()
    {
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(false, IPAddress.Loopback, true));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(false, IPAddress.IPv6Loopback, true));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(false, IPAddress.Parse("127.0.0.5"), true));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(false, IPAddress.Loopback.MapToIPv6(), true));
    }

    [Test]
    [Category("FolderStatus")]
    public void LoopbackCallerIsDeniedWhenServiceDisabled()
    {
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, IPAddress.Loopback, false));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, IPAddress.IPv6Loopback, false));
    }

    [Test]
    [Category("FolderStatus")]
    public void RemoteCallerIsDeniedWithoutAuthentication()
    {
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, Remote, true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, Remote.MapToIPv6(), true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, IPAddress.Parse("2001:db8::1"), true));
    }

    [Test]
    [Category("FolderStatus")]
    public void UnknownAddressIsDeniedWithoutAuthentication()
    {
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, null, true));
    }
}
