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

using System.Runtime.CompilerServices;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots.Windows;

namespace Duplicati.Library.SourceProvider.Builtin.HyperV
{
    /// <summary>
    /// The root entry of the Hyper-V virtual hierarchy, mounted at <c>%HYPERV%\</c>.
    /// Enumerating it yields one folder per virtual machine selected for backup.
    /// </summary>
    internal class HyperVRootEntry(string path, IReadOnlyList<HyperVGuest> guests, ISnapshotService? snapshotService)
        : HyperVEntryBase(Util.AppendDirSeparator(path))
    {
        /// <inheritdoc />
        public override bool IsRootEntry => true;

        /// <inheritdoc />
        public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>
            {
                { "hyperv:v", "1" },
                { "hyperv:Type", "HyperVRoot" },
                { "hyperv:Name", "Hyper-V Machines" },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var guest in guests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new HyperVGuestEntry(Path, guest, snapshotService);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A virtual folder representing a single Hyper-V virtual machine.
    /// The folder name is the VM's GUID; the friendly name is exposed via
    /// the <c>hyperv:Name</c> metadata key.
    /// Enumerating it yields the VM's data paths (configuration, disks, snapshots)
    /// mapped into the virtual hierarchy.
    /// </summary>
    internal class HyperVGuestEntry(string parentPath, HyperVGuest guest, ISnapshotService? snapshotService)
        : HyperVEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, guest.ID.ToString())))
    {
        /// <summary>
        /// The guest represented by this entry
        /// </summary>
        private readonly HyperVGuest _guest = guest;

        /// <summary>
        /// The snapshot service used to read the underlying files, null when browsing without a snapshot
        /// </summary>
        private readonly ISnapshotService? _snapshotService = snapshotService;

        /// <inheritdoc />
        public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>
            {
                { "hyperv:v", "1" },
                { "hyperv:Type", "VirtualMachine" },
                { "hyperv:Name", _guest.Name },
                { "hyperv:Id", _guest.ID.ToString() },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_snapshotService == null)
                throw new InvalidOperationException("Cannot enumerate Hyper-V guest files without a snapshot service");

            foreach (var dataPath in _guest.DataPaths ?? Enumerable.Empty<string>())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var isFolder = dataPath.EndsWith(System.IO.Path.DirectorySeparatorChar) || _snapshotService.DirectoryExists(dataPath);
                var entry = _snapshotService.GetFilesystemEntry(dataPath, isFolder);
                if (entry == null)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "HyperVDataPathMissing", null, "Hyper-V data path not found, skipping: {0}", dataPath);
                    continue;
                }

                yield return HyperVMappedEntry.Create(entry, this.Path, _snapshotService);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Base class for the virtual folder entries in the Hyper-V hierarchy
    /// </summary>
    internal abstract class HyperVEntryBase(string path) : ISourceProviderEntry
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        protected static readonly string LOGTAG = Logging.Log.LogTagFromType<HyperVEntryBase>();

        /// <inheritdoc />
        public bool IsFolder => true;

        /// <inheritdoc />
        public virtual bool IsMetaEntry => false;

        /// <inheritdoc />
        public virtual bool IsRootEntry => false;

        /// <inheritdoc />
        public DateTime CreatedUtc => DateTime.UnixEpoch;

        /// <inheritdoc />
        public DateTime LastModificationUtc => DateTime.UnixEpoch;

        /// <inheritdoc />
        public string Path => path;

        /// <inheritdoc />
        public long Size => -1;

        /// <inheritdoc />
        public bool IsSymlink => false;

        /// <inheritdoc />
        public string? SymlinkTarget => null;

        /// <inheritdoc />
        public FileAttributes Attributes => FileAttributes.Directory;

        /// <inheritdoc />
        public bool IsBlockDevice => false;

        /// <inheritdoc />
        public bool IsCharacterDevice => false;

        /// <inheritdoc />
        public bool IsAlternateStream => false;

        /// <inheritdoc />
        public string? HardlinkTargetId => null;

        /// <inheritdoc />
        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => throw new NotSupportedException("Cannot read from a folder");

        /// <inheritdoc />
        public virtual Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>());

        /// <inheritdoc />
        public virtual Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => Task.FromResult(false);

        /// <inheritdoc />
        public abstract IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Wraps a snapshot-backed filesystem entry and exposes it under a virtual
    /// Hyper-V path (<c>%HYPERV%\&lt;guid&gt;\&lt;original path&gt;</c>).
    /// All file operations are delegated to the wrapped entry.
    /// </summary>
    internal class HyperVMappedEntry : ISourceProviderEntry
    {
        /// <summary>
        /// The wrapped snapshot entry
        /// </summary>
        private readonly ISourceProviderEntry _inner;

        /// <summary>
        /// The virtual path prefix of the VM this entry belongs to (e.g. <c>%HYPERV%\&lt;guid&gt;\</c>)
        /// </summary>
        private readonly string _vmPrefix;

        /// <summary>
        /// The snapshot service, used to resolve children
        /// </summary>
        private readonly ISnapshotService _snapshotService;

        /// <summary>
        /// The mapped virtual path of this entry
        /// </summary>
        private readonly string _mappedPath;

        /// <summary>
        /// Creates a new mapped entry
        /// </summary>
        /// <param name="inner">The snapshot-backed entry to wrap</param>
        /// <param name="vmPrefix">The virtual path prefix of the VM (ends with a directory separator)</param>
        /// <param name="mappedPath">The mapped virtual path of this entry</param>
        /// <param name="snapshotService">The snapshot service</param>
        private HyperVMappedEntry(ISourceProviderEntry inner, string vmPrefix, string mappedPath, ISnapshotService snapshotService)
        {
            _inner = inner;
            _vmPrefix = Util.AppendDirSeparator(vmPrefix);
            _mappedPath = mappedPath;
            _snapshotService = snapshotService;
        }

        /// <summary>
        /// Creates a mapped entry for a snapshot entry, computing the mapped path
        /// </summary>
        /// <param name="inner">The snapshot-backed entry to wrap</param>
        /// <param name="vmPrefix">The virtual path prefix of the VM (ends with a directory separator)</param>
        /// <param name="snapshotService">The snapshot service</param>
        /// <returns>The mapped entry</returns>
        public static HyperVMappedEntry Create(ISourceProviderEntry inner, string vmPrefix, ISnapshotService snapshotService)
        {
            var mapped = Util.AppendDirSeparator(vmPrefix) + inner.Path;
            if (inner.IsFolder)
                mapped = Util.AppendDirSeparator(mapped);

            return new HyperVMappedEntry(inner, vmPrefix, mapped, snapshotService);
        }

        /// <inheritdoc />
        public bool IsFolder => _inner.IsFolder;

        /// <inheritdoc />
        public bool IsMetaEntry => _inner.IsMetaEntry;

        /// <inheritdoc />
        public bool IsRootEntry => false;

        /// <inheritdoc />
        public DateTime CreatedUtc => _inner.CreatedUtc;

        /// <inheritdoc />
        public DateTime LastModificationUtc => _inner.LastModificationUtc;

        /// <inheritdoc />
        public string Path => _mappedPath;

        /// <inheritdoc />
        public long Size => _inner.Size;

        /// <inheritdoc />
        public bool IsSymlink => _inner.IsSymlink;

        /// <inheritdoc />
        public string? SymlinkTarget => _inner.SymlinkTarget;

        /// <inheritdoc />
        public FileAttributes Attributes => _inner.Attributes;

        /// <inheritdoc />
        public bool IsBlockDevice => _inner.IsBlockDevice;

        /// <inheritdoc />
        public bool IsCharacterDevice => _inner.IsCharacterDevice;

        /// <inheritdoc />
        public bool IsAlternateStream => _inner.IsAlternateStream;

        /// <inheritdoc />
        public string? HardlinkTargetId => _inner.HardlinkTargetId;

        /// <inheritdoc />
        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => _inner.OpenRead(cancellationToken);

        /// <inheritdoc />
        public async Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        {
            var metadata = await _inner.GetMinorMetadata(cancellationToken).ConfigureAwait(false) ?? [];
            metadata["hyperv:v"] = "1";
            metadata["hyperv:Type"] = IsFolder ? "Folder" : "File";
            return metadata;
        }

        /// <inheritdoc />
        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => _inner.FileExists(filename, cancellationToken);

        /// <inheritdoc />
        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var child in _inner.Enumerate(cancellationToken).ConfigureAwait(false))
                yield return Create(child, _vmPrefix, _snapshotService);
        }
    }
}
