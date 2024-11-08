using System.Globalization;

namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Produces system information.
/// </summary>
public interface ISystemInfoProvider
{
    /// <summary>
    /// Gets the system information.
    /// </summary>
    /// <param name="browserlanguage">The browser language.</param>
    /// <returns>The system information.</returns>
    Dto.SystemInfoDto GetSystemInfo(CultureInfo? browserlanguage);
}
