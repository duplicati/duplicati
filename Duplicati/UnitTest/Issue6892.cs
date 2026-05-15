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
using System.IO;
using System.Linq;
using NUnit.Framework;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using Newtonsoft.Json;

namespace Duplicati.UnitTest;

public class Issue6892 : BasicSetupHelper
{
    [Test]
    [Category("Targeted")]
    public void TestReadEmptyBlocklistInIndexFile()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);

        using (var wr = new IndexVolumeWriter(opts))
        {
            // Write an empty blocklist (0 bytes) - should not be rejected
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var emptyHash = Convert.ToBase64String(hasher.ComputeHash(Array.Empty<byte>()));
                wr.WriteBlocklist(emptyHash, Array.Empty<byte>(), 0, 0);
            }

            // Write a valid blocklist
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var data = new byte[32];
                new Random(42).NextBytes(data);
                var hash = Convert.ToBase64String(hasher.ComputeHash(data));
                wr.WriteBlocklist(hash, data, 0, 32);
            }

            // Start a volume with no blocks
            var blockfilename = VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts, VolumeWriterBase.GenerateGuid(), DateTime.UtcNow);
            wr.StartVolume(blockfilename);
            wr.FinishVolume("hash", 123);

            wr.Close();

            using (var rd = new IndexVolumeReader(opts.CompressionModule, wr.LocalFilename, opts, opts.BlockhashSize))
            {
                var blocklists = rd.BlockLists.ToList();
                Assert.That(blocklists.Count, Is.EqualTo(2), "Expected two blocklists");
                var bl = blocklists[0];
                Assert.That(bl.Length, Is.EqualTo(0), "Expected blocklist with 32 bytes");
                var hashes = bl.Blocklist.ToList();
                Assert.That(hashes.Count, Is.EqualTo(0), "Expected one hash in blocklist");

                bl = blocklists[1];
                Assert.That(bl.Length, Is.EqualTo(32), "Expected blocklist with 32 bytes");
                hashes = bl.Blocklist.ToList();
                Assert.That(hashes.Count, Is.EqualTo(1), "Expected one hash in blocklist");

                var volumes = rd.Volumes.ToList();
                Assert.That(volumes.Count, Is.EqualTo(1), "Expected one volume");
                var vol = volumes[0];
                var blocks = vol.Blocks.ToList();
                Assert.That(blocks.Count, Is.EqualTo(0), "Expected no blocks in volume");
            }
        }
    }

    [Test]
    [Category("Targeted")]
    public void TestReadIndexFileWithOnlyBlocklists()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);

        using (var wr = new IndexVolumeWriter(opts))
        {
            // Write a blocklist without any volume entry
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var data = new byte[32];
                new Random(42).NextBytes(data);
                var hash = Convert.ToBase64String(hasher.ComputeHash(data));
                wr.WriteBlocklist(hash, data, 0, 32);
            }

            wr.Close();

            using (var rd = new IndexVolumeReader(opts.CompressionModule, wr.LocalFilename, opts, opts.BlockhashSize))
            {
                var blocklists = rd.BlockLists.ToList();
                Assert.That(blocklists.Count, Is.EqualTo(1), "Expected one blocklist");

                var volumes = rd.Volumes.ToList();
                Assert.That(volumes.Count, Is.EqualTo(0), "Expected no volumes");
            }
        }
    }

    [Test]
    [Category("Targeted")]
    public void TestReadIndexFileSkipsInvalidEntries()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);

        using (var wr = new IndexVolumeWriter(opts))
        {
            // Write a valid blocklist
            using (var hasher = System.Security.Cryptography.SHA256.Create())
            {
                var data = new byte[32];
                new Random(42).NextBytes(data);
                var hash = Convert.ToBase64String(hasher.ComputeHash(data));
                wr.WriteBlocklist(hash, data, 0, 32);
            }

            // Add a volume entry with a valid filename
            var blockfilename = VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts, VolumeWriterBase.GenerateGuid(), DateTime.UtcNow);
            wr.StartVolume(blockfilename);
            wr.AddBlock("abc123", 100);
            wr.FinishVolume("hash", 123);

            wr.Close();

            using (var rd = new IndexVolumeReader(opts.CompressionModule, wr.LocalFilename, opts, opts.BlockhashSize))
            {
                var blocklists = rd.BlockLists.ToList();
                Assert.That(blocklists.Count, Is.EqualTo(1), "Expected one blocklist");

                var volumes = rd.Volumes.ToList();
                Assert.That(volumes.Count, Is.EqualTo(1), "Expected one volume");
            }
        }
    }

    [Test]
    [Category("Targeted")]
    public void TestReadIndexFileWithEmptyBlocksArray()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);

        using (var wr = new IndexVolumeWriter(opts))
        {
            // Start a volume with no blocks
            var blockfilename = VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts, VolumeWriterBase.GenerateGuid(), DateTime.UtcNow);
            wr.StartVolume(blockfilename);
            wr.FinishVolume("hash", 123);

            wr.Close();

            using (var rd = new IndexVolumeReader(opts.CompressionModule, wr.LocalFilename, opts, opts.BlockhashSize))
            {
                var volumes = rd.Volumes.ToList();
                Assert.That(volumes.Count, Is.EqualTo(1), "Expected one volume");
                var vol = volumes[0];
                var blocks = vol.Blocks.ToList();
                Assert.That(blocks.Count, Is.EqualTo(0), "Expected no blocks in volume");
                Assert.That(vol.Hash, Is.EqualTo("hash"));
                Assert.That(vol.Length, Is.EqualTo(123));
            }
        }
    }

    [Test]
    [Category("Targeted")]
    public void TestFinishVolumeWithNullHashProducesValidJson()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);

        using (var wr = new IndexVolumeWriter(opts))
        {
            var blockfilename = VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts, VolumeWriterBase.GenerateGuid(), DateTime.UtcNow);
            wr.StartVolume(blockfilename);
            wr.FinishVolume(null, 0);

            wr.Close();

            using (var rd = new IndexVolumeReader(opts.CompressionModule, wr.LocalFilename, opts, opts.BlockhashSize))
            {
                var volumes = rd.Volumes.ToList();
                Assert.That(volumes.Count, Is.EqualTo(1), "Expected one volume");
                var vol = volumes[0];
                var blocks = vol.Blocks.ToList();
                Assert.That(blocks.Count, Is.EqualTo(0), "Expected no blocks in volume");
                Assert.That(vol.Hash, Is.Null);
                Assert.That(vol.Length, Is.EqualTo(0));
            }
        }
    }

    [Test]
    [Category("Targeted")]
    public void TestReadIndexFileWithMissingVolumeHash()
    {
        var testopts = TestOptions.Expand(new { no_encryption = true });
        var opts = new Library.Main.Options(testopts);

        using (var tmpfile = new TempFile())
        {
            // Manually create an index file with the old broken format: {"blocks":[]}
            using (var fs = new FileStream(tmpfile, FileMode.Create, FileAccess.Write))
            using (var compression = Library.DynamicLoader.CompressionLoader.GetModule(opts.CompressionModule, fs, ArchiveMode.Write, opts.RawOptions))
            {
                // Write manifest file required by the reader
                using (var sr = new StreamWriter(compression.CreateFile("manifest", CompressionHint.Compressible, DateTime.UtcNow)))
                    sr.Write("{\"Version\":2,\"Encoding\":\"utf8\",\"Blocksize\":" + opts.Blocksize + ",\"Created\":\"" + DateTime.UtcNow.ToString("o") + "\",\"BlockHash\":\"" + opts.BlockHashAlgorithm + "\",\"FileHash\":\"" + opts.FileHashAlgorithm + "\",\"AppVersion\":\"test\"}");

                using (var sw = new StreamWriter(compression.CreateFile("vol/" + VolumeBase.GenerateFilename(Library.Main.RemoteVolumeType.Blocks, opts, VolumeWriterBase.GenerateGuid(), DateTime.UtcNow), CompressionHint.Compressible, DateTime.UtcNow)))
                using (var jw = new JsonTextWriter(sw))
                {
                    jw.WriteStartObject();
                    jw.WritePropertyName("blocks");
                    jw.WriteStartArray();
                    jw.WriteEndArray();
                    jw.WriteEndObject();
                }
            }

            using (var rd = new IndexVolumeReader(opts.CompressionModule, tmpfile, opts, opts.BlockhashSize))
            {
                var volumes = rd.Volumes.ToList();
                Assert.That(volumes.Count, Is.EqualTo(1), "Expected one volume");
                var vol = volumes[0];
                var blocks = vol.Blocks.ToList();
                Assert.That(blocks.Count, Is.EqualTo(0), "Expected no blocks in volume");
                Assert.That(vol.Hash, Is.Null);
                Assert.That(vol.Length, Is.EqualTo(0));
            }
        }
    }
}
