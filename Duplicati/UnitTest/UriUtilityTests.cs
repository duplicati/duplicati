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
        public static void TestUrlBuilderWithEncodedPath()
        {
            // A pre-encoded path must be emitted verbatim by UriBuilderWithEncodedPath,
            // so an object name whose '/' is encoded as %2F is not double-encoded into
            // %252F (which is what broke reading back files on GCS). The GCS object url
            // is /o/{object} where the object name is a single path parameter.
            const string baseUrl = "https://www.googleapis.com/storage/v1";

            // The object name "test-tool/duplicati-access-privileges-test.txt" encodes
            // its '/' once; the base path (/storage/v1) is preserved.
            Assert.AreEqual(
                baseUrl + "/b/bucket/o/test-tool%2Fduplicati-access-privileges-test.txt?alt=media",
                Library.Utility.RelaxedUri.UriBuilderWithEncodedPath(
                    baseUrl,
                    "b/bucket/o/" + Library.Utility.UrlEncoding.UrlPathEncode("test-tool/duplicati-access-privileges-test.txt"),
                    new NameValueCollection { { "alt", "media" } }, false));

            // A name that itself contains a percent-escape sequence encodes the '%' once
            Assert.AreEqual(
                baseUrl + "/b/bucket/o/test-tool%2Ftest%252f%25abc.txt",
                Library.Utility.RelaxedUri.UriBuilderWithEncodedPath(
                    baseUrl,
                    "b/bucket/o/" + Library.Utility.UrlEncoding.UrlPathEncode("test-tool/test%2f%abc.txt"),
                    null, false));

            // A space encodes once, not twice
            Assert.AreEqual(
                baseUrl + "/b/bucket/o/test-tool%2Fa%20b.txt",
                Library.Utility.RelaxedUri.UriBuilderWithEncodedPath(
                    baseUrl,
                    "b/bucket/o/" + Library.Utility.UrlEncoding.UrlPathEncode("test-tool/a b.txt"),
                    null, false));
        }

        [Test]
        [Category("UriUtility")]
        public static void TestSetEncodedPathEmitsVerbatim()
        {
            // The regular SetPath re-encodes the path, while SetEncodedPath keeps it as-is
            var uri = new Library.Utility.RelaxedUri("https://www.googleapis.com/storage/v1");
            Assert.AreEqual(
                "https://www.googleapis.com/storage/v1/b/bucket/o/test-tool%2Ffile.txt",
                uri.SetEncodedPath("storage/v1/b/bucket/o/test-tool%2Ffile.txt").ToString());
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUrlBuilder()
        {
            var baseUrl = "http://localhost";
            var path = "files";
            var query = new NameValueCollection { { "a", "b" }, { "c", "d" }, { "e", "+ %" } };
            var url = Library.Utility.RelaxedUri.UriBuilder(baseUrl, path, query, false);
            Assert.AreEqual(baseUrl + "/" + path + "?a=b&c=d&e=%2B+%25", url);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUrlBuilderWithNonStandardScheme()
        {
            // System.UriBuilder collapses host-less urls with schemes it does not
            // know ("s3://" becomes "s3:/"); the relaxed assembly must keep them intact
            var query = new NameValueCollection { { "a", "b" } };
            Assert.AreEqual("s3://bucket/prefix/sub?a=b", Library.Utility.RelaxedUri.UriBuilder("s3://bucket/prefix", "sub", query, true));
            // A host-less url without a path puts the appended path in the authority position
            Assert.AreEqual("s3://sub?a=b", Library.Utility.RelaxedUri.UriBuilder("s3://", "sub", query, true));
            // An existing absolute path keeps its leading slash
            Assert.AreEqual("file:///mnt/backup/sub", Library.Utility.RelaxedUri.UriBuilder("file:///mnt/backup", "sub", null, true));
            Assert.AreEqual("dropbox://folder/sub", Library.Utility.RelaxedUri.UriBuilder("dropbox://folder?authid=x", "sub", null, true));
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUrlBuilderWithIpv6Host()
        {
            // The brackets of an IPv6 literal host must not be percent-encoded,
            // or System.Uri can no longer parse the result
            Assert.AreEqual("http://[::1]:8080/a/b", Library.Utility.RelaxedUri.UriBuilder("http://[::1]:8080/a", "b"));
            Assert.AreEqual("http://[1:2:3::4]/a/b?x=1", Library.Utility.RelaxedUri.UriBuilder("http://[1:2:3::4]/a", "b", new NameValueCollection { { "x", "1" } }, true));
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUriToStringKeepsIpv6Brackets()
        {
            var url = "http://user:pw@[::1]:8080/a/b?x=1";
            Assert.AreEqual(url, new Library.Utility.RelaxedUri(url).ToString());
        }

        [Test]
        [Category("UriUtility")]
        public static void TestExtractPath()
        {
            var url = "http://localhost/a/b";
            var path = Library.Utility.RelaxedUri.ExtractPath(url);
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

            var uri = new Library.Utility.RelaxedUri(uriStr);
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
                var a = new Library.Utility.RelaxedUri("file://c:/a/b/");
                var b = new Library.Utility.RelaxedUri("c:/a/b/");

                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);

                a = new Library.Utility.RelaxedUri("file://C:\\a\\b");
                b = new Library.Utility.RelaxedUri("C:\\a\\b");
                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);
            }
            else
            {
                var a = new Library.Utility.RelaxedUri("file:///a/b");
                var b = new Library.Utility.RelaxedUri("/a/b");
                Assert.AreEqual(a.ToString(), b.ToString());
                Assert.AreEqual(a.Path, b.Path);

                a = new Library.Utility.RelaxedUri("file:///a/b/");
                b = new Library.Utility.RelaxedUri("/a/b/");
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

            var a = new Library.Utility.RelaxedUri("file://c:\\@folder\\");
            Assert.AreEqual("file", a.Scheme);
            Assert.IsNull(a.Host, "Host should be null for a local path");
            Assert.IsNull(a.Username, "Username should be null");
            Assert.IsNull(a.Password, "Password should be null");
            Assert.IsTrue(a.Path.Contains("@folder"), "Path should keep the @ folder name");
            Assert.IsTrue(System.IO.Path.IsPathRooted(a.Path), "Path should be a rooted local path");

            // The file:// form must parse the same as the raw path form
            var b = new Library.Utility.RelaxedUri("c:\\@folder\\");
            Assert.AreEqual(b.Path, a.Path);
            Assert.AreEqual(b.ToString(), a.ToString());

            // Re-parsing ToString() round-trips to the same path (no corruption)
            var roundtrip = new Library.Utility.RelaxedUri(a.ToString());
            Assert.AreEqual(a.Path, roundtrip.Path);
            Assert.IsNull(roundtrip.Host);
            Assert.IsNull(roundtrip.Username);

            // The url-encoded form (%40) resolves to the same path
            var encoded = new Library.Utility.RelaxedUri("file://c:\\%40folder\\");
            Assert.AreEqual(a.Path, encoded.Path);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestWindowsFileUrlKeepsTheQueryString()
        {
            // A file:// url with an encoded Windows path can still carry a query
            // string, e.g. the file backend option "use-move-for-put". The query
            // must be mapped to Query/QueryParameters and not swallowed into the
            // local path.
            var uri = new Library.Utility.RelaxedUri("file:///C%3A%5CDownloads%5C?use-move-for-put=true");

            Assert.AreEqual("file", uri.Scheme);
            Assert.IsNull(uri.Host, "Host should be null for a local path");
            Assert.AreEqual("use-move-for-put=true", uri.Query);
            Assert.AreEqual("true", uri.QueryParameters["use-move-for-put"]);
            Assert.IsFalse(uri.Path.Contains("?"), "The query delimiter must not be part of the path");
            Assert.IsFalse(uri.Path.Contains("use-move-for-put"), "The query must not be part of the path");

            // The query survives being written out and parsed again
            var roundtrip = new Library.Utility.RelaxedUri(uri.ToString());
            Assert.AreEqual("use-move-for-put=true", roundtrip.Query);
            Assert.AreEqual(uri.Path, roundtrip.Path);

            // An encoded %3F is not a query delimiter; following standard query
            // string logic it stays part of the path and no query is mapped
            var encoded = new Library.Utility.RelaxedUri("file:///C%3A%5CDownloads%5%3Fuse-move-for-put%3Dtrue");
            Assert.IsNull(encoded.Query, "An encoded %3F must not start a query string");
            Assert.IsNull(encoded.QueryParameters["use-move-for-put"]);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestDoubledFileSchemeDoesNotEncodeTheQueryString()
        {
            // A malformed url with the file:// scheme prepended twice still has a
            // recognizable query string, and it must stay exactly as given - not
            // percent-encoded or folded into the path.
            var uri = new Library.Utility.RelaxedUri("file://file:///C%3A%5CDownloads%5?use-move-for-put=true");

            Assert.AreEqual("use-move-for-put=true", uri.Query);
            Assert.AreEqual("true", uri.QueryParameters["use-move-for-put"]);
            Assert.IsFalse(uri.ToString().Contains("%3F"), "The query string must not be encoded");
            Assert.IsFalse(uri.ToString().Contains("%3D"), "The query string must not be encoded");
            Assert.IsFalse(uri.Query.Contains("%"), "The query string must not be encoded");
            Assert.IsFalse(uri.Path.Contains("use-move-for-put"), "The query must not be part of the path");

            // The query survives being written out and parsed again
            var roundtrip = new Library.Utility.RelaxedUri(uri.ToString());
            Assert.AreEqual("use-move-for-put=true", roundtrip.Query);
            Assert.AreEqual("true", roundtrip.QueryParameters["use-move-for-put"]);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestHostKeepsItsCase()
        {
            // System.Uri lowercases the host, this parser does not. #7097 replaced the
            // parser with System.Uri and had to be closed because Box broke on exactly
            // this difference, so it is pinned here for whoever tries again.
            var uri = new Library.Utility.RelaxedUri("https://ExAmPle.COM/Some/Path");

            Assert.AreEqual("ExAmPle.COM", uri.Host);
            Assert.AreEqual("Some/Path", uri.Path);
            Assert.IsTrue(uri.ToString().Contains("ExAmPle.COM"), "ToString must not change the host case");
        }

        [Test]
        [Category("UriUtility")]
        public static void TestSetSchemeOnlyChangesTheScheme()
        {
            var uri = new Library.Utility.RelaxedUri("http://user:pw@example.com:8080/a/b?x=1");
            var changed = uri.SetScheme("https");

            Assert.AreEqual("https", changed.Scheme);
            Assert.AreEqual("http", uri.Scheme, "The original instance must not change");
            Assert.AreEqual(uri.Host, changed.Host);
            Assert.AreEqual(uri.Port, changed.Port);
            Assert.AreEqual(uri.Path, changed.Path);
            Assert.AreEqual(uri.Query, changed.Query);
            Assert.AreEqual(uri.Username, changed.Username);
            Assert.AreEqual(uri.Password, changed.Password);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestSetPathOnlyChangesThePath()
        {
            var uri = new Library.Utility.RelaxedUri("http://user:pw@example.com:8080/a/b?x=1");
            var changed = uri.SetPath("c/d");

            Assert.AreEqual("c/d", changed.Path);
            Assert.AreEqual("a/b", uri.Path, "The original instance must not change");
            Assert.AreEqual(uri.Scheme, changed.Scheme);
            Assert.AreEqual(uri.Host, changed.Host);
            Assert.AreEqual(uri.Port, changed.Port);
            Assert.AreEqual(uri.Query, changed.Query);
            Assert.AreEqual(uri.Username, changed.Username);
            Assert.AreEqual(uri.Password, changed.Password);

            Assert.AreEqual("", uri.SetPath(null).Path, "A null path becomes an empty path");
        }

        [Test]
        [Category("UriUtility")]
        public static void TestSetQueryOnlyChangesTheQuery()
        {
            var uri = new Library.Utility.RelaxedUri("http://user:pw@example.com:8080/a/b?x=1");
            var changed = uri.SetQuery("y=2");

            Assert.AreEqual("y=2", changed.Query);
            Assert.AreEqual("x=1", uri.Query, "The original instance must not change");
            Assert.AreEqual(uri.Scheme, changed.Scheme);
            Assert.AreEqual(uri.Host, changed.Host);
            Assert.AreEqual(uri.Port, changed.Port);
            Assert.AreEqual(uri.Path, changed.Path);
            Assert.AreEqual(uri.Username, changed.Username);
            Assert.AreEqual(uri.Password, changed.Password);

            Assert.IsNull(uri.SetQuery(null).Query, "A null query stays null");
        }

        [Test]
        [Category("UriUtility")]
        public static void TestSetCredentialsOnlyChangesTheCredentials()
        {
            var uri = new Library.Utility.RelaxedUri("http://user:pw@example.com:8080/a/b?x=1");
            var changed = uri.SetCredentials("other", "secret");

            Assert.AreEqual("other", changed.Username);
            Assert.AreEqual("secret", changed.Password);
            Assert.AreEqual("user", uri.Username, "The original instance must not change");
            Assert.AreEqual(uri.Scheme, changed.Scheme);
            Assert.AreEqual(uri.Host, changed.Host);
            Assert.AreEqual(uri.Port, changed.Port);
            Assert.AreEqual(uri.Path, changed.Path);
            Assert.AreEqual(uri.Query, changed.Query);

            var cleared = uri.SetCredentials(null, null);
            Assert.IsNull(cleared.Username);
            Assert.IsNull(cleared.Password);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestToStringRoundTripsThroughTheParser()
        {
            var uri = new Library.Utility.RelaxedUri("http://user:pw@example.com:8080/a/b?x=1");
            var rebuilt = new Library.Utility.RelaxedUri(uri.ToString());

            Assert.AreEqual(uri.Scheme, rebuilt.Scheme);
            Assert.AreEqual(uri.Host, rebuilt.Host);
            Assert.AreEqual(uri.Port, rebuilt.Port);
            Assert.AreEqual(uri.Path, rebuilt.Path);
            Assert.AreEqual(uri.Query, rebuilt.Query);
            Assert.AreEqual(uri.Username, rebuilt.Username);
            Assert.AreEqual(uri.Password, rebuilt.Password);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestHostAndPath()
        {
            Assert.AreEqual("example.com/a/b", new Library.Utility.RelaxedUri("http://example.com/a/b").HostAndPath);
            Assert.AreEqual("example.com", new Library.Utility.RelaxedUri("http://example.com").HostAndPath);
            Assert.AreEqual("a/b", new Library.Utility.RelaxedUri("http", null, "a/b").HostAndPath);
            Assert.AreEqual("", new Library.Utility.RelaxedUri("http", null, null).HostAndPath);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestPathAndQuery()
        {
            Assert.AreEqual("a/b?x=1", new Library.Utility.RelaxedUri("http://example.com/a/b?x=1").PathAndQuery);
            Assert.AreEqual("a/b", new Library.Utility.RelaxedUri("http://example.com/a/b").PathAndQuery);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestQueryParameters()
        {
            var query = new Library.Utility.RelaxedUri("http://example.com/?a=b&c=d").QueryParameters;
            Assert.AreEqual("b", query["a"]);
            Assert.AreEqual("d", query["c"]);
            Assert.AreEqual("b", query["A"], "Keys are matched without regard to case");

            var decoded = new Library.Utility.RelaxedUri("http://example.com/?a=x%20y&b=p+q").QueryParameters;
            Assert.AreEqual("x y", decoded["a"], "%20 is decoded");
            Assert.AreEqual("p q", decoded["b"], "+ is decoded as a space");

            var repeated = new Library.Utility.RelaxedUri("http://example.com/?a=1&a=2").QueryParameters;
            Assert.AreEqual("1,2", repeated["a"], "A repeated key keeps both values");

            Assert.AreEqual(0, new Library.Utility.RelaxedUri("http://example.com/").QueryParameters.Count,
                "A url without a query has no parameters");
        }

        [Test]
        [Category("UriUtility")]
        public static void TestRequireHost()
        {
            Assert.DoesNotThrow(() => new Library.Utility.RelaxedUri("http://example.com/a").RequireHost());

            var withoutHost = new Library.Utility.RelaxedUri("http", null, "a", null, "user", "secret");
            var ex = Assert.Throws<System.ArgumentException>(() => withoutHost.RequireHost());
            Assert.IsFalse(ex!.Message.Contains("secret"), "The message must not carry the password");
        }

        [Test]
        [Category("UriUtility")]
        public static void TestHostAndPathKeepsTheFolderCase()
        {
            // Several backends take a case sensitive remote name out of the authority:
            // BoxBackend reads HostAndPath as the remote path, Dropbox reads
            // UrlDecode(HostAndPath), and OpenStackStorage reads Host as the container.
            // An RFC 3986 conformant parser lower-cases the authority, which is why #7097
            // failed the Box.com tests on all three platforms, and CloudStack, with "The
            // requested folder does not exist". That only showed up in the backend tests,
            // which need credentials, so it is pinned here as well.
            var box = new Library.Utility.RelaxedUri("box://MyBackups/Sub/");
            Assert.AreEqual("MyBackups", box.Host);
            Assert.AreEqual("MyBackups/Sub/", box.HostAndPath);

            var container = new Library.Utility.RelaxedUri("openstack://MyContainer");
            Assert.AreEqual("MyContainer", container.Host);
            Assert.AreEqual("MyContainer", container.HostAndPath);

            // The same has to survive being written back out and parsed again, because
            // that is how the url reaches the backend after being edited in the UI.
            var roundtrip = new Library.Utility.RelaxedUri(box.ToString());
            Assert.AreEqual(box.HostAndPath, roundtrip.HostAndPath);
        }
    }
}
