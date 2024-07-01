namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the input for a login operation.
/// </summary>
/// <param name="Password">The password.</param>
/// <param name="RememberMe">Whether to stay signed in.</param>
public sealed record LoginInputDto(string Password, bool? RememberMe);
