using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using DeviceId.Internal.CommandExecutors;

namespace DeviceId.Linux.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the root drive's serial number.
/// </summary>
public class LinuxRootDriveSerialNumberDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// Command executor.
    /// </summary>
    private readonly ICommandExecutor _commandExecutor;

    /// <summary>
    /// JSON serializer options.
    /// </summary>
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxRootDriveSerialNumberDeviceIdComponent"/> class.
    /// </summary>
    public LinuxRootDriveSerialNumberDeviceIdComponent()
        : this(CommandExecutor.Bash) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="LinuxRootDriveSerialNumberDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="commandExecutor">The command executor to use.</param>
    internal LinuxRootDriveSerialNumberDeviceIdComponent(ICommandExecutor commandExecutor)
    {
        _commandExecutor = commandExecutor;
        _jsonSerializerOptions = new JsonSerializerOptions();
        _jsonSerializerOptions.PropertyNameCaseInsensitive = true;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var outputJson = _commandExecutor.Execute("lsblk -f -J");
        var output = JsonSerializer.Deserialize<LsblkOutput>(outputJson, _jsonSerializerOptions);

        var device = FindRootParent(output);
        if (device == null)
        {
            return null;
        }

        var udevInfo = _commandExecutor.Execute($"udevadm info --query=all --name=/dev/{device.Name} | grep ID_SERIAL=");
        if (udevInfo == null)
        {
            return null;
        }

        var components = udevInfo.Split('=');
        if (components.Length < 2)
        {
            return null;
        }

        return components[1];
    }

    private static LsblkDevice FindRootParent(LsblkOutput devices)
    {
        return devices.BlockDevices.FirstOrDefault(x => DeviceContainsRoot(x));
    }

    private static bool DeviceContainsRoot(LsblkDevice device)
    {
        if (device.MountPoint == "/")
        {
            return true;
        }

        if (device.Children == null || device.Children.Count == 0)
        {
            return false;
        }

        return device.Children.Any(x => DeviceContainsRoot(x));
    }

    internal sealed class LsblkOutput
    {
        public List<LsblkDevice> BlockDevices { get; set; } = new List<LsblkDevice>();
    }

    internal sealed class LsblkDevice
    {
        public string Name { get; set; } = string.Empty;
        public string MountPoint { get; set; } = string.Empty;
        public List<LsblkDevice> Children { get; set; } = new List<LsblkDevice>();
    }
}
