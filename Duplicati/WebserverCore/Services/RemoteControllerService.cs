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

using System.Net.Http.Headers;
using Duplicati.Library.RemoteControl;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Controller for toggling remote control.
/// </summary>
/// <param name="connection">The connection to the database</param>
/// <param name="httpClientFactory">The HTTP client factory</param>
/// <param name="jwtTokenProvider">The JWT token provider</param>
public class RemoteControllerService(Connection connection, IHttpClientFactory httpClientFactory, IJWTTokenProvider jwtTokenProvider) : IRemoteController
{
    /// <summary>
    /// Gets a value indicating whether remote control is enabled.
    /// </summary>
    public bool IsEnabled => _keepRemoteConnection != null;

    /// <summary>
    /// Gets a value indicating whether remote control can be enabled.
    /// </summary>
    public bool CanEnable => string.IsNullOrWhiteSpace(connection.ApplicationSettings.RemoteControlConfig);

    /// <summary>
    /// Gets a value indicating whether the remote control is connected.
    /// </summary>
    public bool Connected
    {
        get
        {
            if (!IsEnabled || _keepRemoteConnection == null)
                return false;

            return _keepRemoteConnection?.State == KeepRemoteConnection.ConnectionState.Authenticated;
        }
    }

    /// <summary>
    /// The remote control connection handler that this service is wrapping.
    /// </summary>
    private KeepRemoteConnection? _keepRemoteConnection;

    /// </inheritdoc>
    public void Enable()
    {
        if (IsEnabled)
            return;

        if (!CanEnable)
            throw new InvalidOperationException("Remote control is not configured");

        var config = JsonConvert.DeserializeObject<RemoteControlConfig>(connection.ApplicationSettings.RemoteControlConfig)
            ?? throw new InvalidOperationException("Invalid remote control configuration");

        _keepRemoteConnection = KeepRemoteConnection.CreateRemoteListener(
            config.ServerUrl,
            config.Token,
            config.ServerCertificates.Select(x => new ServerCertificate(x.MachineId, x.PublicKey, x.Expiry, x.Obtained)).ToArray(),
            CancellationToken.None,
            ReKey,
            OnMessage
        );
    }

    /// <summary>
    /// Re-keys the remote control connection.
    /// </summary>
    /// <param name="data">The data to re-key with</param>
    /// <returns>An awaitable task</returns>
    private Task ReKey(ClaimedClientData data)
    {
        connection.ApplicationSettings.RemoteControlConfig = JsonConvert.SerializeObject(new RemoteControlConfig
        {
            Token = data.JWT,
            ServerCertificates = data.ServerCertificates.Select(x => new RemoteControlConfig.CertificateKey(x.MachineId, x.PublicKey, x.Expiry, x.Obtained)),
            ServerUrl = data.ServerUrl
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handles a command message.
    /// </summary>
    /// <param name="commandMessage">The command message to handle</param>
    /// <returns>An awaitable task</returns>
    private async Task OnMessage(KeepRemoteConnection.CommandMessage commandMessage)
    {
        using var httpClient = httpClientFactory.CreateClient();
        var token = jwtTokenProvider.CreateAccessToken("remote-control", "remote-control");

        httpClient.BaseAddress = new Uri($"http{(connection.ApplicationSettings.ServerSSLCertificate == null ? "" : "s")}://127.0.0.1:{connection.ApplicationSettings.LastWebserverPort}");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await commandMessage.Handle(httpClient);
    }

    /// </inheritdoc>
    public void Disable()
    {
        _keepRemoteConnection?.Dispose();
        _keepRemoteConnection = null;
    }

    /// </inheritdoc>
    public void DeleteRegistration()
    {
        Disable();
        connection.ApplicationSettings.RemoteControlConfig = string.Empty;
    }
}
