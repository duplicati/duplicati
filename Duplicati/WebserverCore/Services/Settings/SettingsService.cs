using Duplicati.Library.Utility.Abstractions;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Database;
using ServerSettings = Duplicati.WebserverCore.Abstractions.ServerSettings;
using SettingsOptionsNames = Duplicati.Server.Database.ServerSettings.CONST;

namespace Duplicati.WebserverCore.Services.Settings;

public class SettingsService(MainDbContext dbContext, IBoolParser boolParser) : ISettingsService
{
    private const int ServerSettingsId = Connection.SERVER_SETTINGS_ID;

    private ServerSettings? _cachedSettings;

    public ServerSettings GetSettings()
    {
        if (_cachedSettings is not null)
        {
            return _cachedSettings;
        }

        var options = dbContext.Options.Where(o => o.BackupID == ServerSettingsId).ToArray();

        _cachedSettings = new ServerSettings();

        foreach (var option in options)
        {
            switch (option.Name)
            {
                case SettingsOptionsNames.STARTUP_DELAY:
                    _cachedSettings.StartupDelay = option.Value;
                    break;
                case SettingsOptionsNames.DOWNLOAD_SPEED_LIMIT:
                    _cachedSettings.DownloadSpeedLimit = option.Value;
                    break;
                case SettingsOptionsNames.UPLOAD_SPEED_LIMIT:
                    _cachedSettings.UploadSpeedLimit = option.Value;
                    break;
                case SettingsOptionsNames.THREAD_PRIORITY:
                    _cachedSettings.ThreadPriority = option.Value;
                    break;
                case SettingsOptionsNames.LAST_WEBSERVER_PORT:
                    _cachedSettings.LastWebserverPort = option.Value;
                    break;
                case SettingsOptionsNames.IS_FIRST_RUN:
                    _cachedSettings.IsFirstRun = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_PORT_CHANGED:
                    _cachedSettings.ServerPortChanged = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_PASSPHRASE:
                    _cachedSettings.ServerPassphrase = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_PASSPHRASE_SALT:
                    _cachedSettings.ServerPassphraseSalt = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_PASSPHRASETRAYICON:
                    _cachedSettings.ServerPassphraseTrayIcon = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_PASSPHRASETRAYICONHASH:
                    _cachedSettings.ServerPassphraseTrayIconHash = option.Value;
                    break;
                case SettingsOptionsNames.UPDATE_CHECK_LAST:
                    _cachedSettings.UpdateCheckLast = option.Value;
                    break;
                case SettingsOptionsNames.UPDATE_CHECK_INTERVAL:
                    _cachedSettings.UpdateCheckInterval = option.Value;
                    break;
                case SettingsOptionsNames.UPDATE_CHECK_NEW_VERSION:
                    _cachedSettings.UpdateCheckNewVersion = option.Value;
                    break;
                case SettingsOptionsNames.UNACKED_ERROR:
                    _cachedSettings.UnackedError = boolParser.ParseBool(option.Value);
                    break;
                case SettingsOptionsNames.UNACKED_WARNING:
                    _cachedSettings.UnackedWarning = boolParser.ParseBool(option.Value);
                    break;
                case SettingsOptionsNames.SERVER_LISTEN_INTERFACE:
                    _cachedSettings.ServerListenInterface = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_SSL_CERTIFICATE:
                    _cachedSettings.ServerSslCertificate = option.Value;
                    break;
                case SettingsOptionsNames.HAS_FIXED_INVALID_BACKUPID:
                    _cachedSettings.HasFixedInvalidBackupId = option.Value;
                    break;
                case SettingsOptionsNames.UPDATE_CHANNEL:
                    _cachedSettings.UpdateChannel = option.Value;
                    break;
                case SettingsOptionsNames.USAGE_REPORTER_LEVEL:
                    _cachedSettings.UsageReporterLevel = option.Value;
                    break;
                case SettingsOptionsNames.HAS_ASKED_FOR_PASSWORD_PROTECTION:
                    _cachedSettings.HasAskedForPasswordProtection = option.Value;
                    break;
                case SettingsOptionsNames.DISABLE_TRAY_ICON_LOGIN:
                    _cachedSettings.DisableTrayIconLogin = option.Value;
                    break;
                case SettingsOptionsNames.SERVER_ALLOWED_HOSTNAMES:
                    _cachedSettings.ServerAllowedHostnames = option.Value;
                    break;
                default:
                    throw new ArgumentOutOfRangeException($"Server Option with name '{option.Name}' and value: '{option.Value}' was not recognized");
            }
        }

        return _cachedSettings;
    }
}