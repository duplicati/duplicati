// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.Partition;
using Duplicati.Proprietary.DiskImage.SourceItems;

namespace Duplicati.Proprietary.DiskImage;

/// <summary>
/// Source provider for disk images. Provides access to disk, partition, and filesystem structures
/// as a virtual folder hierarchy for backup operations.
/// </summary>
public sealed class SourceProvider : ISourceProviderModule, IDisposable
{
    /// <summary>
    /// The path to the disk device.
    /// </summary>
    private readonly string _devicePath;

    /// <summary>
    /// The disk object representing the physical disk.
    /// </summary>
    private IRawDisk? _disk;

    /// <summary>
    /// Indicates whether the provider has been disposed.
    /// </summary>
    private bool _disposed;

    /// <summary>
    /// Cache for source provider entries to optimize repeated access. Keyed by entry path.
    /// </summary>
    /// <remarks>
    /// This cache is populated on demand when entries are accessed via GetEntry to avoid having to re-enumerate the disk structure.
    /// </remarks>
    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class.
    /// Default constructor for metadata loading.
    /// </summary>
    public SourceProvider()
    {
        _devicePath = null!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class with the specified URL and options.
    /// </summary>
    /// <param name="url">The device URL (e.g., "diskimage://\\.\PhysicalDrive0").</param>
    /// <param name="mountPoint">The mount point (not supported for disk images).</param>
    /// <param name="options">Provider options.</param>
    /// <exception cref="UserInformationException">Thrown when mount point is specified.</exception>
    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
    {
        if (!string.IsNullOrEmpty(mountPoint))
            throw new UserInformationException("Mount point option is not supported for DiskImage provider. The entire disk will be mounted/treated as root.", "MountPointNotSupported");

        var uri = new Library.Utility.Uri(url);
        _devicePath = uri.HostAndPath;
    }

    /// <inheritdoc />
    public string MountedPath => $"root{System.IO.Path.DirectorySeparatorChar}";

    /// <inheritdoc />
    public string DisplayName => Strings.ProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.ProviderDescription;

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

    /// <inheritdoc />
    public async Task Initialize(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_devicePath))
            throw new UserInformationException("Disk device path is not specified.", "DiskDeviceNotSpecified");

        if (OperatingSystem.IsWindows())
        {
            _disk = new Windows(_devicePath);
            if (!await _disk.InitializeAsync(cancellationToken))
                throw new UserInformationException($"Failed to initialize disk: {_devicePath}", "DiskInitializeFailed");
        }
        else
        {
            throw new PlatformNotSupportedException("DiskImage source provider is currently only supported on Windows.");
        }
    }

    /// <inheritdoc />
    public Task Test(CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");

        var root = new DiskSourceEntry(this, _disk);
        yield return root;
    }

    /// <inheritdoc />
    public async Task<ISourceProviderEntry?> GetEntry(string path, bool isFolder, CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");

        if (_entryCache.TryGetValue(path, out var cachedEntry))
            return cachedEntry;

        // Simple implementation: enumerate from root to find the entry
        // In a real implementation, we would parse the path and resolve it efficiently
        await foreach (var entry in EnumerateRecursive(new DiskSourceEntry(this, _disk), cancellationToken))
        {
            if (entry.Path == path && entry.IsFolder == isFolder)
            {
                _entryCache[path] = entry;
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// Recursively enumerates entries starting from the specified parent entry.
    /// </summary>
    /// <param name="parent">The parent entry to enumerate from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An async enumerable of all entries in the hierarchy.</returns>
    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateRecursive(ISourceProviderEntry parent, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return parent;
        if (parent.IsFolder && parent is DiskImageEntryBase dse)
        {
            await foreach (var child in dse.Enumerate(cancellationToken))
                await foreach (var e in EnumerateRecursive(child, cancellationToken))
                    yield return e;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disk?.Dispose();
        _disposed = true;
    }
}
