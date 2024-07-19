namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The schedule DTO
/// </summary>
public sealed record ScheduleDto
{
    /// <summary>
    /// The Schedule ID
    /// </summary>
    public required long ID { get; init; }
    /// <summary>
    /// The tags that this schedule affects
    /// </summary>
    public required string[] Tags { get; init; }
    /// <summary>
    /// The time this schedule is based on
    /// </summary>
    public required DateTime Time { get; init; }
    /// <summary>
    /// How often the backup is repeated
    /// </summary>
    public required string Repeat { get; init; }
    /// <summary>
    /// The time this schedule was last executed
    /// </summary>
    public required DateTime LastRun { get; init; }
    /// <summary>
    /// The rule that is parsed to figure out when to run this backup next time
    /// </summary>
    public required string Rule { get; init; }
    /// <summary>
    /// The days that the backup is allowed to run
    /// </summary>
    public required DayOfWeek[] AllowedDays { get; init; }
}
