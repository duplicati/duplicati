// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Generic;

namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Provides bidirectional mapping between GPT partition type GUIDs and <see cref="PartitionType"/> values.
/// </summary>
internal static class GptPartitionTypeGuids
{
    // GPT partition type GUIDs as constants for maintainability
    private static readonly Guid EfiSystemGuid = Guid.Parse("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    private static readonly Guid MicrosoftReservedGuid = Guid.Parse("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    private static readonly Guid MicrosoftBasicDataGuid = Guid.Parse("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
    private static readonly Guid WindowsRecoveryGuid = Guid.Parse("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");
    private static readonly Guid LinuxFilesystemGuid = Guid.Parse("0FC63DAF-8483-4772-8E79-3D69D8477DE4");
    private static readonly Guid LinuxSwapGuid = Guid.Parse("0657FD6D-A4AB-43C4-84E5-0933C84B4F4F");
    private static readonly Guid LinuxLvmGuid = Guid.Parse("E6D6D379-F507-44C2-A23C-238F2A3DF928");
    private static readonly Guid LinuxRaidGuid = Guid.Parse("A19D880F-05FC-4D3B-A006-743F0F84911E");
    private static readonly Guid AppleHfsGuid = Guid.Parse("48465300-0000-11AA-AA11-00306543ECAC");
    private static readonly Guid AppleApfsGuid = Guid.Parse("7C3457EF-0000-11AA-AA11-00306543ECAC");
    private static readonly Guid AppleBootGuid = Guid.Parse("426F6F74-0000-11AA-AA11-00306543ECAC");
    private static readonly Guid BiosBootGuid = Guid.Parse("21686148-6449-6E6F-744E-656564454649");

    /// <summary>
    /// Maps a GPT partition type GUID to the corresponding <see cref="PartitionType"/>.
    /// </summary>
    /// <param name="typeGuid">The partition type GUID.</param>
    /// <returns>The corresponding <see cref="PartitionType"/>, or <see cref="PartitionType.Unknown"/> if not recognized.</returns>
    public static PartitionType ToPartitionType(Guid typeGuid)
    {
        // Normalize to uppercase string for comparison
        var guidString = typeGuid.ToString().ToUpperInvariant();

        return guidString switch
        {
            "C12A7328-F81F-11D2-BA4B-00A0C93EC93B" => PartitionType.EFI,
            "E3C9E316-0B5C-4DB8-817D-F92DF00215AE" => PartitionType.MicrosoftReserved,
            "EBD0A0A2-B9E5-4433-87C0-68B6B72699C7" => PartitionType.Primary, // Microsoft Basic Data
            "DE94BBA4-06D1-4D40-A16A-BFD50179D6AC" => PartitionType.Recovery, // Windows Recovery
            "0FC63DAF-8483-4772-8E79-3D69D8477DE4" => PartitionType.LinuxFilesystem,
            "0657FD6D-A4AB-43C4-84E5-0933C84B4F4F" => PartitionType.LinuxSwap,
            "E6D6D379-F507-44C2-A23C-238F2A3DF928" => PartitionType.LinuxLVM,
            "A19D880F-05FC-4D3B-A006-743F0F84911E" => PartitionType.LinuxRAID,
            "48465300-0000-11AA-AA11-00306543ECAC" => PartitionType.AppleHFS,
            "7C3457EF-0000-11AA-AA11-00306543ECAC" => PartitionType.AppleAPFS,
            "426F6F74-0000-11AA-AA11-00306543ECAC" => PartitionType.AppleBoot,
            "21686148-6449-6E6F-744E-656564454649" => PartitionType.BIOSBoot,
            _ => PartitionType.Unknown
        };
    }

    /// <summary>
    /// Maps a <see cref="PartitionType"/> to the corresponding GPT partition type GUID.
    /// </summary>
    /// <param name="partitionType">The partition type.</param>
    /// <returns>The corresponding GPT partition type GUID, or the Microsoft Basic Data GUID as the default.</returns>
    public static Guid ToGuid(PartitionType partitionType)
    {
        return partitionType switch
        {
            PartitionType.EFI => EfiSystemGuid,
            PartitionType.MicrosoftReserved => MicrosoftReservedGuid,
            PartitionType.Recovery => WindowsRecoveryGuid,
            PartitionType.LinuxFilesystem => LinuxFilesystemGuid,
            PartitionType.LinuxSwap => LinuxSwapGuid,
            PartitionType.LinuxLVM => LinuxLvmGuid,
            PartitionType.LinuxRAID => LinuxRaidGuid,
            PartitionType.BIOSBoot => BiosBootGuid,
            _ => MicrosoftBasicDataGuid  // Microsoft Basic Data (default for Primary and others)
        };
    }
}
