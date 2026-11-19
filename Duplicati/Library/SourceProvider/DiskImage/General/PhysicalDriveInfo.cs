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
/// Represents a physical drive returned by PowerShell Get-Disk.
/// </summary>
public class PhysicalDriveInfo
{
    /// <summary>
    /// The drive number (e.g., 0 for \\.\PHYSICALDRIVE0), 3 for /dev/disk3, a for /dev/sda, etc.
    /// </summary>
    public string Number { get; set; } = "";

    /// <summary>
    /// The device path (e.g., \\.\PHYSICALDRIVE0, /dev/disk3, /dev/sda, etc.).
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The size of the drive in bytes.
    /// </summary>
    public ulong Size { get; set; }

    /// <summary>
    /// The display name of the drive.
    /// </summary>
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The GUID of the drive.
    /// </summary>
    public string? Guid { get; set; }

    /// <summary>
    /// The mount points associated with this drive.
    /// </summary>
    public string[] MountPoints { get; set; } = [];

    /// <summary>
    /// Whether the drive is online.
    /// </summary>
    public bool? Online { get; set; }
}