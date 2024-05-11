// Copyright (C) 2024, The Duplicati Team
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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [Category("Compression")]
    public class CompressionTests : BasicSetupHelper
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

                using (var z0 = Library.DynamicLoader.CompressionLoader.GetModule(module, tf0, Library.Interface.ArchiveMode.Write, opts))
                using (var fs0 = z0.CreateFile("sample", Library.Interface.CompressionHint.Noncompressible, DateTime.Now))
                    fs0.Write(new byte[TESTSIZE], 0, TESTSIZE);

                using (var z1 = Library.DynamicLoader.CompressionLoader.GetModule(module, tf1, Library.Interface.ArchiveMode.Write, opts))
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
                using (var z0 = Library.DynamicLoader.CompressionLoader.GetModule(module, stream, Library.Interface.ArchiveMode.Write, opts))
                {
                    // Add two files to the archive
                    using (var fs1 = z0.CreateFile("sample1", Library.Interface.CompressionHint.Compressible, DateTime.Now))
                        fs1.Write(testset1.ToArray(), 0, TESTSIZE);

                    using (var fs1 = z0.CreateFile("sample2", Library.Interface.CompressionHint.Compressible, DateTime.Now))
                        fs1.Write(testset2.ToArray(), 0, TESTSIZE);
                }

                Console.WriteLine("Compression rate for module {0}: {1:0.00}%", module, 100.0 * stream.Length / (TESTSIZE * 2));
                // Decompress
                using (var z0 = Library.DynamicLoader.CompressionLoader.GetModule(module, stream, Library.Interface.ArchiveMode.Read, opts))
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
