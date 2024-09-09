namespace Duplicati.WebserverCore.Abstractions;

/// <summary>
/// Interface for hostname validation
/// </summary>
public interface IHostnameValidator
{
    /// <summary>
    /// Validates a hostname
    /// </summary>
    /// <param name="hostname">The hostname to validate</param>
    /// <returns>True if the hostname is valid, false otherwise</returns>
    bool IsValidHostname(string hostname);
}
