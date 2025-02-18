// Copyright (C) 2025, The Duplicati Team
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

namespace Duplicati.UnitTest
{
    public class UriUtilityTests : BasicSetupHelper
    {
        [Test]
        [Category("UriUtility")]
        public static void TestBuildUriQuery()
        {
            var query = new NameValueCollection { { "a", "b" } };
            var queryUrl = Library.Utility.Uri.BuildUriQuery(query);
            Assert.AreEqual("a=b", queryUrl);
            query.Add(new NameValueCollection { { "c", "d" } });
            queryUrl = Library.Utility.Uri.BuildUriQuery(query);
            Assert.AreEqual("a=b&c=d", queryUrl);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestUrlBuilder()
        {
            var baseUrl = "http://localhost";
            var path = "files";
            var query = new NameValueCollection { { "a", "b" }, { "c", "d" } };
            var url = Library.Utility.Uri.UriBuilder(baseUrl, path, query);
            Assert.AreEqual(baseUrl + "/" + path + "?a=b&c=d", url);
        }

        [Test]
        [Category("UriUtility")]
        public static void TestExtractPath()
        {
            var url = "http://localhost/a/b";
            var path = Library.Utility.Uri.ExtractPath(url);
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

            var uri = new Library.Utility.Uri(uriStr);
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
    }
}
