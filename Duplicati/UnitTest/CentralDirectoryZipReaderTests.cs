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
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime;
using System.Security.Cryptography;
using Duplicati.Library.Compression.ZipCompression;
using Duplicati.Library.Interface;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests for the compact ZIP reader that a restore uses for block volumes (#7538).
    /// </summary>
    [TestFixture]
    public class CentralDirectoryZipReaderTests
    {
        private string m_folder = null!;

        [SetUp]
        public void SetUp()
        {
            m_folder = Path.Combine(Path.GetTempPath(), "cdzip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(m_folder);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(m_folder, true); } catch { }
        }

        /// <summary>
        /// Writes a block volume the way a backup does, with entries named by the base64url hash of random data.
        /// </summary>
        private string WriteVolume(int count, Dictionary<string, string?> options, out Dictionary<string, byte[]> content)
        {
            var path = Path.Combine(m_folder, Guid.NewGuid().ToString("N") + ".zip");
            content = [];
            var rnd = new Random(7538);
            using (var fs = new FileStream(path, FileMode.Create))
            using (var zip = new FileArchiveZip(fs, ArchiveMode.Write, options))
            {
                using (var s = zip.CreateFile("manifest", CompressionHint.Compressible, DateTime.UtcNow))
                    s.Write("{\"Version\":2}"u8);
                content["manifest"] = "{\"Version\":2}"u8.ToArray();

                for (var i = 0; i < count; i++)
                {
                    var data = new byte[rnd.Next(1, 3000)];
                    // Half compressible, half random
                    if (i % 2 == 0)
                        rnd.NextBytes(data);
                    var name = Library.Utility.Utility.Base64PlainToBase64Url(Convert.ToBase64String(SHA256.HashData(BitConverter.GetBytes(i))));
                    using (var s = zip.CreateFile(name, i % 2 == 0 ? CompressionHint.Noncompressible : CompressionHint.Compressible, DateTime.UtcNow))
                        s.Write(data);
                    content[name] = data;
                }
            }
            return path;
        }

        private static byte[] ReadAll(Stream? s)
        {
            Assert.That(s, Is.Not.Null);
            using (s)
            using (var ms = new MemoryStream())
            {
                s!.CopyTo(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Asserts that the compact reader gives the same names, sizes and data as the regular reader.
        /// </summary>
        private static void AssertSameAsRegularReader(string path, IReadOnlyDictionary<string, byte[]> content)
        {
            using var compact = CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>());
            Assert.That(compact, Is.Not.Null, "The compact reader should read this archive");

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var regular = new FileArchiveZip(fs, ArchiveMode.Read, new Dictionary<string, string?>());

            Assert.That(compact!.ListFilesWithSize(null!).OrderBy(x => x.Key, StringComparer.Ordinal),
                Is.EqualTo(regular.ListFilesWithSize(null!).OrderBy(x => x.Key, StringComparer.Ordinal)));

            foreach (var (name, data) in content)
            {
                Assert.That(compact.FileExists(name), Is.True, name);
                Assert.That(ReadAll(compact.OpenRead(name)), Is.EqualTo(data), name);
                Assert.That(ReadAll(regular.OpenRead(name)), Is.EqualTo(data), name);
            }

            Assert.That(compact.OpenRead("not-there"), Is.Null);
            Assert.That(compact.FileExists("not-there"), Is.False);
        }

        [Test]
        [Category("Targeted")]
        public void ReadsBlockVolumeLikeRegularReader()
        {
            var path = WriteVolume(2000, new Dictionary<string, string?>(), out var content);
            AssertSameAsRegularReader(path, content);
        }

        [Test]
        [Category("Targeted")]
        public void ReadsVolumeWrittenWithSharpCompress()
        {
            var path = WriteVolume(500, new Dictionary<string, string?> { ["zip-compression-library"] = "SharpCompress" }, out var content);
            AssertSameAsRegularReader(path, content);
        }

        [Test]
        [Category("Targeted")]
        public void ReadsZip64ArchiveWithMoreThan65535Entries()
        {
            // More than 65535 entries need the Zip64 end record
            var path = WriteVolume(70000, new Dictionary<string, string?>(), out var content);
            AssertSameAsRegularReader(path, content);
        }

        [Test]
        [Category("Targeted")]
        public void ReadsStoredEntriesBackslashNamesAndDuplicates()
        {
            var path = Path.Combine(m_folder, "mixed.zip");
            using (var fs = new FileStream(path, FileMode.Create))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                void Add(string name, byte[] data, CompressionLevel level)
                {
                    using var s = zip.CreateEntry(name, level).Open();
                    s.Write(data);
                }

                Add("stored", [1, 2, 3, 4], CompressionLevel.NoCompression);
                Add("folder\\file", [5, 6], CompressionLevel.Optimal);
                Add("empty", [], CompressionLevel.Optimal);
                Add("dup", [7], CompressionLevel.Optimal);
                Add("dup", [8, 9], CompressionLevel.Optimal);
            }

            using var compact = CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>());
            Assert.That(compact, Is.Not.Null);
            Assert.That(ReadAll(compact!.OpenRead("stored")), Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
            // Either separator finds the entry, as with the regular reader
            Assert.That(ReadAll(compact.OpenRead("folder/file")), Is.EqualTo(new byte[] { 5, 6 }));
            Assert.That(ReadAll(compact.OpenRead("folder\\file")), Is.EqualTo(new byte[] { 5, 6 }));
            Assert.That(ReadAll(compact.OpenRead("empty")), Is.Empty);

            // The last entry with a name wins, as with the regular reader
            using var fs2 = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var regular = new FileArchiveZip(fs2, ArchiveMode.Read, new Dictionary<string, string?>());
            Assert.That(ReadAll(compact.OpenRead("dup")), Is.EqualTo(ReadAll(regular.OpenRead("dup"))));
            Assert.That(ReadAll(compact.OpenRead("dup")), Is.EqualTo(new byte[] { 8, 9 }));
        }

        [Test]
        [Category("Targeted")]
        public void LeavesOtherCompressionMethodsToRegularReader()
        {
            var options = new Dictionary<string, string?> { ["zip-compression-method"] = "lzma", ["zip-compression-library"] = "SharpCompress" };
            var path = WriteVolume(10, options, out _);
            Assert.That(CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>()), Is.Null);
        }

        [Test]
        [Category("Targeted")]
        public void LeavesNonZipFilesToRegularReader()
        {
            var path = Path.Combine(m_folder, "not.zip");
            File.WriteAllBytes(path, new byte[1000]);
            Assert.That(CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>()), Is.Null);

            // The file is not kept open when the reader is not used
            File.Delete(path);
            Assert.That(File.Exists(path), Is.False);
        }

        [Test]
        [Category("Targeted")]
        public void KeepsFarLessMemoryPerEntryThanRegularReader()
        {
            const int count = 50000;
            var path = WriteVolume(count, new Dictionary<string, string?>(), out _);

            // On macOS, GC.GetTotalMemory(true) still counted the large temporary buffers the readers had
            // dropped, about 80 bytes per entry, so the large object heap is compacted before each reading
            static long Settled()
            {
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                return GC.GetTotalMemory(false);
            }

            static long Retained(Func<IDisposable> open, out IDisposable reader)
            {
                var before = Settled();
                reader = open();
                return Settled() - before;
            }

            var compactBytes = Retained(() => CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>())!, out var compact);
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            var regularBytes = Retained(() => new FileArchiveZip(fs, ArchiveMode.Read, new Dictionary<string, string?>()), out var regular);

            TestContext.Progress.WriteLine($"Retained per entry: compact {compactBytes / count} bytes, regular {regularBytes / count} bytes");
            Assert.That(compactBytes / count, Is.LessThan(120), "The compact index should keep under 120 bytes per entry");
            Assert.That(compactBytes * 3, Is.LessThan(regularBytes), "The compact index should keep a third or less of the regular reader");

            compact.Dispose();
            regular.Dispose();
            fs.Dispose();
        }

        [Test]
        [Category("Targeted")]
        public void AllocatesLittleMoreThanTheIndexWhenOpening()
        {
            // A restore opens many volumes at once. Temporary buffers of this size go to the large
            // object heap, which is not compacted, so what each opening dropped stayed as free space
            // for the rest of the restore (#7538). The index itself is about 75 bytes per entry here.
            const int count = 20000;
            var path = WriteVolume(count, new Dictionary<string, string?>(), out _);

            // The first opening fills the buffer pool, as the first volume of a restore does
            CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>())!.Dispose();

            var before = GC.GetAllocatedBytesForCurrentThread();
            using var reader = CentralDirectoryZipReader.TryOpen(path, new Dictionary<string, string?>());
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Assert.That(reader, Is.Not.Null);
            TestContext.Progress.WriteLine($"Allocated per entry when opening: {allocated / count} bytes");
            Assert.That(allocated / count, Is.LessThan(100), "Opening a volume should allocate little more than its index");
        }
    }
}
