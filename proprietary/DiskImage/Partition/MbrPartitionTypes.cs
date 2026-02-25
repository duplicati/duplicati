// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Provides bidirectional mapping between MBR partition type bytes and <see cref="PartitionType"/> / <see cref="FileSystemType"/> values.
/// </summary>
internal static class MbrPartitionTypes
{
    // MBR partition type byte constants
    public const byte Fat12 = 0x01;
    public const byte Fat16Small = 0x04;
    public const byte Fat16 = 0x06;
    public const byte Ntfs = 0x07;
    public const byte Fat32Chs = 0x0B;
    public const byte Fat32Lba = 0x0C;
    public const byte Fat16Lba = 0x0E;
    public const byte ExtendedLba = 0x0F;
    public const byte HiddenFat12 = 0x11;
    public const byte HiddenFat16Small = 0x14;
    public const byte HiddenFat16 = 0x16;
    public const byte HiddenNtfs = 0x17;
    public const byte HiddenFat32Chs = 0x1B;
    public const byte HiddenFat32Lba = 0x1C;
    public const byte HiddenFat16Lba = 0x1E;
    public const byte ExtendedChs = 0x05;
    public const byte LinuxExtended = 0x85;
    public const byte LinuxLvm = 0x8E;
    public const byte GptProtective = 0xEE;
    public const byte EfiSystem = 0xEF;
    public const byte LinuxRaid = 0xFD;
    public const byte AppleHfs = 0xAF;

    /// <summary>
    /// Maps an MBR partition type byte to the corresponding <see cref="PartitionType"/>.
    /// </summary>
    /// <param name="typeByte">The partition type byte from the MBR.</param>
    /// <returns>The corresponding <see cref="PartitionType"/>, or <see cref="PartitionType.Unknown"/> if not recognized.</returns>
    public static PartitionType ToPartitionType(byte typeByte)
    {
        return typeByte switch
        {
            Fat12 => PartitionType.Primary,
            Fat16Small => PartitionType.Primary,
            Fat16 => PartitionType.Primary,
            Ntfs => PartitionType.Primary,
            Fat32Chs => PartitionType.Primary,
            Fat32Lba => PartitionType.Primary,
            Fat16Lba => PartitionType.Primary,
            ExtendedLba => PartitionType.Extended,
            HiddenFat12 => PartitionType.Primary,
            HiddenFat16Small => PartitionType.Primary,
            HiddenFat16 => PartitionType.Primary,
            HiddenNtfs => PartitionType.Primary,
            HiddenFat32Chs => PartitionType.Primary,
            HiddenFat32Lba => PartitionType.Primary,
            HiddenFat16Lba => PartitionType.Primary,
            ExtendedChs => PartitionType.Extended,
            LinuxExtended => PartitionType.Logical,
            LinuxLvm => PartitionType.Primary,
            GptProtective => PartitionType.Protective,
            EfiSystem => PartitionType.EFI,
            LinuxRaid => PartitionType.Primary,
            AppleHfs => PartitionType.AppleHFS,
            _ => PartitionType.Unknown
        };
    }

    /// <summary>
    /// Maps an MBR partition type byte to the corresponding <see cref="FileSystemType"/>.
    /// </summary>
    /// <param name="typeByte">The partition type byte from the MBR.</param>
    /// <returns>The corresponding <see cref="FileSystemType"/>, or <see cref="FileSystemType.Unknown"/> if not recognized.</returns>
    public static FileSystemType ToFilesystemType(byte typeByte)
    {
        return typeByte switch
        {
            Fat12 or HiddenFat12 or 0x81 => FileSystemType.FAT12,
            Fat16Small or Fat16 or Fat16Lba or HiddenFat16Small or HiddenFat16 or HiddenFat16Lba => FileSystemType.FAT16,
            Fat32Chs or Fat32Lba or HiddenFat32Chs or HiddenFat32Lba => FileSystemType.FAT32,
            Ntfs or HiddenNtfs => FileSystemType.NTFS,
            EfiSystem => FileSystemType.Unknown, // EFI System Partition, actual FS unknown
            AppleHfs => FileSystemType.HFSPlus,
            _ => FileSystemType.Unknown
        };
    }

    /// <summary>
    /// Maps a <see cref="FileSystemType"/> to the corresponding MBR partition type byte.
    /// </summary>
    /// <param name="filesystemType">The filesystem type.</param>
    /// <param name="partitionType">The partition type (used as fallback).</param>
    /// <returns>The corresponding MBR partition type byte.</returns>
    public static byte ToTypeByte(FileSystemType filesystemType, PartitionType partitionType)
    {
        // Map filesystem type to MBR type byte
        return filesystemType switch
        {
            FileSystemType.NTFS => Ntfs,
            FileSystemType.FAT12 => Fat12,
            FileSystemType.FAT16 => Fat16,
            FileSystemType.FAT32 => Fat32Lba,  // LBA
            FileSystemType.ExFAT => Ntfs,      // Same as NTFS
            FileSystemType.HFSPlus => AppleHfs,
            _ => partitionType switch
            {
                PartitionType.EFI => EfiSystem,
                PartitionType.Extended => ExtendedLba,
                _ => Ntfs  // Default to NTFS/IFS type
            }
        };
    }
}
