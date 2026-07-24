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

using NUnit.Framework;
using System.Collections.Specialized;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class UriUtilityTests : BasicSetupHelper
    {
        [Test]
        [Category("UriUtility")]
        public static void TestBuildUriQuery()
        {
            var query = new NameValueCollection { { "a", "b" } };
            var queryUrl = Library.Utility.CompatUri.BuildUriQuery(query);
            Assert.AreEqual("a=b", queryUrl);
            query.Add(new NameValueCollection { { "c", "d" } });
            queryUrl = Library.Utility.CompatUri.BuildUriQuery(query);
            Assert.AreEqual("a=b&c=d", queryUrl);

            // Test with space in value
            query = new NameValueCollection { { "key", "value with space" } };
            queryUrl = Library.Utility.CompatUri.BuildUriQuery(query);
            Assert.AreEqual("key=value with space", queryUrl);

            // Test with + in value
            query = new NameValueCollection { { "key", "value+plus" } };
            queryUrl = Library.Utility.CompatUri.BuildUriQuery(query);
            Assert.AreEqual("key=value+plus", queryUrl);

            // Test with % in value
            query = new NameValueCollection { { "key", "value%percent" } };
            queryUrl = Library.Utility.CompatUri.BuildUriQuery(query);
            Assert.AreEqual("key=value%percent", queryUrl);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUrlBuilder()
        {
            var baseUrl = "http://localhost";
            var path = "files";
            var query = new NameValueCollection { { "a", "b" }, { "c", "d" }, { "e", "+ %" } };
            var url = Library.Utility.CompatUri.UriBuilder(baseUrl, path, query);
            Assert.AreEqual(baseUrl + "/" + path + "?a=b&c=d&e=+%20%25", url);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestExtractPath()
        {
            var url = "http://localhost/a/b";
            var path = Library.Utility.CompatUri.ExtractPath(url);
            Assert.AreEqual("a/b", path);
        }


        [Test]
        [Category("UriUtility")]
        public static void TestConcatPaths()
        {
            var path1 = "/a";
            var path2 = "b/";
            Assert.AreEqual("/a/b/", Library.Utility.UrlPath.Create(path1).Append(path2).ToString());
            Assert.AreEqual("/a", Library.Utility.UrlPath.Create(path1).Append(null).ToString());
            Assert.AreEqual("/b/", Library.Utility.UrlPath.Create(string.Empty).Append(path2).ToString());
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUriParse(
            [Values("[1:2:3::4]", "127.0.0.1", "hostname")] string host,
            [Values("", "user@", "user:pw@")] string user,
            [Values("", ":80")] string port,
            [Values("", "/path")] string path,
            [Values("", "?query")] string query)
        {
            string uriStr = $"http://{user}{host}{port}{path}{query}";

            // This exercises the legacy relaxed parser directly, locking in its contract.
            var uri = new Library.Utility.LegacyUri(uriStr);
            Assert.AreEqual("http", uri.Scheme);
            Assert.AreEqual(host, uri.Host);
            if (port.Length != 0)
            {
                Assert.AreEqual(80, uri.Port);
            }
            else
            {
                Assert.AreEqual(-1, uri.Port);
            }
            Assert.AreEqual(path.TrimStart('/'), uri.Path);
            Assert.AreEqual(query.Length == 0 ? null : query.TrimStart('?'), uri.Query);
            if (user.Length == 0)
            {
                Assert.IsNull(uri.Username);
                Assert.IsNull(uri.Password);
            }
            else
            {
                Assert.AreEqual("user", uri.Username);
                if (user.Contains(":"))
                {
                    Assert.AreEqual("pw", uri.Password);
                }
                else
                {
                    Assert.IsNull(uri.Password);
                }
            }
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUriParsePaths()
        {
            if (System.OperatingSystem.IsWindows())
            {
                var a = new Library.Utility.LegacyUri("file://c:/a/b/");
                var b = new Library.Utility.LegacyUri("c:/a/b/");

                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);

                a = new Library.Utility.LegacyUri("file://C:\\a\\b");
                b = new Library.Utility.LegacyUri("C:\\a\\b");
                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);
            }
            else
            {
                var a = new Library.Utility.LegacyUri("file:///a/b");
                var b = new Library.Utility.LegacyUri("/a/b");
                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);

                a = new Library.Utility.LegacyUri("file:///a/b/");
                b = new Library.Utility.LegacyUri("/a/b/");
                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);
            }
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUriParseWindowsPathWithAtSign()
        {
            // Regression for #2681: an '@' in a local (file://) Windows drive path must
            // not be parsed as user:password@host.
            if (!System.OperatingSystem.IsWindows())
                return;

            var a = new Library.Utility.LegacyUri("file://c:\\@folder\\");
            Assert.AreEqual("file", a.Scheme);
            Assert.IsNull(a.Host, "Host should be null for a local path");
            Assert.IsNull(a.Username, "Username should be null");
            Assert.IsNull(a.Password, "Password should be null");
            Assert.IsTrue(a.Path.Contains("@folder"), "Path should keep the @ folder name");
            Assert.IsTrue(System.IO.Path.IsPathRooted(a.Path), "Path should be a rooted local path");

            // The file:// form must parse the same as the raw path form
            var b = new Library.Utility.LegacyUri("c:\\@folder\\");
            Assert.AreEqual(b.Path, a.Path);
            Assert.AreEqual(b.ToString(), a.ToString());

            // Re-parsing ToString() round-trips to the same path (no corruption)
            var roundtrip = new Library.Utility.LegacyUri(a.ToString());
            Assert.AreEqual(a.Path, roundtrip.Path);
            Assert.IsNull(roundtrip.Host);
            Assert.IsNull(roundtrip.Username);

            // The url-encoded form (%40) resolves to the same path
            var encoded = new Library.Utility.LegacyUri("file://c:\\%40folder\\");
            Assert.AreEqual(a.Path, encoded.Path);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriStrictParse()
        {
            // In default (strict) mode CompatUri parses with System.Uri, so the components
            // mirror what System.Uri reports rather than the relaxed legacy interpretation.
            var uri = new Library.Utility.CompatUri("http://user:pw@example.com:8080/path?query=1");
            Assert.AreEqual("http", uri.Scheme);
            Assert.AreEqual("example.com", uri.Host);
            Assert.AreEqual(8080, uri.Port);
            Assert.AreEqual("path", uri.Path);
            Assert.AreEqual("query=1", uri.Query);
            Assert.AreEqual("user", uri.Username);
            Assert.AreEqual("pw", uri.Password);
            Assert.AreEqual("http://user:pw@example.com:8080/path?query=1", uri.OriginalUri);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriRejectsMalformedInStrictMode()
        {
            // System.Uri cannot parse this, and strict mode surfaces that as an error rather
            // than guessing at the structure. (The relaxed parser would accept it.)
            Assert.Throws<System.ArgumentException>(() => new Library.Utility.CompatUri("://no-scheme"));
            // null/empty input is rejected by the argument guard before parsing
            Assert.Throws<System.ArgumentNullException>(() => new Library.Utility.CompatUri(""));
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriSetMethodsRoundTrip()
        {
            var uri = new Library.Utility.CompatUri("http://example.com/a?x=1");
            Assert.AreEqual("https://example.com/a?x=1", uri.SetScheme("https").ToString());
            Assert.AreEqual("http://other.com/a?x=1", uri.SetHost("other.com").ToString());
            Assert.AreEqual("http://example.com/b?x=1", uri.SetPath("b").ToString());
            Assert.AreEqual("http://example.com/a?y=2", uri.SetQuery("y=2").ToString());
            Assert.AreEqual(9000, uri.SetPort(9000).Port);
            var creds = uri.SetCredentials("bob", "secret");
            Assert.AreEqual("bob", creds.Username);
            Assert.AreEqual("secret", creds.Password);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriQueryAttributes()
        {
            var uri = new Library.Utility.CompatUri("http://example.com/path?a=1&b=2");
            Assert.AreEqual("a=1&b=2", uri.Query);
            Assert.AreEqual("1", uri.QueryParameters["a"]);
            Assert.AreEqual("2", uri.QueryParameters["b"]);
            Assert.AreEqual("example.com/path", uri.HostAndPath);
            Assert.AreEqual("path?a=1&b=2", uri.PathAndQuery);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriRequireHost()
        {
            // A scheme-only URL with no host must trip RequireHost.
            var uri = new Library.Utility.CompatUri("file:///path");
            Assert.Throws<System.ArgumentException>(() => uri.RequireHost());
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriStaticHelpersDelegateToLegacy()
        {
            // The static encoding/query helpers must behave identically regardless of the
            // active parsing mode, because they are pure string utilities.
            var query = new NameValueCollection { { "a", "b" } };
            Assert.AreEqual(Library.Utility.LegacyUri.BuildUriQuery(query), Library.Utility.CompatUri.BuildUriQuery(query));
            Assert.AreEqual(Library.Utility.LegacyUri.UrlEncode("a b"), Library.Utility.CompatUri.UrlEncode("a b"));
            Assert.AreEqual(Library.Utility.LegacyUri.UrlDecode("a+b"), Library.Utility.CompatUri.UrlDecode("a+b"));
        }

        [Test]
        [Category("UriUtility")]
        public static void TestCompatUriHostLessPathKeepsLeadingSlash()
        {
            // A host-less file:// URI must keep its leading slash so it round-trips through
            // ToString() back to the triple-slash form. The FileBackend relies on
            // HostAndPath returning a rooted path here.
            var uri = new Library.Utility.CompatUri("file:///some/path/to/destination/");
            Assert.AreEqual("file", uri.Scheme);
            Assert.IsNull(uri.Host);
            Assert.AreEqual("/some/path/to/destination/", uri.Path);
            Assert.AreEqual("/some/path/to/destination/", uri.HostAndPath);
            Assert.AreEqual("file:///some/path/to/destination/", uri.ToString());
        }
    }
}
