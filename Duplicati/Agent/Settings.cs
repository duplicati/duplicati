// Copyright (C) 2024, The Duplicati Team
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
using Duplicati.Library.Encryption;
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
public sealed record Settings(
    string Filename,
    string JWT,
    string ServerUrl,
    string CertificateUrl,
    string? SettingsEncryptionKey,
    IEnumerable<MiniServerCertificate> ServerCertificates
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
    /// Loads the settings from a file
    /// </summary>
    /// <param name="path">The path to the file</param>
    /// <returns>The loaded settings</returns>
    public static Settings Load(string? path = null)
    {
        path ??= DefaultSettingsFile;
        var tmp = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path))
            ?? new Settings(path, string.Empty, string.Empty, string.Empty, string.Empty, Array.Empty<MiniServerCertificate>());

        return tmp with
        {
            Filename = path,
            JWT = EncryptedFieldHelper.Decrypt(tmp.JWT, DefaultKey),
            SettingsEncryptionKey = EncryptedFieldHelper.Decrypt(tmp.SettingsEncryptionKey, DefaultKey)
        };
    }

    /// <summary>
    /// Saves the settings 
    /// </summary>
    public void Save()
    {
        var tmp = DefaultKey != null && !DefaultKey.IsBlacklisted
            ? this
            : this with
            {
                Filename = null!,
                JWT = EncryptFieldIfPossible(JWT),
                SettingsEncryptionKey = EncryptFieldIfPossible(SettingsEncryptionKey)
            };

        File.WriteAllText(Filename, JsonSerializer.Serialize(tmp));
    }

    /// <summary>
    /// Encrypts a field if possible
    /// </summary>
    /// <param name="value">The value to encrypt</param>
    /// <returns>The encrypted value</returns>
    [return: NotNullIfNotNull("value")]
    private static string? EncryptFieldIfPossible(string? value)
    {
        if (DefaultKey == null || DefaultKey.IsBlacklisted)
            return value;
        if (value == null)
            return null;
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        return EncryptedFieldHelper.Encrypt(value, DefaultKey);
    }

    /// <summary>
    /// Gets the default settings file path
    /// </summary>
    /// <returns>The path to the settings file</returns>
    private static string GetSettingsPath()
    {
        var folder = DatabaseLocator.GetDefaultStorageFolderWithDebugSupport(DefaultSettingsFilename);
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        return Path.Combine(folder, DefaultSettingsFilename);
    }
}
