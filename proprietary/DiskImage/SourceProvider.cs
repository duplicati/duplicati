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

public sealed class SourceProvider : ISourceProviderModule, IDisposable
{
    private readonly string _devicePath;
    private IRawDisk? _disk;
    private bool _disposed;

    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    public SourceProvider()
    {
        _devicePath = null!;
    }

    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
    {
        if (!string.IsNullOrEmpty(mountPoint))
            throw new UserInformationException("Mount point option is not supported for DiskImage provider. The entire disk will be mounted/treated as root.", "MountPointNotSupported");

        var uri = new Library.Utility.Uri(url);
        _devicePath = uri.HostAndPath;
    }

    public string MountedPath => $"root{System.IO.Path.DirectorySeparatorChar}";

    public string DisplayName => Strings.ProviderDisplayName;

    public string Description => Strings.ProviderDescription;

    public string Key => OptionsHelper.ModuleKey;

    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

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

    public Task Test(CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");

        var root = new DiskSourceEntry(this, _disk);
        yield return root;
    }

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

    private async IAsyncEnumerable<ISourceProviderEntry> EnumerateRecursive(ISourceProviderEntry parent, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return parent;
        if (parent.IsFolder)
        {
            // This is a bit simplified, we need to cast to something that can enumerate
            if (parent is DiskSourceEntry dse)
            {
                await foreach (var child in dse.Enumerate(cancellationToken))
                    await foreach (var e in EnumerateRecursive(child, cancellationToken))
                        yield return e;
            }
            else if (parent is PartitionSourceEntry pse)
            {
                await foreach (var child in pse.Enumerate(cancellationToken))
                    await foreach (var e in EnumerateRecursive(child, cancellationToken))
                        yield return e;
            }
            else if (parent is FilesystemSourceEntry fse)
            {
                await foreach (var child in fse.Enumerate(cancellationToken))
                    await foreach (var e in EnumerateRecursive(child, cancellationToken))
                        yield return e;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disk?.Dispose();
        _disposed = true;
    }
}
