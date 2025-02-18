// Copyright (C) 2025, The Duplicati Team
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
using System.Text.Json;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Utility;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Settings instance for the server utility.
/// </summary>
/// <param name="Password">The commandline password</param>
/// <param name="RefreshToken">The saved refresh token</param>
/// <param name="HostUrl">The host url to connect to</param>
/// <param name="SettingsFile">The settings file where data is loaded/saved</param>
/// <param name="Insecure">Whether to disable TLS/SSL certificate trust check</param>
/// <param name="Key">The encryption key to use for the settings file</param>
/// <param name="SecretProvider">The secret provider to use for reading secrets</param>
/// <param name="SecretProviderPattern">The pattern to use for the secret provider</param>
/// <param name="AcceptedHostCertificate">The SHA1 hash of the host certificate to accept</param>
public sealed record Settings(
    string? Password,
    string? RefreshToken,
    System.Uri HostUrl,
    string SettingsFile,
    bool Insecure,
    EncryptedFieldHelper.KeyInstance? Key,
    ISecretProvider? SecretProvider,
    string SecretProviderPattern,
    string? AcceptedHostCertificate
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
        System.Uri HostUrl,
        string? ServerDatafolder
    );

    /// <summary>
    /// Gets the default data folder for the settings file
    /// </summary>
    /// <param name="filename">The filename to use</param>
    /// <returns>The default storage folder</returns>
    private static string GetDefaultStorageFolder(string filename)
        // Ideally, this should use DataFolderManager.DATAFOLDER, but we cannot due to backwards compatibility
        => DataFolderLocator.GetDefaultStorageFolder(filename, true);

    /// <summary>
    /// Loads the settings from the settings file
    /// </summary>
    /// <param name="password">The password to use</param>
    /// <param name="hostUrl">The host URL to use</param>
    /// <param name="settingsFile">The settings file to use</param>
    /// <param name="insecure">Whether to disable TLS/SSL certificate trust check</param>
    /// <param name="settingsPassphrase">The encryption key to use</param>
    /// <param name="secretProvider">The secret provider to use</param>
    /// <param name="secretProviderCache">The secret provider cache level to use</param>
    /// <param name="secretProviderPattern">The secret provider pattern to use</param>
    /// <param name="acceptedHostCertificate">The SHA1 hash of the host certificate to accept</param>
    /// <returns>The loaded settings</returns>
    public static Settings Load(string? password, System.Uri? hostUrl, string settingsFile, bool insecure, string? settingsPassphrase, string? secretProvider, SecretProviderHelper.CachingLevel secretProviderCache, string secretProviderPattern, string? acceptedHostCertificate)
    {
        hostUrl ??= new System.Uri("http://localhost:8200");

        ISecretProvider? secretInstance = null;
        if (!string.IsNullOrWhiteSpace(secretProvider))
        {
            var secretProviderInstance = SecretProviderLoader.CreateInstance(secretProvider);

            // Map into expected structure
            var opts = new Dictionary<string, string?>
            {
                { "secret-provider", secretProvider },
                { "secret-provider-pattern", secretProviderPattern },
                { "secret-provider-cache", secretProviderCache.ToString() },
                { "password", password },
                { "settings-encryption-key", settingsPassphrase }
            };

            var args = new[] { hostUrl };
            secretInstance = SecretProviderHelper.ApplySecretProviderAsync(args, [], opts, Library.Utility.TempFolder.SystemTempPath, null, CancellationToken.None).Await();

            // Read back transformed values
            hostUrl = args[0];
            password = opts["password"];
            settingsPassphrase = opts["settings-encryption-key"];
        }

        if (!string.IsNullOrWhiteSpace(settingsFile) && !Path.IsPathRooted(settingsFile))
            settingsFile = Path.Combine(GetDefaultStorageFolder(settingsFile), settingsFile);

        var key = EncryptedFieldHelper.KeyInstance.CreateKeyIfValid(settingsPassphrase);
        var persistedSettings = LoadSettings(settingsFile, key)
            .FirstOrDefault(x => x.HostUrl == hostUrl);

        return new Settings(
            password,
            persistedSettings?.RefreshToken,
            hostUrl,
            settingsFile,
            insecure,
            key,
            secretInstance,
            secretProviderPattern,
            acceptedHostCertificate
        );
    }

    /// <summary>
    /// Replaces secrets inside arguments and options
    /// </summary>
    /// <param name="args">The arguments to replace</param>
    /// <param name="options">The options to replace</param>
    /// <returns>The task to await</returns>
    public Task ReplaceSecrets(Dictionary<string, string?> options)
    {
        if (SecretProvider == null)
            return Task.CompletedTask;

        return SecretProviderHelper.ReplaceSecretsAsync(SecretProvider, [], [], options, SecretProviderPattern, CancellationToken.None);
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
            .Append(new PersistedSettings(RefreshToken, HostUrl, DataFolderManager.DATAFOLDER))
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
