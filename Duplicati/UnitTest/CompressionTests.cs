//  Copyright (C) 2017, The Duplicati Team
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
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    [Category("Compression")]
    public class CompressionTests
    {
        [Test]
        public void TestZipCompressionHints()
        {
            TestCompressionHints("zip");
        }

        [Test]
        public void Test7zCompressionHints()
        {
            TestCompressionHints("7z");
        }

        public void TestCompressionHints(string module)
        {
            const int TESTSIZE = 1024 * 1024;

            using (var tf0 = new Library.Utility.TempFile())
            using (var tf1 = new Library.Utility.TempFile())
            {
                var opts = new Dictionary<string, string>();
                opts["zip-compression-level"] = "9";

                using (var z0 = Library.DynamicLoader.CompressionLoader.GetModule(module, tf0, opts))
                using (var fs0 = z0.CreateFile("sample", Library.Interface.CompressionHint.Noncompressible, DateTime.Now))
                    fs0.Write(new byte[TESTSIZE], 0, TESTSIZE);

                using (var z1 = Library.DynamicLoader.CompressionLoader.GetModule(module, tf1, opts))
                using (var fs1 = z1.CreateFile("sample", Library.Interface.CompressionHint.Compressible, DateTime.Now))
                    fs1.Write(new byte[TESTSIZE], 0, TESTSIZE);


                if (new FileInfo(tf0).Length < TESTSIZE)
                    throw new Exception("Compression hint non-compressible is not honored");

                if (new FileInfo(tf1).Length > new FileInfo(tf0).Length * 0.25)
                    throw new Exception("Compression is not applied");
            }
        }
        
    }
}
