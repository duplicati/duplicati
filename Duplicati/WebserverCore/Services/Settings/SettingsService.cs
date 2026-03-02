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
using Duplicati.Library.Utility;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;


namespace Duplicati.WebserverCore.Services.Settings;

public class SettingsService(Connection connection) : ISettingsService
{
    // Remove sensitive information from the output
    private static readonly string[] GUARDED_OUTPUT = [
        Server.Database.ServerSettings.CONST.JWT_CONFIG,
        Server.Database.ServerSettings.CONST.PBKDF_CONFIG,
        Server.Database.ServerSettings.CONST.PRELOAD_SETTINGS_HASH,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_CONFIG,
        Server.Database.ServerSettings.CONST.SERVER_SSL_CERTIFICATE,
        Server.Database.ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_STORAGE_API_KEY,
        Server.Database.ServerSettings.CONST.CLIENT_LICENSE_KEY,
        // Not used anymore, but not completely removed
        Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE,
        Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE_SALT,
        // Completely removed, but no need to expose
        "server-passphrase-trayicon-hash",
        "server-passphrase-trayicon-salt"
    ];

    private static readonly string[] GUARDED_INPUT = [
        Server.Database.ServerSettings.CONST.JWT_CONFIG,
        Server.Database.ServerSettings.CONST.PBKDF_CONFIG,
        Server.Database.ServerSettings.CONST.PRELOAD_SETTINGS_HASH,
        Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE,
        Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE_SALT,
        Server.Database.ServerSettings.CONST.SERVER_SSL_CERTIFICATE,
        Server.Database.ServerSettings.CONST.DISABLE_VISUAL_CAPTCHA,
        Server.Database.ServerSettings.CONST.DISABLE_SIGNIN_TOKENS,
        Server.Database.ServerSettings.CONST.ENCRYPTED_FIELDS,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_CONFIG,
        Server.Database.ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD,
        Server.Database.ServerSettings.CONST.ADDITIONAL_REPORT_URL,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_STORAGE_ENDPOINT_URL,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_STORAGE_API_KEY,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_STORAGE_API_ID,
        Server.Database.ServerSettings.CONST.CLIENT_LICENSE_KEY,
        "server-passphrase-trayicon-hash",
        "server-passphrase-trayicon-salt"
    ];

    /// <summary>
    /// Cache of the valid setting names 
    /// </summary>
    private static readonly IReadOnlySet<string> VALID_OPTION_NAMES = typeof(Server.Database.ServerSettings.CONST).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
        .Where(x => x.FieldType == typeof(string))
        .Select(x => x.GetValue(null)?.ToString())
        .WhereNotNullOrWhiteSpace()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);


    public Abstractions.ServerSettings GetSettings()
        => new Abstractions.ServerSettings(connection.ApplicationSettings);

    public Dictionary<string, string> GetSettingsMasked()
    {
        // Join server settings and global settings
        var dict =
            connection.GetSettings(Server.Database.Connection.SERVER_SETTINGS_ID)
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Union(
                    connection.Settings
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Name.StartsWith("--", StringComparison.Ordinal))
                )
                .DistinctBy(x => x.Name)
                .ToDictionary(x => x.Name, x => x.Value);

        // Patch cert to boolean
        dict.TryGetValue("server-ssl-certificate", out var sslcert);
        dict["server-ssl-certificate"] = (!string.IsNullOrWhiteSpace(sslcert)).ToString();

        foreach (var key in GUARDED_OUTPUT)
            dict.Remove(key);

        return dict;
    }

    public void PatchSettingsMasked(Dictionary<string, object?>? values)
    {
        if (values == null)
            throw new BadRequestException("No values provided");

        var passphrase = values.GetValueOrDefault(Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE)?.ToString();
        foreach (var key in GUARDED_INPUT)
            values.Remove(key);

        // Split into server settings and global settings
        var serversettings = values.Where(x => !string.IsNullOrWhiteSpace(x.Key) && !x.Key.StartsWith("--", StringComparison.Ordinal))
            .ToDictionary(x => x.Key, x => x.Value?.ToString());

        var globalsettings = values.Where(x => !string.IsNullOrWhiteSpace(x.Key) && x.Key.StartsWith("--", StringComparison.Ordinal));

        if (!string.IsNullOrWhiteSpace(passphrase))
            connection.ApplicationSettings.SetWebserverPassword(passphrase);
        if (serversettings.Any())
            connection.ApplicationSettings.UpdateSettings(serversettings, false);

        if (globalsettings.Any())
        {
            // Update based on inputs
            var existing = connection.Settings.ToDictionary(x => x.Name, x => x);
            foreach (var g in globalsettings)
                if (g.Value == null)
                    existing.Remove(g.Key);
                else
                {
                    if (existing.ContainsKey(g.Key))
                        existing[g.Key].Value = g.Value?.ToString();
                    else
                        existing[g.Key] = new Setting() { Name = g.Key, Value = g.Value?.ToString() };
                }

            connection.Settings = existing.Select(x => x.Value).ToArray();
        }
    }

    public string? GetSettingMasked(string key)
    {
        if (key.Equals("server-ssl-certificate", StringComparison.OrdinalIgnoreCase))
            return connection.ApplicationSettings.ServerSSLCertificate != null ? "true" : "false";

        if (GUARDED_OUTPUT.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
            throw new NotFoundException("Key not found");

        if (key.StartsWith("--", StringComparison.Ordinal))
            return connection.Settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase))?.Value;

        if (!VALID_OPTION_NAMES.Contains(key))
            throw new NotFoundException("Key not found");

        return connection.GetSettings(Server.Database.Connection.SERVER_SETTINGS_ID).FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    public void PatchSettingMasked(string key, string value)
    {
        if (key == Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE)
        {
            connection.ApplicationSettings.SetWebserverPassword(value);
            return;
        }

        if (GUARDED_INPUT.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
            throw new BadRequestException($"Cannot update {key} setting");

        if (key.StartsWith("--", StringComparison.Ordinal))
        {
            var settings = connection.Settings.ToList();

            var prop = settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase));
            if (prop == null)
                settings.Add(prop = new Server.Database.Setting() { Name = key, Value = value });
            else
                prop.Value = value;

            connection.Settings = settings.ToArray();
        }
        else
        {
            if (!VALID_OPTION_NAMES.Contains(key))
                throw new NotFoundException("Key not found");

            var dict = new Dictionary<string, string?>
            {
                { key,  value }
            };
            connection.ApplicationSettings.UpdateSettings(dict, false);
        }
    }
}