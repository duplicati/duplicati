namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// The required data for starting a registration
/// </summary>
/// <param name="RegistrationUrl">The URL to register the machine with</param>
public sealed record StartRegistrationInput(string RegistrationUrl);
