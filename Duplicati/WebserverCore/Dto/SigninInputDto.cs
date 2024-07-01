namespace Duplicati.WebserverCore.Dto;
/// <summary>
/// Represents the input for a signin operation.
/// </summary>
/// <param name="SigninToken">The signin token.</param>
/// <param name="RememberMe">Whether to stay signed in.</param>
public sealed record SigninInputDto(string SigninToken, bool? RememberMe);