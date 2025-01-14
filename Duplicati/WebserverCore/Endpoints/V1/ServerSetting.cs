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

using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V1;

public class ServerSetting : IEndpointV1
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/serversettings", ([FromServices] Connection connection) => GetSettings(connection)).RequireAuthorization();
        group.MapPatch("/serversettings", ([FromServices] Connection connection, [FromBody] Dictionary<string, object?> values) => UpdateSettings(connection, values)).RequireAuthorization();

        group.MapGet("/serversetting/{key}", ([FromRoute] string key, [FromServices] Connection connection, [FromServices] ISettingsService settingsService) => GetSetting(key, connection, settingsService)).RequireAuthorization();
        group.MapPut("/serversetting/{key}", ([FromRoute] string key, [FromBody] string value, [FromServices] Connection connection) => UpdateSetting(key, value, connection)).RequireAuthorization();
    }

    // Remove sensitive information from the output
    private static readonly string[] GUARDED_OUTPUT = [
        Server.Database.ServerSettings.CONST.JWT_CONFIG,
        Server.Database.ServerSettings.CONST.PBKDF_CONFIG,
        Server.Database.ServerSettings.CONST.PRELOAD_SETTINGS_HASH,
        Server.Database.ServerSettings.CONST.REMOTE_CONTROL_CONFIG,
        Server.Database.ServerSettings.CONST.SERVER_SSL_CERTIFICATE,
        Server.Database.ServerSettings.CONST.SERVER_SSL_CERTIFICATEPASSWORD,
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
        "ServerSSLCertificate",
        "server-passphrase-trayicon-hash",
        "server-passphrase-trayicon-salt"
    ];


    private static Dictionary<string, string> GetSettings(Connection connection)
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

        // Patch cert to boolean
        dict.TryGetValue("server-ssl-certificate", out var sslcert);
        dict["server-ssl-certificate"] = (!string.IsNullOrWhiteSpace(sslcert)).ToString();

        foreach (var key in GUARDED_OUTPUT)
            dict.Remove(key);

        return dict;
    }

    private static void UpdateSettings(Connection connection, Dictionary<string, object?>? values)
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

    private static string? GetSetting(string key, Connection connection, ISettingsService settingsService)
    {
        if (key.Equals("server-ssl-certificate", StringComparison.OrdinalIgnoreCase) || key.Equals("ServerSSLCertificate", StringComparison.OrdinalIgnoreCase))
            return settingsService.GetSettings().HasSSLCertificate.ToString();

        if (GUARDED_OUTPUT.Any(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase)))
            throw new NotFoundException("Key not found");


        if (key.StartsWith("--", StringComparison.Ordinal))
        {
            return connection.Settings.FirstOrDefault(x => string.Equals(key, x.Name, StringComparison.OrdinalIgnoreCase))?.Value;
        }
        else
        {
            var prop = connection.ApplicationSettings.GetType().GetProperty(key);
            if (prop == null)
                throw new NotFoundException("Key not found");

            return prop.GetValue(connection.ApplicationSettings)?.ToString();
        }
    }

    private static void UpdateSetting(string key, string value, Connection connection)
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
            var prop = connection.ApplicationSettings.GetType().GetProperty(key);
            if (prop == null)
                throw new NotFoundException("Key not found");

            var dict = new Dictionary<string, string?>
            {
                { key,  value }
            };
            connection.ApplicationSettings.UpdateSettings(dict, false);
        }
    }

}
