// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// The delta token of a drive and the folders it knew at that point with their parents, kept
/// between backups so the path of an item reported by a delta walk can be resolved without a
/// request per ancestor. Entry paths are chains of item ids, so an item's path depends on its
/// ancestors, which an incremental delta response does not include. The map describes the
/// drive as of the token, so the two are stored together. A folder missing from the map costs
/// a lookup, so the map is an optimization and need not be complete.
/// </summary>
internal sealed class DriveFolderMap
{
    /// <summary>
    /// The prefix marking a compressed serialized map
    /// </summary>
    private const string COMPRESSED_PREFIX = "gz:";

    /// <summary>
    /// Serialized maps longer than this are compressed
    /// </summary>
    private const int COMPRESSION_THRESHOLD = 2048;

    /// <summary>
    /// The serialized form
    /// </summary>
    private sealed class Document
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("root")]
        public string? Root { get; set; }

        [JsonPropertyName("folders")]
        public Dictionary<string, string> Folders { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>
    /// The lock guarding the map
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// The parent id of each folder id
    /// </summary>
    private readonly Dictionary<string, string> _parents = new(StringComparer.Ordinal);

    /// <summary>
    /// The delta token the map was built with
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// The id of the drive root item, which is the parent of the top-level items
    /// and is not part of any entry path
    /// </summary>
    public string? RootId { get; set; }

    /// <summary>
    /// The number of folders in the map
    /// </summary>
    public int Count
    {
        get { lock (_lock) return _parents.Count; }
    }

    /// <summary>
    /// Gets the parent of a folder
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="parentId">The parent id</param>
    /// <returns><c>true</c> if the folder is known</returns>
    public bool TryGetParent(string folderId, out string parentId)
    {
        lock (_lock)
            return _parents.TryGetValue(folderId, out parentId!);
    }

    /// <summary>
    /// Whether the folder is known
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <returns><c>true</c> if the folder is known</returns>
    public bool Contains(string folderId)
    {
        lock (_lock)
            return _parents.ContainsKey(folderId);
    }

    /// <summary>
    /// Records or updates the parent of a folder
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <param name="parentId">The parent id</param>
    public void SetParent(string folderId, string parentId)
    {
        lock (_lock)
            _parents[folderId] = parentId;
    }

    /// <summary>
    /// Removes a folder
    /// </summary>
    /// <param name="folderId">The folder id</param>
    /// <returns><c>true</c> if the folder was known</returns>
    public bool Remove(string folderId)
    {
        lock (_lock)
            return _parents.Remove(folderId);
    }

    /// <summary>
    /// Removes the folders whose parent chain does not reach the root, which are the
    /// descendants of removed folders that the delta did not report individually
    /// </summary>
    /// <returns>The number of folders removed</returns>
    public int PruneUnreachable()
    {
        lock (_lock)
        {
            if (RootId == null)
                return 0;

            var reachable = new Dictionary<string, bool>(StringComparer.Ordinal);
            var removed = 0;

            foreach (var id in _parents.Keys.ToList())
            {
                if (!IsReachable(id, reachable))
                {
                    _parents.Remove(id);
                    removed++;
                }
            }

            return removed;
        }
    }

    /// <summary>
    /// Whether the parent chain of a folder reaches the root, with memoization
    /// </summary>
    /// <param name="id">The folder id</param>
    /// <param name="cache">The results found so far</param>
    /// <returns><c>true</c> if the folder is reachable from the root</returns>
    private bool IsReachable(string id, Dictionary<string, bool> cache)
    {
        var chain = new List<string>();
        var current = id;
        bool result;
        while (true)
        {
            if (current == RootId)
            {
                result = true;
                break;
            }

            if (cache.TryGetValue(current, out var known))
            {
                result = known;
                break;
            }

            if (!_parents.TryGetValue(current, out var parent) || chain.Contains(current))
            {
                result = false;
                break;
            }

            chain.Add(current);
            current = parent;
        }

        foreach (var c in chain)
            cache[c] = result;

        return result;
    }

    /// <summary>
    /// Serializes the map for storage, compressing it when it is large
    /// </summary>
    /// <returns>The serialized map</returns>
    public string Serialize()
    {
        string json;
        lock (_lock)
            json = JsonSerializer.Serialize(new Document { Token = Token, Root = RootId, Folders = new Dictionary<string, string>(_parents, StringComparer.Ordinal) });

        if (json.Length < COMPRESSION_THRESHOLD)
            return json;

        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
            gz.Write(Encoding.UTF8.GetBytes(json));

        return COMPRESSED_PREFIX + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Restores a map from its serialized form
    /// </summary>
    /// <param name="value">The serialized map</param>
    /// <returns>The map, or <c>null</c> if the value cannot be read</returns>
    public static DriveFolderMap? Deserialize(string value)
    {
        try
        {
            var json = value;
            if (value.StartsWith(COMPRESSED_PREFIX, StringComparison.Ordinal))
            {
                using var ms = new MemoryStream(Convert.FromBase64String(value.Substring(COMPRESSED_PREFIX.Length)));
                using var gz = new GZipStream(ms, CompressionMode.Decompress);
                using var reader = new StreamReader(gz, Encoding.UTF8);
                json = reader.ReadToEnd();
            }

            var doc = JsonSerializer.Deserialize<Document>(json);
            if (doc == null || string.IsNullOrWhiteSpace(doc.Root) || string.IsNullOrWhiteSpace(doc.Token))
                return null;

            var map = new DriveFolderMap { Token = doc.Token, RootId = doc.Root };
            foreach (var kv in doc.Folders)
                map._parents[kv.Key] = kv.Value;

            return map;
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidDataException)
        {
            return null;
        }
    }
}
