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

using System.Net.Http.Headers;
using Duplicati.Library.RemoteControl;
using Duplicati.Library.RestAPI;
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
    public bool IsEnabled => connection.ApplicationSettings.RemoteControlEnabled;

    /// <summary>
    /// Gets a value indicating whether remote control can be enabled.
    /// </summary>
    public bool CanEnable => !string.IsNullOrWhiteSpace(connection.ApplicationSettings.RemoteControlConfig);

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
        if (_keepRemoteConnection != null)
            return;

        if (!CanEnable)
            throw new InvalidOperationException("Remote control is not configured");

        var config = JsonConvert.DeserializeObject<RemoteControlConfig>(connection.ApplicationSettings.RemoteControlConfig ?? string.Empty)
            ?? throw new InvalidOperationException("Invalid remote control configuration");

        _keepRemoteConnection = KeepRemoteConnection.CreateRemoteListener(
            config.ServerUrl,
            config.Token,
            config.CertificateUrl,
            config.ServerCertificates,
            CancellationToken.None,
            ReKey,
            OnMessage
        );

        connection.ApplicationSettings.RemoteControlEnabled = true;
    }

    /// <summary>
    /// Re-keys the remote control connection.
    /// </summary>
    /// <param name="data">The data to re-key with</param>
    /// <returns>An awaitable task</returns>
    private Task ReKey(ClaimedClientData data)
    {
        var oldCerts = connection.ApplicationSettings.RemoteControlConfig == null
            ? null
            : JsonConvert.DeserializeObject<RemoteControlConfig>(connection.ApplicationSettings.RemoteControlConfig)?.ServerCertificates;

        connection.ApplicationSettings.RemoteControlConfig = JsonConvert.SerializeObject(new RemoteControlConfig
        {
            Token = data.JWT,
            ServerCertificates = data.ServerCertificates ?? oldCerts ?? [],
            ServerUrl = data.ServerUrl,
            CertificateUrl = data.CertificateUrl
        });

        if (!FIXMEGlobal.SettingsEncryptionKeyProvidedExternally)
        {
            // TODO: Implement changing the encryption key
            // if (!string.IsNullOrWhiteSpace(data.LocalEncryptionKey) && data.LocalEncryptionKey != connection.ApplicationSettings.SettingsEncryptionKey)
            //     connection.ChangeDbKey(keydata.LocalEncryptionKey);
        }

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
        var token = jwtTokenProvider.CreateAccessToken("remote-control", jwtTokenProvider.TemporaryFamilyId, TimeSpan.FromMinutes(2));

        httpClient.BaseAddress = new Uri($"{(connection.ApplicationSettings.UseHTTPS ? "https" : "http")}://127.0.0.1:{connection.ApplicationSettings.LastWebserverPort}");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        await commandMessage.Handle(httpClient);
    }

    /// </inheritdoc>
    public void Disable()
    {
        _keepRemoteConnection?.Dispose();
        _keepRemoteConnection = null;
        connection.ApplicationSettings.RemoteControlEnabled = false;
    }

    /// </inheritdoc>
    public void DeleteRegistration()
    {
        Disable();
        connection.ApplicationSettings.RemoteControlConfig = string.Empty;
    }
}
