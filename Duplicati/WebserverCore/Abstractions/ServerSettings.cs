namespace Duplicati.WebserverCore.Abstractions;

public class ServerSettings
{
    public string StartupDelay { get; set; } = "";
    public string DownloadSpeedLimit { get; set; }= "";
    public string UploadSpeedLimit { get; set; }= "";
    public string ThreadPriority { get; set; }= "";
    public string LastWebserverPort { get; set; }= "";
    public string IsFirstRun { get; set; }= "";
    public string ServerPortChanged { get; set; }= "";
    public string ServerPassphrase { get; set; }= "";
    public string ServerPassphraseSalt { get; set; }= "";
    public string ServerPassphraseTrayIcon { get; set; }= "";
    public string ServerPassphraseTrayIconHash { get; set; }= "";
    public string UpdateCheckLast { get; set; }= "";
    public string UpdateCheckInterval { get; set; }= "";
    public string UpdateCheckNewVersion { get; set; }= "";
    public bool UnackedError { get; set; }
    public bool UnackedWarning { get; set; }
    public string ServerListenInterface { get; set; }= "";
    public string ServerSslCertificate { get; set; }= "";
    public string HasFixedInvalidBackupId { get; set; }= "";
    public string UpdateChannel { get; set; }= "";
    public string UsageReporterLevel { get; set; }= "";
    public string HasAskedForPasswordProtection { get; set; }= "";
    public string DisableTrayIconLogin { get; set; }= "";
    public string ServerAllowedHostnames { get; set; }= "";
}