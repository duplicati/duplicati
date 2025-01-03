namespace Duplicati.WebserverCore.Dto;
public sealed record GenerateCaptchaOutput(string Token, string? Answer, bool NoVisualChallenge);
