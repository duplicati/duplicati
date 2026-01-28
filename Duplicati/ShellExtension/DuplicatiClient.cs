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

using System.Net.Http.Headers;
using System.Text.Json;

namespace Duplicati.ShellExtension;

/// <summary>
/// Simple client for communicating with the Duplicati server to check folder backup status
/// </summary>
public sealed class DuplicatiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private bool _disposed;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private Dictionary<string, FolderStatusInfo> _folderStatusCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>
    /// Information about a folder's backup status
    /// </summary>
    public record FolderStatusInfo(
        FolderBackupStatus Status,
        string? BackupName,
        DateTime? LastBackupTime,
        string? BackupId
    );

    /// <summary>
    /// Creates a new instance of the Duplicati client
    /// </summary>
    /// <param name="baseUrl">The base URL of the Duplicati server (default: http://localhost:8200)</param>
    public DuplicatiClient(string? baseUrl = null)
    {
        _baseUrl = (baseUrl ?? GetServerUrl()).TrimEnd('/');
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
    }

    /// <summary>
    /// Gets the Duplicati server URL from settings or returns default
    /// </summary>
    private static string GetServerUrl()
    {
        // Try to read from registry or config file
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Duplicati\ShellExtension");
            if (key != null)
            {
                var url = key.GetValue("ServerUrl") as string;
                if (!string.IsNullOrEmpty(url))
                    return url;
            }
        }
        catch
        {
            // Ignore registry errors
        }

        return "http://localhost:8200";
    }

    /// <summary>
    /// Sets the authentication token for API requests
    /// </summary>
    /// <param name="token">The bearer token</param>
    public void SetAuthToken(string token)
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Gets the backup status for a specific folder path
    /// </summary>
    /// <param name="folderPath">The full path to the folder</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The folder's backup status information</returns>
    public async Task<FolderStatusInfo> GetFolderStatusAsync(
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        // Normalize the path
        folderPath = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar);

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Check if cache is still valid
            if (DateTime.UtcNow - _lastCacheTime > CacheExpiration)
            {
                await RefreshCacheAsync(cancellationToken);
            }

            // Look up the folder in the cache
            if (_folderStatusCache.TryGetValue(folderPath, out var status))
                return status;

            // Check if any cached folder is a parent of this folder
            foreach (var kvp in _folderStatusCache)
            {
                if (folderPath.StartsWith(kvp.Key + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            return new FolderStatusInfo(FolderBackupStatus.NotInBackup, null, null, null);
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Refreshes the folder status cache from the server
    /// </summary>
    private async Task RefreshCacheAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Get folder status from the dedicated API endpoint
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/v1/folderstatus",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // If API not available, fall back to backup listing
                await RefreshCacheFromBackupsAsync(cancellationToken);
                return;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusList = JsonSerializer.Deserialize<FolderStatusResponse[]>(content, _jsonOptions);

            _folderStatusCache.Clear();
            if (statusList != null)
            {
                foreach (var item in statusList)
                {
                    if (!string.IsNullOrEmpty(item.Path))
                    {
                        var normalizedPath = Path.GetFullPath(item.Path).TrimEnd(Path.DirectorySeparatorChar);
                        _folderStatusCache[normalizedPath] = new FolderStatusInfo(
                            ParseStatus(item.Status),
                            item.BackupName,
                            item.LastBackupTime,
                            item.BackupId
                        );
                    }
                }
            }

            _lastCacheTime = DateTime.UtcNow;
        }
        catch
        {
            // If we can't reach the server, keep the old cache
            if (_folderStatusCache.Count == 0)
            {
                _lastCacheTime = DateTime.UtcNow;
            }
        }
    }

    /// <summary>
    /// Fallback method to refresh cache from backup listings
    /// </summary>
    private async Task RefreshCacheFromBackupsAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"{_baseUrl}/api/v1/backups",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
                return;

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var backups = JsonSerializer.Deserialize<BackupInfo[]>(content, _jsonOptions);

            _folderStatusCache.Clear();
            if (backups != null)
            {
                foreach (var backup in backups)
                {
                    if (backup.Backup?.Sources != null)
                    {
                        var status = DetermineBackupStatus(backup);
                        foreach (var source in backup.Backup.Sources)
                        {
                            if (!string.IsNullOrEmpty(source))
                            {
                                var normalizedPath = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
                                _folderStatusCache[normalizedPath] = new FolderStatusInfo(
                                    status,
                                    backup.Backup.Name,
                                    backup.Backup.Metadata?.TryGetValue("LastBackupDate", out var dateStr) == true
                                        ? DateTime.TryParse(dateStr, out var date) ? date : null
                                        : null,
                                    backup.Backup.ID
                                );
                            }
                        }
                    }
                }
            }

            _lastCacheTime = DateTime.UtcNow;
        }
        catch
        {
            // Silently fail - server might not be running
        }
    }

    private static FolderBackupStatus DetermineBackupStatus(BackupInfo backup)
    {
        // Check if backup has metadata about last run
        if (backup.Backup?.Metadata == null)
            return FolderBackupStatus.NeverBackedUp;

        if (!backup.Backup.Metadata.TryGetValue("LastBackupDate", out var lastDateStr) ||
            string.IsNullOrEmpty(lastDateStr))
            return FolderBackupStatus.NeverBackedUp;

        // Check for error/warning status
        if (backup.Backup.Metadata.TryGetValue("LastBackupError", out var error) &&
            !string.IsNullOrEmpty(error))
            return FolderBackupStatus.BackupFailed;

        if (backup.Backup.Metadata.TryGetValue("LastBackupWarning", out var warning) &&
            !string.IsNullOrEmpty(warning))
            return FolderBackupStatus.BackedUpWithWarning;

        return FolderBackupStatus.BackedUp;
    }

    private static FolderBackupStatus ParseStatus(string? status)
    {
        return status?.ToLowerInvariant() switch
        {
            "backedup" => FolderBackupStatus.BackedUp,
            "warning" => FolderBackupStatus.BackedUpWithWarning,
            "failed" => FolderBackupStatus.BackupFailed,
            "inprogress" => FolderBackupStatus.BackupInProgress,
            "never" => FolderBackupStatus.NeverBackedUp,
            _ => FolderBackupStatus.NotInBackup
        };
    }

    /// <summary>
    /// Disposes the client and releases resources
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _httpClient.Dispose();
            _cacheLock.Dispose();
            _disposed = true;
        }
    }

    // Response DTOs for JSON deserialization
    private record FolderStatusResponse(
        string? Path,
        string? Status,
        string? BackupName,
        DateTime? LastBackupTime,
        string? BackupId
    );

    private record BackupInfo(BackupData? Backup);

    private record BackupData(
        string? ID,
        string? Name,
        string[]? Sources,
        Dictionary<string, string>? Metadata
    );
}
