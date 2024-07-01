namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the commandline for exporting a backup
/// </summary>
/// <param name="Command">The commandline</param>
public sealed record ExportCommandlineDto(string? Command);
