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

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Encryption;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.RemoteControl;

namespace Duplicati.Agent;

/// <summary>
/// Settings for the agent
/// </summary>
/// <param name="Filename">The filename of the settings</param>
/// <param name="JWT">The JWT used for authentication</param>
/// <param name="ServerUrl">The URL of the server</param>
/// <param name="SettingsEncryptionKey">The encryption key for the local settings</param>
/// <param name="ServerCertificates">The server certificates</param>
/// <param name="Key">The encryption key to use for agent settings</param>
public sealed record Settings(
    string Filename,
    string JWT,
    string ServerUrl,
    string CertificateUrl,
    string? SettingsEncryptionKey,
    IEnumerable<MiniServerCertificate> ServerCertificates,
    EncryptedFieldHelper.KeyInstance? Key
)
{
    /// <summary>
    /// Gets the default settings file path
    /// </summary>
    public static readonly string DefaultSettingsFile = GetSettingsPath();
    /// <summary>
    /// Gets the default settings file name
    /// </summary>
    public const string DefaultSettingsFilename = "agent.json";

    /// <summary>
    /// The encryption key to use for agent settings
    /// </summary>
    private static readonly EncryptedFieldHelper.KeyInstance? DefaultKey = null;

    /// <summary>
    /// Shared JSON serialization options
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Loads the settings from a file
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <returns>The loaded settings</returns>
    public static Settings Load(string path, string? key)
    {
        var keyInstance = string.IsNullOrWhiteSpace(key)
            ? DefaultKey
            : EncryptedFieldHelper.KeyInstance.CreateKey(key);

        var tmp = (File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) : null)
            ?? new Settings(path, string.Empty, string.Empty, string.Empty, string.Empty, Array.Empty<MiniServerCertificate>(), null);

        try
        {
            return tmp with
            {
                Filename = path,
                JWT = EncryptedFieldHelper.Decrypt(tmp.JWT, keyInstance),
                SettingsEncryptionKey = EncryptedFieldHelper.Decrypt(tmp.SettingsEncryptionKey, keyInstance),
                Key = keyInstance
            };
        }
        catch (SettingsEncryptionKeyMismatchException sek)
        {
            throw new UserInformationException("Invalid settings key provided", "InvalidAgentSettingsKey", sek);
        }
        catch (SettingsEncryptionKeyMissingException sek)
        {
            throw new UserInformationException("Settings file is encrypted but key is missing", "AgentSettingsKeyMissing", sek);
        }
    }

    /// <summary>
    /// Saves the settings 
    /// </summary>
    public void Save()
    {
        var tmp = this with { Key = null, Filename = null! };
        tmp = Key == null || Key.IsBlacklisted
            ? tmp
            : tmp with
            {
                JWT = EncryptFieldIfPossible(JWT, Key),
                SettingsEncryptionKey = EncryptFieldIfPossible(SettingsEncryptionKey, Key)
            };

        File.WriteAllText(Filename, JsonSerializer.Serialize(tmp, JsonOptions));
    }

    /// <summary>
    /// Encrypts a field if possible
    /// </summary>
    /// <param name="value">The value to encrypt</param>
    /// <returns>The encrypted value</returns>
    [return: NotNullIfNotNull("value")]
    private static string? EncryptFieldIfPossible(string? value, EncryptedFieldHelper.KeyInstance? key)
    {
        if (key == null || key.IsBlacklisted)
            return value;
        if (value == null)
            return null;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return EncryptedFieldHelper.Encrypt(value, key);
    }

    /// <summary>
    /// Gets the default settings file path
    /// </summary>
    /// <returns>The path to the settings file</returns>
    private static string GetSettingsPath()
    {
        // Ideally, this should use DataFolderManager.DATAFOLDER, but we cannot due to backwards compatibility
        var folder = Library.AutoUpdater.DataFolderLocator.GetDefaultStorageFolder(DefaultSettingsFilename, true);
        return Path.Combine(folder, DefaultSettingsFilename);
    }
}
