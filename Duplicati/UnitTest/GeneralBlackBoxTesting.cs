//  Copyright (C) 2015, The Duplicati Team
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
using System.Linq;
using System.IO;
using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
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

        public override void OneTimeSetUp()
        {
            base.OneTimeSetUp();
            
            using (WebClient client = new WebClient())
            {
                client.DownloadFile($"https://testfiles.duplicati.com/{this.zipFilename}", this.zipFilepath);
            }
            
            System.IO.Compression.ZipFile.ExtractToDirectory(this.zipFilepath, BASEFOLDER);
        }

        public override void OneTimeTearDown()
        {
            if (Directory.Exists(SOURCE_FOLDERS))
            {
                Directory.Delete(SOURCE_FOLDERS, true);
            }
            if (File.Exists(this.zipFilepath))
            {
                File.Delete(this.zipFilepath);
            }
        }

        [Test]
        [Category("SVNData")]
        public void TestWithSVNShort()
        {
            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), TestOptions, TestTarget);
        }
        
        [Test]
        [Category("SVNDataLong")]
        public void TestWithSVNLong()
        {
            SVNCheckoutTest.RunTest(TestFolders.ToArray(), TestOptions, TestTarget);
        }

        [Test]
        [Category("SVNData")]
        public void TestWithErrors()
        {
            var u = new Library.Utility.Uri(TestUtils.GetDefaultTarget());
            RandomErrorBackend.WrappedBackend = u.Scheme;
            var target = u.SetScheme(new RandomErrorBackend().ProtocolKey).ToString();

            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), TestOptions, target);
        }

        [Test]
        [Category("SVNData")]
        public void TestWithoutSizeInfo()
        {
            var u = new Library.Utility.Uri(TestUtils.GetDefaultTarget());
            SizeOmittingBackend.WrappedBackend = u.Scheme;
            var target = u.SetScheme(new SizeOmittingBackend().ProtocolKey).ToString();

            SVNCheckoutTest.RunTest(TestFolders.Take(5).ToArray(), TestOptions, target);
        }

    }
}

