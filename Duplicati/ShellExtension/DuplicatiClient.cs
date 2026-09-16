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

namespace Duplicati.ShellExtension;

/// <summary>
/// Client for querying the Duplicati server for folder backup status.
/// Lookups are always served from an in-memory cache so Explorer is never
/// blocked on the network. The cache is refreshed in the background, and
/// Explorer is asked to redraw the folders whose status changed.
/// </summary>
public sealed class DuplicatiClient : IDisposable
{
    /// <summary>
    /// The header carrying the folder status access key.
    /// Must match the name expected by the server (FolderStatusAccessFilter).
    /// </summary>
    private const string AccessKeyHeaderName = "X-Duplicati-FolderStatus-Key";
    /// <summary>
    /// The file in the server data folder holding the access key.
    /// Must match the name written by the server (ServerSettings).
    /// </summary>
    private const string AccessKeyFileName = "folder-status-access-key.txt";
    /// <summary>
    /// The registry key holding optional overrides for the server url and access key
    /// </summary>
    private const string RegistryKeyPath = @"Software\Duplicati\ShellExtension";
    /// <summary>
    /// How long a successfully fetched status list is used before refreshing
    /// </summary>
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(30);
    /// <summary>
    /// How long to wait before retrying after a failed fetch
    /// </summary>
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(10);

    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _lock = new();
    private Dictionary<string, FolderStatusInfo> _folderStatusCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _nextRefreshTime = DateTime.MinValue;
    private Task? _refreshTask;
    private bool _disposed;

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
    /// Reads a string value from the shell extension registry key
    /// </summary>
    private static string? ReadRegistryValue(string name)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            return key?.GetValue(name) as string;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the Duplicati server URL from the registry or returns the default
    /// </summary>
    private static string GetServerUrl()
    {
        var url = ReadRegistryValue("ServerUrl");
        return string.IsNullOrWhiteSpace(url) ? "http://localhost:8200" : url;
    }

    /// <summary>
    /// Reads the access key, preferring a registry override and otherwise
    /// looking in the locations the server may use as its data folder
    /// </summary>
    private static string? ReadAccessKey()
    {
        var fromRegistry = ReadRegistryValue("AccessKey");
        if (!string.IsNullOrWhiteSpace(fromRegistry))
            return fromRegistry.Trim();

        var candidates = new[]
        {
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolder.CommonApplicationData
        };

        foreach (var folder in candidates)
        {
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(folder), "Duplicati", AccessKeyFileName);
                if (!File.Exists(path))
                    continue;

                var key = File.ReadAllText(path).Trim();
                if (key.Length > 0)
                    return key;
            }
            catch
            {
                // Try the next location
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the backup status for a folder from the cache.
    /// Never blocks on the network; a stale cache is refreshed in the background.
    /// </summary>
    /// <param name="folderPath">The full path to the folder</param>
    /// <returns>The folder's backup status information</returns>
    public FolderStatusInfo GetFolderStatus(string folderPath)
    {
        folderPath = NormalizePath(folderPath);
        EnsureCacheIsFresh();

        Dictionary<string, FolderStatusInfo> cache;
        lock (_lock)
            cache = _folderStatusCache;

        if (cache.TryGetValue(folderPath, out var status))
            return status;

        // A folder inside a backed up folder inherits its status
        foreach (var kvp in cache)
            if (folderPath.StartsWith(kvp.Key + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;

        return new FolderStatusInfo(FolderBackupStatus.NotInBackup, null, null, null);
    }

    /// <summary>
    /// Starts a background refresh if the cache is stale and no refresh is running
    /// </summary>
    private void EnsureCacheIsFresh()
    {
        lock (_lock)
        {
            if (_disposed)
                return;
            if (_refreshTask != null && !_refreshTask.IsCompleted)
                return;
            if (DateTime.UtcNow < _nextRefreshTime)
                return;

            _nextRefreshTime = DateTime.UtcNow + RetryInterval;
            _refreshTask = Task.Run(RefreshCacheAsync);
        }
    }

    /// <summary>
    /// Fetches the status list, swaps the cache, and asks Explorer to redraw changed folders
    /// </summary>
    private async Task RefreshCacheAsync()
    {
        var updated = await FetchStatusesAsync().ConfigureAwait(false);

        Dictionary<string, FolderStatusInfo> previous;
        lock (_lock)
        {
            previous = _folderStatusCache;
            if (updated != null)
                _folderStatusCache = updated;
            _nextRefreshTime = DateTime.UtcNow + (updated != null ? CacheExpiration : RetryInterval);
        }

        if (updated != null)
            NotifyChangedFolders(previous, updated);
    }

    /// <summary>
    /// Fetches the folder status list from the server
    /// </summary>
    /// <returns>The statuses keyed by normalized path, or null if the server could not be queried</returns>
    private async Task<Dictionary<string, FolderStatusInfo>?> FetchStatusesAsync()
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/api/v1/folderstatus");
            var accessKey = ReadAccessKey();
            if (accessKey != null)
                request.Headers.TryAddWithoutValidation(AccessKeyHeaderName, accessKey);

            using var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var statusList = JsonSerializer.Deserialize<FolderStatusResponse[]>(content, _jsonOptions);

            var result = new Dictionary<string, FolderStatusInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in statusList ?? [])
            {
                if (string.IsNullOrEmpty(item.Path))
                    continue;

                try
                {
                    result[NormalizePath(item.Path)] = new FolderStatusInfo(
                        ParseStatus(item.Status),
                        item.BackupName,
                        item.LastBackupTime,
                        item.BackupId
                    );
                }
                catch
                {
                    // Skip sources that are not plain paths
                }
            }

            return result;
        }
        catch
        {
            // Server not running or not reachable; keep the current cache and retry later
            return null;
        }
    }

    /// <summary>
    /// Asks Explorer to redraw folders whose status differs between the two cache versions
    /// </summary>
    private static void NotifyChangedFolders(Dictionary<string, FolderStatusInfo> previous, Dictionary<string, FolderStatusInfo> updated)
    {
        foreach (var kvp in updated)
            if (!previous.TryGetValue(kvp.Key, out var old) || old.Status != kvp.Value.Status)
                ShellNotify.UpdateFolder(kvp.Key);

        foreach (var path in previous.Keys)
            if (!updated.ContainsKey(path))
                ShellNotify.UpdateFolder(path);
    }

    /// <summary>
    /// Normalizes a path for comparison
    /// </summary>
    private static string NormalizePath(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
        lock (_lock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _httpClient.Dispose();
    }

    /// <summary>
    /// Response DTO for JSON deserialization
    /// </summary>
    private record FolderStatusResponse(
        string? Path,
        string? Status,
        string? BackupName,
        DateTime? LastBackupTime,
        string? BackupId
    );
}
