namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The web module output DTO
/// </summary>
/// <param name="Status">The status</param>
/// <param name="Result">The result</param>
public record WebModuleOutputDto(string Status, IDictionary<string, string> Result);
