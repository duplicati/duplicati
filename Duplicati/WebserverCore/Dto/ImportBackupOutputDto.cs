namespace Duplicati.WebserverCore.Dto;
/// <summary>
/// The import backup output DTO
/// </summary>
/// <param name="Id">The id if the backup was created</param>
/// <param name="data">The backup configuration if the backup was just loaded</param>
public sealed record ImportBackupOutputDto
(
    string? Id,
    object? data
);