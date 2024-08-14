using DeviceId.Internal.CommandExecutors;
using DeviceId.Linux.Components;
using FluentAssertions;
using Moq;
using Xunit;

namespace DeviceId.Tests.Components;

public class LinuxRootDriveSerialNumberDeviceIdComponentTests
{
    [Fact]
    public void GoogleCloudUbuntu1804Vm()
    {
        const string deviceName = "sda";

        const string lsblkOutput = @"
        {
            ""blockdevices"": [
                {""name"": ""loop0"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/core18/1705""},
                {""name"": ""loop1"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/google-cloud-sdk/127""},
                {""name"": ""loop2"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/core/8935""},
                {""name"": ""loop3"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/core/9066""},
                {""name"": ""loop4"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/google-cloud-sdk/128""},
                {""name"": ""sda"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null,
                    ""children"": [
                    {""name"": ""sda1"", ""fstype"": ""ext4"", ""label"": ""cloudimg-rootfs"", ""uuid"": ""e0c095ca-21b2-4a02-bbea-0dc95c07cfc8"", ""mountpoint"": ""/""},
                    {""name"": ""sda14"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null},
                    {""name"": ""sda15"", ""fstype"": ""vfat"", ""label"": ""UEFI"", ""uuid"": ""84B5-FED0"", ""mountpoint"": ""/boot/efi""}
                    ]
                }
            ]
        }";

        const string udevadmOutput = "E: ID_SERIAL=0Google_PersistentDisk_webserver";

        var componentValue = GetComponentValue(deviceName, lsblkOutput, udevadmOutput);

        componentValue.Should().Be("0Google_PersistentDisk_webserver");
    }

    [Fact]
    public void VirtualBoxVm()
    {
        const string deviceName = "sda";

        const string lsblkOutput = @"
        {
            ""blockdevices"": [
                {""name"": ""loop0"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/gnome-logs/45""},
                {""name"": ""loop1"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/gnome-characters/139""},
                {""name"": ""loop2"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/gnome-calculator/260""},
                {""name"": ""loop3"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/gnome-3-26-1604/74""},
                {""name"": ""loop4"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/core/6350""},
                {""name"": ""loop5"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/gtk-common-themes/818""},
                {""name"": ""loop6"", ""fstype"": ""squashfs"", ""label"": null, ""uuid"": null, ""mountpoint"": ""/snap/gnome-system-monitor/57""},
                {""name"": ""sda"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null,
                    ""children"": [
                    {""name"": ""sda1"", ""fstype"": ""LVM2_member"", ""label"": null, ""uuid"": ""Eqsoey-BADi-y8Sf-JFVM-UPyk-gXQE-uu9XCv"", ""mountpoint"": null,
                        ""children"": [
                            {""name"": ""ubuntu--vg-root"", ""fstype"": ""ext4"", ""label"": null, ""uuid"": ""c72aecd8-a254-4b1c-9a17-4257e89796d2"", ""mountpoint"": ""/""},
                            {""name"": ""ubuntu--vg-swap_1"", ""fstype"": ""swap"", ""label"": null, ""uuid"": ""93ff3926-f0df-470d-996b-af57b0991b06"", ""mountpoint"": ""[SWAP]""}
                        ]
                    }
                    ]
                },
                {""name"": ""sr0"", ""fstype"": ""iso9660"", ""label"": ""VBox_GAs_6.0.14"", ""uuid"": ""2019-10-10-18-52-14-12"", ""mountpoint"": ""/media/kevin/VBox_GAs_6.0.14""}
            ]
        }";

        const string udevadmOutput = "E: ID_SERIAL=VBOX_HARDDISK_VB5e245c00-220de489";

        var componentValue = GetComponentValue(deviceName, lsblkOutput, udevadmOutput);

        componentValue.Should().Be("VBOX_HARDDISK_VB5e245c00-220de489");
    }

    [Fact]
    public void BareMetalServer()
    {
        const string deviceName = "sda";

        const string lsblkOutput = @"
        {
            ""blockdevices"": [
                {""name"": ""sda"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null,
                    ""children"": [
                    {""name"": ""sda1"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null},
                    {""name"": ""sda2"", ""fstype"": ""linux_raid_member"", ""label"": ""s590:0"", ""uuid"": ""22aca146-b40c-6b41-265b-383c15d49106"", ""mountpoint"": null,
                        ""children"": [
                            {""name"": ""md0"", ""fstype"": ""xfs"", ""label"": null, ""uuid"": ""3d22ab9f-9ddb-43d5-82c7-a02725c1f4cb"", ""mountpoint"": ""/""}
                        ]
                    },
                    {""name"": ""sda3"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null},
                    {""name"": ""sda4"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null}
                    ]
                },
                {""name"": ""sdb"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null,
                    ""children"": [
                    {""name"": ""sdb1"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null},
                    {""name"": ""sdb2"", ""fstype"": ""linux_raid_member"", ""label"": ""s590:0"", ""uuid"": ""22aca146-b40c-6b41-265b-383c15d49106"", ""mountpoint"": null,
                        ""children"": [
                            {""name"": ""md0"", ""fstype"": ""xfs"", ""label"": null, ""uuid"": ""3d22ab9f-9ddb-43d5-82c7-a02725c1f4cb"", ""mountpoint"": ""/""}
                        ]
                    },
                    {""name"": ""sdb3"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null},
                    {""name"": ""sdb4"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null}
                    ]
                }
            ]
        }";

        const string udevadmOutput = "E: ID_SERIAL=WDC_WD4000FYYZ-01UL1B1_WD-WCC131942520";

        var componentValue = GetComponentValue(deviceName, lsblkOutput, udevadmOutput);

        componentValue.Should().Be("WDC_WD4000FYYZ-01UL1B1_WD-WCC131942520");
    }

    [Fact]
    public void DigitalOceanVmWithoutSerialId()
    {
        const string deviceName = "sda";

        const string lsblkOutput = @"
        {
            ""blockdevices"": [
                {""name"": ""vda"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null,
                    ""children"": [
                    {""name"": ""vda1"", ""fstype"": ""ext4"", ""label"": ""cloudimg-rootfs"", ""uuid"": ""bd5e9837-a464-48d5-a8f9-323fc9074bf3"", ""mountpoint"": ""/""},
                    {""name"": ""vda14"", ""fstype"": null, ""label"": null, ""uuid"": null, ""mountpoint"": null},
                    {""name"": ""vda15"", ""fstype"": ""vfat"", ""label"": ""UEFI"", ""uuid"": ""B99C-461D"", ""mountpoint"": ""/boot/efi""}
                    ]
                }
            ]
        }";

        const string udevadmOutput = "";

        var componentValue = GetComponentValue(deviceName, lsblkOutput, udevadmOutput);

        componentValue.Should().BeNull();
    }

    private static string GetComponentValue(string rootParentDeviceName, string lsblkOutput, string udevadmOutput)
    {
        var commandExecutorMock = new Mock<ICommandExecutor>();
        commandExecutorMock.Setup(x => x.Execute("lsblk -f -J")).Returns(lsblkOutput);
        commandExecutorMock.Setup(x => x.Execute($"udevadm info --query=all --name=/dev/{rootParentDeviceName} | grep ID_SERIAL=")).Returns(udevadmOutput);

        var component = new LinuxRootDriveSerialNumberDeviceIdComponent(commandExecutorMock.Object);

        var value = component.GetValue();

        return value;
    }
}
