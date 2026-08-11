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

using System;
using Duplicati.Library.Backend;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers how an smb url is split into the server, the share and the path inside it.
    /// The expectations here were measured against the parser that was in place before,
    /// so that replacing the parser shows up as a failure rather than as a silent change.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class SmbUrlTests
    {
        [TestCase("smb://host/share", "host", "share", "")]
        [TestCase("smb://host/share/", "host", "share", "")]
        [TestCase("smb://host/share/sub/dir", "host", "share", "sub/dir")]
        [TestCase("smb://host/share/sub/dir/", "host", "share", "sub/dir")]
        [TestCase("smb://host", "host", "", "")]
        [TestCase("smb://host/", "host", "", "")]
        // An empty first segment leaves the share empty and puts everything in the path
        [TestCase("smb://host//share/sub", "host", "", "share/sub")]
        // The port has no meaning to this backend, so it is only skipped over
        [TestCase("smb://host:445/share/sub", "host", "share", "sub")]
        [TestCase("smb://host/My%20Share/My%20Folder", "host", "My Share", "My Folder")]
        [TestCase("smb://host/share;name/x", "host", "share;name", "x")]
        [TestCase("smb://[fe80::1]/share", "[fe80::1]", "share", "")]
        // The cifs backend derives from this one and shares the parser
        [TestCase("cifs://host/share/sub", "host", "share", "sub")]
        public void TheShareIsTheFirstSegmentOfThePath(string url, string host, string share, string path)
        {
            var parsed = SMBBackend.ParseSmbUrl(url);
            Assert.AreEqual(host, parsed.Host, url);
            Assert.AreEqual(share, parsed.ShareName, url);
            Assert.AreEqual(path, parsed.Path, url);
        }

        [TestCase("smb://user:pw@host/share/sub", "user", "pw")]
        // A domain user is written with the '@' encoded
        [TestCase("smb://user%40dom:p%40ss@host/share", "user@dom", "p@ss")]
        public void TheCredentialsAreDecoded(string url, string? username, string? password)
        {
            var parsed = SMBBackend.ParseSmbUrl(url);
            Assert.AreEqual(username, parsed.Username, url);
            Assert.AreEqual(password, parsed.Password, url);
        }

        [Test]
        public void AUrlWithoutAHostIsRejected()
        {
            Assert.Throws<ArgumentException>(() => SMBBackend.ParseSmbUrl("smb:///share"));
        }

        [Test]
        public void TheHostKeepsItsCaseButTheShareAndPathAreWhatMatter()
        {
            var parsed = SMBBackend.ParseSmbUrl("smb://SERVER01/Share/Sub");
            Assert.AreEqual("SERVER01", parsed.Host);
            Assert.AreEqual("Share", parsed.ShareName);
            Assert.AreEqual("Sub", parsed.Path);
        }

        [TestCase("smb://host/share\\sub")]
        [TestCase("smb://host/share%5Csub")]
        public void ABackslashDoesNotSeparateTheShareFromThePath(string url)
        {
            var parsed = SMBBackend.ParseSmbUrl(url);
            Assert.AreEqual("share\\sub", parsed.ShareName, url);
            Assert.AreEqual("", parsed.Path, url);
        }

        [Test]
        public void APlusInThePathIsReadAsASpace()
        {
            Assert.AreEqual("a b", SMBBackend.ParseSmbUrl("smb://host/share/a+b").Path);
        }

        [Test]
        public void AHashInTheShareIsPartOfTheShareName()
        {
            var parsed = SMBBackend.ParseSmbUrl("smb://host/share#1/x");
            Assert.AreEqual("share#1", parsed.ShareName);
            Assert.AreEqual("x", parsed.Path);
        }
    }
}
