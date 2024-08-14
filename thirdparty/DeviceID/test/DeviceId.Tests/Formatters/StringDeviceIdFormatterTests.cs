using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using DeviceId.Components;
using DeviceId.Encoders;
using DeviceId.Formatters;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Formatters;

public class StringDeviceIdFormatterTests
{
    [Fact]
    public void Constructor_EncoderIsNull_ThrowsArgumentNullException()
    {
        var action = () => new StringDeviceIdFormatter(null);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDeviceId_ComponentsIsNull_ThrowsArgumentNullException()
    {
        var formatter = new StringDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var action = () => formatter.GetDeviceId(null);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetDeviceId_ComponentsIsEmpty_ReturnsEmptyString()
    {
        var formatter = new StringDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase));

        deviceId.Should().Be(string.Empty);
    }

    [Fact]
    public void GetDeviceId_ComponentsAreValid_ReturnsDeviceId()
    {
        var formatter = new StringDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test1"] = new DeviceIdComponent("Test1"),
            ["Test2"] = new DeviceIdComponent("Test2"),
        });

        deviceId.Should().Be("e1b849f9631ffc1829b2e31402373e3c.c454552d52d55d3ef56408742887362b");
    }

    [Fact]
    public void GetDeviceId_ComponentReturnsNull_ReturnsDeviceId()
    {
        var formatter = new StringDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()));

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test1"] = new DeviceIdComponent(default(string)),
        });

        deviceId.Should().Be("d41d8cd98f00b204e9800998ecf8427e");
    }

    [Fact]
    public void NoDelimiter()
    {
        var formatter = new StringDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()), null);

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test1"] = new DeviceIdComponent("Test1"),
            ["Test2"] = new DeviceIdComponent("Test2"),
        });

        deviceId.Should().Be("e1b849f9631ffc1829b2e31402373e3cc454552d52d55d3ef56408742887362b");
    }

    [Fact]
    public void CustomDelimiter()
    {
        var formatter = new StringDeviceIdFormatter(new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder()), "+");

        var deviceId = formatter.GetDeviceId(new Dictionary<string, IDeviceIdComponent>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test1"] = new DeviceIdComponent("Test1"),
            ["Test2"] = new DeviceIdComponent("Test2"),
        });

        deviceId.Should().Be("e1b849f9631ffc1829b2e31402373e3c+c454552d52d55d3ef56408742887362b");
    }
}
