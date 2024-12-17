namespace Duplicati.WebserverCore.Abstractions;

public class ServerSettings
{
    private readonly Server.Database.ServerSettings applicationSettings;

    public ServerSettings(Server.Database.ServerSettings applicationSettings)
        => this.applicationSettings = applicationSettings;

    public string StartupDelay
    {
        get => applicationSettings.StartupDelayDuration;
        set => applicationSettings.StartupDelayDuration = value;
    }

    public string DownloadSpeedLimit
    {
        get => applicationSettings.DownloadSpeedLimit;
        set => applicationSettings.DownloadSpeedLimit = value;
    }
    public string UploadSpeedLimit
    {
        get => applicationSettings.UploadSpeedLimit;
        set => applicationSettings.UploadSpeedLimit = value;
    }
    public int LastWebserverPort
    {
        get => applicationSettings.LastWebserverPort;
        set => applicationSettings.LastWebserverPort = value;
    }

    public bool IsFirstRun
    {
        get => applicationSettings.IsFirstRun;
        set => applicationSettings.IsFirstRun = value;
    }

    public bool ServerPortChanged
    {
        get => applicationSettings.ServerPortChanged;
        set => applicationSettings.ServerPortChanged = value;
    }
    public DateTime UpdateCheckLast
    {
        get => applicationSettings.LastUpdateCheck;
        set => applicationSettings.LastUpdateCheck = value;
    }
    public string UpdateCheckInterval
    {
        get => applicationSettings.UpdateCheckInterval;
        set => applicationSettings.UpdateCheckInterval = value;
    }
    public string? NewVersionUpdateUrl
    {
        get => applicationSettings.UpdatedVersion == null
            ? null
            : applicationSettings.UpdatedVersion.GetUpdateUrls()?.FirstOrDefault();
    }
    public UpdateInfo? NewVersion
    {
        get => UpdateInfo.FromSrc(applicationSettings.UpdatedVersion);
    }
    public bool UnackedError
    {
        get => applicationSettings.UnackedError;
        set => applicationSettings.UnackedError = value;
    }
    public bool UnackedWarning
    {
        get => applicationSettings.UnackedWarning;
        set => applicationSettings.UnackedWarning = value;
    }
    public string ServerListenInterface
    {
        get => applicationSettings.ServerListenInterface;
        set => applicationSettings.ServerListenInterface = value;
    }
    public bool HasFixedInvalidBackupId
    {
        get => applicationSettings.FixedInvalidBackupId;
        set => applicationSettings.FixedInvalidBackupId = value;
    }
    public string UpdateChannel
    {
        get => applicationSettings.UpdateChannel;
        set => applicationSettings.UpdateChannel = value;
    }
    public string UsageReporterLevel
    {
        get => applicationSettings.UsageReporterLevel;
        set => applicationSettings.UsageReporterLevel = value;
    }
    public bool DisableTrayIconLogin
    {
        get => applicationSettings.DisableTrayIconLogin;
        set => applicationSettings.DisableTrayIconLogin = value;
    }
    public string ServerAllowedHostnames
    {
        get => applicationSettings.AllowedHostnames;
        set => applicationSettings.SetAllowedHostnames(value);
    }

    public bool DisableVisualCaptcha
    {
        get => applicationSettings.DisableVisualCaptcha;
    }

    public bool HasSSLCertificate
    {
        get => applicationSettings.ServerSSLCertificate != null;
    }
}