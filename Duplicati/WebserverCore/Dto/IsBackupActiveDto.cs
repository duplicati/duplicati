namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the status of the backup
/// </summary>
/// <param name="Status">The status of the backup</param>
/// <param name="Active">Whether the backup is active</param>
public sealed record IsBackupActiveDto(string Status, bool Active);
