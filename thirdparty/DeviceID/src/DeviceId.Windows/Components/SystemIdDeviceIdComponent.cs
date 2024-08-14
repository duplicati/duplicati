#if NET5_0_OR_GREATER && WINDOWS10_0_17763_0_OR_GREATER

using System.Runtime.InteropServices.WindowsRuntime;
using Windows.System.Profile;

namespace DeviceId.Windows.Components;

/// <summary>
/// An implementation of <see cref="IDeviceIdComponent"/> that uses the SystemIdentification value.
/// </summary>
/// <remarks>
/// See: https://devblogs.microsoft.com/oldnewthing/20180131-00/?p=97945
/// </remarks>
internal class SystemIdDeviceIdComponent : IDeviceIdComponent
{
    /// <summary>
    /// The byte array encoder to use.
    /// </summary>
    private readonly IByteArrayEncoder _byteArrayEncoder;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemIdDeviceIdComponent"/> class.
    /// </summary>
    /// <param name="byteArrayEncoder">The byte array encoder to use.</param>
    public SystemIdDeviceIdComponent(IByteArrayEncoder byteArrayEncoder)
    {
        _byteArrayEncoder = byteArrayEncoder;
    }

    /// <summary>
    /// Gets the component value.
    /// </summary>
    /// <returns>The component value.</returns>
    public string GetValue()
    {
        var systemId = SystemIdentification.GetSystemIdForPublisher();
        if (systemId == null)
        {
            return null;
        }

        var systemIdBytes = systemId.Id.ToArray();
        return _byteArrayEncoder.Encode(systemIdBytes);
    }
}

#endif
