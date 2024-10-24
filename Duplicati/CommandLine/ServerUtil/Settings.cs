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
/// <param name="Insecure">Whether to disable TLS/SSL certificate trust check</param>
/// <param name="Key">The encryption key to use for the settings file</param>
public sealed record Settings(
    string? Password,
    string? RefreshToken,
    Uri HostUrl,
    string? ServerDatafolder,
    string SettingsFile,
    bool Insecure,
    EncryptedFieldHelper.KeyInstance? Key
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
    /// <param name="insecure">Whether to disable TLS/SSL certificate trust check</param>
    /// <param name="settingsPassphrase">The encryption key to use</param>
    /// <returns>The loaded settings</returns>
    public static Settings Load(string? password, Uri? hostUrl, string? serverDataFolder, string settingsFile, bool insecure, string? settingsPassphrase)
    {
        hostUrl ??= new Uri("http://localhost:8200");
        if (string.IsNullOrWhiteSpace(serverDataFolder))
            serverDataFolder = GetDefaultStorageFolder("Duplicati-server.sqlite");

        if (!string.IsNullOrWhiteSpace(settingsFile) && !Path.IsPathRooted(settingsFile))
            settingsFile = Path.Combine(GetDefaultStorageFolder(settingsFile), settingsFile);

        var key = EncryptedFieldHelper.KeyInstance.CreateKeyIfValid(settingsPassphrase);
        var persistedSettings = LoadSettings(settingsFile, key)
            .FirstOrDefault(x => x.HostUrl == hostUrl);

        return new Settings(
            password,
            persistedSettings?.RefreshToken,
            hostUrl,
            serverDataFolder,
            settingsFile,
            insecure,
            key
        );
    }

    /// <summary>
    /// Saves the settings to the settings file
    /// </summary>
    public void Save()
    {
        var thisKey = Key;
        if (!string.IsNullOrWhiteSpace(RefreshToken))
        {
            if (Key == null)
            {
                Console.WriteLine("Warning: The encryption key is missing, saving login token without encryption");
            }
            else if (Key?.IsBlacklisted ?? false)
            {
                Console.WriteLine("Warning: The current encryption key is blacklisted and cannot be used, saving login token without encryption");
                thisKey = null;
            }

        }

        File.WriteAllText(SettingsFile, JsonSerializer.Serialize(LoadSettings(SettingsFile, thisKey)
            .Where(x => x.HostUrl != HostUrl)
            .Append(new PersistedSettings(RefreshToken, HostUrl, ServerDatafolder))
            .Select(x => x with
            {
                RefreshToken = string.IsNullOrWhiteSpace(x.RefreshToken) || thisKey == null
                    ? x.RefreshToken
                    : EncryptedFieldHelper.Encrypt(x.RefreshToken, thisKey)
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
    /// <param name="key">The encryption key to use</param>
    /// <returns>The loaded settings</returns>
    private static List<PersistedSettings> LoadSettings(string filename, EncryptedFieldHelper.KeyInstance? key)
    {
        if (File.Exists(filename))
            return (JsonSerializer.Deserialize<List<PersistedSettings>>(File.ReadAllText(filename)) ?? [])
                .Select(x => x with { RefreshToken = EncryptedFieldHelper.Decrypt(x.RefreshToken, key) })
                .ToList();

        return [];
    }
}
