namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// DTO for the repair endpoint
/// </summary>
/// <param name="only_paths">Whether to only repair paths</param>
/// <param name="time">The time to repair to</param>
/// <param name="version">The version to repair to</param>
/// <param name="paths">The paths to repair</param>
public sealed record RepairInputDto(bool? only_paths, string? time, string? version, string[]? paths);
