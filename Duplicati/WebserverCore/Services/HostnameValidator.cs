using System.Net;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Implementation of hostname validation
/// </summary>
public class HostnameValidator : IHostnameValidator
{
    /// <summary>
    /// Hostnames that are always allowed
    /// </summary>
    private static readonly string[] DefaultAllowedHostnames = ["localhost", "127.0.0.1", "[::1]", "localhost.localdomain"];
    /// <summary>
    /// The list of allowed hostnames
    /// </summary>
    private readonly HashSet<string> m_allowedHostnames;
    /// <summary>
    /// A flag that indicates if any hostname is allowed
    /// </summary>
    private readonly bool m_allowAny;

    /// <summary>
    /// Creates a new instance of the <see cref="HostnameValidator"/> class
    /// </summary>
    /// <param name="allowedHostnames">The list of allowed hostnames</param>
    public HostnameValidator(IEnumerable<string> allowedHostnames)
    {
        m_allowedHostnames = (allowedHostnames ?? []).Concat(DefaultAllowedHostnames).ToHashSet(StringComparer.OrdinalIgnoreCase);
        m_allowAny = m_allowedHostnames.Contains("*");
    }

    /// <inheritdoc />
    public bool IsValidHostname(string hostname)
    {
        if (m_allowAny)
            return true;

        if (string.IsNullOrWhiteSpace(hostname))
            return false;

        if (m_allowedHostnames.Contains(hostname))
            return true;

        if (IPAddress.TryParse(hostname, out _))
            return true;

        return false;
    }
}
