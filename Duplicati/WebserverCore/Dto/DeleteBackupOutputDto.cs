namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The delete backup output DTO
/// </summary>
/// <param name="Status">The status</param>
/// <param name="Reason">The reason</param>
/// <param name="ID">The ID</param>
public record DeleteBackupOutputDto(string Status, string? Reason, long? ID);
