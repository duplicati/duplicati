namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the arguments for exporting a backup arguments
/// </summary>
/// <param name="Backend">The backend</param>
/// <param name="Arguments">The arguments</param>
/// <param name="Options">The options</param>
public sealed record ExportArgsOnlyDto(string Backend, IEnumerable<string> Arguments, IEnumerable<string> Options);
