using System.Diagnostics;
using DeviceId.Windows.Mmi.Components;
using DeviceId.Windows.Wmi.Components;
using DeviceId.Windows.WmiLight.Components;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Components;

public class WmiAndMmiDriveSerialNumberPerfTests
{
    [Fact]
    public void WmiDriveSerialNumberPerfShouldBeAcceptable()
    {
        var sw = Stopwatch.StartNew();
        var deviceId = new WmiSystemDriveSerialNumberDeviceIdComponent().GetValue();
        sw.Stop();

        deviceId.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(500);
    }

    [Fact]
    public void MmiDriveSerialNumberPerfShouldBeAcceptable()
    {
        var sw = Stopwatch.StartNew();
        var deviceId = new MmiSystemDriveSerialNumberDeviceIdComponent().GetValue();
        sw.Stop();

        deviceId.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(500);
    }

    [Fact]
    public void WmiLightDriveSerialNumberPerfShouldBeAcceptable()
    {
        var sw = Stopwatch.StartNew();
        var deviceId = new WmiLightSystemDriveSerialNumberDeviceIdComponent().GetValue();
        sw.Stop();

        deviceId.Should().NotBeEmpty();
        sw.ElapsedMilliseconds.Should().BeLessOrEqualTo(500);
    }
}
