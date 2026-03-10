namespace Duplicati.Proprietary.DiskImage.General;

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