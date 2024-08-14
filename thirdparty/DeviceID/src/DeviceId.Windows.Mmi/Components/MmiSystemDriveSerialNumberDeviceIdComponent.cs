using System;
using Microsoft.Management.Infrastructure;

namespace DeviceId.Windows.Mmi.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the system drive's serial number.
/// </summary>
public class MmiSystemDriveSerialNumberDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MmiSystemDriveSerialNumberDeviceIdComponent"/> class.
    /// </summary>
    public MmiSystemDriveSerialNumberDeviceIdComponent() { }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);

        // SystemDirectory can sometimes be null or empty.
        // See: https://github.com/dotnet/runtime/issues/21430 and https://github.com/MatthewKing/DeviceId/issues/64
        if (string.IsNullOrEmpty(systemDirectory) || systemDirectory.Length < 2)
        {
            return null;
        }

        try
        {
            var systemLogicalDiskDeviceId = systemDirectory.Substring(0, 2);

            using var session = CimSession.Create(null);

            foreach (var logicalDiskAssociator in session.QueryInstances(@"root\cimv2", "WQL", $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID=\"{systemLogicalDiskDeviceId}\"}} WHERE ResultClass = Win32_DiskPartition"))
            {
                if (logicalDiskAssociator.CimClass.CimSystemProperties.ClassName == "Win32_DiskPartition")
                {
                    if (logicalDiskAssociator.CimInstanceProperties["DeviceId"].Value is string diskPartitionDeviceId)
                    {
                        foreach (var diskPartitionAssociator in session.QueryInstances(@"root\cimv2", "WQL", $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID=\"{diskPartitionDeviceId}\"}}"))
                        {
                            if (diskPartitionAssociator.CimClass.CimSystemProperties.ClassName == "Win32_DiskDrive")
                            {
                                if (diskPartitionAssociator.CimInstanceProperties["SerialNumber"].Value is string diskDriveSerialNumber)
                                {
                                    return diskDriveSerialNumber;
                                }
                            }
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
