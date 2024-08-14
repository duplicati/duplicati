using DeviceId.Components;
using DeviceId.Encoders;
using DeviceId.Formatters;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests;

public class DeviceIdManagerTests
{
    [Fact]
    public void NoBuilderSpecified()
    {
        var manager = new DeviceIdManager();

        manager.GetDeviceId().Should().BeNull();
    }

    [Fact]
    public void InvalidBuilderSpecified()
    {
        var formatter = new StringDeviceIdFormatter(new PlainTextDeviceIdComponentEncoder(), string.Empty);

        var manager = new DeviceIdManager()
            .AddBuilder(1, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("C1V"))
                .AddComponent("C2", new DeviceIdComponent("C2V")))
            .AddBuilder(2, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("C1V"))
                .AddComponent("C3", new DeviceIdComponent("C3V")));

        manager.GetDeviceId(-1).Should().BeNull();
    }

    [Fact]
    public void GetCurrentValue()
    {
        var formatter = new StringDeviceIdFormatter(new PlainTextDeviceIdComponentEncoder(), string.Empty);

        var manager = new DeviceIdManager()
            .AddBuilder(1, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C2", new DeviceIdComponent("B")))
            .AddBuilder(2, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C3", new DeviceIdComponent("C")));

        manager.GetDeviceId().Should().Be("$2$AC");
    }

    [Fact]
    public void GetPreviousValue()
    {
        var formatter = new StringDeviceIdFormatter(new PlainTextDeviceIdComponentEncoder(), string.Empty);

        var manager = new DeviceIdManager()
            .AddBuilder(1, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C2", new DeviceIdComponent("B")))
            .AddBuilder(2, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C3", new DeviceIdComponent("C")));

        manager.GetDeviceId(1).Should().Be("$1$AB");
    }

    [Fact]
    public void Validation()
    {
        var formatter = new StringDeviceIdFormatter(new PlainTextDeviceIdComponentEncoder(), string.Empty);

        var manager = new DeviceIdManager()
            .AddBuilder(1, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C2", new DeviceIdComponent("B")))
            .AddBuilder(2, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C3", new DeviceIdComponent("C")));

        manager.Validate("$1$AB").Should().BeTrue();
        manager.Validate("$2$AB").Should().BeFalse();

        manager.Validate("$2$AC").Should().BeTrue();
        manager.Validate("$1$AC").Should().BeFalse();
    }

    [Fact]
    public void ValidationWithoutVersionEncoded()
    {
        var formatter = new StringDeviceIdFormatter(new PlainTextDeviceIdComponentEncoder(), string.Empty);

        var manager = new DeviceIdManager()
            .AddBuilder(1, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C2", new DeviceIdComponent("B")))
            .AddBuilder(2, builder => builder
                .UseFormatter(formatter)
                .AddComponent("C1", new DeviceIdComponent("A"))
                .AddComponent("C3", new DeviceIdComponent("C")));

        // If we can't decode the version, try to use the first builder.

        manager.Validate("AB").Should().BeTrue();
        manager.Validate("AC").Should().BeFalse();
    }
}
