using DeviceId.Internal;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Internal;

public class MacAddressFormatterTests
{
    [Fact]
    public void FormatMac_NonMac()
    {
        var input = "Try me";
        var result = MacAddressFormatter.FormatMacAddress(input);
        result.Should().BeEquivalentTo(input, "Non MAC addresses are not formatted");
    }

    [Fact]
    public void FormatMac_48BitMac()
    {
        var input = "AABBCCDDEEFF";
        var result = MacAddressFormatter.FormatMacAddress(input);
        result.Should().BeEquivalentTo("AA:BB:CC:DD:EE:FF", "MAC address should be formatted");
    }

    [Fact]
    public void FormatMac_64BitMac()
    {
        var input = "AABBCCDDEEFF0011";
        var result = MacAddressFormatter.FormatMacAddress(input);
        result.Should().BeEquivalentTo("AA:BB:CC:DD:EE:FF:00:11", "MAC address should be formatted");
    }
}
