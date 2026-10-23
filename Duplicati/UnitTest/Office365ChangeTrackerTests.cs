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

using Duplicati.Proprietary.Office365;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The Graph specific parts of the Microsoft 365 change tracking
/// </summary>
public class Office365ChangeTrackerTests
{
    [Test]
    [Category("ChangeTracking")]
    public void DeltaTokenIsExtractedFromLinks()
    {
        Assert.That(DeltaToken.FromMailDeltaLink("https://graph.microsoft.com/v1.0/users/u/mailFolders/f/messages/delta?$deltatoken=Lzt%2BZw%3D%3D"), Is.EqualTo("Lzt+Zw=="));
        Assert.That(DeltaToken.FromMailDeltaLink("https://graph.microsoft.com/v1.0/users/u/mailFolders/f/messages/delta?%24deltatoken=abc"), Is.EqualTo("abc"), "The parameter name may be encoded");
        Assert.That(DeltaToken.FromMailDeltaLink("https://graph.microsoft.com/v1.0/users/u/mailFolders/f/messages/delta?$skiptoken=abc"), Is.Null, "A next link is not a delta link");
        Assert.That(DeltaToken.FromMailDeltaLink(null), Is.Null);

        Assert.That(DeltaToken.FromDriveDeltaLink("https://graph.microsoft.com/v1.0/drives/d/root/delta?$select=id&token=MzslMjM7JTIzOyUyMzQ"), Is.EqualTo("MzslMjM7JTIzOyUyMzQ"));
        Assert.That(DeltaToken.FromDriveDeltaLink("https://graph.microsoft.com/v1.0/drives/d/root/delta?$deltatoken=xyz"), Is.EqualTo("xyz"), "Either spelling of the token parameter is accepted");
        Assert.That(DeltaToken.FromDriveDeltaLink("https://graph.microsoft.com/v1.0/drives/d/root/delta"), Is.Null);
    }
}
