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

using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProviders;
using Duplicati.Library.Utility;

namespace Duplicati.WebserverCore.Endpoints.V1.FilesystemPlugins;

/// <summary>
/// A filesystem plugin that exposes a prefix-based source provider
/// (e.g. <c>%HYPERV%</c> or <c>%MSSQL%</c>) in the source picker tree.
/// The virtual hierarchy is enumerated via the provider without a snapshot
/// service, so entries below the payload level (e.g. individual VM or database
/// files) are not browsable and are presented as leaf nodes.
/// </summary>
public class SourceProviderFilesystemPlugin : IFilesystemPlugin
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SourceProviderFilesystemPlugin>();

    /// <summary>
    /// The prefix module this plugin wraps
    /// </summary>
    private readonly IPrefixedSourceProviderModule _module;

    /// <summary>
    /// The application options
    /// </summary>
    private readonly IReadOnlyDictionary<string, string?> _options;

    /// <summary>
    /// The metadata key used to look up a friendly display name, derived from the module key
    /// </summary>
    private readonly string _nameMetadataKey;

    /// <summary>
    /// Creates a new instance of the plugin
    /// </summary>
    /// <param name="module">The prefix-based source provider module to wrap</param>
    /// <param name="options">The application options</param>
    public SourceProviderFilesystemPlugin(IPrefixedSourceProviderModule module, IReadOnlyDictionary<string, string?> options)
    {
        _module = module;
        _options = options;
        _nameMetadataKey = $"{module.Key}:Name";
    }

    /// <inheritdoc />
    public string RootName => _module.MountedPath.TrimEnd(Path.DirectorySeparatorChar);

    /// <inheritdoc />
    public IEnumerable<Dto.TreeNodeDto> GetEntries(string[] pathSegments)
    {
        if (!_module.IsSupported)
            return [];

        try
        {
            return GetEntriesInternal(pathSegments);
        }
        catch (Exception ex)
        {
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "SourceProviderEnumerationFailed", ex, "Failed to enumerate {0} sources: {1}", _module.Key, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// Enumerates the provider's virtual hierarchy at the requested level
    /// </summary>
    private IEnumerable<Dto.TreeNodeDto> GetEntriesInternal(string[] pathSegments)
    {
        // Browse with a catch-all source so all top-level items are visible
        using var provider = _module.CreateForSources([RootName], _options);
        provider.InitializeAsync(CancellationToken.None).Await();

        // Get the root entry
        ISourceProviderEntry? current = null;
        foreach (var entry in provider.EnumerateAsync(CancellationToken.None).ToBlockingEnumerable())
        {
            current = entry;
            break;
        }

        if (current == null)
            return [];

        // pathSegments[0] is the root name itself (e.g. "%HYPERV%")
        // Navigate to the requested level
        for (var i = 1; i < pathSegments.Length; i++)
        {
            var segment = pathSegments[i];
            var child = current.Enumerate(CancellationToken.None).ToBlockingEnumerable()
                .FirstOrDefault(x => GetEntryName(x).Equals(segment, StringComparison.OrdinalIgnoreCase));

            if (child == null)
                return [];

            current = child;
        }

        // At the filesystem root level (no segments), return the root node itself
        if (pathSegments.Length == 0)
        {
            // Only show the root if there is content
            if (!current.Enumerate(CancellationToken.None).ToBlockingEnumerable().Any())
                return [];

            return [CreateNode(current, isRootNode: true)];
        }

        // Enumerate children of the current entry
        return current.Enumerate(CancellationToken.None).ToBlockingEnumerable()
            .Select(x => CreateNode(x, isRootNode: false))
            .ToList();
    }

    /// <summary>
    /// Creates a tree node from a source provider entry
    /// </summary>
    private Dto.TreeNodeDto CreateNode(ISourceProviderEntry entry, bool isRootNode)
    {
        var metadata = entry.GetMinorMetadata(CancellationToken.None).Await();
        var displayName = metadata.TryGetValue(_nameMetadataKey, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : GetEntryName(entry);

        // Entries that cannot be enumerated further (e.g. VM or database entries
        // that would require a snapshot service) are presented as leaf nodes
        var isLeaf = !entry.IsFolder || !CanEnumerate(entry);

        var id = isRootNode
            ? RootName
            : isLeaf
                ? entry.Path.TrimEnd(Path.DirectorySeparatorChar)
                : entry.Path;

        return new Dto.TreeNodeDto
        {
            id = id,
            text = displayName ?? entry.Path,
            cls = isLeaf ? "file" : "folder",
            iconCls = isLeaf ? "x-tree-icon-leaf" : "x-tree-icon-parent",
            check = false,
            leaf = isLeaf,
            hidden = false,
            systemFile = false,
            temporary = false,
            symlink = false,
            fileSize = -1,
            resolvedpath = null
        };
    }

    /// <summary>
    /// Gets the last path segment of an entry's path
    /// </summary>
    private static string GetEntryName(ISourceProviderEntry entry)
        => entry.Path.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last();

    /// <summary>
    /// Checks if an entry can be enumerated. Entries that require a snapshot service
    /// (e.g. VM or database file entries) cannot be enumerated in browse mode.
    /// </summary>
    private static bool CanEnumerate(ISourceProviderEntry entry)
    {
        try
        {
            return entry.Enumerate(CancellationToken.None).ToBlockingEnumerable().Any();
        }
        catch (InvalidOperationException)
        {
            // Entry requires a snapshot service that is not available in browse mode
            return false;
        }
    }
}
