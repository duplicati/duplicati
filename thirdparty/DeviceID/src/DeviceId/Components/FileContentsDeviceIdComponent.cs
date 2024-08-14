using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace DeviceId.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that retrieves its value from a file.
/// </summary>
public class FileContentsDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// The paths to read.
    /// </summary>
    private readonly string[] _paths;

    /// <summary>
    /// Should the contents of the file be hashed?
    /// </summary>
    private readonly bool _hashContents;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTokenDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="path">The path of the file holding the component ID.</param>
    /// <param name="hashContents">A value determining whether the file contents should be hashed.</param>
    public FileContentsDeviceIdComponent(string path, bool hashContents = false)
        : this(new[] { path }, hashContents) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileTokenDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="paths">The paths to read. The first path that can be successfully read will be used.</param>
    /// <param name="hashContents">A value determining whether the file contents should be hashed.</param>
    public FileContentsDeviceIdComponent(IEnumerable<string> paths, bool hashContents = false)
    {
        _paths = paths.ToArray();
        _hashContents = hashContents;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        foreach (var path in _paths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                string contents;

                using (var file = File.OpenText(path))
                {
                    contents = file.ReadToEnd(); // File.ReadAllBytes() fails for special files such as /sys/class/dmi/id/product_uuid
                }

                contents = contents.Trim();

                if (!_hashContents)
                {
                    return contents;
                }

                using var hasher = MD5.Create();
                var hash = hasher.ComputeHash(Encoding.ASCII.GetBytes(contents));
                return BitConverter.ToString(hash).Replace("-", "").ToUpper();
            }
            catch
            {
                // Can fail if we have no permissions to access the file.
            }
        }

        return null;
    }
}

