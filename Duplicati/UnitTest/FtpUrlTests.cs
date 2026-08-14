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
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers the sanitized url the ftp backend connects with. The expectations here were
    /// measured against the parser that was in place before, so that replacing the parser
    /// shows up as a failure rather than as a silent change.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class FtpUrlTests
    {
        [TestCase("ftp://host", "ftp://host/")]
        [TestCase("ftp://host/", "ftp://host/")]
        [TestCase("ftp://host/backup", "ftp://host/backup/")]
        [TestCase("ftp://host/backup/", "ftp://host/backup/")]
        [TestCase("ftp://host/a/b/c", "ftp://host/a/b/c/")]
        [TestCase("ftp://host/a//b", "ftp://host/a//b/")]
        [TestCase("ftp://host:2121/backup", "ftp://host:2121/backup/")]
        // The ftp default port is left out of the sanitized url
        [TestCase("ftp://host:21/backup", "ftp://host/backup/")]
        [TestCase("ftp://host:0/p", "ftp://host:0/p/")]
        // The sanitized scheme is always ftp, so the other two spellings arrive the same way
        [TestCase("aftp://host/backup", "ftp://host/backup/")]
        [TestCase("aftp://host:2121/backup", "ftp://host:2121/backup/")]
        [TestCase("ftps://host/backup", "ftp://host/backup/")]
        // A host name is not case sensitive and is already lower cased here, because the
        // sanitized url has always been handed to System.Uri
        [TestCase("ftp://HOST/Backup", "ftp://host/Backup/")]
        [TestCase("ftp://host/My%20Folder", "ftp://host/My%20Folder/")]
        [TestCase("ftp://host/a b", "ftp://host/a%20b/")]
        [TestCase("ftp://host/M%C3%A4ppe", "ftp://host/M%C3%A4ppe/")]
        [TestCase("ftp://host/%23hash", "ftp://host/%23hash/")]
        [TestCase("ftp://host/a/../b", "ftp://host/b/")]
        [TestCase("ftp://host/./a", "ftp://host/a/")]
        // The query carries the backend options and is not part of the remote path. This is
        // the case to watch: System.Uri has no query for the ftp scheme and would keep the
        // '?' in the path, so the query has to come off the string before it is parsed.
        [TestCase("ftp://host/p?x=1", "ftp://host/p/")]
        [TestCase("aftp://host/p?x=1", "ftp://host/p/")]
        [TestCase("ftps://host/p?x=1", "ftp://host/p/")]
        // A '#' inside a query that is thrown away is not a fragment
        [TestCase("ftp://host/p?a#b", "ftp://host/p/")]
        [TestCase("ftp://user:secret@host/p", "ftp://host/p/")]
        public void TheSanitizedUrlIsBuiltFromTheUrl(string url, string expected)
        {
            Assert.AreEqual(expected, FTP.ParseFtpUrl(url).Url.AbsoluteUri, url);
        }

        [TestCase("ftp://host/p", 21)]
        // An input without a port, on a scheme that has no default, still lands on the ftp
        // default. The `Port == -1` fallback in CreateClient is therefore unreachable.
        [TestCase("aftp://host/p", 21)]
        [TestCase("ftps://host/p", 21)]
        [TestCase("ftp://host:2121/p", 2121)]
        [TestCase("ftp://host:21/p", 21)]
        public void ThePortIsNeverUnset(string url, int port)
        {
            Assert.AreEqual(port, FTP.ParseFtpUrl(url).Url.Port, url);
        }

        [TestCase("ftp://host/p", null, null)]
        [TestCase("ftp://user@host/p", "user", null)]
        [TestCase("ftp://user:secret@host/p", "user", "secret")]
        // A colon with nothing after it is an empty password, not a missing one
        [TestCase("ftp://user:@host/p", "user", "")]
        [TestCase("ftp://us%65r:p%40ss@host/p", "user", "p@ss")]
        [TestCase("ftp://user name@host/p", "user name", null)]
        public void TheCredentialsAreDecoded(string url, string? username, string? password)
        {
            var parsed = FTP.ParseFtpUrl(url);
            Assert.AreEqual(username, parsed.Username, url);
            Assert.AreEqual(password, parsed.Password, url);
        }

        [TestCase("aftp:///p")]
        [TestCase("ftps:///p")]
        [TestCase("ftp:///p")]
        [TestCase("ftp://")]
        public void AUrlWithoutAHostIsRejected(string url)
        {
            Assert.Throws<ArgumentException>(() => FTP.ParseFtpUrl(url), url);
        }

        [TestCase("ftp://host:99999/p")]
        [TestCase("ftp://user:p@ss@host/p")]
        public void AUrlThatCannotBeParsedIsRejected(string url)
        {
            // Both of these already fail, because the sanitized url is handed to System.Uri.
            var ex = Assert.Throws<UriFormatException>(() => FTP.ParseFtpUrl(url), url);
            Assert.IsFalse(ex!.Message.Contains("ss"), ex.Message);
        }

        [Test]
        public void AnEmptyUserWithAPasswordIsReadAsAMissingHost()
        {
            var ex = Assert.Throws<ArgumentException>(() => FTP.ParseFtpUrl("ftp://:pass@host/p"));
            Assert.IsFalse(ex!.Message.Contains("pass"), ex.Message);
        }

        [Test]
        public void APlusInThePathIsReadAsASpace()
        {
            Assert.AreEqual("ftp://host/a%20b/", FTP.ParseFtpUrl("ftp://host/a+b").Url.AbsoluteUri);
        }

        [Test]
        public void AHashInThePathIsPartOfTheFolderName()
        {
            Assert.AreEqual("ftp://host/back%23up/", FTP.ParseFtpUrl("ftp://host/back#up/").Url.AbsoluteUri);
        }

        [Test]
        public void AnEncodedSeparatorBecomesARealSeparator()
        {
            Assert.AreEqual("ftp://host/a/b/", FTP.ParseFtpUrl("ftp://host/a%2Fb").Url.AbsoluteUri);
        }

        [TestCase("ftp://[fe80::1]/p")]
        [TestCase("ftp://[fe80::1]:2121/p")]
        [TestCase("ftp://münchen.de/p")]
        public void AnIpv6OrNonAsciiHostIsRejected(string url)
        {
            // The host is percent encoded while the url is rebuilt, and the encoded form is
            // not a host System.Uri can parse.
            Assert.Throws<UriFormatException>(() => FTP.ParseFtpUrl(url), url);
        }
    }
}
