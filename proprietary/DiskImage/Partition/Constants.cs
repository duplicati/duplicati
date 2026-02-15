namespace Duplicati.Proprietary.DiskImage.Partition;

/// <summary>
/// Shared constants for partition table parsing and synthesis.
/// </summary>
internal static class PartitionConstants
{
    // MBR constants
    public const int MbrSize = 512;
    public const ushort MbrBootSignature = 0xAA55;
    public const byte ProtectiveMbrType = 0xEE;
    public const int MbrPartitionEntrySize = 16;
    public const int MaxMbrPartitionEntries = 4;

    // GPT constants
    public const int GptHeaderSize = 92;
    public const long GptSignature = 0x5452415020494645; // "EFI PART" in little-endian
    public const uint GptRevision = 0x00010000; // Version 1.0
    public const int GptPartitionEntrySize = 128; // Standard GPT partition entry size
    public const int GptNumPartitionEntries = 128; // Standard GPT supports 128 entries
}
