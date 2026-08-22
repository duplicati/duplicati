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
    /// Covers how a webdav url is split into the parts the backend configures itself from.
    /// The parts are taken from the whole url here rather than from a path handed in by hand,
    /// because the bug this replaced was that the path had already been decoded by the time
    /// the backend looked at it, and a test that starts from the path cannot see that.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class WebDavUrlTests
    {
        [TestCase("webdav://host", "host", -1, "/")]
        [TestCase("webdav://host/", "host", -1, "/")]
        [TestCase("webdav://host/backup", "host", -1, "/backup/")]
        [TestCase("webdav://host/backup/", "host", -1, "/backup/")]
        [TestCase("webdav://host/a/b/c", "host", -1, "/a/b/c/")]
        // The query carries the backend options and is not part of the remote path
        [TestCase("webdav://host/p?auth-username=x", "host", -1, "/p/")]
        [TestCase("webdav://host:8080/backup", "host", 8080, "/backup/")]
        // 80 is the http port, but webdav is not a scheme the url parser knows a default for
        [TestCase("webdav://host:80/backup", "host", 80, "/backup/")]
        [TestCase("webdav://[1:2:3::4]:8080/p", "[1:2:3::4]", 8080, "/p/")]
        [TestCase("webdav://[fe80::1]/p", "[fe80::1]", -1, "/p/")]
        public void TheHostPortAndPathAreTakenFromTheUrl(string url, string host, int port, string path)
        {
            var parsed = WEBDAV.ParseWebDavUrl(url);
            Assert.AreEqual(host, parsed.Host, url);
            Assert.AreEqual(port, parsed.Port, url);
            Assert.AreEqual(path, parsed.Path, url);
        }

        // A '+' is a character in a path. Only a query string spells a space that way, so reading
        // it as one sends every request to a folder the user does not have. Issue #4880.
        [TestCase("webdav://host/My+Folder/", "/My+Folder/")]
        [TestCase("webdav://host/a+b/c+d/", "/a+b/c+d/")]
        // The encoded spelling of the same character
        [TestCase("webdav://host/My%2BFolder/", "/My+Folder/")]
        // A space is spelled the way a path spells one
        [TestCase("webdav://host/My%20Folder/", "/My Folder/")]
        [TestCase("webdav://host/M%C3%A4ppe/", "/Mäppe/")]
        [TestCase("webdav://host/über/", "/über/")]
        // The path names the remote folder, so its case has to survive
        [TestCase("webdav://host/Backup/SubDir/", "/Backup/SubDir/")]
        [TestCase("webdav://host/a//b/", "/a//b/")]
        public void ThePathIsDecodedAndKeepsWhatTheUserAskedFor(string url, string path)
            => Assert.AreEqual(path, WEBDAV.ParseWebDavUrl(url).Path, url);

        [Test]
        public void ThePathIsDecodedOnlyOnce()
        {
            // The previous parser decoded the path, and the backend decoded it again, so a folder
            // whose name contains a percent sign could not be reached: "a%20b" spelled as
            // "a%2520b" came back as "a b".
            Assert.AreEqual("/a%20b/", WEBDAV.ParseWebDavUrl("webdav://host/a%2520b/").Path);
        }

        [Test]
        public void TheEscapedPathIsWhatTheRequestsUse()
        {
            // The requests are built from this one, so it has to stay escaped. Keeping both is the
            // point: one is compared against the responses, the other is sent to the server.
            var parsed = WEBDAV.ParseWebDavUrl("webdav://host/My%20Folder/");
            Assert.AreEqual("/My%20Folder/", parsed.EscapedPath);
            Assert.AreEqual("/My Folder/", parsed.Path);
        }

        [TestCase("webdav://host/p/", null, null)]
        [TestCase("webdav://user@host/p/", "user", null)]
        [TestCase("webdav://user:secret@host/p/", "user", "secret")]
        // A colon with nothing after it is an empty password, not a missing one
        [TestCase("webdav://user:@host/p/", "user", "")]
        [TestCase("webdav://us%65r:p%40ss@host/p/", "user", "p@ss")]
        [TestCase("webdav://user%20name:p%3Ass@host/p/", "user name", "p:ss")]
        public void TheCredentialsAreDecoded(string url, string? username, string? password)
        {
            var parsed = WEBDAV.ParseWebDavUrl(url);
            Assert.AreEqual(username, parsed.Username, url);
            Assert.AreEqual(password, parsed.Password, url);
        }

        [TestCase("webdav:///p/")]
        [TestCase("webdav://")]
        public void AUrlWithoutAHostIsRejected(string url)
            => Assert.Throws<ArgumentException>(() => WEBDAV.ParseWebDavUrl(url), url);

        [Test]
        public void TheHostIsLowerCased()
        {
            // The previous parser kept the case. A host name is a dns name or an ip literal and
            // neither is case sensitive, so this only matters for the backends that read a remote
            // folder name out of the authority instead.
            Assert.AreEqual("host.example.com", WEBDAV.ParseWebDavUrl("webdav://Host.Example.COM/Path/").Host);
            Assert.AreEqual("/Path/", WEBDAV.ParseWebDavUrl("webdav://Host.Example.COM/Path/").Path);
        }

        [Test]
        public void ADotDotSegmentIsResolved()
        {
            // The previous parser passed "a/../b" through and asked the server for it.
            Assert.AreEqual("/b/", WEBDAV.ParseWebDavUrl("webdav://host/a/../b/").Path);
        }

        [Test]
        public void AFragmentIsReportedRatherThanSilentlyDroppingTheRestOfThePath()
        {
            // A '#' starts a fragment, so "backup#1" is not a folder name. The previous parser had
            // no fragment and kept the characters, which quietly used a different folder.
            Assert.Throws<UserInformationException>(() => WEBDAV.ParseWebDavUrl("webdav://host/backup#1/"));
            // The encoded spelling is a folder name and still works
            Assert.AreEqual("/backup#1/", WEBDAV.ParseWebDavUrl("webdav://host/backup%231/").Path);
        }

        [TestCase("webdav://host:99999/p/")]
        [TestCase("webdav://user:p@ss@host/p/")]
        public void AUrlThatCannotMeanWhatItSaysIsRejected(string url)
        {
            // The previous parser answered port 99999, and read the host of the second one as
            // "ss@host", so both went somewhere the user did not name.
            Assert.Throws<UriFormatException>(() => WEBDAV.ParseWebDavUrl(url), url);
        }
    }
}
