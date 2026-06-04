// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.IO;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Filesystem.Ntfs;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Ntfs;

/// <summary>
/// Unit tests for the <see cref="NtfsFilesystem.NtfsZeroStream"/> class.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class NtfsZeroStreamTests
{
    [Test]
    public void Test_NtfsZeroStream_Read_ReturnsZeros()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        var buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        Assert.AreEqual(1024, bytesRead, "Should read requested number of bytes");

        // Verify all bytes are zero
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.AreEqual(0, buffer[i], $"Byte at position {i} should be zero");
        }
    }

    [Test]
    public async Task Test_NtfsZeroStream_ReadAsync_ReturnsZeros()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        var buffer = new byte[1024];
        int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

        Assert.AreEqual(1024, bytesRead, "Should read requested number of bytes");

        // Verify all bytes are zero
        for (int i = 0; i < buffer.Length; i++)
        {
            Assert.AreEqual(0, buffer[i], $"Byte at position {i} should be zero");
        }
    }

    [Test]
    public void Test_NtfsZeroStream_Length_MatchesBlockSize()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        Assert.AreEqual(blockSize, stream.Length, "Stream length should match block size");
    }

    [Test]
    public void Test_NtfsZeroStream_Seek_Works()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        // Seek to beginning
        var position = stream.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(0, position, "Should be at position 0");

        // Seek to middle
        position = stream.Seek(blockSize / 2, SeekOrigin.Begin);
        Assert.AreEqual(blockSize / 2, position, "Should be at middle position");

        // Seek relative to current
        position = stream.Seek(1024, SeekOrigin.Current);
        Assert.AreEqual(blockSize / 2 + 1024, position, "Should be at relative position");

        // Seek from end
        position = stream.Seek(-1024, SeekOrigin.End);
        Assert.AreEqual(blockSize - 1024, position, "Should be at position from end");
    }

    [Test]
    public void Test_NtfsZeroStream_Write_ThrowsNotSupported()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        var buffer = new byte[1024];
        Assert.Throws<NotSupportedException>(() => stream.Write(buffer, 0, buffer.Length));
    }

    [Test]
    public void Test_NtfsZeroStream_ReadPartial_ReturnsCorrectCount()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        // Seek to near the end
        stream.Seek(blockSize - 100, SeekOrigin.Begin);

        var buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        Assert.AreEqual(100, bytesRead, "Should only read remaining bytes");
    }

    [Test]
    public void Test_NtfsZeroStream_ReadPastEnd_ReturnsZero()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        // Seek to end
        stream.Seek(0, SeekOrigin.End);

        var buffer = new byte[1024];
        int bytesRead = stream.Read(buffer, 0, buffer.Length);

        Assert.AreEqual(0, bytesRead, "Should return 0 when reading past end");
    }

    [Test]
    public void Test_NtfsZeroStream_CanRead_CanWrite_CanSeek()
    {
        const int blockSize = 1024 * 1024; // 1MB
        using var stream = new NtfsFilesystem.NtfsZeroStream(blockSize);

        Assert.IsTrue(stream.CanRead, "CanRead should be true");
        Assert.IsFalse(stream.CanWrite, "CanWrite should be false");
        Assert.IsTrue(stream.CanSeek, "CanSeek should be true");
    }

    [Test]
    public void Test_NtfsZeroStream_InvalidBlockSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => new NtfsFilesystem.NtfsZeroStream(0));
        Assert.Throws<ArgumentException>(() => new NtfsFilesystem.NtfsZeroStream(-1));
    }
}
