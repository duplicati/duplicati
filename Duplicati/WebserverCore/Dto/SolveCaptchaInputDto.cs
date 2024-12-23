namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The solve captcha input dto
/// </summary>
/// <param name="target">The target</param>
/// <param name="answer">The answer</param>
public record SolveCaptchaInputDto(string target, string? answer);