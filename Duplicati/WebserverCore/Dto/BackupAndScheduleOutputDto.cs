
namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The backup and schedule DTO
/// </summary>
public sealed record BackupAndScheduleOutputDto
{
    /// <summary>
    /// The backup DTO
    /// </summary>
    public required BackupDto Backup { get; init; }
    /// <summary>
    /// The schedule DTO
    /// </summary>
    public required ScheduleDto? Schedule { get; init; }
}
