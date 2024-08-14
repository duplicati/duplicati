using System;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests;

public class DeviceIdBuilderTests
{
    [Fact]
    public void ToString_FormatterIsNull_ThrowsInvalidOperationException()
    {
        var builder = new DeviceIdBuilder();
        builder.Formatter = null;

        var action = () => builder.ToString();
        action.Should().Throw<InvalidOperationException>().WithMessage("The Formatter property must not be null in order for ToString to be called.");
    }
}
