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

namespace Duplicati.Library.SourceProvider.DiskImage.General;

/// <summary>
/// Represents the type of filesystem on a partition.
/// </summary>
public enum FileSystemType
{
    /// <summary>New Technology File System (Windows NT filesystem)</summary>
    NTFS,

    /// <summary>File Allocation Table 12 (typically for small removable media)</summary>
    FAT12,

    /// <summary>File Allocation Table 16 (for larger volumes)</summary>
    FAT16,

    /// <summary>File Allocation Table 32 (modern FAT filesystem)</summary>
    FAT32,

    /// <summary>Extensible File Allocation Table (Windows)</summary>
    ExFAT,

    /// <summary>Hierarchical File System Plus (macOS legacy)</summary>
    HFSPlus,

    /// <summary>Apple File System (modern macOS filesystem)</summary>
    APFS,

    /// <summary>Linux Ext2 filesystem</summary>
    Ext2,

    /// <summary>Linux Ext3 filesystem</summary>
    Ext3,

    /// <summary>Linux Ext4 filesystem</summary>
    Ext4,

    /// <summary>Linux XFS filesystem</summary>
    XFS,

    /// <summary>Linux Btrfs filesystem</summary>
    Btrfs,

    /// <summary>ZFS filesystem</summary>
    ZFS,

    /// <summary>Windows ReFS (Resilient File System)</summary>
    ReFS,

    /// <summary>Unknown or unsupported filesystem</summary>
    Unknown
}

/// <summary>
/// Represents the type of partition table.
/// </summary>
public enum PartitionTableType
{
    /// <summary>Master Boot Record (traditional BIOS partition table)</summary>
    MBR,

    /// <summary>GUID Partition Table (modern UEFI partition table)</summary>
    GPT,

    /// <summary>Apple Partition Map (legacy Mac partition table)</summary>
    //ApplePartitionMap,

    /// <summary>Unknown or unsupported partition table</summary>
    Unknown
}

/// <summary>
/// Represents the type of a partition.
/// </summary>
public enum PartitionType
{
    /// <summary>Primary partition (standard MBR partition)</summary>
    Primary,

    /// <summary>Extended partition (container for logical partitions)</summary>
    Extended,

    /// <summary>Logical partition (inside an extended partition)</summary>
    Logical,

    /// <summary>EFI System Partition (used for boot)</summary>
    EFI,

    /// <summary>Recovery partition (Windows recovery)</summary>
    Recovery,

    /// <summary>Microsoft Reserved partition (MSR)</summary>
    MicrosoftReserved,

    /// <summary>GPT Protective partition (indicates GPT disk)</summary>
    Protective,

    /// <summary>Linux filesystem partition</summary>
    LinuxFilesystem,

    /// <summary>Linux swap partition</summary>
    LinuxSwap,

    /// <summary>Linux LVM partition</summary>
    LinuxLVM,

    /// <summary>Linux RAID partition</summary>
    LinuxRAID,

    /// <summary>Apple HFS/HFS+ partition</summary>
    AppleHFS,

    /// <summary>Apple APFS partition</summary>
    AppleAPFS,

    /// <summary>Apple Boot partition</summary>
    AppleBoot,

    /// <summary>BIOS Boot partition (used by GRUB on GPT)</summary>
    BIOSBoot,

    /// <summary>Unknown partition type</summary>
    Unknown
}
