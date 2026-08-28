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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers how a sharepoint url is split into the parts the backend configures itself from.
    /// Both derived paths are asserted from the whole url, because the parser is what the backend
    /// sees the url through and a test that starts from a path cannot see what it did.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class SharePointUrlTests
    {
        [TestCase("mssp://host", "host", -1)]
        [TestCase("mssp://host/sites/Team/", "host", -1)]
        [TestCase("mssp://host:8443/sites/Team/", "host", 8443)]
        // 443 is the https port, but mssp is not a scheme the url parser knows a default for
        [TestCase("mssp://host:443/sites/Team/", "host", 443)]
        [TestCase("mssp://[1:2:3::4]:8443/p/", "[1:2:3::4]", 8443)]
        [TestCase("mssp://[fe80::1]/p/", "[fe80::1]", -1)]
        public void TheHostAndPortAreTakenFromTheUrl(string url, string host, int port)
        {
            var parsed = SharePointBackend.ParseSharePointUrl(url);
            Assert.AreEqual(host, parsed.Host, url);
            Assert.AreEqual(port, parsed.Port, url);
        }

        /// <summary>
        /// A double slash is the hint the user writes to say where the sharepoint web ends.
        /// FindCorrectWebPathAsync looks for it in Path, and ServerRelativePath is the same path
        /// with the marker taken out, so both have to come out of the parser intact.
        /// </summary>
        [TestCase("mssp://host/sites/Team/Docs/", "sites/Team/Docs/", "/sites/Team/Docs/")]
        [TestCase("mssp://host/sites/Team//Docs/", "sites/Team//Docs/", "/sites/Team/Docs/")]
        [TestCase("mssp://host/sites/Team///Docs/", "sites/Team///Docs/", "/sites/Team//Docs/")]
        // A marker in the first position is eaten with the leading separator, as it was before
        [TestCase("mssp://host//Docs/", "/Docs/", "/Docs/")]
        [TestCase("mssp://host//", "/", "/")]
        [TestCase("mssp://host/", "", "/")]
        [TestCase("mssp://host", "", "/")]
        // The query carries the backend options and is not part of the path
        [TestCase("mssp://host/sites/Team//Docs/?integrated-authentication=true", "sites/Team//Docs/", "/sites/Team/Docs/")]
        public void TheWebMarkerSurvivesInThePathAndIsTakenOutOfTheServerRelativeOne(string url, string path, string serverRelative)
        {
            var parsed = SharePointBackend.ParseSharePointUrl(url);
            Assert.AreEqual(path, parsed.Path, url);
            Assert.AreEqual(serverRelative, parsed.ServerRelativePath, url);
        }

        // A '+' is a character in a path. Only a query string spells a space that way, which is
        // what issue #4880 reported for the webdav backend; this backend read it the same way.
        [TestCase("mssp://host/sites/My+Folder/", "/sites/My+Folder/")]
        [TestCase("mssp://host/sites/a+b/c+d/", "/sites/a+b/c+d/")]
        // The encoded spelling of the same character
        [TestCase("mssp://host/sites/My%2BFolder/", "/sites/My+Folder/")]
        // A space is spelled the way a path spells one
        [TestCase("mssp://host/sites/My%20Folder/", "/sites/My Folder/")]
        [TestCase("mssp://host/sites/M%C3%A4ppe/", "/sites/Mäppe/")]
        [TestCase("mssp://host/sites/über/", "/sites/über/")]
        // A percent sign that is part of the folder name is spelled encoded, and is decoded once
        [TestCase("mssp://host/sites/a%2520b/", "/sites/a%20b/")]
        // The path names the remote folder, so its case has to survive
        [TestCase("mssp://host/Sites/Team/", "/Sites/Team/")]
        // The path is decoded before the marker is taken out, as it was before
        [TestCase("mssp://host/a%2Fb/", "/a/b/")]
        public void TheServerRelativePathKeepsWhatTheUserAskedFor(string url, string expected)
            => Assert.AreEqual(expected, SharePointBackend.ParseSharePointUrl(url).ServerRelativePath, url);

        [TestCase("mssp://host/p/", null, null)]
        [TestCase("mssp://user@host/p/", "user", null)]
        [TestCase("mssp://user:secret@host/p/", "user", "secret")]
        // A colon with nothing after it is an empty password, not a missing one
        [TestCase("mssp://user:@host/p/", "user", "")]
        [TestCase("mssp://us%65r:p%40ss@host/p/", "user", "p@ss")]
        [TestCase("mssp://user%20name:p%3Ass@host/p/", "user name", "p:ss")]
        public void TheCredentialsAreDecoded(string url, string? username, string? password)
        {
            var parsed = SharePointBackend.ParseSharePointUrl(url);
            Assert.AreEqual(username, parsed.Username, url);
            Assert.AreEqual(password, parsed.Password, url);
        }

        [TestCase("mssp:///p/")]
        [TestCase("mssp://")]
        public void AUrlWithoutAHostIsRejected(string url)
            => Assert.Throws<ArgumentException>(() => SharePointBackend.ParseSharePointUrl(url), url);

        [Test]
        public void TheHostIsLowerCased()
        {
            // The previous parser kept the case. A host name is a dns name or an ip literal and
            // neither is case sensitive, unlike the path, which still keeps its case above.
            Assert.AreEqual("host.example.com", SharePointBackend.ParseSharePointUrl("mssp://Host.Example.COM/Sites/Team/").Host);
        }

        [Test]
        public void ADotDotSegmentIsResolved()
        {
            // The previous parser passed "a/../b" through and named a folder with it.
            Assert.AreEqual("/b/", SharePointBackend.ParseSharePointUrl("mssp://host/a/../b/").ServerRelativePath);
        }

        [Test]
        public void AFragmentIsReportedRatherThanSilentlyDroppingTheRestOfThePath()
        {
            // A '#' starts a fragment, so "backup#1" is not a folder name. The previous parser had
            // no fragment and kept the characters, which quietly used a different folder.
            Assert.Throws<UserInformationException>(() => SharePointBackend.ParseSharePointUrl("mssp://host/backup#1/"));
            // The encoded spelling is a folder name and still works
            Assert.AreEqual("/backup#1/", SharePointBackend.ParseSharePointUrl("mssp://host/backup%231/").ServerRelativePath);
        }

        [TestCase("mssp://host:99999/p/")]
        [TestCase("mssp://user:p@ss@host/p/")]
        public void AUrlThatCannotMeanWhatItSaysIsRejected(string url)
        {
            // The previous parser answered port 99999, and read the host of the second one as
            // "ss@host", so both went somewhere the user did not name.
            Assert.Throws<UriFormatException>(() => SharePointBackend.ParseSharePointUrl(url), url);
        }

        /// <summary>
        /// The constructor is documented as not throwing, because the web is only searched for on
        /// first use. This is the one place the wiring from the parse method to the fields shows.
        /// </summary>
        [Test]
        public async Task TheConstructorTakesTheUrlAndReportsTheHostAsync()
        {
            var backend = new SharePointBackend("mssp://Host.Example.COM/sites/Team//Docs/", new Dictionary<string, string?>());

            Assert.AreEqual("mssp", backend.ProtocolKey);
            Assert.AreEqual(new[] { "host.example.com" }, await backend.GetDNSNamesAsync(CancellationToken.None));
        }

        /// <summary>
        /// The backend loader picks the constructor by argument count.
        /// </summary>
        [Test]
        public void TheLoaderStillFindsTheTwoArgumentConstructor()
        {
            var backend = (IBackend)Activator.CreateInstance(
                typeof(SharePointBackend), "mssp://host/sites/Team/", new Dictionary<string, string?>())!;

            Assert.AreEqual("mssp", backend.ProtocolKey);
        }
    }
}
