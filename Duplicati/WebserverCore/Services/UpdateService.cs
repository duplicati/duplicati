using Duplicati.WebserverCore.Abstractions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Services;

public class UpdateService(ISettingsService settingsService, JsonSerializerSettings options, ILogger<ISettingsService> logger) : IUpdateService
{
    private UpdateInfo? _updateInfo;

    public UpdateInfo? GetUpdateInfo()
    {
        var settings = settingsService.GetSettings();
        if (settings.UpdateCheckNewVersion is not { Length: > 0 } newVersion) return null;
        try
        {
            if (_updateInfo != null)
                return _updateInfo;

            return _updateInfo = JsonConvert.DeserializeObject<UpdateInfo>(newVersion, options);
        }
        catch
        {
            UpdateServiceLogger.CouldNotDeserialize(logger, settings.UpdateCheckNewVersion);
        }

        return null;

    }
}