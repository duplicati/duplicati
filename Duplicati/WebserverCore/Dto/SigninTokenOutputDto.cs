namespace Duplicati.WebserverCore.Dto;
/// <summary>
/// Represents the output for a signin operation.
/// </summary>
/// <param name="Token">The token.</param>
public sealed record SigninTokenOutputDto(string Token);