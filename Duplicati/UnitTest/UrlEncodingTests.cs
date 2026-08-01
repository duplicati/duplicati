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

using System.Collections.Specialized;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The url encoding and query string helpers do not follow the framework, and
    /// backends depend on the differences: a space becomes a plus, a plus decodes
    /// back to a space, the set of characters left alone is smaller than RFC 3986
    /// asks for, and building a query does not encode at all. These tests write
    /// that down, so a later replacement has something to fail against.
    /// </summary>
    public class UrlEncodingTests : BasicSetupHelper
    {
        [Test]
        [Category("Utility")]
        public static void SpaceIsEncodedPerHelper()
        {
            Assert.AreEqual("a+b", Library.Utility.UrlEncoding.UrlEncode("a b"));
            Assert.AreEqual("a%20b", Library.Utility.UrlEncoding.UrlPathEncode("a b"));
            Assert.AreEqual("a-b", Library.Utility.UrlEncoding.UrlEncode("a b", spacevalue: "-"));
        }

        [Test]
        [Category("Utility")]
        public static void PlusDecodesToSpace()
        {
            // The framework leaves a plus alone, so this is one of the differences
            Assert.AreEqual("a b", Library.Utility.UrlEncoding.UrlDecode("a+b"));
            Assert.AreEqual("a b", Library.Utility.UrlEncoding.UrlDecode("a%20b"));
        }

        [Test]
        [Category("Utility")]
        public static void MultiByteSequencesAreDecoded()
        {
            // Every %XX is matched on its own, so decoding a character that takes
            // more than one byte only works because the decoder keeps its state
            // between the matches
            Assert.AreEqual("æ", Library.Utility.UrlEncoding.UrlDecode("%C3%A6"));
            Assert.AreEqual("日本", Library.Utility.UrlEncoding.UrlDecode("%E6%97%A5%E6%9C%AC"));
        }

        [Test]
        [Category("Utility")]
        public static void OnlyAlphanumericAndThreeSignsAreLeftAlone()
        {
            Assert.AreEqual("aA1-_.", Library.Utility.UrlEncoding.UrlEncode("aA1-_."));

            // RFC 3986 counts the tilde as unreserved; this helper does not
            Assert.AreEqual("%7E", Library.Utility.UrlEncoding.UrlEncode("~"));
            Assert.AreEqual("%2F", Library.Utility.UrlEncoding.UrlEncode("/"));
        }

        [Test]
        [Category("Utility")]
        public static void EncodingRoundTrips()
        {
            var value = "a b/c~d%e+f æ日本";
            Assert.AreEqual(value, Library.Utility.UrlEncoding.UrlDecode(Library.Utility.UrlEncoding.UrlEncode(value)));
            Assert.AreEqual(value, Library.Utility.UrlEncoding.UrlDecode(Library.Utility.UrlEncoding.UrlPathEncode(value)));
        }

        [Test]
        [Category("Utility")]
        public static void QueryStringIsParsedCaseInsensitively()
        {
            var parsed = Library.Utility.UrlEncoding.ParseQueryString("?Key=value&other=a+b");

            Assert.AreEqual("value", parsed["key"], "The keys are compared without case");
            Assert.AreEqual("a b", parsed["other"], "The values are decoded");
        }

        [Test]
        [Category("Utility")]
        public static void QueryStringValuesCanBeLeftEncoded()
        {
            var parsed = Library.Utility.UrlEncoding.ParseQueryString("key=a+b", false);

            Assert.AreEqual("a+b", parsed["key"]);
        }

        [Test]
        [Category("Utility")]
        public static void EmptyQueryStringGivesNoValues()
        {
            Assert.AreEqual(0, Library.Utility.UrlEncoding.ParseQueryString("").Count);
            Assert.AreEqual(0, Library.Utility.UrlEncoding.ParseQueryString("?").Count);
        }

        [Test]
        [Category("Utility")]
        public static void QueryIsBuiltWithoutEncoding()
        {
            var query = new NameValueCollection { { "key", "value with space" } };

            // The caller is responsible for encoding, which the backends rely on
            Assert.AreEqual("key=value with space", Library.Utility.UrlEncoding.BuildUriQuery(query));
            Assert.AreEqual("key=value with space", Library.Utility.UrlEncoding.BuildUriQuery(query, ";"));
        }

        [Test]
        [Category("Utility")]
        public static void EmptyValuesAreLeftOutOfTheQuery()
        {
            var query = new NameValueCollection { { "a", "b" }, { "empty", "" }, { "c", "d" } };

            Assert.AreEqual("a=b&c=d", Library.Utility.UrlEncoding.BuildUriQuery(query));
            Assert.AreEqual("a=b;c=d", Library.Utility.UrlEncoding.BuildUriQuery(query, ";"));
        }

        [Test]
        [Category("Utility")]
        public static void TestBuildUriQuery()
        {
            var query = new NameValueCollection { { "a", "b" } };
            var queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query);
            Assert.AreEqual("a=b", queryUrl);
            query.Add(new NameValueCollection { { "c", "d" } });
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query);
            Assert.AreEqual("a=b&c=d", queryUrl);

            // Test with space in value
            query = new NameValueCollection { { "key", "value with space" } };
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query);
            Assert.AreEqual("key=value with space", queryUrl);

            // Test with + in value
            query = new NameValueCollection { { "key", "value+plus" } };
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query);
            Assert.AreEqual("key=value+plus", queryUrl);

            // Test with % in value
            query = new NameValueCollection { { "key", "value%percent" } };
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query);
            Assert.AreEqual("key=value%percent", queryUrl);
        }
    }
}
