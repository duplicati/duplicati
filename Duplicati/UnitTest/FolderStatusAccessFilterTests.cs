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
    private const string Key = "kzq1yJm3F0nUv2t5w8XbA6cDeGhIjLoP9rSsTuVwXyZ=";

    [Test]
    [Category("FolderStatus")]
    public void AuthenticatedCallerIsAllowedWithoutKey()
    {
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(true, null, Key, true));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(true, null, null, false));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(true, "wrong", Key, true));
    }

    [Test]
    [Category("FolderStatus")]
    public void MatchingKeyIsAllowedWhenServiceEnabled()
    {
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(false, Key, Key, true));
        Assert.IsTrue(FolderStatusAccessFilter.IsAllowed(false, " " + Key + "\n", Key, true));
    }

    [Test]
    [Category("FolderStatus")]
    public void MatchingKeyIsDeniedWhenServiceDisabled()
    {
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, Key, Key, false));
    }

    [Test]
    [Category("FolderStatus")]
    public void WrongOrMissingKeyIsDenied()
    {
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, null, Key, true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, "", Key, true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, "other", Key, true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, Key + "x", Key, true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, Key.Substring(1), Key, true));
    }

    [Test]
    [Category("FolderStatus")]
    public void MissingStoredKeyDeniesUnauthenticatedCallers()
    {
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, Key, null, true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, "", "", true));
        Assert.IsFalse(FolderStatusAccessFilter.IsAllowed(false, " ", " ", true));
    }
}
