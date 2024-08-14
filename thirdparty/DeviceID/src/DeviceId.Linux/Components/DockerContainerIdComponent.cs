using System.IO;
using System.Text.RegularExpressions;

namespace DeviceId.Linux.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the cgroup to read the Docker container id.
/// </summary>
public class DockerContainerIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// The cgroup file.
    /// </summary>
    private readonly string _cGroupFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="DockerContainerIdComponent"/> class.
    /// </summary>
    /// <param name="cGroupFile">The cgroup file.</param>
    public DockerContainerIdComponent(string cGroupFile)
    {
        _cGroupFile = cGroupFile;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        if (string.IsNullOrWhiteSpace(_cGroupFile) || !File.Exists(_cGroupFile))
        {
            return null;
        }

        using var file = File.OpenText(_cGroupFile);

        if (TryGetContainerId(file, out string containerId))
        {
            return containerId;
        }

        return null;
    }

    private static bool TryGetContainerId(StreamReader reader, out string containerId)
    {
        var regex = new Regex("(\\d)+\\:(.)+?\\:(/.+?)??(/docker[-/])([0-9a-f]+)");

        string line;
        while ((line = reader?.ReadLine()) != null)
        {
            var match = regex.Match(line);
            if (match.Success)
            {
                containerId = match.Groups[5].Value;
                return true;
            }
        }

        containerId = default;
        return false;
    }
}
