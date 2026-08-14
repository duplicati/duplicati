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
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Backend;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Covers the url the Tahoe-LAFS backend builds its requests from. The expectations here
    /// were measured against the parser that was in place before, so that replacing the
    /// parser shows up as a failure rather than as a silent change.
    /// There is no Tahoe job in the backend test workflow, so the request url is also
    /// asserted through the message handler seam rather than only through the parser.
    /// </summary>
    [TestFixture]
    [Category("UriUtility")]
    public class TahoeUrlTests
    {
        [TestCase("tahoe://host/uri/URI:DIR2:a", "http://host/uri/URI%3ADIR2%3Aa/")]
        // An already encoded colon is left as it is
        [TestCase("tahoe://host/uri/URI%3ADIR2%3Aa", "http://host/uri/URI%3ADIR2%3Aa/")]
        // Decoding happens once, so a doubly encoded colon matches the second spelling the
        // dircap check accepts
        [TestCase("tahoe://host/uri/URI%253ADIR2%253Aa", "http://host/uri/URI%253ADIR2%253Aa/")]
        [TestCase("tahoe://user:pw@host/uri/URI:DIR2:a", "http://host/uri/URI%3ADIR2%3Aa/")]
        [TestCase("tahoe://host/uri/URI:DIR2:a?x=1", "http://host/uri/URI%3ADIR2%3Aa/")]
        [TestCase("tahoe://host:3456/uri/URI:DIR2:aaa:bbb/sub", "http://host:3456/uri/URI%3ADIR2%3Aaaa%3Abbb/sub/")]
        public void TheRequestUrlIsBuiltFromTheUrl(string url, string expected)
        {
            Assert.AreEqual(expected, TahoeBackend.ParseTahoeUrl(url, false).Url, url);
        }

        [Test]
        public void TheSchemeFollowsTheSslOption()
        {
            Assert.AreEqual("https://host/uri/URI%3ADIR2%3Aa/", TahoeBackend.ParseTahoeUrl("tahoe://host/uri/URI:DIR2:a", true).Url);
        }

        [TestCase("tahoe://host/")]
        // The check is ordinal, so a different case is not the same prefix
        [TestCase("tahoe://host/Uri/URI:DIR2:a")]
        [TestCase("tahoe://host/uri/URI:DIR3:a")]
        public void AUrlWithoutADirectoryCapabilityIsRejected(string url)
        {
            Assert.Throws<UserInformationException>(() => TahoeBackend.ParseTahoeUrl(url, false), url);
        }

        [Test]
        public void AUrlWithoutAHostIsRejected()
        {
            Assert.Throws<ArgumentException>(() => TahoeBackend.ParseTahoeUrl("tahoe:///uri/URI:DIR2:a", false));
        }

        [Test]
        public void TheHostKeepsItsCase()
        {
            Assert.AreEqual("http://HOST/uri/URI%3ADIR2%3Aa/", TahoeBackend.ParseTahoeUrl("tahoe://HOST/uri/URI:DIR2:a", false).Url);
        }

        [Test]
        public void AWrittenDefaultPortIsKept()
        {
            Assert.AreEqual("http://host:80/uri/URI%3ADIR2%3Aa/", TahoeBackend.ParseTahoeUrl("tahoe://host:80/uri/URI:DIR2:a", false).Url);
        }

        [Test]
        public void APlusInThePathIsReadAsASpace()
        {
            Assert.AreEqual("http://host/uri/URI%3ADIR2%3Aa%20b/", TahoeBackend.ParseTahoeUrl("tahoe://host/uri/URI:DIR2:a+b", false).Url);
        }

        [Test]
        public void AnEncodedSeparatorBecomesARealSeparator()
        {
            Assert.AreEqual("http://host/uri/URI%3ADIR2%3Aa/", TahoeBackend.ParseTahoeUrl("tahoe://host/uri%2FURI:DIR2:a", false).Url);
        }

        [Test]
        public void AHashInThePathIsPartOfTheName()
        {
            Assert.AreEqual("http://host/uri/URI%3ADIR2%3Aa%23b/", TahoeBackend.ParseTahoeUrl("tahoe://host/uri/URI:DIR2:a#b", false).Url);
        }

        [Test]
        public void ADotSegmentInThePathIsKept()
        {
            Assert.AreEqual("http://host/uri/URI%3ADIR2%3Aa/../b/", TahoeBackend.ParseTahoeUrl("tahoe://host/uri/URI:DIR2:a/../b", false).Url);
        }

        [Test]
        public void AnIpv6HostProducesAUrlThatCannotBeParsedBack()
        {
            // The host is percent encoded while the url is rebuilt, which leaves a string
            // that GetDNSNamesAsync cannot parse back.
            var built = TahoeBackend.ParseTahoeUrl("tahoe://[fe80::1]:3456/uri/URI:DIR2:a", false).Url;
            Assert.AreEqual("http://%5Bfe80%3A%3A1%5D:3456/uri/URI%3ADIR2%3Aa/", built);
            Assert.Throws<UriFormatException>(() => new Uri(built));
        }

        private sealed class CapturingHandler : HttpMessageHandler
        {
            public Uri? LastUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                LastUri = request.RequestUri;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[\"dirnode\",{\"children\":{}}]", Encoding.UTF8, "text/plain")
                });
            }
        }

        [Test]
        public void TheRequestGoesToTheUrlTheParserBuilt()
        {
            var handler = new CapturingHandler();
            using var backend = new TahoeBackend("tahoe://example.invalid/uri/URI:DIR2:aaa:bbb/", new Dictionary<string, string?>(), handler);

            Assert.DoesNotThrowAsync(async () =>
            {
                await foreach (var _ in backend.ListAsync(CancellationToken.None))
                    break;
            });

            Assert.AreEqual("http://example.invalid/uri/URI%3ADIR2%3Aaaa%3Abbb/?t=json", handler.LastUri?.OriginalString);
        }
    }
}
