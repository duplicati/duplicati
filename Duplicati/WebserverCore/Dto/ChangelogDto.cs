namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The changelog dto
/// </summary>
public sealed record ChangelogDto
{
    /// <summary>
    /// The version of the changelog
    /// </summary>
    public required string? Version { get; init; }
    /// <summary>
    /// The changelog
    /// </summary>
    public required string? Changelog { get; init; }
}
