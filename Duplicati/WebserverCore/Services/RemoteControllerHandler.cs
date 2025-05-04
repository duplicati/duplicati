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
using Duplicati.Library.Logging;
using Duplicati.Library.RemoteControl;
using Duplicati.Library.RestAPI;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Controller for handling remote control messages.
/// </summary>
/// <param name="connection">The connection to the database</param>
/// <param name="httpClientFactory">The HTTP client factory</param>
/// <param name="jwtTokenProvider">The JWT token provider</param>
public class RemoteControllerHandler(Connection connection, IHttpClientFactory httpClientFactory, IJWTTokenProvider jwtTokenProvider) : IRemoteControllerHandler
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<RemoteControllerHandler>();

    /// <inheritdoc/>
    public Task<Dictionary<string, string?>> OnConnect(Dictionary<string, string?> metadata)
    {
        metadata["feature:additional-report-url"] = connection.ApplicationSettings.AdditionalReportUrl;
        return Task.FromResult(metadata);
    }

    /// <inheritdoc/>
    public Task ReKey(ClaimedClientData data)
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

    /// <inheritdoc/>
    public async Task OnControl(KeepRemoteConnection.ControlMessage message)
    {
        // Make method async
        await Task.CompletedTask;

        Dictionary<string, string?>? result = null;

        Log.WriteMessage(LogMessageType.Verbose, LOGTAG, "OnControl", "Received control message: {0}", message);
        try
        {
            if (string.Equals(message.ControlRequestMessage.Command, ControlRequestMessage.ConfigureReportUrlSet, StringComparison.OrdinalIgnoreCase))
                connection.ApplicationSettings.AdditionalReportUrl = message.ControlRequestMessage.Parameters.GetValueOrDefault(ControlRequestMessage.ConfigureReportUrlParameter);

            if (string.Equals(message.ControlRequestMessage.Command, ControlRequestMessage.ConfigureReportUrlGet, StringComparison.OrdinalIgnoreCase))
                result = new Dictionary<string, string?> { { ControlRequestMessage.ConfigureReportUrlParameter, connection.ApplicationSettings.AdditionalReportUrl } };
        }
        catch (Exception ex)
        {
            Log.WriteMessage(LogMessageType.Error, LOGTAG, "OnControl", ex, "Failed to handle control message: {0}", message);
            message.Respond(new ControlResponseMessage(false, null, ex.Message));
            return;
        }

        message.Respond(new ControlResponseMessage(true, result, null));
    }


    /// <inheritdoc/>
    public async Task OnMessage(KeepRemoteConnection.CommandMessage commandMessage)
    {
        using var httpClient = httpClientFactory.CreateClient();
        var token = jwtTokenProvider.CreateAccessToken("remote-control", jwtTokenProvider.TemporaryFamilyId, TimeSpan.FromMinutes(2));

        httpClient.BaseAddress = new Uri($"{(connection.ApplicationSettings.UseHTTPS ? "https" : "http")}://127.0.0.1:{connection.ApplicationSettings.LastWebserverPort}");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var psk = Middlewares.PreSharedKeyFilter.PreSharedKey;
        if (!string.IsNullOrWhiteSpace(psk))
            httpClient.DefaultRequestHeaders.Add(Middlewares.PreSharedKeyFilter.HeaderName, psk);

        await commandMessage.Handle(httpClient);
    }
}
