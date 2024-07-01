namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The DTO for checking if the database is used elsewhere
/// </summary>
/// <param name="inuse">Whether the database is used elsewhere</param>
public sealed record IsDbUsedElsewhereDto(bool inuse);
