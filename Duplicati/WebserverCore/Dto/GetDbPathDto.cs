namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the response of the RemoteOperation GetDbPath endpoint.
/// </summary>
/// <param name="Exists">Whether the database path exists.</param>
/// <param name="Path">The database path.</param>
public sealed record GetDbPathDto(bool Exists, string Path);
