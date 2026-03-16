// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Proprietary.DiskImage.Filesystem;
using Duplicati.Proprietary.DiskImage.Filesystem.Fat32;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest.DiskImage.Fat32;

/// <summary>
/// Unit tests for the <see cref="Fat32Filesystem"/> class and related types.
/// </summary>
[TestFixture]
[Category("DiskImageUnit")]
public class Fat32FilesystemTests
{
    [Test]
    public void Test_Fat32FilesystemMetadata_Properties()
    {
        var metadata = new Fat32FilesystemMetadata
        {
            BlockSize = 1024 * 1024,
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500
        };

        Assert.AreEqual(1024 * 1024, metadata.BlockSize);
        Assert.AreEqual(4096, metadata.ClusterSize);
        Assert.AreEqual(1000, metadata.TotalClusters);
        Assert.AreEqual(500, metadata.AllocatedClusters);
        Assert.AreEqual(500, metadata.FreeClusters);
    }

    [Test]
    public void Test_Fat32FilesystemMetadata_RecordEquality()
    {
        var metadata1 = new Fat32FilesystemMetadata
        {
            BlockSize = 1024 * 1024,
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500
        };

        var metadata2 = new Fat32FilesystemMetadata
        {
            BlockSize = 1024 * 1024,
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500
        };

        var metadata3 = new Fat32FilesystemMetadata
        {
            BlockSize = 512 * 1024, // Different
            ClusterSize = 4096,
            TotalClusters = 1000,
            AllocatedClusters = 500,
            FreeClusters = 500
        };

        Assert.AreEqual(metadata1, metadata2);
        Assert.AreNotEqual(metadata1, metadata3);
    }

    [Test]
    public void Test_Fat32File_DefaultValues()
    {
        var file = new Fat32File();

        Assert.IsNull(file.Path);
        Assert.IsNull(file.Address);
        Assert.AreEqual(0, file.Size);
        Assert.IsFalse(file.IsDirectory);
        Assert.IsNull(file.LastModified);
        Assert.IsFalse(file.IsAllocated);
    }

    [Test]
    public void Test_Fat32File_WithValues()
    {
        var timestamp = new DateTime(2023, 6, 15, 10, 30, 0, DateTimeKind.Utc);
        var file = new Fat32File
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
    public void Test_Fat32File_ImplementsIFile()
    {
        var file = new Fat32File();
        Assert.IsInstanceOf<IFile>(file);
    }

    [Test]
    public void Test_Fat32File_LastModified_Nullable()
    {
        var file1 = new Fat32File { LastModified = DateTime.UtcNow };
        var file2 = new Fat32File { LastModified = null };

        Assert.IsTrue(file1.LastModified.HasValue);
        Assert.IsFalse(file2.LastModified.HasValue);
    }

    [Test]
    public void Test_Fat32File_IsAllocated_FalseByDefault()
    {
        var file = new Fat32File();
        Assert.IsFalse(file.IsAllocated);
    }

    [Test]
    public void Test_Fat32File_IsAllocated_CanBeTrue()
    {
        var file = new Fat32File { IsAllocated = true };
        Assert.IsTrue(file.IsAllocated);
    }

    [Test]
    public void Test_Fat32File_Address_LongNullable()
    {
        var file1 = new Fat32File { Address = 0 };
        var file2 = new Fat32File { Address = long.MaxValue };
        var file3 = new Fat32File { Address = null };

        Assert.AreEqual(0, file1.Address);
        Assert.AreEqual(long.MaxValue, file2.Address);
        Assert.IsNull(file3.Address);
    }

    [Test]
    public void Test_Fat32File_Size_Long()
    {
        var file1 = new Fat32File { Size = 0 };
        var file2 = new Fat32File { Size = 1024 * 1024 };
        var file3 = new Fat32File { Size = long.MaxValue };

        Assert.AreEqual(0, file1.Size);
        Assert.AreEqual(1024 * 1024, file2.Size);
        Assert.AreEqual(long.MaxValue, file3.Size);
    }

    [Test]
    public void Test_Fat32File_Path_CanBeNull()
    {
        var file1 = new Fat32File { Path = null };
        var file2 = new Fat32File { Path = "test/path" };

        Assert.IsNull(file1.Path);
        Assert.AreEqual("test/path", file2.Path);
    }
}
