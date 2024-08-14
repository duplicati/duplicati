using System;
using System.Security.Cryptography;
using DeviceId.Components;
using DeviceId.Encoders;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Encoders;

public class HashDeviceIdComponentEncoderTests
{
    [Fact]
    public void Constructor_ByteArrayHasherIsNull_ThrowsArgumentNullException()
    {
        var action = () => new HashDeviceIdComponentEncoder(null as IByteArrayHasher, new HexByteArrayEncoder());

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ByteArrayEncoderIsNull_ThrowsArgumentNullException()
    {
        var action = () => new HashDeviceIdComponentEncoder(() => MD5.Create(), null);

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_HashAlgorithmIsNull_ThrowsArgumentNullException()
    {
        var action = () => new HashDeviceIdComponentEncoder(null as Func<HashAlgorithm>, new HexByteArrayEncoder());

        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Encode_SHA256_Hex_ReturnsHashedComponentValue()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => SHA256.Create(), new HexByteArrayEncoder());

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("8e37953d23daca5ff01b8282c33f4e0a2152f1d1885f94c06418617e3ee1d24e");
    }

    [Fact]
    public void Encode_SHA256_Base64_ReturnsHashedComponentValue()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => SHA256.Create(), new Base64ByteArrayEncoder());

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("jjeVPSPayl/wG4KCwz9OCiFS8dGIX5TAZBhhfj7h0k4=");
    }

    [Fact]
    public void Encode_SHA256_Base64Url_ReturnsHashedComponentValue()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => SHA256.Create(), new Base64UrlByteArrayEncoder());

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("jjeVPSPayl_wG4KCwz9OCiFS8dGIX5TAZBhhfj7h0k4");
    }

    [Fact]
    public void Encode_MD5_Hex_ReturnsHashedComponentValue()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => MD5.Create(), new HexByteArrayEncoder());

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("689202409e48743b914713f96d93947c");
    }

    [Fact]
    public void Encode_MD5_Base64_ReturnsHashedComponentValue()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => MD5.Create(), new Base64ByteArrayEncoder());

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("aJICQJ5IdDuRRxP5bZOUfA==");
    }

    [Fact]
    public void Encode_MD5_Base64Url_ReturnsHashedComponentValue()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => MD5.Create(), new Base64UrlByteArrayEncoder());

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("aJICQJ5IdDuRRxP5bZOUfA");
    }

    [Fact]
    public void Encode_ValueIsNull_TreatItAsAnEmptyString()
    {
        var encoder = new HashDeviceIdComponentEncoder(() => MD5.Create(), new Base64UrlByteArrayEncoder());
        var expected = encoder.Encode(new DeviceIdComponent(string.Empty));
        var actual = encoder.Encode(new DeviceIdComponent(default(string)));
        actual.Should().Be(expected);
    }
}
