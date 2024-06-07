namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The commandline task started DTO
/// </summary>
/// <param name="Status">The status</param>
/// <param name="ID">The ID</param>
public record CommandlineTaskStartedDto(string Status, string ID);
