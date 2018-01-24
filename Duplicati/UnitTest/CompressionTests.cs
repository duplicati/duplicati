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
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    [Category("Compression")]
    public class CompressionTests
    {
        [TestCase("zip")]
        [TestCase("7z")]
        public void TestCompressionHints(string module)
        {
            const int TESTSIZE = 1024 * 1024;

            using (var tf0 = new MemoryStream())
            using (var tf1 = new MemoryStream())
            {
                var opts = new Dictionary<string, string>();
                opts["zip-compression-level"] = "9";

                using (var z0 = Library.DynamicLoader.CompressionLoader.GetArchiveWriter(module, tf0, opts))
                using (var fs0 = z0.CreateFile("sample", Library.Interface.CompressionHint.Noncompressible, DateTime.Now))
                    fs0.Write(new byte[TESTSIZE], 0, TESTSIZE);

                using (var z1 = Library.DynamicLoader.CompressionLoader.GetArchiveWriter(module, tf1, opts))
                using (var fs1 = z1.CreateFile("sample", Library.Interface.CompressionHint.Compressible, DateTime.Now))
                    fs1.Write(new byte[TESTSIZE], 0, TESTSIZE);


                if (tf0.Length < TESTSIZE)
                    throw new Exception("Compression hint non-compressible is not honored");

                if (tf1.Length > tf0.Length * 0.25)
                    throw new Exception("Compression is not applied");
            }
        }

        /// <summary>
        /// Test compression reversibility
        /// </summary>
        /// <param name="module"></param>
        [TestCase("zip")]
        [TestCase("7z")]
        public void TestCompressionReversibility(string module)
        {
            const int TESTSIZE = 1024 * 1024;
            var testset1 = Enumerable.Range(0, TESTSIZE).Select(i => (byte)((i * 22695477) % 257));
            var testset2 = Enumerable.Range(0, TESTSIZE).Select(i => (byte)((i * 48271) % 257));

            using (var stream = new MemoryStream())
            {
                var opts = new Dictionary<string, string>();
                opts["zip-compression-level"] = "9";

                // Compress test streams
                using (var z0 = Library.DynamicLoader.CompressionLoader.GetArchiveWriter(module, stream, opts))
                {
                    // Add two files to the archive
                    using (var fs1 = z0.CreateFile("sample1", Library.Interface.CompressionHint.Compressible, DateTime.Now))
                        fs1.Write(testset1.ToArray(), 0, TESTSIZE);

                    using (var fs1 = z0.CreateFile("sample2", Library.Interface.CompressionHint.Compressible, DateTime.Now))
                        fs1.Write(testset2.ToArray(), 0, TESTSIZE);
                }

                Console.WriteLine("Compression rate for module {0}: {1:0.00}%", module, 100.0 * stream.Length / (TESTSIZE * 2));
                // Decompress
                using (var z0 = Library.DynamicLoader.CompressionLoader.GetArchiveReader(module, stream, opts))
                {
                    // Get files list
                    var files = z0.ListFiles(null);

                    // Read second file
                    using (var fd = z0.OpenRead(files[1]))
                    {
                        bool match = testset2.All(b => b == fd.ReadByte()) && fd.ReadByte() == -1;
                        if (!match)
                            throw new Exception("Decompressed file sample2 contents do not match the source file.");
                    }

                    // Read first file
                    using (var fd = z0.OpenRead(files[0]))
                    {
                        bool match = testset1.All(b => b == fd.ReadByte()) && fd.ReadByte() == -1;
                        if (!match)
                            throw new Exception("Decompressed file sample1 contents do not match the source file.");
                    }
                }
            }
        }
    }
}
