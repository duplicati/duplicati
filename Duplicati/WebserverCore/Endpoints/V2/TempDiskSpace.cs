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
using Duplicati.WebserverCore.Dto.V2;
using Microsoft.AspNetCore.Mvc;

namespace Duplicati.WebserverCore.Endpoints.V2;

/// <summary>
/// Endpoint for checking temporary disk space
/// </summary>
public class TempDiskSpace : IEndpointV2
{
    /// <summary>
    /// Maps the endpoint routes
    /// </summary>
    /// <param name="group">The route group builder</param>
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/system/temp-disk-space", ([FromServices] Connection connection, [FromBody] TempDiskSpaceRequestDto input)
            => Execute(connection, input))
            .RequireAuthorization();
    }

    /// <summary>
    /// Executes the temp disk space check
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="input">The request input</param>
    /// <returns>The response DTO</returns>
    private static TempDiskSpaceResponseDto Execute(Connection connection, TempDiskSpaceRequestDto input)
    {
        try
        {
            // Get the merged options for the backup (or defaults)
            var options = GetMergedOptions(connection, input.BackupId);

            // Determine the temp folder path from the merged options
            var tempPath = GetTempPath(options);

            // Get the free space for the temp path
            var spaceInfo = Utility.GetFreeSpaceForPath(tempPath);

            // Get the default options
            var defaultOptions = new Library.Main.Options(new Dictionary<string, string?>());

            // Get dblock-size and restore-cache-max from the options
            var dblockSize = Sizeparser.ParseSize(
                options.TryGetValue("dblock-size", out var dblockVal) && !string.IsNullOrWhiteSpace(dblockVal)
                    ? dblockVal
                    : $"{defaultOptions.VolumeSize}b",
                "mb");

            var restoreCacheMax = Sizeparser.ParseSize(
                options.TryGetValue("restore-cache-max", out var cacheVal) && !string.IsNullOrWhiteSpace(cacheVal)
                    ? cacheVal
                    : $"{defaultOptions.RestoreCacheMax}b",
                "mb");

            return TempDiskSpaceResponseDto.Create(
                tempPath: tempPath,
                freeSpace: spaceInfo?.FreeSpace,
                totalSpace: spaceInfo?.TotalSpace,
                dblockSize: dblockSize,
                restoreCacheMax: restoreCacheMax
            );
        }
        catch (Exception ex)
        {
            return TempDiskSpaceResponseDto.Failure(
                error: ex.Message,
                statusCode: "error-checking-temp-space"
            );
        }
    }

    /// <summary>
    /// Gets the merged options dictionary, combining application-level settings
    /// with backup-specific settings (if a backupId is provided).
    /// </summary>
    /// <param name="connection">The database connection</param>
    /// <param name="backupId">The optional backup ID</param>
    /// <returns>The merged options dictionary</returns>
    private static Dictionary<string, string?> GetMergedOptions(Connection connection, string? backupId)
    {
        // Start with application-level settings (ANY_BACKUP_ID)
        var options = connection.Settings
            .GroupBy(
                k => k.Name.StartsWith("--", StringComparison.Ordinal) ? k.Name.Substring(2) : k.Name,
                k => (string?)k.Value
            )
            .ToDictionary(g => g.Key, g => g.FirstOrDefault());

        // If a backupId is provided, overlay the backup-specific settings
        if (!string.IsNullOrWhiteSpace(backupId))
        {
            var backup = connection.GetBackup(backupId);
            if (backup?.Settings != null)
            {
                foreach (var setting in backup.Settings)
                {
                    var name = setting.Name.StartsWith("--", StringComparison.Ordinal)
                        ? setting.Name.Substring(2)
                        : setting.Name;
                    options[name] = setting.Value;
                }
            }
        }

        return options;
    }

    /// <summary>
    /// Gets the temporary folder path from the merged options.
    /// Falls back to the system default temp path if no override is configured.
    /// </summary>
    /// <param name="options">The merged options dictionary</param>
    /// <returns>The temp folder path</returns>
    private static string GetTempPath(Dictionary<string, string?> options)
    {
        if (options.TryGetValue("tempdir", out var tempDir) && !string.IsNullOrWhiteSpace(tempDir))
            return tempDir;

        return TempFolder.SystemTempPath;
    }
}
