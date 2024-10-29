namespace Duplicati.WebserverCore.Dto;

/// <summary>
/// Represents the input for updating a certificate.
/// </summary>
/// <param name="Certificate">The base64 encoded certificate.</param>
/// <param name="Password">The password for the certificate.</param>
public sealed record UpdateCertificateInputDto(
    string Certificate,
    string Password
);
