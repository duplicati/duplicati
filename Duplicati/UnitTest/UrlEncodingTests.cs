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
        public static void PlusIsACharacterInAPath()
        {
            // Only a query string spells a space as a plus, so a folder named "a+b" keeps its
            // name. Decoding a path with UrlDecode instead renames it to "a b", which is what
            // issue #4880 was about.
            Assert.AreEqual("a+b", Library.Utility.UrlEncoding.UrlPathDecode("a+b"));
            Assert.AreEqual("a b", Library.Utility.UrlEncoding.UrlPathDecode("a%20b"));
            Assert.AreEqual("a+b", Library.Utility.UrlEncoding.UrlPathDecode("a%2Bb"));

            // The rest of the decoding is unchanged
            Assert.AreEqual("Mäppe", Library.Utility.UrlEncoding.UrlPathDecode("M%C3%A4ppe"));
            Assert.AreEqual("æ", Library.Utility.UrlEncoding.UrlPathDecode("%u00e6"));

            // And what the five call sites in the WebDAV backend spell by hand is the same thing
            Assert.AreEqual(
                Library.Utility.UrlEncoding.UrlDecode("a+b%20c".Replace("+", "%2B")),
                Library.Utility.UrlEncoding.UrlPathDecode("a+b%20c"));
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
        public static void UnicodeEscapeIsDecodedAsCodeUnit()
        {
            // The four hex digits of a %uXXXX escape are a UTF-16 code unit, not
            // a pair of bytes to hand to the byte decoder
            Assert.AreEqual("æ", Library.Utility.UrlEncoding.UrlDecode("%u00e6"));
            Assert.AreEqual("A", Library.Utility.UrlEncoding.UrlDecode("%u0041"));
            Assert.AreEqual("a日b", Library.Utility.UrlEncoding.UrlDecode("a%u65e5b"));
        }

        [Test]
        [Category("Utility")]
        public static void UnicodeEscapeMatchesTheFramework()
        {
            // This decoder is documented as behaving like the one it is named
            // after, which is what settles how %uXXXX should be read
            foreach (var value in new[] { "%u00e6", "%u0041", "a%u65e5b" })
                Assert.AreEqual(System.Web.HttpUtility.UrlDecode(value), Library.Utility.UrlEncoding.UrlDecode(value), value);
        }

        [Test]
        [Category("Utility")]
        public static void SurrogatePairIsDecoded()
        {
            // Each escape carries one code unit, so a pair has to survive being
            // decoded one at a time
            Assert.AreEqual("\U0001F600", Library.Utility.UrlEncoding.UrlDecode("%uD83D%uDE00"));
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
            var parsed = Library.Utility.UrlEncoding.ParseQueryString("?Key=value&other=a+b", true);

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
            Assert.AreEqual(0, Library.Utility.UrlEncoding.ParseQueryString("", true).Count);
            Assert.AreEqual(0, Library.Utility.UrlEncoding.ParseQueryString("?", true).Count);
        }

        [Test]
        [Category("Utility")]
        public static void QueryIsBuiltWithoutEncoding()
        {
            var query = new NameValueCollection { { "key", "value with space" } };

            // The caller is responsible for encoding, which the backends rely on
            Assert.AreEqual("key=value with space", Library.Utility.UrlEncoding.BuildUriQuery(query, true));
            Assert.AreEqual("key=value with space", Library.Utility.UrlEncoding.BuildUriQuery(query, ";"));
        }

        [Test]
        [Category("Utility")]
        public static void EmptyValuesAreLeftOutOfTheQuery()
        {
            var query = new NameValueCollection { { "a", "b" }, { "empty", "" }, { "c", "d" } };

            Assert.AreEqual("a=b&c=d", Library.Utility.UrlEncoding.BuildUriQuery(query, true));
            Assert.AreEqual("a=b;c=d", Library.Utility.UrlEncoding.BuildUriQuery(query, ";"));
        }

        [Test]
        [Category("Utility")]
        public static void TestBuildUriQuery()
        {
            var query = new NameValueCollection { { "a", "b" } };
            var queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query, true);
            Assert.AreEqual("a=b", queryUrl);
            query.Add(new NameValueCollection { { "c", "d" } });
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query, true);
            Assert.AreEqual("a=b&c=d", queryUrl);

            // Test with space in value
            query = new NameValueCollection { { "key", "value with space" } };
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query, true);
            Assert.AreEqual("key=value with space", queryUrl);

            // Test with + in value
            query = new NameValueCollection { { "key", "value+plus" } };
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query, true);
            Assert.AreEqual("key=value+plus", queryUrl);

            // Test with % in value
            query = new NameValueCollection { { "key", "value%percent" } };
            queryUrl = Library.Utility.UrlEncoding.BuildUriQuery(query, true);
            Assert.AreEqual("key=value%percent", queryUrl);
        }
    }
}
