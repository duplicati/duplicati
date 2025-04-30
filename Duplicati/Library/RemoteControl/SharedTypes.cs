// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

namespace Duplicati.Library.RemoteControl;

/// <summary>
/// Data returned when registering a client
/// </summary>
/// <param name="ClaimLink">The link the user needs to visit to claim the machine</param>
/// <param name="StatusLink">The link the process can use to check for the claim status</param>
/// <param name="RetrySeconds">The minimum number of seconds between calls to the status link</param>
/// <param name="MaxRetries">The maximum number of calls before the status link is invalid</param>
/// <param name="MaxLifetimeSeconds">The maximum number of seconds the registration is valid</param>
public sealed record RegisterClientData(
    string ClaimLink,
    string StatusLink,
    int RetrySeconds,
    int MaxRetries,
    int MaxLifetimeSeconds
);

/// <summary>
/// Data returned when the machine is claimed
/// </summary>
/// <param name="Success">True if the claim was successful</param>
/// <param name="StatusMessage">The status message for the claim</param>
/// <param name="JWT">The JWT token for the machine</param>
/// <param name="ServerUrl">The URL for the remote server</param>
/// <param name="CertificateUrl">The URL for getting new server certificates</param>
/// <param name="ServerCertificates">The certificates for the remote server</param>
/// <param name="LocalEncryptionKey">The encryption key for the local settings</param>
public sealed record ClaimedClientData(
    string JWT,
    string ServerUrl,
    string CertificateUrl,
    IEnumerable<MiniServerCertificate> ServerCertificates,
    string? LocalEncryptionKey
);

/// <summary>
/// The server certificate for a machine.
/// This data is serialized to various files, and chosen instead of X509 certificates.
/// If we find a non-complex certificate format, we should switch to that.
/// </summary>
/// <param name="PublicKeyHash">The hash of the certificate public key</param>
/// <param name="PublicKey">The certificate public key</param>
/// <param name="Obtained">The date the certificate was obtained</param>
/// <param name="Expiry">The expiry date of the certificate</param>
public sealed record MiniServerCertificate(
    string PublicKeyHash,
    string PublicKey,
    DateTimeOffset Obtained,
    DateTimeOffset Expiry
)
{
    /// <summary>
    /// Checks if the certificate has expired
    /// </summary>
    /// <returns><c>true</c> if the certificate has expired; otherwise, <c>false</c></returns>
    public bool HasExpired() => DateTimeOffset.UtcNow > Expiry;
}