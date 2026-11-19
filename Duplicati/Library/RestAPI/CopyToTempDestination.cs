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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.Server.Serialization.Interface;

namespace Duplicati.Library.RestAPI;

/// <summary>
/// Copying a job to a temporary job that reads one of its secondary destinations, so a
/// restore lists and restores the versions that destination holds
/// </summary>
public static class CopyToTempDestination
{
    /// <summary>
    /// The query parameter of the copy request that names the destination
    /// </summary>
    public const string QueryParameter = "destination";

    /// <summary>
    /// The API extension that signals a job can be copied to a temporary job for one of its secondary destinations
    /// </summary>
    public const string ApiExtension = "v1:backup:copytotemp-destination";

    /// <summary>
    /// The options of a secondary destination that belong to the job and are not taken from the destination
    /// </summary>
    private static readonly string[] JobOptions = ["url", "mode", "interval", "dbpath", "passphrase", "encryption-module", "remote-sync-json-config"];

    /// <summary>
    /// Finds the secondary destination a copy request names, before anything is created
    /// </summary>
    /// <param name="backup">The job</param>
    /// <param name="destination">The url key of the destination, or null for the main destination</param>
    /// <returns>The destination, or null for the main destination</returns>
    /// <exception cref="UserInformationException">When the job has no destination with the key</exception>
    public static ITargetUrlEntry? Resolve(IBackup backup, string? destination)
    {
        if (destination == null)
            return null;

        if (string.IsNullOrWhiteSpace(destination))
            throw new UserInformationException("No destination is named. Leave out the parameter to use the main destination.", "CopyToTempDestinationEmpty");

        return (backup.AdditionalTargetURLs ?? []).FirstOrDefault(x => string.Equals(x.TargetUrlKey, destination, StringComparison.Ordinal))
            ?? throw new UserInformationException($"The job has no secondary destination with the key: {destination}", "CopyToTempDestinationUnknown");
    }

    /// <summary>
    /// Points a copy of a job at a secondary destination of it: the url, the saved connection
    /// and the options of the destination replace those of the main destination, and the copy
    /// has no secondary destinations of its own. The passphrase and the other settings of the
    /// job are kept, as the destination holds a copy of the files of the main destination.
    /// </summary>
    /// <param name="copy">The copy of the job</param>
    /// <param name="target">The destination</param>
    public static void Apply(IBackup copy, ITargetUrlEntry target)
    {
        copy.TargetURL = target.TargetUrl;
        copy.ConnectionStringID = target.ConnectionStringID;

        var options = (target.Options ?? [])
            .Where(x => x.Value != null && !JobOptions.Contains(x.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(x => x.Key, x => x.Value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() ?? "" : x.Value.ToString() ?? "", StringComparer.OrdinalIgnoreCase);

        if (options.Count > 0)
        {
            var settings = (copy.Settings ?? [])
                .Where(x => !options.ContainsKey(x.Name.TrimStart('-')))
                .ToList();
            settings.AddRange(options.Select(x => (ISetting)new Server.Database.Setting { Name = "--" + x.Key, Value = x.Value, Filter = "" }));
            copy.Settings = settings.ToArray();
        }

        copy.AdditionalTargetURLs = new List<ITargetUrlEntry>();
    }
}
