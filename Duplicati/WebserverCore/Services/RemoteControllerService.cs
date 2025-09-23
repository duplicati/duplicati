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

using Duplicati.Library.RemoteControl;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Controller for toggling remote control.
/// </summary>
/// <param name="connection">The connection to the database</param>
/// <param name="controllerHandler">The remote controller handler</param>
public class RemoteControllerService(Connection connection, IRemoteControllerHandler controllerHandler) : IRemoteController
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
            controllerHandler.OnConnect,
            controllerHandler.ReKey,
            controllerHandler.OnControl,
            controllerHandler.OnMessage
        );

        connection.ApplicationSettings.RemoteControlEnabled = true;
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
        connection.ApplicationSettings.AdditionalReportUrl = string.Empty;
    }
}
