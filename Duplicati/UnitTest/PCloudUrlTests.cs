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
        public void TheHostKeepsItsCase()
        {
            Assert.AreEqual("API.pCloud.com", pCloudBackend.ParsePCloudUrl("pcloud://API.pCloud.com/").Host);
        }

        [Test]
        public void APlusInThePathIsReadAsASpace()
        {
            Assert.AreEqual("a b", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/a+b").Path);
        }

        [Test]
        public void ADotSegmentInThePathIsKept()
        {
            Assert.AreEqual("a/../b", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/a/../b").Path);
        }

        [Test]
        public void ABackslashInThePathIsKept()
        {
            Assert.AreEqual("a\\b", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/a\\b").Path);
        }

        [Test]
        public void AHashInThePathIsPartOfTheFolderName()
        {
            Assert.AreEqual("back#up", pCloudBackend.ParsePCloudUrl("pcloud://api.pcloud.com/back#up").Path);
        }

        [Test]
        public void AServerWrittenInAnotherCaseIsRejected()
        {
            // The server check compares the host against the known endpoints with the default
            // comparer, so it is case sensitive even though the dictionary is not.
            var options = new Dictionary<string, string?> { ["authid"] = "x" };
            var ex = Assert.Throws<UserInformationException>(() => new pCloudBackend("pcloud://API.pCloud.com/", options));
            Assert.AreEqual("InvalidpCloudServerSpecified", ex!.HelpID);
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
