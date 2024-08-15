using Duplicati.WebserverCore.Abstractions;
using Newtonsoft.Json;

namespace Duplicati.WebserverCore.Services;

public class UpdateService(ISettingsService settingsService) : IUpdateService
{
    public UpdateInfo? GetUpdateInfo()
        => settingsService.GetSettings().NewVersion;
}