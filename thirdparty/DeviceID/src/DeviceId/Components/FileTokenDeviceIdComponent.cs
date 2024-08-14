using System;
using System.IO;
using System.Text;

namespace DeviceId.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that retrieves its value from a file.
/// </summary>
/// <remarks>
/// If the file exists, the contents of that file will be used as the component value.
/// If the file does not exist, a new file will be created and populated with a new GUID,
/// which will be used as the component value.
/// </remarks>
public class FileTokenDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// The path where the token will be stored.
    /// </summary>
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTokenDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="path">The path where the component will be stored.</param>
    public FileTokenDeviceIdComponent(string path)
    {
        _path = path;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        if (File.Exists(_path))
        {
            try
            {
                var bytes = File.ReadAllBytes(_path);
                var value = Encoding.ASCII.GetString(bytes);
                return value;
            }
            catch { }
        }
        else
        {
            try
            {
                var value = Guid.NewGuid().ToString().ToUpper();
                var bytes = Encoding.ASCII.GetBytes(value);
                File.WriteAllBytes(_path, bytes);
                return value;
            }
            catch { }
        }

        return null;
    }
}
