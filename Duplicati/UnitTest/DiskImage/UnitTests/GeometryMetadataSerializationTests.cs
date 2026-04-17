using System;
using Duplicati.Proprietary.DiskImage.General;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest.DiskImage.UnitTests;

public partial class DiskImageUnitTests : BasicSetupHelper
{

    /// <summary>
    /// Tests that a fully populated GeometryMetadata can be serialized to JSON and
    /// deserialized back, preserving all field values.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_RoundTrip_PreservesAllFields()
    {
        // Create a GeometryMetadata with all fields populated
        var original = new GeometryMetadata
        {
            Version = 1,
            Disk = new DiskGeometry
            {
                DevicePath = "/dev/sda",
                Size = 100 * MiB,
                SectorSize = 512,
                Sectors = (int)(100 * MiB / 512),
                TableType = PartitionTableType.GPT
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.GPT,
                Size = 17408, // Typical GPT size
                SectorSize = 512,
                HasProtectiveMbr = true,
                HeaderSize = 92
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048,
                    Size = 40 * MiB,
                    Name = "Test Partition 1",
                    FilesystemType = FileSystemType.FAT32,
                    VolumeGuid = Guid.Parse("12345678-1234-1234-1234-123456789abc"),
                    TableType = PartitionTableType.GPT
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048 + 40 * MiB,
                    Size = 40 * MiB,
                    Name = "Test Partition 2",
                    FilesystemType = FileSystemType.NTFS,
                    VolumeGuid = Guid.Parse("abcdef12-3456-7890-abcd-ef1234567890"),
                    TableType = PartitionTableType.GPT
                }
            ],
            Filesystems =
            [
                new FilesystemGeometry
                {
                    PartitionNumber = 1,
                    Type = FileSystemType.FAT32,
                    PartitionStartOffset = 512 * 2048,
                    BlockSize = 4096,
                    Metadata = "{\"label\":\"FAT32_VOL\",\"serial\":\"1234-5678\"}"
                },
                new FilesystemGeometry
                {
                    PartitionNumber = 2,
                    Type = FileSystemType.NTFS,
                    PartitionStartOffset = 512 * 2048 + 40 * MiB,
                    BlockSize = 4096,
                    Metadata = "{\"label\":\"NTFS_VOL\",\"guid\":\"{12345678-1234-1234-1234-123456789ABC}\"}"
                }
            ]
        };

        // Serialize to JSON
        var json = original.ToJson();

        // Verify JSON is not null or empty
        Assert.IsNotNull(json, "JSON output should not be null.");
        Assert.IsNotEmpty(json, "JSON output should not be empty.");

        // Deserialize from JSON
        var deserialized = GeometryMetadata.FromJson(json);

        // Verify deserialized object is not null
        Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

        // Verify Version
        Assert.AreEqual(original.Version, deserialized!.Version, "Version should match.");

        // Verify Disk properties
        Assert.IsNotNull(deserialized.Disk, "Disk should not be null after deserialization.");
        Assert.AreEqual(original.Disk!.DevicePath, deserialized.Disk!.DevicePath, "Disk DevicePath should match.");
        Assert.AreEqual(original.Disk.Size, deserialized.Disk.Size, "Disk Size should match.");
        Assert.AreEqual(original.Disk.SectorSize, deserialized.Disk.SectorSize, "Disk SectorSize should match.");
        Assert.AreEqual(original.Disk.Sectors, deserialized.Disk.Sectors, "Disk Sectors should match.");
        Assert.AreEqual(original.Disk.TableType, deserialized.Disk.TableType, "Disk TableType should match.");

        // Verify PartitionTable properties
        Assert.IsNotNull(deserialized.PartitionTable, "PartitionTable should not be null after deserialization.");
        Assert.AreEqual(original.PartitionTable!.Type, deserialized.PartitionTable!.Type, "PartitionTable Type should match.");
        Assert.AreEqual(original.PartitionTable.Size, deserialized.PartitionTable.Size, "PartitionTable Size should match.");
        Assert.AreEqual(original.PartitionTable.SectorSize, deserialized.PartitionTable.SectorSize, "PartitionTable SectorSize should match.");
        Assert.AreEqual(original.PartitionTable.HasProtectiveMbr, deserialized.PartitionTable.HasProtectiveMbr, "PartitionTable HasProtectiveMbr should match.");
        Assert.AreEqual(original.PartitionTable.HeaderSize, deserialized.PartitionTable.HeaderSize, "PartitionTable HeaderSize should match.");

        // Verify Partitions
        Assert.IsNotNull(deserialized.Partitions, "Partitions should not be null after deserialization.");
        Assert.AreEqual(original.Partitions!.Count, deserialized.Partitions!.Count, "Partition count should match.");

        for (int i = 0; i < original.Partitions.Count; i++)
        {
            var originalPart = original.Partitions[i];
            var deserializedPart = deserialized.Partitions[i];

            Assert.AreEqual(originalPart.Number, deserializedPart.Number, $"Partition {i + 1} Number should match.");
            Assert.AreEqual(originalPart.Type, deserializedPart.Type, $"Partition {i + 1} Type should match.");
            Assert.AreEqual(originalPart.StartOffset, deserializedPart.StartOffset, $"Partition {i + 1} StartOffset should match.");
            Assert.AreEqual(originalPart.Size, deserializedPart.Size, $"Partition {i + 1} Size should match.");
            Assert.AreEqual(originalPart.Name, deserializedPart.Name, $"Partition {i + 1} Name should match.");
            Assert.AreEqual(originalPart.FilesystemType, deserializedPart.FilesystemType, $"Partition {i + 1} FilesystemType should match.");
            Assert.AreEqual(originalPart.VolumeGuid, deserializedPart.VolumeGuid, $"Partition {i + 1} VolumeGuid should match.");
            Assert.AreEqual(originalPart.TableType, deserializedPart.TableType, $"Partition {i + 1} TableType should match.");
        }

        // Verify Filesystems
        Assert.IsNotNull(deserialized.Filesystems, "Filesystems should not be null after deserialization.");
        Assert.AreEqual(original.Filesystems!.Count, deserialized.Filesystems!.Count, "Filesystem count should match.");

        for (int i = 0; i < original.Filesystems.Count; i++)
        {
            var originalFs = original.Filesystems[i];
            var deserializedFs = deserialized.Filesystems[i];

            Assert.AreEqual(originalFs.PartitionNumber, deserializedFs.PartitionNumber, $"Filesystem {i + 1} PartitionNumber should match.");
            Assert.AreEqual(originalFs.Type, deserializedFs.Type, $"Filesystem {i + 1} Type should match.");
            Assert.AreEqual(originalFs.PartitionStartOffset, deserializedFs.PartitionStartOffset, $"Filesystem {i + 1} PartitionStartOffset should match.");
            Assert.AreEqual(originalFs.BlockSize, deserializedFs.BlockSize, $"Filesystem {i + 1} BlockSize should match.");
            Assert.AreEqual(originalFs.Metadata, deserializedFs.Metadata, $"Filesystem {i + 1} Metadata should match.");
        }
    }

    /// <summary>
    /// Tests that a GeometryMetadata with null optional fields can be serialized
    /// and deserialized correctly.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_RoundTrip_NullOptionalFields_PreservesStructure()
    {
        // Create a GeometryMetadata with minimal/null optional fields
        var original = new GeometryMetadata
        {
            Version = 1,
            Disk = new DiskGeometry
            {
                DevicePath = null, // Null optional field
                Size = 50 * MiB,
                SectorSize = 512,
                Sectors = (int)(50 * MiB / 512),
                TableType = PartitionTableType.MBR
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.MBR,
                Size = 512,
                SectorSize = 512,
                HasProtectiveMbr = false,
                HeaderSize = 0,
                MbrSize = 512
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048,
                    Size = 40 * MiB,
                    Name = null, // Null optional field
                    FilesystemType = FileSystemType.Unknown,
                    VolumeGuid = null, // Null optional field
                    TableType = PartitionTableType.MBR
                }
            ],
            Filesystems = null // Null optional collection
        };

        // Serialize to JSON
        var json = original.ToJson();

        // Verify JSON is not null or empty
        Assert.IsNotNull(json, "JSON output should not be null.");
        Assert.IsNotEmpty(json, "JSON output should not be empty.");

        // Deserialize from JSON
        var deserialized = GeometryMetadata.FromJson(json);

        // Verify deserialized object is not null
        Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

        // Verify null fields remain null
        Assert.IsNull(deserialized!.Disk!.DevicePath, "DevicePath should be null after deserialization.");
        Assert.IsNull(deserialized.Filesystems, "Filesystems should be null after deserialization.");

        // Verify partition with null fields
        Assert.IsNotNull(deserialized.Partitions, "Partitions should not be null.");
        Assert.AreEqual(1, deserialized.Partitions!.Count, "Should have 1 partition.");
        Assert.IsNull(deserialized.Partitions[0].Name, "Partition Name should be null.");
        Assert.IsNull(deserialized.Partitions[0].VolumeGuid, "Partition VolumeGuid should be null.");

        // Verify other fields are preserved
        Assert.AreEqual(original.Disk.Size, deserialized.Disk.Size, "Disk Size should match.");
        Assert.AreEqual(original.Disk.SectorSize, deserialized.Disk.SectorSize, "Disk SectorSize should match.");
        Assert.AreEqual(original.PartitionTable!.Type, deserialized.PartitionTable!.Type, "PartitionTable Type should match.");
    }

    /// <summary>
    /// Tests that an empty GeometryMetadata (with all null/empty fields) can be
    /// serialized and deserialized correctly.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_RoundTrip_EmptyMetadata_HandlesGracefully()
    {
        // Create a GeometryMetadata with mostly empty/null fields
        var original = new GeometryMetadata
        {
            Version = 1,
            Disk = null,
            PartitionTable = null,
            Partitions = null,
            Filesystems = null
        };

        // Serialize to JSON
        var json = original.ToJson();

        // Verify JSON is not null or empty
        Assert.IsNotNull(json, "JSON output should not be null.");
        Assert.IsNotEmpty(json, "JSON output should not be empty.");

        // Deserialize from JSON
        var deserialized = GeometryMetadata.FromJson(json);

        // Verify deserialized object is not null
        Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

        // Verify fields are null
        Assert.IsNull(deserialized!.Disk, "Disk should be null.");
        Assert.IsNull(deserialized.PartitionTable, "PartitionTable should be null.");
        Assert.IsNull(deserialized.Partitions, "Partitions should be null.");
        Assert.IsNull(deserialized.Filesystems, "Filesystems should be null.");
        Assert.AreEqual(1, deserialized.Version, "Version should be preserved.");
    }

    /// <summary>
    /// Tests that a GeometryMetadata with multiple partitions and filesystems of
    /// different types can be serialized and deserialized correctly.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_RoundTrip_MultiplePartitionsAndFilesystems_PreservesAllData()
    {
        // Create a GeometryMetadata with multiple partitions and filesystems
        var original = new GeometryMetadata
        {
            Version = 1,
            Disk = new DiskGeometry
            {
                DevicePath = "/dev/nvme0n1",
                Size = 500 * MiB,
                SectorSize = 4096, // 4K sectors
                Sectors = (int)(500 * MiB / 4096),
                TableType = PartitionTableType.GPT
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.GPT,
                Size = 32768,
                SectorSize = 4096,
                HasProtectiveMbr = true,
                HeaderSize = 92
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    StartOffset = 4096 * 6, // Start at sector 6 (after GPT headers)
                    Size = 100 * MiB,
                    Name = "EFI System Partition",
                    FilesystemType = FileSystemType.FAT32,
                    VolumeGuid = Guid.NewGuid(),
                    TableType = PartitionTableType.GPT
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    StartOffset = 4096 * 6 + 100 * MiB,
                    Size = 150 * MiB,
                    Name = "Windows OS",
                    FilesystemType = FileSystemType.NTFS,
                    VolumeGuid = Guid.NewGuid(),
                    TableType = PartitionTableType.GPT
                },
                new PartitionGeometry
                {
                    Number = 3,
                    Type = PartitionType.Primary,
                    StartOffset = 4096 * 6 + 250 * MiB,
                    Size = 200 * MiB,
                    Name = "Linux Root",
                    FilesystemType = FileSystemType.Ext4,
                    VolumeGuid = Guid.NewGuid(),
                    TableType = PartitionTableType.GPT
                }
            ],
            Filesystems =
            [
                new FilesystemGeometry
                {
                    PartitionNumber = 1,
                    Type = FileSystemType.FAT32,
                    PartitionStartOffset = 4096 * 6,
                    BlockSize = 4096,
                    Metadata = "{\"label\":\"EFI\"}"
                },
                new FilesystemGeometry
                {
                    PartitionNumber = 2,
                    Type = FileSystemType.NTFS,
                    PartitionStartOffset = 4096 * 6 + 100 * MiB,
                    BlockSize = 4096,
                    Metadata = "{\"label\":\"Windows\"}"
                },
                new FilesystemGeometry
                {
                    PartitionNumber = 3,
                    Type = FileSystemType.Ext4,
                    PartitionStartOffset = 4096 * 6 + 250 * MiB,
                    BlockSize = 4096,
                    Metadata = "{\"label\":\"/\"}"
                }
            ]
        };

        // Serialize to JSON
        var json = original.ToJson();

        // Verify JSON is not null or empty
        Assert.IsNotNull(json, "JSON output should not be null.");
        Assert.IsNotEmpty(json, "JSON output should not be empty.");

        // Deserialize from JSON
        var deserialized = GeometryMetadata.FromJson(json);

        // Verify deserialized object is not null
        Assert.IsNotNull(deserialized, "Deserialized object should not be null.");

        // Verify counts
        Assert.AreEqual(3, deserialized!.Partitions!.Count, "Should have 3 partitions.");
        Assert.AreEqual(3, deserialized.Filesystems!.Count, "Should have 3 filesystems.");

        // Verify each partition's filesystem type
        Assert.AreEqual(FileSystemType.FAT32, deserialized.Partitions[0].FilesystemType, "Partition 1 should be FAT32.");
        Assert.AreEqual(FileSystemType.NTFS, deserialized.Partitions[1].FilesystemType, "Partition 2 should be NTFS.");
        Assert.AreEqual(FileSystemType.Ext4, deserialized.Partitions[2].FilesystemType, "Partition 3 should be Ext4.");

        // Verify each filesystem's type
        Assert.AreEqual(FileSystemType.FAT32, deserialized.Filesystems[0].Type, "Filesystem 1 should be FAT32.");
        Assert.AreEqual(FileSystemType.NTFS, deserialized.Filesystems[1].Type, "Filesystem 2 should be NTFS.");
        Assert.AreEqual(FileSystemType.Ext4, deserialized.Filesystems[2].Type, "Filesystem 3 should be Ext4.");

        // Verify 4K sector size is preserved
        Assert.AreEqual(4096, deserialized.Disk!.SectorSize, "Disk SectorSize should be 4096.");
        Assert.AreEqual(4096, deserialized.PartitionTable!.SectorSize, "PartitionTable SectorSize should be 4096.");
    }

    /// <summary>
    /// Tests that serializing an MBR-based GeometryMetadata produces valid JSON
    /// that can be correctly deserialized.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_RoundTrip_MBRType_PreservesMBRData()
    {
        var original = new GeometryMetadata
        {
            Version = 1,
            Disk = new DiskGeometry
            {
                DevicePath = "/dev/sdb",
                Size = 200 * MiB,
                SectorSize = 512,
                Sectors = (int)(200 * MiB / 512),
                TableType = PartitionTableType.MBR
            },
            PartitionTable = new PartitionTableGeometry
            {
                Type = PartitionTableType.MBR,
                Size = 512,
                SectorSize = 512,
                HasProtectiveMbr = false,
                MbrSize = 512
            },
            Partitions =
            [
                new PartitionGeometry
                {
                    Number = 1,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048,
                    Size = 100 * MiB,
                    Name = "Primary Partition",
                    FilesystemType = FileSystemType.FAT32,
                    TableType = PartitionTableType.MBR
                },
                new PartitionGeometry
                {
                    Number = 2,
                    Type = PartitionType.Primary,
                    StartOffset = 512 * 2048 + 100 * MiB,
                    Size = 90 * MiB,
                    Name = "Secondary Partition",
                    FilesystemType = FileSystemType.NTFS,
                    TableType = PartitionTableType.MBR
                }
            ],
            Filesystems =
            [
                new FilesystemGeometry
                {
                    PartitionNumber = 1,
                    Type = FileSystemType.FAT32,
                    PartitionStartOffset = 512 * 2048,
                    BlockSize = 4096,
                    Metadata = null
                }
            ]
        };

        // Serialize to JSON
        var json = original.ToJson();

        // Deserialize from JSON
        var deserialized = GeometryMetadata.FromJson(json);

        // Verify MBR-specific fields
        Assert.IsNotNull(deserialized, "Deserialized object should not be null.");
        Assert.AreEqual(PartitionTableType.MBR, deserialized!.Disk!.TableType, "Disk TableType should be MBR.");
        Assert.AreEqual(PartitionTableType.MBR, deserialized.PartitionTable!.Type, "PartitionTable Type should be MBR.");
        Assert.AreEqual(512, deserialized.PartitionTable.MbrSize, "PartitionTable MbrSize should be 512.");
        Assert.IsFalse(deserialized.PartitionTable.HasProtectiveMbr, "PartitionTable HasProtectiveMbr should be false.");

        // Verify all partitions have MBR table type
        foreach (var partition in deserialized.Partitions!)
        {
            Assert.AreEqual(PartitionTableType.MBR, partition.TableType, "All partitions should have MBR TableType.");
        }
    }

    /// <summary>
    /// Tests that deserializing invalid JSON throws an appropriate exception.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_FromJson_InvalidJson_ThrowsException()
    {
        // Test with invalid JSON - should throw JsonException
        var invalidJson = "{invalid json";
        Assert.Throws<System.Text.Json.JsonException>(() => GeometryMetadata.FromJson(invalidJson));
    }

    /// <summary>
    /// Tests that deserializing empty JSON object creates a minimal GeometryMetadata
    /// with default property values set by the class.
    /// </summary>
    [Test]
    public void Test_GeometryMetadata_FromJson_EmptyObject_CreatesDefaultInstance()
    {
        var emptyJson = "{}";
        var result = GeometryMetadata.FromJson(emptyJson);

        Assert.IsNotNull(result, "Should create an instance from empty JSON object.");
        // Version has a default value of 1 in the class definition
        Assert.AreEqual(1, result!.Version, "Version should have default value (1) from class definition.");
        Assert.IsNull(result.Disk, "Disk should be null.");
        Assert.IsNull(result.PartitionTable, "PartitionTable should be null.");
        Assert.IsNull(result.Partitions, "Partitions should be null.");
        Assert.IsNull(result.Filesystems, "Filesystems should be null.");
    }

}