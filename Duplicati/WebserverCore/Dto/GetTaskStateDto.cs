namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The get task state DTO
/// </summary>
/// <param name="Status">The status</param>
/// <param name="ID">The ID</param>
/// <param name="ErrorMessage">The error message</param>
/// <param name="Exception">The exception</param>
public record GetTaskStateDto(string Status, long ID, string? ErrorMessage = null, string? Exception = null);
