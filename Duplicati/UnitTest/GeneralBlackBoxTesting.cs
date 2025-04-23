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
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest
{
    public class GeneralBlackBoxTesting : BasicSetupHelper
    {
        private static readonly string SOURCE_FOLDERS = Path.Combine(BASEFOLDER, "DSMCBE");
        private readonly string zipFilename = "DSMCBE.zip";
        private string zipFilepath => Path.Combine(BASEFOLDER, this.zipFilename);

        protected IEnumerable<string> TestFolders
        {
            get
            {
                var rx = new System.Text.RegularExpressions.Regex("r(?<number>\\d+)");
                return
                    from n in Directory.EnumerateDirectories(SOURCE_FOLDERS)
                    let m = rx.Match(n)
                    where m.Success
                    orderby int.Parse(m.Groups["number"].Value)
                    select n;
            }
        }

        protected override Dictionary<string, string> TestOptions
        {
            get
            {
                return TestUtils.DefaultOptions;
            }
        }

        protected string TestTarget
        {
            get
            {
                var x = TestUtils.GetDefaultTarget("x");
                return x == "x" ? null : x;
            }
        }


        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            this.OneTimeTearDown();
            CommandLineOperationsTests.DownloadS3FileIfNewerAsync(this.zipFilepath, $"{CommandLineOperationsTests.S3_URL}{this.zipFilename}").Await();
            System.IO.Compression.ZipFile.ExtractToDirectory(this.zipFilepath, BASEFOLDER);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(SOURCE_FOLDERS))
            {
                Directory.Delete(SOURCE_FOLDERS, true);
            }
        }

        [Test]
        [Category("SVNData")]
        [TestCase("zip")]
        public void TestWithSVNShort(string compression)
        {
            var opts = TestOptions;
            opts["compression-module"] = compression;
            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), opts, TestTarget);
        }

        [Test]
        [Category("SVNDataLong")]
        [TestCase("zip")]
        public void TestWithSVNLong(string compression)
        {
            var opts = TestOptions;
            opts["compression-module"] = compression;
            SVNCheckoutTest.RunTest(TestFolders.ToArray(), opts, TestTarget);
        }

        [Test]
        [Category("SVNData")]
        public void TestWithErrors()
        {
            var u = new Library.Utility.Uri(TestUtils.GetDefaultTarget());
            RandomErrorBackend.WrappedBackend = u.Scheme;
            var target = u.SetScheme(new RandomErrorBackend().ProtocolKey).ToString();
            Library.DynamicLoader.BackendLoader.AddBackend(new RandomErrorBackend());

            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), TestOptions, target);
        }

        [Test]
        [Category("SVNData")]
        public void TestWithoutSizeInfo()
        {
            var u = new Library.Utility.Uri(TestUtils.GetDefaultTarget());
            SizeOmittingBackend.WrappedBackend = u.Scheme;
            var target = u.SetScheme(new SizeOmittingBackend().ProtocolKey).ToString();
            Library.DynamicLoader.BackendLoader.AddBackend(new SizeOmittingBackend());

            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), TestOptions, target);
        }

    }
}

