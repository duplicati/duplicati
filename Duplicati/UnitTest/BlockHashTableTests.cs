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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography;
using Duplicati.Library.Main.Operation.Restore;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the table of needed blocks that a restore without a local database keeps in memory (#7538).
    /// </summary>
    [TestFixture]
    public class BlockHashTableTests
    {
        private static string Sha256Hash(int i)
            => Convert.ToBase64String(SHA256.HashData(BitConverter.GetBytes(i)));

        [Test]
        [Category("Targeted")]
        public void KeepsSha256HashesAsBytesAndGivesThemBack()
        {
            var table = new BlockHashTable<long>();
            var hashes = Enumerable.Range(0, 1000).Select(Sha256Hash).ToList();
            for (var i = 0; i < hashes.Count; i++)
                table.Add(hashes[i], i);

            Assert.That(table.Count, Is.EqualTo(1000));
            Assert.That(table.StringCount, Is.Zero, "A SHA-256 hash should be kept as bytes");
            for (var i = 0; i < hashes.Count; i++)
            {
                Assert.That(table.TryGetValue(hashes[i], out var value), Is.True);
                Assert.That(value, Is.EqualTo(i));
            }

            table[hashes[5]] = -5;
            Assert.That(table[hashes[5]], Is.EqualTo(-5));
            Assert.That(table.TryGetValue(Sha256Hash(5000), out _), Is.False);
            Assert.Throws<KeyNotFoundException>(() => _ = table[Sha256Hash(5000)]);

            Assert.That(table.Entries().ToDictionary(x => x.Hash, x => x.Value),
                Is.EqualTo(hashes.Select((h, i) => (h, v: i == 5 ? -5L : i)).ToDictionary(x => x.h, x => x.v)));
        }

        [Test]
        [Category("Targeted")]
        public void KeepsOtherHashesAsTheStringsTheyWereAddedWith()
        {
            var canonical = Sha256Hash(1);
            // The last character before the padding holds two bits that are not part of the hash.
            // A string with them set reads as the same bytes, but is a different hash string
            var last = canonical[^2];
            var alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            var nonCanonical = canonical[..^2] + alphabet[alphabet.IndexOf(last) | 1] + "=";
            Assert.That(nonCanonical, Is.Not.EqualTo(canonical));
            Assert.That(Convert.FromBase64String(nonCanonical), Is.EqualTo(Convert.FromBase64String(canonical)));

            var others = new[]
            {
                nonCanonical,
                Convert.ToBase64String(SHA1.HashData([1])),
                Convert.ToBase64String(SHA512.HashData([1])),
                canonical.Replace('+', '-').Replace('/', '_').TrimEnd('=') + "_",
                "not base64 at all, but forty-four chars long",
            };

            var table = new BlockHashTable<int>();
            table.Add(canonical, 0);
            for (var i = 0; i < others.Length; i++)
                table.Add(others[i], i + 1);

            Assert.That(table.Count, Is.EqualTo(others.Length + 1));
            Assert.That(table.StringCount, Is.EqualTo(others.Length));
            Assert.That(table[canonical], Is.Zero, "The other spelling of the same bytes should not replace the hash");
            for (var i = 0; i < others.Length; i++)
                Assert.That(table[others[i]], Is.EqualTo(i + 1), others[i]);

            Assert.That(table.Entries().Select(x => x.Hash), Is.EquivalentTo(others.Append(canonical)));
        }

        [Test]
        [Category("Targeted")]
        public void KeepsFarLessMemoryPerBlockThanStringKeys()
        {
            const int count = 100000;

            static long Settled()
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                return GC.GetTotalMemory(false);
            }

            // The hashes are made while filling, as the database reader makes a new string for each
            var before = Settled();
            var table = new BlockHashTable<(long, long, long)>();
            for (var i = 0; i < count; i++)
                table.Add(Sha256Hash(i), (i, i, i));
            var tableBytes = Settled() - before;
            GC.KeepAlive(table);

            before = Settled();
            var strings = new Dictionary<string, (long, long, long)>(StringComparer.Ordinal);
            for (var i = 0; i < count; i++)
                strings.Add(Sha256Hash(i), (i, i, i));
            var stringBytes = Settled() - before;
            GC.KeepAlive(strings);

            TestContext.Progress.WriteLine($"Retained per block: table {tableBytes / count} bytes, string keys {stringBytes / count} bytes");
            Assert.That(tableBytes / count, Is.LessThan(120), "The table should keep under 120 bytes per block");
            Assert.That(tableBytes * 3, Is.LessThan(stringBytes * 2), "The table should keep a third less than string keys");
        }
    }
}
