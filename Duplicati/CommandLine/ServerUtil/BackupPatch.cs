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

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Duplicati.CommandLine.ServerUtil;

/// <summary>
/// Applies a partial backup configuration to the configuration a server reports,
/// producing the full configuration that the server stores on an update
/// </summary>
/// <remarks>
/// The patch is a JSON object with an optional <c>Backup</c> and an optional <c>Schedule</c> object.
/// Each property given in <c>Backup</c> replaces the current one, except <c>Settings</c>:
/// when it is an object, each entry sets the setting with that name, and a <c>null</c> value removes it;
/// when it is an array, it replaces all settings.
/// Each property given in <c>Schedule</c> replaces the current one, and <c>"Schedule": null</c> removes the schedule.
/// Anything not mentioned is kept as it is.
/// </remarks>
public static class BackupPatch
{
    /// <summary>
    /// The top-level properties a patch can have
    /// </summary>
    private static readonly string[] TopLevelProperties = ["Backup", "Schedule"];

    /// <summary>
    /// Parses a patch
    /// </summary>
    /// <param name="json">The patch text</param>
    /// <returns>The patch</returns>
    public static JsonObject Parse(string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            throw new UserReportedException($"The update file is not valid JSON: {ex.Message}");
        }

        if (node is not JsonObject patch)
            throw new UserReportedException("The update file must contain a JSON object");

        foreach (var property in patch)
            if (!TopLevelProperties.Contains(property.Key, StringComparer.OrdinalIgnoreCase))
                throw new UserReportedException($"Unsupported property \"{property.Key}\" in the update file, only {string.Join(" and ", TopLevelProperties)} can be updated");

        if (TryGetProperty(patch, "Backup", out var backup) && backup is not JsonObject)
            throw new UserReportedException("Backup in the update file must be an object");

        if (TryGetProperty(patch, "Schedule", out var schedule) && schedule is not null and not JsonObject)
            throw new UserReportedException("Schedule in the update file must be an object or null");

        return patch;
    }

    /// <summary>
    /// Applies a patch to the current configuration
    /// </summary>
    /// <param name="current">The configuration reported by the server, with a <c>Backup</c> and an optional <c>Schedule</c></param>
    /// <param name="patch">The patch to apply, as returned by <see cref="Parse"/></param>
    /// <returns>The configuration to send to the server, with a <c>Backup</c> and a <c>Schedule</c></returns>
    public static JsonObject Apply(JsonObject current, JsonObject patch)
    {
        var backup = GetProperty(current, "Backup")?.DeepClone() as JsonObject
            ?? throw new UserReportedException("The server did not report the backup configuration");
        var schedule = GetProperty(current, "Schedule")?.DeepClone() as JsonObject;

        if (GetProperty(patch, "Backup") is JsonObject backupPatch)
        {
            foreach (var property in backupPatch)
            {
                if (string.Equals(property.Key, "Settings", StringComparison.OrdinalIgnoreCase) && property.Value is JsonObject settingsPatch)
                    PatchSettings(backup, settingsPatch);
                else
                    SetProperty(backup, property.Key, property.Value?.DeepClone());
            }
        }

        if (TryGetProperty(patch, "Schedule", out var schedulePatch))
        {
            if (schedulePatch is JsonObject schedulePatchObject)
            {
                schedule ??= new JsonObject();
                foreach (var property in schedulePatchObject)
                    SetProperty(schedule, property.Key, property.Value?.DeepClone());
            }
            else
            {
                // Sending no schedule makes the server remove it
                schedule = null;
            }
        }

        return new JsonObject
        {
            ["Backup"] = backup,
            ["Schedule"] = schedule
        };
    }

    /// <summary>
    /// Sets or removes the named settings of a backup
    /// </summary>
    /// <param name="backup">The backup to change</param>
    /// <param name="settingsPatch">The settings to set, with <c>null</c> for those to remove</param>
    private static void PatchSettings(JsonObject backup, JsonObject settingsPatch)
    {
        if (GetProperty(backup, "Settings") is not JsonArray settings)
        {
            settings = new JsonArray();
            SetProperty(backup, "Settings", settings);
        }

        foreach (var entry in settingsPatch)
        {
            // Only the settings that apply to the whole backup are addressed by name
            var existing = settings
                .OfType<JsonObject>()
                .Where(x => string.IsNullOrEmpty(GetString(x, "Filter"))
                    && string.Equals(GetString(x, "Name"), entry.Key, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entry.Value is null)
            {
                foreach (var item in existing)
                    settings.Remove(item);
                continue;
            }

            var value = entry.Value.GetValueKind() switch
            {
                JsonValueKind.String => entry.Value.GetValue<string>(),
                JsonValueKind.Number => entry.Value.ToJsonString(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => throw new UserReportedException($"The value of the setting \"{entry.Key}\" must be a string, a number, a boolean or null")
            };

            if (existing.Count == 0)
                settings.Add(new JsonObject
                {
                    ["Filter"] = "",
                    ["Name"] = entry.Key,
                    ["Value"] = value
                });
            else
                foreach (var item in existing)
                    SetProperty(item, "Value", value);
        }
    }

    /// <summary>
    /// Gets a string property, matching the name without regard to case
    /// </summary>
    /// <param name="obj">The object to read</param>
    /// <param name="name">The name of the property</param>
    /// <returns>The value, or <c>null</c> if it is missing or not a string</returns>
    private static string? GetString(JsonObject obj, string name)
        => GetProperty(obj, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>
    /// Gets a property, matching the name without regard to case
    /// </summary>
    /// <param name="obj">The object to read</param>
    /// <param name="name">The name of the property</param>
    /// <returns>The value, or <c>null</c> if it is missing</returns>
    private static JsonNode? GetProperty(JsonObject obj, string name)
        => TryGetProperty(obj, name, out var value) ? value : null;

    /// <summary>
    /// Gets a property, matching the name without regard to case
    /// </summary>
    /// <param name="obj">The object to read</param>
    /// <param name="name">The name of the property</param>
    /// <param name="value">The value</param>
    /// <returns><c>true</c> if the property is present, even with a <c>null</c> value</returns>
    private static bool TryGetProperty(JsonObject obj, string name, out JsonNode? value)
    {
        foreach (var property in obj)
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }

        value = null;
        return false;
    }

    /// <summary>
    /// Sets a property, replacing an existing one whose name differs only in case
    /// </summary>
    /// <param name="obj">The object to change</param>
    /// <param name="name">The name of the property</param>
    /// <param name="value">The value</param>
    private static void SetProperty(JsonObject obj, string name, JsonNode? value)
    {
        var key = obj.Select(x => x.Key).FirstOrDefault(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase)) ?? name;
        obj[key] = value;
    }
}
