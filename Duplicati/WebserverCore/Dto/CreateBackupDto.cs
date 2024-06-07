namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The create backup DTO
/// </summary>
/// <param name="ID">The backup ID</param>
/// <param name="Temporary">Whether the backup is temporary</param>
public sealed record CreateBackupDto(string? ID, bool Temporary);
