//  Copyright (C) 2018, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA

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
    }
}
