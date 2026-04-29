// Copyright (C) 2026, The Duplicati Team
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
    /// <summary>
    /// Legacy guarded options that are no longer consts, but should be guarded anyway
    /// to protect older databases
    /// </summary>
    private static readonly string[] LEGACY_GUARDED_OPTIONS =
        ["server-passphrase-trayicon-hash", "server-passphrase-trayicon-salt"];

    /// <summary>
    /// Options that should not be returned to the client for security reasons
    /// </summary>
    private static readonly IReadOnlySet<string> GUARDED_OUTPUT =
        typeof(Server.Database.ServerSettings.CONST)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.GetCustomAttributes(typeof(GuardedOutputAttribute), false).Any())
        .Select(f => f.GetValue(null)?.ToString())
        .Concat(LEGACY_GUARDED_OPTIONS)
        .WhereNotNullOrWhiteSpace()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Options that should not be set by the client
    /// </summary>
    private static readonly IReadOnlySet<string> GUARDED_INPUT =
        typeof(Server.Database.ServerSettings.CONST)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.GetCustomAttributes(typeof(GuardedInputAttribute), false).Any())
        .Select(f => f.GetValue(null)?.ToString())
        .Concat(LEGACY_GUARDED_OPTIONS)
        .WhereNotNullOrWhiteSpace()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Options that are reported as boolean values, but are stored as strings
    /// </summary>
    private static readonly IReadOnlySet<string> BOOLEAN_MAPPED_OUTPUT =
        typeof(Server.Database.ServerSettings.CONST)
        .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.GetCustomAttributes(typeof(BooleanOutputAttribute), false).Any())
        .Select(f => f.GetValue(null)?.ToString())
        .WhereNotNullOrWhiteSpace()
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public Abstractions.ServerSettings GetSettings()
        => new Abstractions.ServerSettings(connection.ApplicationSettings);

    public Dictionary<string, string> GetSettingsMasked()
    {
        // Join server settings and global settings
        var dict =
            connection.GetSettings(Connection.SERVER_SETTINGS_ID)
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Union(
                    connection.Settings
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name) && x.Name.StartsWith("--", StringComparison.Ordinal))
                )
                .DistinctBy(x => x.Name)
                .ToDictionary(x => x.Name, x => x.Value);

        // Handle boolean mapped values
        var booleanMap = BOOLEAN_MAPPED_OUTPUT
            .Where(x => dict.ContainsKey(x))
            .ToDictionary(x => x, x => (!string.IsNullOrWhiteSpace(dict[x])).ToString());

        foreach (var key in GUARDED_OUTPUT)
            dict.Remove(key);

        // Replace boolean mapped values, emit even if they are guarded
        foreach (var kvp in booleanMap)
            dict[kvp.Key] = kvp.Value;

        return dict;
    }

    public void PatchSettingsMasked(Dictionary<string, object?>? values)
    {
        if (values == null)
            throw new BadRequestException("No values provided");

        var passphrase = values.GetValueOrDefault(Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE)?.ToString();
        foreach (var key in GUARDED_INPUT.Concat(BOOLEAN_MAPPED_OUTPUT))
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
        if (BOOLEAN_MAPPED_OUTPUT.Contains(key))
        {
            var value = connection.GetSettings(Connection.SERVER_SETTINGS_ID)
                .FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase))?.Value;
            return (!string.IsNullOrWhiteSpace(value)).ToString();
        }

        if (GUARDED_OUTPUT.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
            throw new NotFoundException("Key not found");

        if (key.StartsWith("--", StringComparison.Ordinal))
            return connection.Settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase))?.Value;

        return connection.GetSettings(Connection.SERVER_SETTINGS_ID).FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    public void PatchSettingMasked(string key, string value)
    {
        if (key == Server.Database.ServerSettings.CONST.SERVER_PASSPHRASE)
        {
            connection.ApplicationSettings.SetWebserverPassword(value);
            return;
        }

        if (GUARDED_INPUT.Contains(key) || BOOLEAN_MAPPED_OUTPUT.Contains(key))
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
            var dict = new Dictionary<string, string?>
            {
                { key,  value }
            };
            connection.ApplicationSettings.UpdateSettings(dict, false);
        }
    }
}