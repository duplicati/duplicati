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
using System;
using System.Linq;
using System.Text;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class Crc32CTests : BasicSetupHelper
    {
        /// <summary>
        /// Test vectors from RFC 3720 appendix B.4 and the standard "123456789" check value.
        /// </summary>
        private static readonly (byte[] Data, uint Expected)[] VECTORS =
        [
            (Encoding.ASCII.GetBytes(""), 0x00000000u),
            (Encoding.ASCII.GetBytes("123456789"), 0xE3069283u),
            (new byte[32], 0x8A9136AAu),
            (Enumerable.Repeat((byte)0xFF, 32).ToArray(), 0x62A8AB43u),
            (Enumerable.Range(0, 32).Select(i => (byte)i).ToArray(), 0x46DD794Eu),
            (Enumerable.Range(0, 32).Select(i => (byte)(31 - i)).ToArray(), 0x113FDB5Cu),
        ];

        [Test]
        [Category("Crc32C")]
        public static void KnownVectors()
        {
            foreach (var (data, expected) in VECTORS)
            {
                Assert.AreEqual(expected, Crc32C.Compute(data), "Compute mismatch for {0} bytes", data.Length);

                using var hasher = new Crc32C();
                var hash = hasher.ComputeHash(data);
                Assert.AreEqual(4, hash.Length);
                Assert.AreEqual(expected, (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]), "Big-endian output mismatch for {0} bytes", data.Length);
            }
        }

        [Test]
        [Category("Crc32C")]
        public static void TableFallbackMatchesHardware()
        {
            foreach (var (data, expected) in VECTORS)
                Assert.AreEqual(expected, Crc32C.ComputeWithTable(data), "Table mismatch for {0} bytes", data.Length);

            var rnd = new Random(7);
            foreach (var length in new[] { 1, 5, 17, 1000, 65537 })
            {
                var data = new byte[length];
                rnd.NextBytes(data);
                Assert.AreEqual(Crc32C.ComputeWithTable(data), Crc32C.Compute(data), "Hardware and table paths differ for {0} bytes", length);
            }
        }

        [Test]
        [Category("Crc32C")]
        public static void IncrementalMatchesOneShot()
        {
            var rnd = new Random(42);
            var data = new byte[64 * 1024 + 13];
            rnd.NextBytes(data);

            var expected = Crc32C.Compute(data);

            // Feed in odd chunk sizes to exercise the 8, 4 and 1 byte paths across boundaries
            foreach (var chunkSize in new[] { 1, 3, 7, 8, 13, 4096, data.Length })
            {
                using var hasher = new Crc32C();
                var offset = 0;
                while (offset < data.Length)
                {
                    var n = Math.Min(chunkSize, data.Length - offset);
                    hasher.TransformBlock(data, offset, n, null, 0);
                    offset += n;
                }
                hasher.TransformFinalBlock([], 0, 0);

                var hash = hasher.Hash!;
                Assert.AreEqual(expected, (uint)((hash[0] << 24) | (hash[1] << 16) | (hash[2] << 8) | hash[3]), "Mismatch for chunk size {0}", chunkSize);
            }
        }

        [Test]
        [Category("Crc32C")]
        public static void GoogleCloudStorageEncoding()
        {
            // The GCS JSON API returns the crc32c as base64 of the big-endian value
            using var hasher = HashFactory.CreateHasher(HashFactory.CRC32C);
            var hash = hasher.ComputeHash(Encoding.ASCII.GetBytes("123456789"));
            Assert.AreEqual("4waSgw==", Convert.ToBase64String(hash));
        }
    }
}
