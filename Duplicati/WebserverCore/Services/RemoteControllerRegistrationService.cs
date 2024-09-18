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

using System.Text.Json;
using Duplicati.Library.RemoteControl;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Internal configuration for remote control.
/// Persisted in the application settings as JSON.
/// </summary>
public sealed record RemoteControlConfig
{
    /// <summary>
    /// The token used for authentication.
    /// </summary>
    public required string Token { get; init; }
    /// <summary>
    /// The URL of the remote control server.
    /// </summary>
    public required string ServerUrl { get; init; }
    /// <summary>
    /// The trusted server certificates.
    /// </summary>
    public required IEnumerable<CertificateKey> ServerCertificates { get; init; }

    /// <summary>
    /// A server certificate.
    /// </summary>
    /// <param name="MachineId">The machine identifier</param>
    /// <param name="PublicKey">The machine public key</param>
    /// <param name="Expiry">The expiry date of the certificate</param>
    /// <param name="Obtained">The date the certificate was obtained</param>
    public sealed record CertificateKey(string MachineId, string PublicKey, DateTimeOffset Expiry, DateTimeOffset Obtained);
}

/// <summary>
/// Service for registering a machine with a remote controller.
/// </summary>
/// <param name="connection">The database connection</param>
/// <param name="httpClientFactory">The HTTP client factory</param>
public class RemoteControllerRegistrationService(Connection connection, IHttpClientFactory httpClientFactory) : IRemoteControllerRegistration
{
    /// <summary>
    /// The registration process controller this service is wrapping.
    /// </summary>
    private RegisterForRemote? _registerForRemote;
    /// <summary>
    /// The registration completion task.
    /// </summary>
    private Task<ClaimedClientData>? _registrationTask;

    /// <inheritdoc />
    public async Task<string> BeginRegisterMachine(string registrationUrl, CancellationToken cancellationToken)
    {
        if (_registerForRemote != null)
            throw new InvalidOperationException("Already registering");

        _registerForRemote = new RegisterForRemote(registrationUrl, httpClientFactory.CreateClient(), cancellationToken);
        var registrationData = await _registerForRemote.Register();

        _registrationTask = _registerForRemote.Claim();
        return registrationData.ClaimLink;
    }

    /// <inheritdoc />
    public void CancelRegisterMachine()
    {
        _registerForRemote?.Dispose();
        _registerForRemote = null;
    }

    /// <inheritdoc />
    public async Task<bool> EndRegisterMachine()
    {
        if (_registrationTask == null)
            throw new InvalidOperationException("Not registering");

        if (!_registrationTask.IsCompleted)
            return false;

        var data = await _registrationTask;

        connection.ApplicationSettings.RemoteControlConfig = JsonSerializer.Serialize(new RemoteControlConfig
        {
            Token = data.JWT,
            ServerCertificates = (data.ServerCertificates ?? Enumerable.Empty<ServerCertificate>())
                .Select(x => new RemoteControlConfig.CertificateKey(x.MachineId, x.PublicKey, x.Expiry, x.Obtained)),
            ServerUrl = data.ServerUrl
        });

        return true;
    }
}
