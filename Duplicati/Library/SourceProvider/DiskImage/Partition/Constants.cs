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

namespace Duplicati.Library.SourceProvider.DiskImage.Partition;

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
