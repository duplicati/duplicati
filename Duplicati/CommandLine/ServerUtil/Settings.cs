using System.Text.Json;
using Duplicati.Library.Encryption;
using Duplicati.Library.Main;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Settings instance for the server utility.
/// </summary>
/// <param name="Password">The commandline password</param>
/// <param name="RefreshToken">The saved refresh token</param>
/// <param name="HostUrl">The host url to connect to</param>
/// <param name="ServerDatafolder">The server datafolder for password-free connections</param>
/// <param name="SettingsFile">The settings file where data is loaded/saved</param>
public sealed record Settings(
    string? Password,
    string? RefreshToken,
    Uri HostUrl,
    string? ServerDatafolder,
    string SettingsFile
)
{
    /// <summary>
    /// The JSON serialized settings for a single host
    /// </summary>
    /// <param name="RefreshToken">Encrypted refresh token</param>
    /// <param name="HostUrl">The host url to connect to</param>
    /// <param name="ServerDatafolder">The server datafolder, if any</param>
    private sealed record PersistedSettings(
        string? RefreshToken,
        Uri HostUrl,
        string? ServerDatafolder
    );
    private static string GetDefaultStorageFolder(string filename)
    {
        var folder = DatabaseLocator.GetDefaultStorageFolderWithDebugSupport(filename);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        return folder;
    }

    /// <summary>
    /// Loads the settings from the settings file
    /// </summary>
    /// <param name="password">The password to use</param>
    /// <param name="hostUrl">The host URL to use</param>
    /// <param name="serverDataFolder">The server data folder to use</param>
    /// <param name="settingsFile">The settings file to use</param>
    /// <returns>The loaded settings</returns>
    public static Settings Load(string? password, Uri? hostUrl, string? serverDataFolder, string settingsFile)
    {
        hostUrl ??= new Uri("http://localhost:8200");
        if (string.IsNullOrWhiteSpace(serverDataFolder))
            serverDataFolder = GetDefaultStorageFolder("Duplicati-server.sqlite");

        if (!string.IsNullOrWhiteSpace(settingsFile) && !Path.IsPathRooted(settingsFile))
            settingsFile = Path.Combine(GetDefaultStorageFolder(settingsFile), settingsFile);

        var persistedSettings = LoadSettings(settingsFile)
            .FirstOrDefault(x => x.HostUrl == hostUrl);

        return new Settings(
            password,
            persistedSettings?.RefreshToken,
            hostUrl,
            serverDataFolder,
            settingsFile
        );
    }

    /// <summary>
    /// Saves the settings to the settings file
    /// </summary>
    public void Save()
    {
        if (!string.IsNullOrWhiteSpace(RefreshToken) && EncryptedFieldHelper.IsCurrentKeyBlacklisted)
            Console.WriteLine("Warning: The current encryption key is blacklisted and cannot be used, saving login token without encryption");

        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(LoadSettings(SettingsFile)
            .Where(x => x.HostUrl != HostUrl)
            .Append(new PersistedSettings(RefreshToken, HostUrl, ServerDatafolder))
            .Select(x => x with
            {
                RefreshToken = string.IsNullOrWhiteSpace(x.RefreshToken) || EncryptedFieldHelper.IsCurrentKeyBlacklisted
                    ? x.RefreshToken
                    : EncryptedFieldHelper.Encrypt(x.RefreshToken)
            })
        ));
    }

    /// <summary>
    /// Gets a connection to the server
    /// </summary>
    /// <returns>The connection</returns>
    public Task<Connection> GetConnection()
    {
        return Connection.Connect(this);
    }

    /// <summary>
    /// Loads the settings from the settings file
    /// </summary>
    /// <param name="filename">The filename to load</param>
    /// <returns>The loaded settings</returns>
    private static List<PersistedSettings> LoadSettings(string filename)
    {
        if (File.Exists(filename))
            return (JsonSerializer.Deserialize<List<PersistedSettings>>(File.ReadAllText(filename)) ?? [])
                .Select(x => x with { RefreshToken = EncryptedFieldHelper.Decrypt(x.RefreshToken) })
                .ToList();

        return [];
    }
}
