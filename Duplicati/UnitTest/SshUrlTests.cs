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
    /// Covers how an ssh url is split into the parts the backend connects with.
    /// The expectations here were measured against the parser that was in place before,
    /// so that replacing the parser shows up as a failure rather than as a silent change.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class SshUrlTests
    {
        [TestCase("ssh://host", "host", -1, "")]
        [TestCase("ssh://host/", "host", -1, "")]
        [TestCase("ssh://host/backup", "host", -1, "backup")]
        [TestCase("ssh://host/backup/", "host", -1, "backup/")]
        [TestCase("ssh://host/a/b/c", "host", -1, "a/b/c")]
        // The query carries the backend options and is not part of the remote path
        [TestCase("ssh://host/p?ssh-fingerprint=x", "host", -1, "p")]
        [TestCase("ssh://host:2222/backup", "host", 2222, "backup")]
        // 22 is the ssh port, but it is not a default the url parser knows, so it is kept
        [TestCase("ssh://host:22/backup", "host", 22, "backup")]
        // The constructor only takes a port that is greater than zero, so this leaves the default
        [TestCase("ssh://host:0/backup", "host", 0, "backup")]
        [TestCase("ssh://[1:2:3::4]:22/p", "[1:2:3::4]", 22, "p")]
        [TestCase("ssh://[fe80::1]/p", "[fe80::1]", -1, "p")]
        public void TheHostPortAndPathAreTakenFromTheUrl(string url, string host, int port, string path)
        {
            var parsed = SSHv2.ParseSshUrl(url);
            Assert.AreEqual(host, parsed.Host, url);
            Assert.AreEqual(port, parsed.Port, url);
            Assert.AreEqual(path, parsed.Path, url);
        }

        // The path is what names the remote folder, so its case has to survive
        [TestCase("ssh://host/Backup/SubDir", "Backup/SubDir")]
        [TestCase("ssh://host/My%20Folder/x", "My Folder/x")]
        [TestCase("ssh://host/M%C3%A4ppe/", "Mäppe/")]
        [TestCase("ssh://host/über/", "über/")]
        // An encoded '#' belongs to the folder name
        [TestCase("ssh://host/%23hash/", "#hash/")]
        // The path is decoded before the leading separator is dropped, so this stays absolute
        [TestCase("ssh://host/%2Fabs", "/abs")]
        [TestCase("ssh://host/a//b", "a//b")]
        // A '%' that starts nothing is left alone
        [TestCase("ssh://host/100%done", "100%done")]
        public void ThePathIsDecodedAndKeepsItsCase(string url, string path)
        {
            Assert.AreEqual(path, SSHv2.ParseSshUrl(url).Path, url);
        }

        [TestCase("ssh://host/p", null, null)]
        [TestCase("ssh://user@host/p", "user", null)]
        [TestCase("ssh://user:secret@host/p", "user", "secret")]
        // A colon with nothing after it is an empty password, not a missing one
        [TestCase("ssh://user:@host/p", "user", "")]
        [TestCase("ssh://us%65r:p%40ss@host/p", "user", "p@ss")]
        [TestCase("ssh://user%20name:p%3Ass@host/p", "user name", "p:ss")]
        [TestCase("ssh://user name@host/p", "user name", null)]
        public void TheCredentialsAreDecoded(string url, string? username, string? password)
        {
            var parsed = SSHv2.ParseSshUrl(url);
            Assert.AreEqual(username, parsed.Username, url);
            Assert.AreEqual(password, parsed.Password, url);
        }

        [TestCase("ssh:///p")]
        [TestCase("ssh://")]
        public void AUrlWithoutAHostIsRejected(string url)
        {
            Assert.Throws<ArgumentException>(() => SSHv2.ParseSshUrl(url), url);
        }

        [Test]
        public void AnEmptyUserWithAPasswordIsNoLongerReadAsAMissingHost()
        {
            // The previous parser read the ':' as the start of the authority and reported
            // a missing hostname. The password is taken now, and the missing user is what
            // the constructor reports.
            var parsed = SSHv2.ParseSshUrl("ssh://:pass@host/p");
            Assert.AreEqual("host", parsed.Host);
            Assert.IsNull(parsed.Username);
            Assert.AreEqual("pass", parsed.Password);
        }

        [Test]
        public void TheHostIsLowerCased()
        {
            // The previous parser kept the case. A host name is a dns name, a netbios name
            // or an ip literal, and none of those are case sensitive, so this only matters
            // for the backends that read a remote folder name out of the authority instead.
            Assert.AreEqual("host.example.com", SSHv2.ParseSshUrl("ssh://Host.Example.COM/p").Host);
        }

        [Test]
        public void APlusInThePathIsNoLongerASpace()
        {
            // The previous parser applied the query string rule that reads '+' as a space
            // to the path as well. A url that has been through the parser carries %2B, so
            // only a hand written url is affected.
            Assert.AreEqual("a+b", SSHv2.ParseSshUrl("ssh://host/a+b").Path);
        }

        [Test]
        public void AHashInThePathIsRejected()
        {
            // The previous parser had no fragment and kept the '#' in the folder name.
            // Accepting the url now would move the backup to "backup" instead, so the
            // user is told to write %23 rather than being sent somewhere else.
            Assert.Throws<UserInformationException>(() => SSHv2.ParseSshUrl("ssh://host/backup#1/"));
        }

        [Test]
        public void ADotSegmentInThePathIsResolved()
        {
            // The previous parser kept the segment as written.
            Assert.AreEqual("b", SSHv2.ParseSshUrl("ssh://host/a/../b").Path);
        }

        [Test]
        public void AnUnencodedAtSignInThePasswordIsRejected()
        {
            // The previous parser read the last '@' as the separator, so the password was
            // cut at the first one and the rest ended up in the host name, which then
            // failed to resolve when connecting. It is reported here instead.
            var ex = Assert.Throws<UriFormatException>(() => SSHv2.ParseSshUrl("ssh://user:p@ss@host/p"));
            Assert.IsFalse(ex!.Message.Contains("ss"), ex.Message);
        }

        [Test]
        public void APortOutsideTheValidRangeIsRejected()
        {
            // The previous parser passed it on and the connection failed later.
            Assert.Throws<UriFormatException>(() => SSHv2.ParseSshUrl("ssh://host:99999/p"));
        }
    }
}
