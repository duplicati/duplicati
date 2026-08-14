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
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers how a pcloud url is split into the api endpoint and the remote folder. The
    /// expectations here were measured against the parser that was in place before, so that
    /// replacing the parser shows up as a failure rather than as a silent change.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class PCloudUrlTests
    {
        [TestCase("pcloud://api.pcloud.com", "api.pcloud.com", "")]
        [TestCase("pcloud://api.pcloud.com/", "api.pcloud.com", "")]
        // The folder name is on the server, so its case has to survive
        [TestCase("pcloud://api.pcloud.com/Backup", "api.pcloud.com", "Backup")]
        [TestCase("pcloud://api.pcloud.com/Backup/Sub/", "api.pcloud.com", "Backup/Sub")]
        [TestCase("pcloud://api.pcloud.com//Backup//", "api.pcloud.com", "Backup")]
        [TestCase("pcloud://api.pcloud.com/Backup\\", "api.pcloud.com", "Backup")]
        [TestCase("pcloud://api.pcloud.com/My%20Folder", "api.pcloud.com", "My Folder")]
        // The separators come off first and the whitespace after, so an encoded space at the
        // edge is trimmed as well
        [TestCase("pcloud://api.pcloud.com/%20Backup%20", "api.pcloud.com", "Backup")]
        [TestCase("pcloud://api.pcloud.com:443/x", "api.pcloud.com", "x")]
        [TestCase("pcloud://eapi.pcloud.com/x", "eapi.pcloud.com", "x")]
        public void TheHostAndFolderAreTakenFromTheUrl(string url, string host, string path)
        {
            var parsed = pCloudBackend.ParsePCloudUrl(url);
            Assert.AreEqual(host, parsed.Host, url);
            Assert.AreEqual(path, parsed.Path, url);
        }

        [Test]
        public void AUrlWithoutAHostIsRejected()
        {
            Assert.Throws<ArgumentException>(() => pCloudBackend.ParsePCloudUrl("pcloud:///x"));
        }

        [Test]
        public void TheHostIsLowerCased()
        {
            // The previous parser kept the case. The endpoint is a host name, so this is only
            // a normalization - but see AServerWrittenInAnotherCaseIsAccepted for what it
            // means for the check against the known endpoints.
            Assert.AreEqual("api.pcloud.com", pCloudBackend.ParsePCloudUrl("pcloud://API.pCloud.com/").Host);
        }

        [Test]
        public void APlusInThePathIsNoLongerASpace()
        {
            // The previous parser applied the query string rule that reads '+' as a space to
            // the path as well, which moved the remote folder.
            Assert.AreEqual("a+b", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/a+b").Path);
        }

        [Test]
        public void ADotSegmentInThePathIsResolved()
        {
            // The previous parser kept the segment as written.
            Assert.AreEqual("b", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/a/../b").Path);
        }

        [Test]
        public void ABackslashInThePathBecomesASeparator()
        {
            // The previous parser left it alone. The path is split on both separators
            // everywhere it is used, so the folder ends up the same either way.
            Assert.AreEqual("a/b", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/a\\b").Path);
        }

        [Test]
        public void AHashInThePathIsRejected()
        {
            // The previous parser had no fragment and kept the '#' in the folder name.
            Assert.Throws<UserInformationException>(() => pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/back#up"));
        }

        [Test]
        public void AServerWrittenInAnotherCaseIsAccepted()
        {
            // The check compares the host against the known endpoints with the default
            // comparer, so it is case sensitive even though the dictionary is not, and this
            // url used to be rejected. The endpoints are written in lower case, so the host
            // now matches one. The check itself is left as it is, so that every expectation
            // that moved in this change traces back to the parser.
            var options = new Dictionary<string, string?> { ["authid"] = "x" };
            Assert.DoesNotThrow(() => new pCloudBackend("pcloud://API.pCloud.com/", options).Dispose());
        }

        [Test]
        public void AnUnknownServerIsRejected()
        {
            var options = new Dictionary<string, string?> { ["authid"] = "x" };
            var ex = Assert.Throws<UserInformationException>(() => new pCloudBackend("pcloud://example.invalid/", options));
            Assert.AreEqual("InvalidpCloudServerSpecified", ex!.HelpID);
        }

        [Test]
        public void TheCredentialsAreCheckedBeforeTheServer()
        {
            var ex = Assert.Throws<UserInformationException>(() => new pCloudBackend("pcloud://example.invalid/", new Dictionary<string, string?>()));
            Assert.AreEqual("MissingAuthID", ex!.HelpID);
        }
    }
}
