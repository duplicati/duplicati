using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using DeviceId.Components;
using DeviceId.Encoders;
using DeviceId.Formatters;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Formatters;

public class XmlDeviceIdFormatterTests
{
    [Fact]
    public void Constructor_EncoderIsNull_ThrowsArgumentNullException()
    {
        var action = () => new XmlDeviceIdFormatter(null);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDeviceId_ComponentsIsNull_ThrowsArgumentNullException()
    {
        var formatter = new XmlDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var action = () => formatter.GetDeviceId(null);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDeviceId_ComponentsIsEmpty_ReturnsEmptyXmlDocument()
    {
        var formatter = new XmlDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase));

        deviceId.Should().Be("<DeviceId />");
    }

    [Fact]
    public void GetDeviceId_ComponentsAreValid_ReturnsDeviceId()
    {
        var formatter = new XmlDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test1"] = new DeviceIdComponent("Test1"),
            ["Test2"] = new DeviceIdComponent("Test2"),
        });

        deviceId.Should().Be("<DeviceId><Component Name=\"Test1\" Value=\"e1b849f9631ffc1829b2e31402373e3c\" /><Component Name=\"Test2\" Value=\"c454552d52d55d3ef56408742887362b\" /></DeviceId>");
    }

    [Fact]
    public void GetDeviceId_ComponentReturnsNull_ReturnsDeviceId()
    {
        var formatter = new XmlDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test1"] = new DeviceIdComponent(default(string)),
        });

        deviceId.Should().Be("<DeviceId><Component Name=\"Test1\" Value=\"d41d8cd98f00b204e9800998ecf8427e\" /></DeviceId>");
    }
}
