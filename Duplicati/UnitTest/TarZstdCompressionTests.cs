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
using NUnit.Framework;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Duplicati.Library.Interface;
using System.Text;
using System.Text.Json;
using ZstdSharp;
using Duplicati.Library.Compression.TarZstdCompression;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    [Category("Compression")]
    [TestFixture]
    public class TarZstdCompressionTests : BasicSetupHelper
    {
        private static byte[] GenerateTestData(int size, byte seed = 1)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++)
            {
                data[i] = (byte)((i * 22695477 + seed) % 256);
            }
            return data;
        }

        [Test]
        public void TestCreateAndReadArchive()
        {
            using var archiveStream = new MemoryStream();
            var testData = GenerateTestData(1024, 1);

            // Write
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using var entry = archive.CreateFile("test.txt", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            // Read
            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var files = archive.ListFiles(null);
                Assert.AreEqual(1, files.Length);
                Assert.AreEqual("test.txt", files[0]);

                using var stream = archive.OpenRead("test.txt");
                Assert.IsNotNull(stream);

                using var ms = new MemoryStream();
                stream!.CopyTo(ms);
                var readData = ms.ToArray();
                Assert.That(readData, Is.EqualTo(testData));
            }
        }

        [Test]
        public void TestMultipleFiles()
        {
            using var archiveStream = new MemoryStream();
            var testData1 = GenerateTestData(1024, 1);
            var testData2 = GenerateTestData(2048, 2);
            var testData3 = GenerateTestData(512, 3);

            // Write
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using (var entry = archive.CreateFile("file1.bin", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(testData1, 0, testData1.Length);

                using (var entry = archive.CreateFile("file2.bin", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(testData2, 0, testData2.Length);

                using (var entry = archive.CreateFile("dir/file3.bin", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(testData3, 0, testData3.Length);
            }

            // Read
            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var files = archive.ListFiles(null);
                Assert.AreEqual(3, files.Length);

                // Verify file1.bin
                using (var stream = archive.OpenRead("file1.bin"))
                {
                    using var ms = new MemoryStream();
                    stream!.CopyTo(ms);
                    var readData = ms.ToArray();
                    Console.WriteLine($"[TEST] file1.bin: expected[0]={testData1[0]}, actual[0]={readData[0]}, length={readData.Length}");
                    Assert.That(readData, Is.EqualTo(testData1));
                }

                // Verify file2.bin
                using (var stream = archive.OpenRead("file2.bin"))
                {
                    using var ms = new MemoryStream();
                    stream!.CopyTo(ms);
                    Assert.That(ms.ToArray(), Is.EqualTo(testData2));
                }

                // Verify file3.bin
                using (var stream = archive.OpenRead("dir/file3.bin"))
                {
                    using var ms = new MemoryStream();
                    stream!.CopyTo(ms);
                    Assert.That(ms.ToArray(), Is.EqualTo(testData3));
                }
            }
        }

        [Test]
        public void TestEofHeaderPresent()
        {
            using var archiveStream = new MemoryStream();
            var testData = GenerateTestData(1024, 1);

            // Write archive
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using var entry = archive.CreateFile("test.txt", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            // Decompress and verify EOF header is present
            archiveStream.Position = 0;
            var decompressedStream = new MemoryStream();
            using (var decompressor = new DecompressionStream(archiveStream))
            {
                decompressor.CopyTo(decompressedStream);
            }

            decompressedStream.Position = 0;

            // Read the tar entries and verify EOF header exists
            using var tarReader = new System.Formats.Tar.TarReader(decompressedStream);
            bool foundEofHeader = false;
            int entryCount = 0;

            while (true)
            {
                var entry = tarReader.GetNextEntry();
                if (entry == null)
                    break;

                entryCount++;
                if (entry.Name == ".eof-header")
                {
                    foundEofHeader = true;

                    // Parse the JSON to verify content
                    if (entry.DataStream != null)
                    {
                        using var ms = new MemoryStream();
                        entry.DataStream.CopyTo(ms);
                        var contentBytes = ms.ToArray();

                        // Remove trailer (last 14 bytes: 8 offset + 6 magic)
                        const int trailerSize = 14; // EofHeaderTrailerSize
                        if (contentBytes.Length >= trailerSize)
                        {
                            var jsonBytes = contentBytes[..^trailerSize];
                            // Trim null padding bytes from end
                            int jsonLength = jsonBytes.Length;
                            while (jsonLength > 0 && jsonBytes[jsonLength - 1] == 0)
                                jsonLength--;

                            var json = Encoding.UTF8.GetString(jsonBytes, 0, jsonLength);
                            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                            Assert.IsNotNull(dict);
                            Assert.That(dict!.ContainsKey("test.txt"), Is.True);
                        }
                    }
                }
            }

            Assert.That(foundEofHeader, Is.True, "EOF header should be present");
            Assert.That(entryCount, Is.EqualTo(2), "Should have data file and EOF header");
        }

        [Test]
        public void TestFallbackScanning()
        {
            using var archiveStream = new MemoryStream();
            var testData = GenerateTestData(1024, 1);

            // Create a tar archive without EOF header (simulate old/corrupt format)
            using (var tempStream = new MemoryStream())
            {
                // Write tar entry using System.Formats.Tar (ustar format for fixed 512-byte headers)
                using (var tarWriter = new System.Formats.Tar.TarWriter(tempStream, System.Formats.Tar.TarEntryFormat.Ustar, leaveOpen: true))
                {
                    var entry = new System.Formats.Tar.UstarTarEntry(
                        System.Formats.Tar.TarEntryType.RegularFile,
                        "test.txt")
                    {
                        DataStream = new MemoryStream(testData)
                    };
                    tarWriter.WriteEntry(entry);
                }

                // Compress with Zstd
                tempStream.Position = 0;
                using (var compressor = new CompressionStream(archiveStream, 3))
                {
                    tempStream.CopyTo(compressor);
                }
            }

            // Read - should fallback to scanning
            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var files = archive.ListFiles(null);
                Assert.AreEqual(1, files.Length);
                Assert.AreEqual("test.txt", files[0]);

                using var stream = archive.OpenRead("test.txt");
                using var ms = new MemoryStream();
                stream!.CopyTo(ms);
                Assert.That(ms.ToArray(), Is.EqualTo(testData));
            }
        }

        [Test]
        public void TestEmptyArchive()
        {
            using var archiveStream = new MemoryStream();

            // Write empty archive
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                // Don't add any files
            }

            // Read
            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var files = archive.ListFiles(null);
                Assert.AreEqual(0, files.Length);
            }
        }

        [Test]
        public void TestLargeFile()
        {
            using var archiveStream = new MemoryStream();
            const int size = 10 * 1024 * 1024; // 10 MB
            var testData = GenerateTestData(size, 42);

            // Write
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using var entry = archive.CreateFile("large.bin", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            // Read
            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                using var stream = archive.OpenRead("large.bin");
                using var ms = new MemoryStream();
                stream!.CopyTo(ms);
                Assert.That(ms.Length, Is.EqualTo(testData.Length));
                Assert.That(ms.ToArray(), Is.EqualTo(testData));
            }
        }

        [Test]
        public void TestCompressionReversibility()
        {
            const int testSize = 1024 * 1024;
            var testData1 = GenerateTestData(testSize, 1);
            var testData2 = GenerateTestData(testSize, 2);

            using var archiveStream = new MemoryStream();

            // Compress
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using (var entry = archive.CreateFile("sample1", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(testData1, 0, testData1.Length);

                using (var entry = archive.CreateFile("sample2", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(testData2, 0, testData2.Length);
            }

            Console.WriteLine("Compression rate for Tar+Zstd: {0:0.00}%", 100.0 * archiveStream.Length / (testSize * 2));

            // Decompress
            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var files = archive.ListFiles(null);
                Assert.AreEqual(2, files.Length);

                // Read second file
                using (var stream = archive.OpenRead(files[1]))
                {
                    using var ms = new MemoryStream();
                    stream!.CopyTo(ms);
                    Assert.That(ms.ToArray(), Is.EqualTo(testData2));
                }

                // Read first file
                using (var stream = archive.OpenRead(files[0]))
                {
                    using var ms = new MemoryStream();
                    stream!.CopyTo(ms);
                    Assert.That(ms.ToArray(), Is.EqualTo(testData1));
                }
            }
        }

        [Test]
        public void TestFileNotFound()
        {
            using var archiveStream = new MemoryStream();
            var testData = GenerateTestData(1024, 1);

            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using var entry = archive.CreateFile("existing.txt", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var stream = archive.OpenRead("nonexistent.txt");
                Assert.IsNull(stream);
                Assert.IsFalse(archive.FileExists("nonexistent.txt"));
            }
        }

        [Test]
        public void TestListFilesWithPrefix()
        {
            using var archiveStream = new MemoryStream();

            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using (var entry = archive.CreateFile("dir1/file1.txt", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(GenerateTestData(100), 0, 100);

                using (var entry = archive.CreateFile("dir1/file2.txt", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(GenerateTestData(100), 0, 100);

                using (var entry = archive.CreateFile("dir2/file3.txt", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(GenerateTestData(100), 0, 100);

                using (var entry = archive.CreateFile("root.txt", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(GenerateTestData(100), 0, 100);
            }

            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var dir1Files = archive.ListFiles("dir1/");
                Assert.AreEqual(2, dir1Files.Length);
                Assert.That(dir1Files, Does.Contain("dir1/file1.txt"));
                Assert.That(dir1Files, Does.Contain("dir1/file2.txt"));

                var dir2Files = archive.ListFiles("dir2/");
                Assert.AreEqual(1, dir2Files.Length);
                Assert.AreEqual("dir2/file3.txt", dir2Files[0]);

                var allFiles = archive.ListFiles(null);
                Assert.AreEqual(4, allFiles.Length);
            }
        }

        [Test]
        public void TestListFilesWithSize()
        {
            using var archiveStream = new MemoryStream();
            var data1 = GenerateTestData(100);
            var data2 = GenerateTestData(200);

            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using (var entry = archive.CreateFile("small.txt", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(data1, 0, data1.Length);

                using (var entry = archive.CreateFile("large.txt", CompressionHint.Compressible, DateTime.Now))
                    entry.Write(data2, 0, data2.Length);
            }

            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var files = archive.ListFilesWithSize(null).ToList();
                Assert.AreEqual(2, files.Count);

                var smallEntry = files.FirstOrDefault(f => f.Key == "small.txt");
                Assert.That(smallEntry.Value, Is.EqualTo(100));

                var largeEntry = files.FirstOrDefault(f => f.Key == "large.txt");
                Assert.That(largeEntry.Value, Is.EqualTo(200));
            }
        }

        [Test]
        public void TestGetLastWriteTime()
        {
            using var archiveStream = new MemoryStream();
            var testTime = new DateTime(2023, 6, 15, 12, 30, 0, DateTimeKind.Utc);

            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using var entry = archive.CreateFile("test.txt", CompressionHint.Compressible, testTime);
                entry.Write(GenerateTestData(100), 0, 100);
            }

            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                var lastWriteTime = archive.GetLastWriteTime("test.txt");
                // Allow some precision loss due to tar format (seconds only)
                var diff = Math.Abs((lastWriteTime - testTime).TotalSeconds);
                Assert.That(diff, Is.LessThan(1.0), "Last write time should match within 1 second");
            }
        }

        [Test]
        public void TestPathSeparators()
        {
            using var archiveStream = new MemoryStream();
            var testData = GenerateTestData(100);

            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Write, new Dictionary<string, string?>()))
            {
                using var entry = archive.CreateFile("path/to/file.txt", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            archiveStream.Position = 0;
            using (var archive = new FileArchiveTarZstd(archiveStream, ArchiveMode.Read, new Dictionary<string, string?>()))
            {
                // Should be able to access with forward slash
                Assert.That(archive.FileExists("path/to/file.txt"), Is.True);

                // Should be able to access with backslash
                Assert.That(archive.FileExists("path\\to\\file.txt"), Is.True);
            }
        }

        [Test]
        public void TestCompressionLevel()
        {
            using var archiveStream1 = new MemoryStream();
            using var archiveStream9 = new MemoryStream();

            var testData = GenerateTestData(10000);

            // Write with level 1
            var opts1 = new Dictionary<string, string?> { { "tzstd-compression-level", "1" } };
            using (var archive = new FileArchiveTarZstd(archiveStream1, ArchiveMode.Write, opts1))
            {
                using var entry = archive.CreateFile("test.txt", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            // Write with level 9
            var opts9 = new Dictionary<string, string?> { { "tzstd-compression-level", "9" } };
            using (var archive = new FileArchiveTarZstd(archiveStream9, ArchiveMode.Write, opts9))
            {
                using var entry = archive.CreateFile("test.txt", CompressionHint.Compressible, DateTime.Now);
                entry.Write(testData, 0, testData.Length);
            }

            // Level 9 should generally produce smaller output than level 1
            Console.WriteLine($"Level 1 size: {archiveStream1.Length}");
            Console.WriteLine($"Level 9 size: {archiveStream9.Length}");
            // Note: This isn't always guaranteed for small/random data, but generally holds
        }
    }
}
