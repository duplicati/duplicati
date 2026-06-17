namespace Duplicati.WebserverCore.Dto;

public sealed record RestoreTaskConfigElementDto
{
    public required string BackupId { get; init; }
    public required string Name { get; init; }
    public required string TargetURLDisplay { get; init; }
    public required IDictionary<string, string?> Metadata { get; init; }
}
