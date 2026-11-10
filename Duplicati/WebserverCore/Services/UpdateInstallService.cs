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


using System.Diagnostics;
using Duplicati.Library.AutoUpdater;
using UpdateInfo = Duplicati.Library.AutoUpdater.UpdateInfo;
using Duplicati.Library.Logging;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;

namespace Duplicati.WebserverCore.Services;

/// <summary>
/// Downloads an update, hands it to the update helper and stops the host so the helper can install it
/// </summary>
public class UpdateInstallService : IUpdateInstallService
{
    private static readonly string LOGTAG = Log.LogTagFromType<UpdateInstallService>();

    /// <summary>
    /// How long to wait for the service wrapper to be terminated before giving up
    /// </summary>
    private static readonly TimeSpan ParentExitTimeout = TimeSpan.FromMinutes(2);
    /// <summary>
    /// How long a supervised server waits to be stopped before concluding the helper failed
    /// </summary>
    private static readonly TimeSpan SupervisedStopTimeout = TimeSpan.FromMinutes(10);
    /// <summary>
    /// How long the console cannot ask again for a version whose install failed, so a failing install
    /// does not repeat on every reconnect
    /// </summary>
    internal static readonly TimeSpan FailedVersionRetryDelay = TimeSpan.FromHours(24);
    /// <summary>
    /// How often a console requested install checks whether the queue has become idle
    /// </summary>
    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromSeconds(30);

    private readonly Connection _connection;
    private readonly UpdatePollThread _updatePollThread;
    private readonly IQueueRunnerService _queueRunnerService;
    private readonly LiveControls _liveControls;
    private readonly IApplicationSettings _applicationSettings;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;
    private readonly Lazy<HostEnvironment> _host;
    private readonly Lazy<UpdateInstallSupport> _support;
    /// <summary>
    /// The version whose install last failed, and when; guarded by <see cref="_lock"/>
    /// </summary>
    private (string Version, DateTime At)? _lastFailure;
    /// <summary>
    /// The version a console requested install waits for an idle queue to install; guarded by <see cref="_lock"/>
    /// </summary>
    private string? _waitingForIdleVersion;

    public UpdateInstallService(Connection connection, UpdatePollThread updatePollThread, IQueueRunnerService queueRunnerService, LiveControls liveControls, IApplicationSettings applicationSettings)
    {
        _connection = connection;
        _updatePollThread = updatePollThread;
        _queueRunnerService = queueRunnerService;
        _liveControls = liveControls;
        _applicationSettings = applicationSettings;
        _host = new Lazy<HostEnvironment>(() => HostEnvironment.Detect(applicationSettings.Origin, Environment.GetCommandLineArgs().Skip(1)));
        _support = new Lazy<UpdateInstallSupport>(() => UpdateInstallSupport.Evaluate(_host.Value));
    }

    public HostEnvironment Host => _host.Value;
    public UpdateInstallSupport Support => _support.Value;
    public bool InProgress { get; private set; }

    public void BeginInstall()
    {
        UpdateInfo update;
        PackageEntry package;
        CancellationTokenSource cts;

        lock (_lock)
        {
            if (InProgress)
                throw new ConflictException("An update install is already in progress");

            if (!Support.Supported)
                throw new ConflictException(Support.Reason ?? "Automatic installation is not supported");

            update = _connection.ApplicationSettings.UpdatedVersion
                ?? throw new ConflictException("No update is available");

            if (UpdaterManager.TryParseVersion(update.Version) <= UpdaterManager.TryParseVersion(UpdaterManager.SelfVersion.Version))
                throw new ConflictException("The available update is not newer than the running version");

            package = update.FindPackage()
                ?? throw new ConflictException("The update has no package for this installation");

            if (_queueRunnerService.GetCurrentTask() != null)
                throw new ConflictException("A task is running; wait for it to finish before installing the update");

            cts = _cts = new CancellationTokenSource();
            InProgress = true;
            _updatePollThread.BeginExternalInstall();
        }

        Log.WriteInformationMessage(LOGTAG, "UpdateInstallStarted", "Installing update {0}; host: {1}", update.Displayname, Host.Describe());
        Task.Run(() => RunInstallAsync(update, package, cts.Token));
    }

    public async Task<ConsoleUpdateInstallResult> InstallLatestForConsoleAsync(CancellationToken cancellationToken)
    {
        if (!Support.Supported)
            throw new ConflictException(Support.Reason ?? "Automatic installation is not supported");

        lock (_lock)
        {
            if (InProgress)
                return new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.Installing, _connection.ApplicationSettings.UpdatedVersion?.Version ?? "");
            if (_waitingForIdleVersion != null)
                return new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.WaitingForIdle, _waitingForIdleVersion);
        }

        UpdateInfo? update;
        try
        {
            update = await Task.Run(CheckForNewerUpdate).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new UpdateInstallException("Checking for updates took too long", ex);
        }
        var selfVersion = UpdaterManager.SelfVersion.Version ?? "";
        if (update == null)
            return new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.UpToDate, selfVersion);

        if (update.FindPackage() == null)
            throw new ConflictException($"The update {update.Displayname} has no package for this installation");

        lock (_lock)
        {
            if (_lastFailure is { } failure
                && string.Equals(failure.Version, update.Version, StringComparison.Ordinal)
                && failure.At + FailedVersionRetryDelay > DateTime.UtcNow)
                throw new ConflictException($"The install of {update.Displayname} failed at {failure.At:u}; it is not retried before {failure.At + FailedVersionRetryDelay:u}");

            if (_waitingForIdleVersion != null)
                return new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.WaitingForIdle, _waitingForIdleVersion);

            if (IsBusy())
            {
                _waitingForIdleVersion = update.Version ?? "";
                Log.WriteInformationMessage(LOGTAG, "UpdateInstallWaitingForIdle", "The console asked to install {0}; waiting for the running tasks to finish", update.Displayname);
                Task.Run(InstallWhenIdleAsync);
                return new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.WaitingForIdle, _waitingForIdleVersion);
            }
        }

        Log.WriteInformationMessage(LOGTAG, "UpdateInstallRequestedByConsole", "The console asked to install {0}", update.Displayname);
        BeginInstall();
        return new ConsoleUpdateInstallResult(ConsoleUpdateInstallStatus.Installing, update.Version ?? "");
    }

    /// <summary>
    /// Checks the configured update channel, and records the update the same way the poll thread does
    /// </summary>
    /// <returns>The update when it is newer than the running version, otherwise null</returns>
    private UpdateInfo? CheckForNewerUpdate()
    {
        if (!Enum.TryParse<Duplicati.Library.AutoUpdater.ReleaseType>(_connection.ApplicationSettings.UpdateChannel, true, out var channel)
            || channel == Duplicati.Library.AutoUpdater.ReleaseType.Unknown)
            channel = AutoUpdateSettings.DefaultUpdateChannel;

        var lastError = (Exception?)null;
        Action<Exception> onError = ex => lastError = ex;
        UpdateInfo? update;
        UpdaterManager.OnError += onError;
        try
        {
            update = UpdaterManager.CheckForUpdate(channel);
        }
        finally
        {
            UpdaterManager.OnError -= onError;
        }

        if (update == null)
        {
            // Every manifest url failing reads as "no update"; that must not be reported to the console as up to date
            if (lastError != null)
                throw new UpdateInstallException($"Checking for updates failed: {lastError.Message}", lastError);
            return null;
        }

        if (UpdaterManager.TryParseVersion(update.Version) <= UpdaterManager.TryParseVersion(UpdaterManager.SelfVersion.Version))
            return null;

        _connection.ApplicationSettings.UpdatedVersion = update;
        return update;
    }

    /// <summary>
    /// True when a task runs or waits in the queue, so an install would interrupt or delay a backup
    /// </summary>
    private bool IsBusy()
        => _queueRunnerService.GetCurrentTask() != null || _queueRunnerService.GetCurrentTasks().Count > 0;

    /// <summary>
    /// Starts the install requested by the console once the queue is idle
    /// </summary>
    private async Task InstallWhenIdleAsync()
    {
        try
        {
            while (!_applicationSettings.ApplicationExit.IsCancellationRequested)
            {
                await Task.Delay(IdlePollInterval, _applicationSettings.ApplicationExit).ConfigureAwait(false);
                if (IsBusy() || InProgress)
                    continue;

                try
                {
                    BeginInstall();
                    return;
                }
                catch (ConflictException) when (IsBusy())
                {
                    // A task started between the check and the install; wait for it as well
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "UpdateInstallFailed", ex, "The update install requested by the console failed: {0}", ex.Message);
            RegisterFailure(ex.Message);
        }
        finally
        {
            lock (_lock)
                _waitingForIdleVersion = null;
        }
    }

    public void CancelInstall()
    {
        lock (_lock)
        {
            if (!InProgress)
                throw new ConflictException("No update install is in progress");
            _cts?.Cancel();
        }
    }

    /// <summary>
    /// Gets the signed manifest text for the update, so the helper can verify the package against it.
    /// The update may have been found by an earlier process, in which case the manifest is downloaded again.
    /// </summary>
    /// <param name="update">The update being installed</param>
    /// <returns>The signed manifest text</returns>
    private string ResolveSignedManifest(UpdateInfo update)
    {
        var manifest = UpdaterManager.LastUpdateCheckManifest;
        if (manifest != null && string.Equals(UpdaterManager.LastUpdateCheckVersion?.Version, update.Version, StringComparison.Ordinal))
            return manifest;

        if (!Enum.TryParse<Duplicati.Library.AutoUpdater.ReleaseType>(_connection.ApplicationSettings.UpdateChannel, true, out var channel))
            channel = Duplicati.Library.AutoUpdater.ReleaseType.Unknown;

        var fresh = UpdaterManager.CheckForUpdate(channel);
        manifest = UpdaterManager.LastUpdateCheckManifest;
        if (fresh == null || manifest == null || !string.Equals(fresh.Version, update.Version, StringComparison.Ordinal))
            throw new UpdateInstallException($"The signed manifest for {update.Version} could not be retrieved; check for updates again and retry");

        return manifest;
    }

    private async Task RunInstallAsync(UpdateInfo update, PackageEntry package, CancellationToken token)
    {
        var paused = false;
        var exiting = false;
        var lastDownloadError = (Exception?)null;
        Action<Exception> onError = ex => lastDownloadError = ex;

        try
        {
            var updatesFolder = PendingInstallStore.GetUpdatesFolder(DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ReadWritePermissionSet));
            var packagePath = Path.Combine(updatesFolder, package.GetFilename());
            var signedManifest = ResolveSignedManifest(update);

            SetState(UpdatePollerStates.Downloading, 0);
            UpdaterManager.OnError += onError;
            bool downloaded;
            try
            {
                downloaded = UpdaterManager.DownloadUpdate(update, package, packagePath, p => SetState(UpdatePollerStates.Downloading, p));
            }
            finally
            {
                UpdaterManager.OnError -= onError;
            }

            if (!downloaded)
                throw new UpdateInstallException("The update package could not be downloaded", lastDownloadError);

            token.ThrowIfCancellationRequested();
            SetState(UpdatePollerStates.Verifying, 1);

            var length = new FileInfo(packagePath).Length;
            if (length != package.Length)
                throw new UpdateInstallException($"The downloaded package has length {length}, expected {package.Length}");

            token.ThrowIfCancellationRequested();
            SetState(UpdatePollerStates.LaunchingHelper, 1);

            // Keep new tasks from starting while the helper is prepared
            if (!_liveControls.IsPaused)
            {
                _liveControls.Pause(false);
                paused = true;
            }

            var request = new UpdateLaunchRequest(
                PackagePath: packagePath,
                PackageLength: package.Length,
                PackageSha256: package.SHA256,
                PackageTypeId: UpdaterManager.PackageTypeId,
                SignedManifest: signedManifest,
                TargetVersion: update.Version ?? "",
                CurrentVersion: UpdaterManager.SelfVersion.Version ?? "",
                Host: Host,
                InstallDir: UpdaterManager.INSTALLATIONDIR,
                HelperSourcePath: UpdateInstallSupport.HelperPath,
                WaitPids: Host.GetWaitPids(),
                TerminatePids: Host.GetTerminatePids(),
                UpdatesFolder: updatesFolder,
                MsiProperties: WindowsMsiProperties.Derive(Host, UpdaterManager.INSTALLATIONDIR),
                PortableMode: DataFolderManager.PORTABLE_MODE,
                FinalCheck: () =>
                {
                    if (token.IsCancellationRequested)
                        return "The update install was cancelled";
                    if (_queueRunnerService.GetCurrentTask() != null)
                        return "A task started while the update was being prepared";
                    return null;
                });

            var launched = await UpdateHelperLauncher.LaunchAsync(request, m => Log.WriteInformationMessage(LOGTAG, "UpdateHelper", m), token).ConfigureAwait(false);

            SetState(UpdatePollerStates.Installing, 1);
            _connection.RegisterNotification(
                NotificationType.Information,
                "Installing update",
                $"Duplicati is restarting to install {update.Displayname}",
                null, null, "update:installing", null, "UpdateInstalling", null,
                (self, all) => all.FirstOrDefault(x => x.Action == "update:installing") ?? self);

            exiting = true;
            await StopHostAsync(launched, updatesFolder).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.WriteInformationMessage(LOGTAG, "UpdateInstallCancelled", "The update install was cancelled");
            RegisterFailure("The update install was cancelled");
        }
        catch (Exception ex)
        {
            Log.WriteWarningMessage(LOGTAG, "UpdateInstallFailed", ex, "The update install failed: {0}", ex.Message);
            RegisterFailure(ex.Message);
            // A task that started while the helper was prepared aborts the install without anything being wrong with the version
            if (!IsBusy())
                RecordFailedVersion(update.Version);
            exiting = false;
        }
        finally
        {
            if (!exiting)
            {
                if (paused)
                    _liveControls.Resume();

                lock (_lock)
                {
                    InProgress = false;
                    _cts?.Dispose();
                    _cts = null;
                }
                _updatePollThread.EndExternalInstall();
            }
        }
    }

    /// <summary>
    /// Makes the host process go away, so the helper can replace the files
    /// </summary>
    private async Task StopHostAsync(UpdateLaunchResult launched, string updatesFolder)
    {
        if (Host.ServerExitsWhenParentExits)
        {
            // Tell the wrapper not to respawn us, in case it is new enough to understand; the helper terminates it regardless
            try
            {
                Console.Out.WriteLine(HostEnvironment.ServiceWrapperUpdatePendingLine);
                Console.Out.Flush();
            }
            catch { }

            var deadline = DateTime.UtcNow + ParentExitTimeout;
            while (IsRunning(Host.ParentPid) && DateTime.UtcNow < deadline)
                await Task.Delay(500).ConfigureAwait(false);

            if (IsRunning(Host.ParentPid))
                throw new UpdateInstallException("The service wrapper was not stopped by the update helper; nothing was changed");

            Log.WriteInformationMessage(LOGTAG, "UpdateInstallExit", "The service wrapper has exited, stopping for the update");
            _applicationSettings.SignalApplicationExit();
            return;
        }

        if (Host.ServerExitsBySelf)
        {
            Log.WriteInformationMessage(LOGTAG, "UpdateInstallExit", "Stopping for the update");
            _applicationSettings.SignalApplicationExit();
            return;
        }

        // Supervised by systemd: the helper stops the unit. If that does not happen, report the failure instead of hanging.
        Log.WriteInformationMessage(LOGTAG, "UpdateInstallExit", "Waiting for the update helper to stop the service");
        var waitUntil = DateTime.UtcNow + SupervisedStopTimeout;
        while (DateTime.UtcNow < waitUntil && !_applicationSettings.ApplicationExit.IsCancellationRequested)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            if (!IsRunning(launched.HelperPid))
            {
                var result = PendingInstallStore.ReadResult(launched.Record.ResultFile);
                PendingInstallStore.Delete(updatesFolder);
                throw new UpdateInstallException(result?.Error ?? "The update helper exited before the service was stopped");
            }
        }

        if (!_applicationSettings.ApplicationExit.IsCancellationRequested)
        {
            PendingInstallStore.Delete(updatesFolder);
            throw new UpdateInstallException("The update helper did not stop the service in time");
        }
    }

    public void CompletePendingInstall()
    {
        Task.Run(async () =>
        {
            try
            {
                var dataFolder = DataFolderManager.GetDataFolder(DataFolderManager.AccessMode.ProbeOnly);
                var updatesFolder = Path.Combine(dataFolder, UpdateHelperProtocol.UpdatesFolderName);
                if (!Directory.Exists(updatesFolder))
                    return;

                var record = PendingInstallStore.Read(updatesFolder);
                if (record == null)
                    return;

                Log.WriteInformationMessage(LOGTAG, "UpdateInstallCompleting", "Completing the install of {0} started by {1}", record.TargetVersion, record.PreviousVersion);
                var currentVersion = UpdaterManager.SelfVersion.Version ?? "";
                var result = await UpdateInstallCompletion.CompleteAsync(updatesFolder, record, currentVersion, m => Log.WriteInformationMessage(LOGTAG, "UpdateInstallCompletion", m), _applicationSettings.ApplicationExit).ConfigureAwait(false);

                if (result.Success)
                {
                    Log.WriteInformationMessage(LOGTAG, "UpdateInstalled", "Updated to {0}", currentVersion);
                    _connection.RegisterNotification(
                        NotificationType.Information,
                        "Update installed",
                        $"Duplicati was updated to {UpdaterManager.SelfVersion.Displayname}",
                        null, null, "update:installed", null, "UpdateInstalled", null,
                        (self, all) => all.FirstOrDefault(x => x.Action == "update:installed") ?? self);

                    // The recorded update is now installed, drop the offer
                    var recorded = _connection.ApplicationSettings.UpdatedVersion;
                    if (recorded != null && UpdaterManager.TryParseVersion(recorded.Version) <= UpdaterManager.TryParseVersion(currentVersion))
                        _connection.ApplicationSettings.UpdatedVersion = null;
                }
                else
                {
                    Log.WriteWarningMessage(LOGTAG, "UpdateInstallFailed", null, "The update to {0} failed in stage {1}: {2}", record.TargetVersion, result.Stage, result.Error);
                    RecordFailedVersion(record.TargetVersion);
                    RegisterFailure($"{result.Error} (stage: {result.Stage}{(result.RolledBack ? ", the previous version was restored" : "")}); the log is in {result.LogFile}");
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "UpdateInstallCompletionFailed", ex, "Failed to complete a pending update install: {0}", ex.Message);
            }
        });
    }

    /// <summary>
    /// Remembers that the install of a version failed, so the console does not start it again right away
    /// </summary>
    /// <param name="version">The version that failed to install</param>
    private void RecordFailedVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
            return;
        lock (_lock)
            _lastFailure = (version, DateTime.UtcNow);
    }

    private void SetState(UpdatePollerStates state, double progress)
        => _updatePollThread.SetExternalInstallState(state, progress);

    private void RegisterFailure(string message)
        => _connection.RegisterNotification(
            NotificationType.Error,
            "Update failed",
            message,
            null, null, "update:failed", null, "UpdateInstallFailed", null,
            (self, all) => all.FirstOrDefault(x => x.Action == "update:failed") ?? self);

    private static bool IsRunning(int pid)
    {
        if (pid <= 0)
            return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            return !p.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
