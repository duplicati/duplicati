namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The task started dto
/// </summary>
/// <param name="Status">The status</param>
/// <param name="ID">The ID</param>
public sealed record TaskStartedDto(string Status, long ID);
