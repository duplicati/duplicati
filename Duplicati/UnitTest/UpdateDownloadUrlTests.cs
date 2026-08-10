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

using Duplicati.Library.AutoUpdater;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The urls an update is downloaded from are built by pointing every configured
    /// alternate url at the package name from the update package. The building was
    /// inside DownloadUpdate and could not be checked without downloading, so these
    /// tests pin what it produces.
    /// </summary>
    [TestFixture]
    [Category("UpdateDownloadUrl")]
    public class UpdateDownloadUrlTests
    {
        private const string PACKAGE = "https://updates.duplicati.com/beta/duplicati-2.1.0.5.zip";

        [Test]
        public void WithoutAlternatesThePackageUrlsAreUsedAsTheyAre()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE, "https://mirror.example.com/duplicati-2.1.0.5.zip"], []);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(PACKAGE, result[0]);
            Assert.AreEqual("https://mirror.example.com/duplicati-2.1.0.5.zip", result[1]);
        }

        [Test]
        public void AnAlternateIsPointedAtThePackageNameAndTriedFirst()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE], ["https://mirror.example.com/duplicati/latest.manifest"]);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("https://mirror.example.com/duplicati/duplicati-2.1.0.5.zip", result[0]);
            Assert.AreEqual(PACKAGE, result[1], "The original package url stays as the last resort");
        }

        [Test]
        public void AlternatesKeepTheirConfiguredOrder()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE],
            [
                "https://first.example.com/a/latest.manifest",
                "https://second.example.com/b/latest.manifest"
            ]);

            Assert.AreEqual(3, result.Count);
            Assert.AreEqual("https://first.example.com/a/duplicati-2.1.0.5.zip", result[0]);
            Assert.AreEqual("https://second.example.com/b/duplicati-2.1.0.5.zip", result[1]);
            Assert.AreEqual(PACKAGE, result[2]);
        }

        [Test]
        public void AQueryOnAnAlternateIsKept()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE], ["https://mirror.example.com/d/latest.manifest?token=abc"]);

            Assert.AreEqual("https://mirror.example.com/d/duplicati-2.1.0.5.zip?token=abc", result[0]);
        }

        [Test]
        public void APortOnAnAlternateIsKept()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE], ["https://mirror.example.com:8443/d/latest.manifest"]);

            Assert.AreEqual("https://mirror.example.com:8443/d/duplicati-2.1.0.5.zip", result[0]);
        }

        [Test]
        public void AnExplicitDefaultPortOnAnAlternateIsNormalizedAway()
        {
            // A port that is the default for the scheme is dropped, because System.Uri
            // normalizes it at parse time. The relaxed parser used to keep it, so this is
            // the one place where the string differs. The two urls are the same url, and
            // the result is only used to issue the download request.
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE], ["https://mirror.example.com:443/d/latest.manifest"]);

            Assert.AreEqual("https://mirror.example.com/d/duplicati-2.1.0.5.zip", result[0]);
        }

        [Test]
        public void AnAlternateWithASinglePathSegmentIsReplacedEntirely()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE], ["https://mirror.example.com/latest.manifest"]);

            Assert.AreEqual("https://mirror.example.com/duplicati-2.1.0.5.zip", result[0]);
        }

        [Test]
        public void AnAlternateWithoutAPathGetsThePackageName()
        {
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE], ["https://mirror.example.com"]);

            Assert.AreEqual("https://mirror.example.com/duplicati-2.1.0.5.zip", result[0]);
        }

        [Test]
        public void APathSegmentEqualToThePackageNameSwallowsTheFilename()
        {
            // The path components are combined with Union, not Concat, so a component that
            // already equals the package name is de-duplicated and the filename is lost.
            // That looks unintended, but it is what happens today, so it is pinned rather
            // than changed here.
            var result = UpdaterManager.BuildDownloadUrls([PACKAGE],
                ["https://mirror.example.com/duplicati-2.1.0.5.zip/latest.manifest"]);

            Assert.AreEqual("https://mirror.example.com/duplicati-2.1.0.5.zip", result[0]);
        }
    }
}
