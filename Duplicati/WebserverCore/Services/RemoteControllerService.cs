// Copyright (C) 2026, The Duplicati Team
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

using Duplicati.Library.Logging;
using Duplicati.Library.RemoteControl;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Controller for toggling remote control.
/// </summary>
/// <param name="connection">The connection to the database</param>
/// <param name="controllerHandler">The remote controller handler</param>
/// <param name="eventPollNotify">The event poll notifier</param>
public class RemoteControllerService(Connection connection, IRemoteControllerHandler controllerHandler, EventPollNotify eventPollNotify) : IRemoteController
{
    /// <summary>
    /// The log tag for messages from this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<RemoteControllerService>();

    /// <summary>
    /// How often the connection is checked for being alive
    /// </summary>
    private static readonly TimeSpan WatchdogInterval = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the connection is allowed to stay unconnected before it is recreated
    /// </summary>
    private static readonly TimeSpan MaxDisconnectedPeriod = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Lock guarding the connection instance and the watchdog state
    /// </summary>
    private readonly object _lock = new object();

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

    /// <summary>
    /// The cancellation source for the watchdog task
    /// </summary>
    private CancellationTokenSource? _watchdogCancellation;

    /// <summary>
    /// The last time the connection was known to be healthy
    /// </summary>
    private DateTime _lastHealthy = DateTime.UtcNow;

    /// </inheritdoc>
    public void Enable(bool forceConnect)
    {
        lock (_lock)
        {
            if (_keepRemoteConnection != null)
            {
                // If the previous connection is still alive, there is nothing to do,
                // otherwise the dead connection is replaced with a new one
                if (!_keepRemoteConnection.RunAsync().IsCompleted)
                    return;

                DisposeConnection();
            }

            if (!CanEnable)
                throw new InvalidOperationException("Remote control is not configured");

            CreateConnection(forceConnect);

            _watchdogCancellation ??= StartWatchdog();
        }

        connection.ApplicationSettings.RemoteControlEnabled = true;
        eventPollNotify.SignalRemoteControlUpdate();
    }

    /// </inheritdoc>
    public void Disable()
    {
        lock (_lock)
        {
            _watchdogCancellation?.Cancel();
            _watchdogCancellation?.Dispose();
            _watchdogCancellation = null;
            DisposeConnection();
        }

        connection.ApplicationSettings.RemoteControlEnabled = false;
        eventPollNotify.SignalRemoteControlUpdate();
    }

    /// <summary>
    /// Creates the connection to the remote server.
    /// Must be called while holding the lock.
    /// </summary>
    /// <param name="forceConnect">If the connection should be force enabled, ignoring re-connect delays</param>
    private void CreateConnection(bool forceConnect)
    {
        var config = JsonConvert.DeserializeObject<RemoteControlConfig>(connection.ApplicationSettings.RemoteControlConfig ?? string.Empty)
            ?? throw new InvalidOperationException("Invalid remote control configuration");

        var remoteConnection = KeepRemoteConnection.CreateRemoteListener(
            config.ServerUrl,
            config.Token,
            config.CertificateUrl,
            config.ServerCertificates,
            config.RefreshSettingsBy,
            forceConnect,
            CancellationToken.None,
            controllerHandler.OnConnectAsync,
            controllerHandler.ReKeyAsync,
            controllerHandler.OnControlAsync,
            controllerHandler.OnMessageAsync
        );

        remoteConnection.StateChanged += (_, _) => eventPollNotify.SignalRemoteControlUpdate();

        // Make sure a crash in the connection handler is reported and not silently discarded
        remoteConnection.RunAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
                Log.WriteWarningMessage(LOGTAG, "RemoteControlStopped", t.Exception, "The remote control connection stopped unexpectedly");
        }, TaskContinuationOptions.ExecuteSynchronously);

        _keepRemoteConnection = remoteConnection;
        _lastHealthy = DateTime.UtcNow;
    }

    /// <summary>
    /// Disposes the current connection, if any.
    /// Must be called while holding the lock.
    /// </summary>
    private void DisposeConnection()
    {
        try { _keepRemoteConnection?.Dispose(); }
        catch (Exception ex) { Log.WriteWarningMessage(LOGTAG, "RemoteControlDisposeError", ex, "Failed to dispose the remote control connection"); }
        _keepRemoteConnection = null;
    }

    /// <summary>
    /// Starts the task that monitors the connection and recreates it if it is stuck.
    /// Must be called while holding the lock.
    /// </summary>
    /// <returns>The cancellation source for the watchdog task</returns>
    private CancellationTokenSource StartWatchdog()
    {
        var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(WatchdogInterval, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    CheckConnection(token);
                }
                catch (Exception ex)
                {
                    Log.WriteWarningMessage(LOGTAG, "RemoteControlWatchdogError", ex, "Failed to check the remote control connection");
                }
            }
        }, CancellationToken.None);

        return cancellation;
    }

    /// <summary>
    /// Checks that the connection is alive, and recreates it if it is not.
    /// The connection is expected to reconnect on its own, so this only guards
    /// against the connection handler stopping or getting stuck in a state
    /// where it no longer attempts to reconnect.
    /// </summary>
    /// <param name="token">The token that is cancelled when remote control is disabled</param>
    private void CheckConnection(CancellationToken token)
    {
        lock (_lock)
        {
            // Checked inside the lock, so a disable cannot be raced by a restart
            if (token.IsCancellationRequested || !IsEnabled || !CanEnable)
                return;

            var remoteConnection = _keepRemoteConnection;

            string reason;
            if (remoteConnection == null)
            {
                reason = "the connection is missing";
            }
            else if (remoteConnection.RunAsync().IsCompleted)
            {
                reason = "the connection handler has stopped";
            }
            else if (remoteConnection.State == KeepRemoteConnection.ConnectionState.Authenticated || remoteConnection.IsAutoReconnectDisabled())
            {
                // Connected, or intentionally not connecting
                _lastHealthy = DateTime.UtcNow;
                return;
            }
            else if (_lastHealthy + MaxDisconnectedPeriod < DateTime.UtcNow)
            {
                reason = $"it has not been connected for {MaxDisconnectedPeriod.TotalMinutes} minutes";
            }
            else
            {
                return;
            }

            Log.WriteWarningMessage(LOGTAG, "RemoteControlRestart", null, "Restarting the remote control connection because {0}", reason);
            DisposeConnection();
            CreateConnection(false);
        }
    }

    /// </inheritdoc>
    public void DeleteRegistration()
    {
        Disable();

        // Clear all remote control related settings
        connection.ApplicationSettings.RemoteControlConfig = string.Empty;
        connection.ApplicationSettings.AdditionalReportUrl = string.Empty;
        connection.ApplicationSettings.AdditionalActivityUrl = string.Empty;
        connection.ApplicationSettings.RemoteControlDashboardUrl = string.Empty;
        connection.ApplicationSettings.RemoteControlStorageApiId = string.Empty;
        connection.ApplicationSettings.RemoteControlStorageApiKey = string.Empty;
        connection.ApplicationSettings.RemoteControlStorageEndpointUrl = string.Empty;
        connection.ApplicationSettings.ClientLicenseKey = string.Empty;
        Duplicati.Proprietary.LicenseChecker.LicenseHelper.SetRemoteClientLicenseKey(null);

        // Remove remotely configured backups
        var remoteBackupIds = connection.Backups.Where(x => !string.IsNullOrWhiteSpace(x.ExternalID) && x.ExternalID.StartsWith(ControlRequestMessage.BackupConfigKeyPrefix, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.ID)
            .Where(x => long.TryParse(x, out _))
            .Select(x => long.Parse(x))
            .ToList();

        foreach (var id in remoteBackupIds)
            connection.DeleteBackup(id);

        eventPollNotify.SignalRemoteControlUpdate();
    }
}
