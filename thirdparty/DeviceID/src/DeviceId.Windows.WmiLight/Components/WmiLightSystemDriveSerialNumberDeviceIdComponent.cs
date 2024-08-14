using System;
using WmiLight;

namespace DeviceId.Windows.WmiLight.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the system drive's serial number.
/// </summary>
public class WmiLightSystemDriveSerialNumberDeviceIdComponent() : IDeviceIdComponent
{
    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var systemLogicalDiskDeviceId = systemDirectory.Substring(0, 2);

        // SystemDirectory can sometimes be null or empty.
        // See: https://github.com/dotnet/runtime/issues/21430 and https://github.com/MatthewKing/DeviceId/issues/64
        if (string.IsNullOrEmpty(systemDirectory) || systemDirectory.Length < 2)
        {
            return null;
        }

        try
        {
            using var wmiConnection = new WmiConnection();

            foreach (var logicalDisk in wmiConnection.CreateQuery($"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID=\"{systemLogicalDiskDeviceId}\"}} WHERE ResultClass = Win32_DiskPartition"))
            {
                try
                {
                    if (logicalDisk.Class != "Win32_DiskPartition") continue;
                    if (logicalDisk["DeviceId"] is not string diskPartitionDeviceId) continue;
                    foreach (var diskPartitionAssociator in wmiConnection.CreateQuery(
                                 $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID=\"{diskPartitionDeviceId}\"}}"))
                    {
                        if (diskPartitionAssociator.Class == "Win32_DiskDrive"
                            && diskPartitionAssociator["SerialNumber"] is string diskDriveSerialNumber)
                        {
                            return diskDriveSerialNumber;
                        }
                    }
                }
                finally
                {
                    logicalDisk.Dispose();
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
