// Copyright (C) 2024, The Duplicati Team
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
/// <param name="JWT">The JWT token for the machine</param>
/// <param name="ServerUrl">The URL for the remote server</param>
/// <param name="ServerCertificateKey">The certificate key for the remote server</param>
/// <param name="LocalEncryptionKey">The encryption key for the local settings</param>
public sealed record ClaimedClientData(
    string JWT,
    string ServerUrl,
    IEnumerable<ServerCertificate> ServerCertificates,
    string? LocalEncryptionKey
);

// TODO: Replace with a standard certificate type

/// <summary>
/// The server certificate for a machine
/// </summary>
/// <param name="MachineId">The machine identifier the key is valid for</param>
/// <param name="PublicKey">The certificate public key</param>
/// <param name="Obtained">The date the certificate was obtained</param>
/// <param name="Expiry">The expiry date of the certificate</param>
public sealed record ServerCertificate(
    string MachineId,
    string PublicKey,
    DateTimeOffset Obtained,
    DateTimeOffset Expiry
);
