using System;
using System.Linq;
using System.Management;

namespace DeviceId.Windows.Wmi.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the system drive's serial number.
/// </summary>
public class WmiSystemDriveSerialNumberDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WmiSystemDriveSerialNumberDeviceIdComponent"/> class.
    /// </summary>
    public WmiSystemDriveSerialNumberDeviceIdComponent() { }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        try
        {
            var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);

            // SystemDirectory can sometimes be null or empty.
            // See: https://github.com/dotnet/runtime/issues/21430 and https://github.com/MatthewKing/DeviceId/issues/64
            if (string.IsNullOrEmpty(systemDirectory) || systemDirectory.Length < 2)
            {
                return null;
            }

            var systemLogicalDiskDeviceId = systemDirectory.Substring(0, 2);

            var queryString = $"SELECT * FROM Win32_LogicalDisk where DeviceId = '{systemLogicalDiskDeviceId}'";
            using var searcher = new ManagementObjectSearcher(queryString);

            foreach (var disk in searcher.Get().OfType<ManagementObject>())
            {
                foreach (var partition in disk.GetRelated("Win32_DiskPartition").OfType<ManagementObject>())
                {
                    foreach (var drive in partition.GetRelated("Win32_DiskDrive").OfType<ManagementObject>())
                    {
                        if (drive["SerialNumber"] is string serialNumber)
                        {
                            return serialNumber;
                        }
                    }
                }
            }
        }
        catch
        {
            // Swallow exceptions.
        }

        return null;
    }
}
