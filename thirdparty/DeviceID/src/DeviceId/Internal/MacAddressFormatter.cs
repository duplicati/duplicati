using System.Linq;

namespace DeviceId.Internal;

/// <summary>
/// Provides functionality to format MAC addresses.
/// </summary>
internal static class MacAddressFormatter
{
    /// <summary>
    /// Formats the specified MAC address.
    /// </summary>
    /// <param name="input">The MAC address to format.</param>
    /// <returns>The formatted MAC address.</returns>
    public static string FormatMacAddress(string input)
    {
        // Check if this can be a hex formatted EUI-48 or EUI-64 identifier.
        if (input.Length != 12 && input.Length != 16)
        {
            return input;
        }

        // Chop up input in 2 character chunks.
        const int partSize = 2;
        var parts = Enumerable.Range(0, input.Length / partSize).Select(x => input.Substring(x * partSize, partSize));

        // Put the parts in the AA:BB:CC format.
        var result = string.Join(":", parts.ToArray());

        return result;
    }
}
