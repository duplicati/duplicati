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
using Duplicati.Library.SourceProvider.DiskImage.Filesystem;
using Duplicati.Library.SourceProvider.DiskImage.Filesystem.Ntfs;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Ntfs;

/// <summary>
/// Unit tests for the <see cref="NtfsFilesystem"/> class and related types.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class NtfsFilesystemTests
{
    [Test]
    public void Test_NtfsFilesystemMetadata_Properties()
    {
        var metadata = new NtfsFilesystemMetadata
        {
            BlockSize = 1024 * 1024,
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500,
            MftRecordSize = 1024
        };

        Assert.AreEqual(1024 * 1024, metadata.BlockSize);
        Assert.AreEqual(4096, metadata.ClusterSize);
        Assert.AreEqual(1000, metadata.TotalClusters);
        Assert.AreEqual(500, metadata.AllocatedClusters);
        Assert.AreEqual(500, metadata.FreeClusters);
        Assert.AreEqual(1024, metadata.MftRecordSize);
    }

    [Test]
    public void Test_NtfsFilesystemMetadata_RecordEquality()
    {
        var metadata1 = new NtfsFilesystemMetadata
        {
            BlockSize = 1024 * 1024,
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500,
            MftRecordSize = 1024
        };

        var metadata2 = new NtfsFilesystemMetadata
        {
            BlockSize = 1024 * 1024,
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500,
            MftRecordSize = 1024
        };

        var metadata3 = new NtfsFilesystemMetadata
        {
            BlockSize = 512 * 1024, // Different
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500,
            MftRecordSize = 1024
        };

        Assert.AreEqual(metadata1, metadata2);
        Assert.AreNotEqual(metadata1, metadata3);
    }

    [Test]
    public void Test_NtfsFilesystemMetadata_HasJournal_True()
    {
        var metadata = new NtfsFilesystemMetadata();

        Assert.IsTrue(metadata.HasJournal, "HasJournal should always be true for NTFS");
    }

    [Test]
    public void Test_NtfsFile_DefaultValues()
    {
        var file = new NtfsFile();

        Assert.IsNull(file.Path);
        Assert.IsNull(file.Address);
        Assert.AreEqual(0, file.Size);
        Assert.IsFalse(file.IsDirectory);
        Assert.IsNull(file.LastModified);
        Assert.IsFalse(file.IsAllocated);
    }

    [Test]
    public void Test_NtfsFile_WithValues()
    {
        var timestamp = new DateTime(2023, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var file = new NtfsFile
        {
            Path = "test_block",
            Address = 1024,
            Size = 1024 * 1024,
            LastModified = timestamp,
            IsAllocated = true
        };

        Assert.AreEqual("test_block", file.Path);
        Assert.AreEqual(1024, file.Address);
        Assert.AreEqual(1024 * 1024, file.Size);
        Assert.IsFalse(file.IsDirectory);
        Assert.AreEqual(timestamp, file.LastModified);
        Assert.IsTrue(file.IsAllocated);
    }

    [Test]
    public void Test_NtfsFile_ImplementsIFile()
    {
        var file = new NtfsFile();
        Assert.IsInstanceOf<IFile>(file);
    }

    [Test]
    public void Test_NtfsFile_LastModified_Nullable()
    {
        var file1 = new NtfsFile { LastModified = DateTime.UtcNow };
        var file2 = new NtfsFile { LastModified = null };

        Assert.IsTrue(file1.LastModified.HasValue);
        Assert.IsFalse(file2.LastModified.HasValue);
    }

    [Test]
    public void Test_NtfsFile_IsAllocated_FalseByDefault()
    {
        var file = new NtfsFile();
        Assert.IsFalse(file.IsAllocated);
    }

    [Test]
    public void Test_NtfsFile_IsAllocated_CanBeTrue()
    {
        var file = new NtfsFile { IsAllocated = true };
        Assert.IsTrue(file.IsAllocated);
    }
}
