using DeviceId.Components;
using DeviceId.Encoders;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Encoders;

public class PlainTextDeviceIdComponentEncoderTests
{
    [Fact]
    public void Encode_ReturnsPlainTextComponentValue()
    {
        var encoder = new PlainTextDeviceIdComponentEncoder();

        var component = new DeviceIdComponent("Value");

        encoder.Encode(component).Should().Be("Value");
    }

    [Fact]
    public void Encode_ValueIsNull_TreatItAsAnEmptyString()
    {
        var encoder = new PlainTextDeviceIdComponentEncoder();
        var expected = encoder.Encode(new DeviceIdComponent(string.Empty));
        var actual = encoder.Encode(new DeviceIdComponent(default(string)));
        actual.Should().Be(expected);
    }
}
