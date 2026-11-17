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
using System.Threading.Tasks;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Restore;
using Duplicati.Library.Main.Volumes;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A restore reads each block into a buffer. The buffer must fit the block, not the block size of the
/// backup: the block cache keeps thousands of blocks, so with small files a buffer of the full block
/// size each made the restore hold gigabytes of mostly empty buffers (#7538).
/// </summary>
public class RestoreBlockBufferTests
{
    [Test]
    [Category("Targeted")]
    public async Task ASmallBlockIsReadIntoABufferThatFitsItAsync()
    {
        // The default block size, where the waste is largest
        var options = new Options(new Dictionary<string, string?> { ["blocksize"] = "1mb" });
        var data = new byte[300];
        new Random(1).NextBytes(data);
        string hash;
        using (var hasher = HashFactory.CreateHasher(options.BlockHashAlgorithm))
            hash = Convert.ToBase64String(hasher.ComputeHash(data));

        using var writer = new BlockVolumeWriter(options);
        await writer.AddBlockAsync(hash, data, 0, data.Length, Library.Interface.CompressionHint.Default);
        writer.Close();

        var channels = new Channels(options);
        var decompressor = VolumeDecompressor.RunAsync(channels, options);
        var volume = new VolumeWrapper(TempFile.WrapExistingFile(writer.LocalFilename), new BlockVolumeReader(options.CompressionModule, writer.LocalFilename, options));
        await channels.DecompressionRequest.WriteAsync((new BlockRequest(1, 0, hash, data.Length, 1, BlockRequestType.Download), volume));
        var (_, block) = await channels.DecompressedBlock.ReadAsync();
        await channels.DecompressionRequest.RetireAsync();
        await decompressor;

        using (block)
        {
            Assert.That(block.Data, Is.Not.Null);
            Assert.That(block.Data!.AsSpan(0, data.Length).ToArray(), Is.EqualTo(data), "The block was not read");
            Assert.That(block.Data!.Length, Is.LessThan(options.Blocksize / 64),
                $"A block of {data.Length} bytes was read into a buffer of {block.Data!.Length} bytes");
        }
    }
}
