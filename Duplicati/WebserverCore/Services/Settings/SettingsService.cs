using Duplicati.WebserverCore.Abstractions;

namespace Duplicati.WebserverCore.Services.Settings;

public class SettingsService(Server.Database.Connection dbConnection) : ISettingsService
{
    public ServerSettings GetSettings()
        => new ServerSettings(dbConnection.ApplicationSettings);
}