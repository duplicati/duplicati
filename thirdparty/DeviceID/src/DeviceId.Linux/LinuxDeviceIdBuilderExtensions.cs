using DeviceId.Components;
using DeviceId.Linux.Components;

namespace DeviceId;

/// <summary>
/// Extension methods for <see cref="LinuxDeviceIdBuilder"/>.
/// </summary>
public static class LinuxDeviceIdBuilderExtensions
{
    /// <summary>
    /// Adds the system drive serial number to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="LinuxDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="LinuxDeviceIdBuilder"/> instance.</returns>
    public static LinuxDeviceIdBuilder AddSystemDriveSerialNumber(this LinuxDeviceIdBuilder builder)
    {
        return builder.AddComponent("SystemDriveSerialNumber", new LinuxRootDriveSerialNumberDeviceIdComponent());
    }

    /// <summary>
    /// Adds the docker container id to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="LinuxDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="LinuxDeviceIdBuilder"/> instance.</returns>
    public static LinuxDeviceIdBuilder AddDockerContainerId(this LinuxDeviceIdBuilder builder)
    {
        return builder.AddComponent("DockerContainerId", new DockerContainerIdComponent("/proc/1/cgroup"));
    }

    /// <summary>
    /// Adds the machine ID (from /var/lib/dbus/machine-id or /etc/machine-id) to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="LinuxDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="LinuxDeviceIdBuilder"/> instance.</returns>
    public static LinuxDeviceIdBuilder AddMachineId(this LinuxDeviceIdBuilder builder)
    {
        return builder.AddComponent("MachineID", new FileContentsDeviceIdComponent(new[] { "/var/lib/dbus/machine-id", "/etc/machine-id" }, false));
    }

    /// <summary>
    /// Adds the product UUID (from /sys/class/dmi/id/product_uuid) to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="LinuxDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="LinuxDeviceIdBuilder"/> instance.</returns>
    public static LinuxDeviceIdBuilder AddProductUuid(this LinuxDeviceIdBuilder builder)
    {
        return builder.AddComponent("ProductUUID", new FileContentsDeviceIdComponent("/sys/class/dmi/id/product_uuid", false));
    }

    /// <summary>
    /// Adds the CPU info (from /proc/cpuinfo) to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="LinuxDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="LinuxDeviceIdBuilder"/> instance.</returns>
    public static LinuxDeviceIdBuilder AddCpuInfo(this LinuxDeviceIdBuilder builder)
    {
        return builder.AddComponent("CPUInfo", new FileContentsDeviceIdComponent("/proc/cpuinfo", true));
    }

    /// <summary>
    /// Adds the motherboard serial number (from /sys/class/dmi/id/board_serial) to the device identifier.
    /// </summary>
    /// <param name="builder">The <see cref="LinuxDeviceIdBuilder"/> to add the component to.</param>
    /// <returns>The <see cref="LinuxDeviceIdBuilder"/> instance.</returns>
    public static LinuxDeviceIdBuilder AddMotherboardSerialNumber(this LinuxDeviceIdBuilder builder)
    {
        return builder.AddComponent("MotherboardSerialNumber", new FileContentsDeviceIdComponent("/sys/class/dmi/id/board_serial", false));
    }
}
