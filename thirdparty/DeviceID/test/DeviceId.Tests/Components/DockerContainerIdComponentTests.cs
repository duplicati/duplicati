using DeviceId.Linux.Components;
using FluentAssertions;
using Xunit;

namespace DeviceId.Tests.Components;

public class DockerContainerIdComponentTests
{
    [InlineData("non-existing", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Linux_4.4.txt", "cde7c2bab394630a42d73dc610b9c57415dced996106665d427f6d0566594411")]
    [InlineData("Linux_4.8-4.13.txt", "afe96d48db6d2c19585572f986fc310c92421a3dac28310e847566fb82166013")]
    [InlineData("linux_nodocker.txt", null)]
    [Theory]
    public void TestGetValue(string file, string expected)
    {
        var component = new DockerContainerIdComponent(file);
        var componentValue = component.GetValue();

        componentValue.Should().Be(expected);
    }
}
