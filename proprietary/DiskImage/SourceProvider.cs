// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Proprietary.DiskImage.Disk;
using Duplicati.Proprietary.DiskImage.General;
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
    /// The mount point for the provider. For disk images, this is typically not used since the entire disk is treated as root.
    /// </summary>
    private string _mountPoint = string.Empty;

    /// <summary>
    /// Cache for source provider entries to optimize repeated access. Keyed by entry path.
    /// </summary>
    /// <remarks>
    /// This cache is populated on demand when entries are accessed via GetEntry to avoid having to re-enumerate the disk structure.
    /// </remarks>
    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    /// <summary>
    /// Indicates whether to treat filesystems as unknown (force raw block-based backup).
    /// </summary>
    private readonly bool _treatFilesystemAsUnknown;

    /// <summary>
    /// The subpath within the disk hierarchy that the device URL points to
    /// (the part after the device name, e.g. a partition), or empty if the URL targets the whole disk.
    /// </summary>
    private string _subpath = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class.
    /// Default constructor for metadata loading.
    /// </summary>
    public SourceProvider()
    {
        _devicePath = null!;
        _treatFilesystemAsUnknown = false;
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
        _mountPoint = mountPoint;

        var uri = new Library.Utility.RelaxedUri(url);
        _devicePath = uri.HostAndPath;

        _treatFilesystemAsUnknown = !Library.Utility.Utility.ParseBoolOption(options, OptionsHelper.DISK_IMAGE_FILESYSTEM_PARSED_OPTION);
    }

    /// <inheritdoc />
    public string MountedPath => _mountPoint;

    /// <inheritdoc />
    public string DisplayName => Strings.ProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.ProviderDescription;

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

    /// <inheritdoc />
    public bool NeedsStoredMetadata => false;

    /// <summary>
    /// Gets a value indicating whether to treat filesystems as unknown (force raw block-based backup).
    /// </summary>
    internal bool TreatFilesystemAsUnknown => _treatFilesystemAsUnknown;

    /// <inheritdoc />
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        // TODO: Should we redesign this? 
        // To support enumerating physical drives, we accept init with no disk
        if (string.IsNullOrEmpty(_devicePath))
            return;

        _disk = (IRawDisk)await GetDiskAsync(_devicePath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<object> GetDiskAsync(string path, CancellationToken cancellationToken)
    {
        var (physicalDrivePath, subpath) = SplitDeviceAndSubpath(path);
        _subpath = subpath;

        if (OperatingSystem.IsWindows())
        {
            var disk = new Windows(physicalDrivePath.TrimEnd(Path.DirectorySeparatorChar));
            var msg = await disk.InitializeAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(msg))
                throw new UserInformationException($"Failed to initialize disk: {physicalDrivePath}, {msg}", "DiskInitializeFailed");

            return disk;
        }
        else if (OperatingSystem.IsMacOS())
        {
            var disk = new Mac(physicalDrivePath);
            var msg = await disk.InitializeAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(msg))
                throw new UserInformationException($"Failed to initialize disk: {physicalDrivePath}, {msg}", "DiskInitializeFailed");

            return disk;
        }
        else if (OperatingSystem.IsLinux())
        {
            var disk = new Linux(physicalDrivePath);
            var msg = await disk.InitializeAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(msg))
                throw new UserInformationException($"Failed to initialize disk: {physicalDrivePath}, {msg}", "DiskInitializeFailed");

            return disk;
        }
        else
        {
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);
        }
    }

    /// <inheritdoc />
    public Task TestAsync(CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");

        yield return new DiskSourceEntry(this, _disk, _subpath);
    }

    /// <inheritdoc />
    public async Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return new MachineRootSourceEntry();

        // Special support for browser enumeration which does call initialize,
        // but does it with an empty path.
        if (_disk == null && string.IsNullOrWhiteSpace(_devicePath) && !string.IsNullOrWhiteSpace(path))
            _disk = (IRawDisk)await GetDiskAsync(path, cancellationToken).ConfigureAwait(false);

        if (_entryCache.TryGetValue(path, out var cachedEntry))
            return cachedEntry;

        if (_disk == null)
            throw new InvalidOperationException("Provider not initialized.");

        var dse = new DiskSourceEntry(this, _disk, "");

        // Workaround for the "root" element in the path
        if (Util.AppendDirSeparator(_disk.DevicePath) == path || _disk.DevicePath == path)
            return dse;

        // Simple implementation: enumerate from root to find the entry
        // In a real implementation, we would parse the path and resolve it efficiently
        await foreach (var entry in EnumerateRecursive(dse, cancellationToken))
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

    /// <summary>
    /// Lists physical drives available on the system. This is a static method that can be used to discover available disks before initializing the provider.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of physical drive information.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown when the platform is not supported.</exception>
    public static IAsyncEnumerable<PhysicalDriveSourceEntry> ListPhysicalDrives(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
            return Windows.ListPhysicalDrivesAsync(cancellationToken)
                .Select(x => new PhysicalDriveSourceEntry(x));
        else if (OperatingSystem.IsMacOS())
            return Mac.ListPhysicalDrivesAsync(cancellationToken)
                .Select(x => new PhysicalDriveSourceEntry(x));
        else if (OperatingSystem.IsLinux())
            return Linux.ListPhysicalDrivesAsync(cancellationToken)
                .Select(x => new PhysicalDriveSourceEntry(x));
        else
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);
    }

    /// <summary>
    /// Gets the platform-specific prefix for disk entries (e.g., "\\.\" on Windows, "/dev/" on Unix). This is used to construct entry paths correctly based on the underlying platform.
    /// </summary>
    /// <returns>The platform-specific prefix for disk entries.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown when the platform is not supported.</exception>
    public static string GetDevicePrefix()
    {
        if (OperatingSystem.IsWindows())
            return Windows.Prefix;
        else if (OperatingSystem.IsMacOS())
            return Mac.Prefix;
        else if (OperatingSystem.IsLinux())
            return Linux.Prefix;
        else
            throw new PlatformNotSupportedException(Strings.PlatformNotSupported);
    }

    /// <summary>
    /// Splits a host-and-path value (e.g. from a device URL) into the disk device
    /// path and an optional subpath within the disk hierarchy (e.g. a partition
    /// segment such as "part_GPT_1").
    /// </summary>
    /// <param name="hostAndPath">The host and path value to split.</param>
    /// <returns>
    /// A tuple with the disk device path and the subpath. The subpath is empty when
    /// the value targets the whole disk.
    /// </returns>
    public static (string DevicePath, string Subpath) SplitDeviceAndSubpath(string hostAndPath)
    {
        var prefix = GetDevicePrefix();
        if (!hostAndPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return (hostAndPath, string.Empty);

        var remainder = hostAndPath[prefix.Length..];
        var separatorIndex = remainder.IndexOf(Path.DirectorySeparatorChar);
        if (separatorIndex < 0)
            return (hostAndPath, string.Empty);

        var devicePath = prefix + remainder[..separatorIndex];
        var subpath = remainder[(separatorIndex + 1)..].Trim(Path.DirectorySeparatorChar);
        return (devicePath, subpath);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disk?.Dispose();
        _disposed = true;
    }
}
