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
    /// The URL for getting new server certificates.
    /// </summary>
    public required string CertificateUrl { get; init; }
    /// <summary>
    /// The trusted server certificates.
    /// </summary>
    public required IEnumerable<MiniServerCertificate> ServerCertificates { get; init; }
}

/// <summary>
/// Service for registering a machine with a remote controller.
/// </summary>
/// <param name="connection">The database connection</param>
/// <param name="httpClientFactory">The HTTP client factory</param>
public class RemoteControllerRegistrationService(Connection connection, IHttpClientFactory httpClientFactory, IRemoteController remoteController) : IRemoteControllerRegistration
{
    /// <summary>
    /// The registration process controller this service is wrapping.
    /// </summary>
    private RegisterForRemote? _registerForRemote;

    /// <summary>
    /// Token for cancelling the operation.
    /// </summary>
    private CancellationTokenSource? _operationCancellation;

    /// <summary>
    /// The registration completion task.
    /// </summary>
    private Task? _registrationTask;

    /// <summary>
    /// A flag indicating if the machine is currently registering
    /// </summary>
    public bool IsRegistering => _registerForRemote != null;

    /// <summary>
    /// A flag indicating if the machine is currently registering
    /// </summary>
    public bool IsClaiming => _registerForRemote != null;

    /// <summary>
    /// The URL to register the machine with, if registring
    /// </summary>
    public string? RegistrationUrl { get; private set; }

    /// <inheritdoc />
    public Task RegisterMachine(string registrationUrl)
    {
        if (_registerForRemote != null)
            throw new InvalidOperationException("Already registering");

        if (!string.IsNullOrWhiteSpace(connection.ApplicationSettings.RemoteControlConfig))
            throw new InvalidOperationException("An existing configuration exists, delete it first");

        _operationCancellation = new CancellationTokenSource();

        return _registrationTask = Task.Run(async () =>
        {
            _registerForRemote = new RegisterForRemote(registrationUrl, httpClientFactory.CreateClient(), _operationCancellation.Token);
            var data = await _registerForRemote.Register(maxRetries: 3, retryInterval: TimeSpan.FromSeconds(5));

            // Make the link visible to outside
            if (data.RegistrationData != null)
                RegistrationUrl = data.RegistrationData.ClaimLink;

            // Grab the claim data once it is returned
            var claimData = await _registerForRemote.Claim();
            connection.ApplicationSettings.RemoteControlConfig = JsonSerializer.Serialize(new RemoteControlConfig
            {
                Token = claimData.JWT,
                ServerCertificates = claimData.ServerCertificates,
                CertificateUrl = claimData.CertificateUrl,
                ServerUrl = claimData.ServerUrl
            });

            // Automatically connect after we are registered
            if (remoteController.CanEnable)
                remoteController.Enable();

        });
    }

    /// </inheritdoc>
    public Task WaitForRegistration()
    {
        if (_registrationTask == null)
            throw new InvalidOperationException("Not registering");

        return _registrationTask;
    }

    /// <inheritdoc />
    public void CancelRegisterMachine()
    {
        _operationCancellation?.Cancel();
        _registerForRemote?.Dispose();
        _operationCancellation?.Dispose();
        _registerForRemote = null;
        _operationCancellation = null;
    }
}
