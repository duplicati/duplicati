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

namespace Duplicati.Library.SourceProvider.Builtin.MSSQL
{
    /// <summary>
    /// The root entry of the MSSQL virtual hierarchy, mounted at <c>%MSSQL%\</c>.
    /// Enumerating it yields one folder per database server.
    /// </summary>
    internal class MSSQLRootEntry(string path, IReadOnlyList<MSSQLDB> databases, ISnapshotService? snapshotService)
        : MSSQLEntryBase(Util.AppendDirSeparator(path))
    {
        /// <summary>
        /// The databases selected for backup
        /// </summary>
        private readonly IReadOnlyList<MSSQLDB> _databases = databases;

        /// <summary>
        /// The snapshot service used to read the underlying files
        /// </summary>
        private readonly ISnapshotService? _snapshotService = snapshotService;

        /// <inheritdoc />
        public override bool IsRootEntry => true;

        /// <inheritdoc />
        public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>
            {
                { "mssql:v", "1" },
                { "mssql:Type", "MsSqlRoot" },
                { "mssql:Name", "Microsoft SQL Servers" },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var server in _databases.Select(x => x.Server).Distinct(Library.Utility.Utility.ClientFilenameStringComparer).OrderBy(x => x))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new MSSQLServerEntry(Path, server, _databases.Where(x => x.Server.Equals(server, Library.Utility.Utility.ClientFilenameStringComparison)).ToList(), _snapshotService);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A virtual folder representing a single database server.
    /// Enumerating it yields one folder per non-default instance,
    /// plus one folder per database on the default (unnamed) instance.
    /// </summary>
    internal class MSSQLServerEntry(string parentPath, string server, IReadOnlyList<MSSQLDB> databases, ISnapshotService? snapshotService)
        : MSSQLEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, server)))
    {
        /// <summary>
        /// The databases on this server selected for backup
        /// </summary>
        private readonly IReadOnlyList<MSSQLDB> _databases = databases;

        /// <summary>
        /// The snapshot service used to read the underlying files
        /// </summary>
        private readonly ISnapshotService? _snapshotService = snapshotService;

        /// <inheritdoc />
        public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>
            {
                { "mssql:v", "1" },
                { "mssql:Type", "Server" },
                { "mssql:Name", server },
                { "mssql:Server", server },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Instances with a name get their own folder
            foreach (var instance in _databases.Where(x => !string.IsNullOrWhiteSpace(x.InstanceId)).Select(x => x.InstanceId).Distinct(Library.Utility.Utility.ClientFilenameStringComparer).OrderBy(x => x))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new MSSQLInstanceEntry(Path, server, instance, _databases.Where(x => x.InstanceId.Equals(instance, Library.Utility.Utility.ClientFilenameStringComparison)).ToList(), _snapshotService);
            }

            // Databases on the default (unnamed) instance are placed directly under the server
            foreach (var db in _databases.Where(x => string.IsNullOrWhiteSpace(x.InstanceId)).OrderBy(x => x.Database))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new MSSQLDatabaseEntry(Path, db, _snapshotService);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A virtual folder representing a named SQL Server instance on a server.
    /// Enumerating it yields one folder per database on the instance.
    /// </summary>
    internal class MSSQLInstanceEntry(string parentPath, string server, string instanceId, IReadOnlyList<MSSQLDB> databases, ISnapshotService? snapshotService)
        : MSSQLEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, instanceId)))
    {
        /// <summary>
        /// The databases on this instance selected for backup
        /// </summary>
        private readonly IReadOnlyList<MSSQLDB> _databases = databases;

        /// <summary>
        /// The snapshot service used to read the underlying files
        /// </summary>
        private readonly ISnapshotService? _snapshotService = snapshotService;

        /// <inheritdoc />
        public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>
            {
                { "mssql:v", "1" },
                { "mssql:Type", "Instance" },
                { "mssql:Name", instanceId },
                { "mssql:Server", server },
                { "mssql:Instance", instanceId },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var db in _databases.OrderBy(x => x.Database))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new MSSQLDatabaseEntry(Path, db, _snapshotService);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A virtual folder representing a single database.
    /// Enumerating it yields the database's data paths (data files, log files)
    /// mapped into the virtual hierarchy.
    /// </summary>
    internal class MSSQLDatabaseEntry(string parentPath, MSSQLDB database, ISnapshotService? snapshotService)
        : MSSQLEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, database.Database)))
    {
        /// <summary>
        /// The database represented by this entry
        /// </summary>
        private readonly MSSQLDB _database = database;

        /// <summary>
        /// The snapshot service used to read the underlying files
        /// </summary>
        private readonly ISnapshotService? _snapshotService = snapshotService;

        /// <inheritdoc />
        public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>
            {
                { "mssql:v", "1" },
                { "mssql:Type", "Database" },
                { "mssql:Name", _database.Database },
                { "mssql:Server", _database.Server },
                { "mssql:Instance", _database.InstanceId },
                { "mssql:Database", _database.Database },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_snapshotService == null)
                throw new InvalidOperationException("Cannot enumerate MSSQL database files without a snapshot service");

            foreach (var dataPath in _database.DataPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var isFolder = dataPath.EndsWith(System.IO.Path.DirectorySeparatorChar) || _snapshotService.DirectoryExists(dataPath);
                var entry = _snapshotService.GetFilesystemEntry(dataPath, isFolder);
                if (entry == null)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "MsSqlDataPathMissing", null, "MSSQL data path not found, skipping: {0}", dataPath);
                    continue;
                }

                yield return MSSQLMappedEntry.Create(entry, this.Path);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Base class for the virtual folder entries in the MSSQL hierarchy
    /// </summary>
    internal abstract class MSSQLEntryBase(string path) : ISourceProviderEntry
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        protected static readonly string LOGTAG = Logging.Log.LogTagFromType<MSSQLEntryBase>();

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
    /// MSSQL path (<c>%MSSQL%\&lt;server&gt;\&lt;instance&gt;\&lt;database&gt;\&lt;original path&gt;</c>).
    /// All file operations are delegated to the wrapped entry.
    /// </summary>
    internal class MSSQLMappedEntry : ISourceProviderEntry
    {
        /// <summary>
        /// The wrapped snapshot entry
        /// </summary>
        private readonly ISourceProviderEntry _inner;

        /// <summary>
        /// The virtual path prefix of the database this entry belongs to
        /// </summary>
        private readonly string _dbPrefix;

        /// <summary>
        /// The mapped virtual path of this entry
        /// </summary>
        private readonly string _mappedPath;

        /// <summary>
        /// Creates a new mapped entry
        /// </summary>
        /// <param name="inner">The snapshot-backed entry to wrap</param>
        /// <param name="dbPrefix">The virtual path prefix of the database (ends with a directory separator)</param>
        /// <param name="mappedPath">The mapped virtual path of this entry</param>
        private MSSQLMappedEntry(ISourceProviderEntry inner, string dbPrefix, string mappedPath)
        {
            _inner = inner;
            _dbPrefix = Util.AppendDirSeparator(dbPrefix);
            _mappedPath = mappedPath;
        }

        /// <summary>
        /// Creates a mapped entry for a snapshot entry, computing the mapped path
        /// </summary>
        /// <param name="inner">The snapshot-backed entry to wrap</param>
        /// <param name="dbPrefix">The virtual path prefix of the database (ends with a directory separator)</param>
        /// <returns>The mapped entry</returns>
        public static MSSQLMappedEntry Create(ISourceProviderEntry inner, string dbPrefix)
        {
            var mapped = Util.AppendDirSeparator(dbPrefix) + inner.Path;
            if (inner.IsFolder)
                mapped = Util.AppendDirSeparator(mapped);

            return new MSSQLMappedEntry(inner, dbPrefix, mapped);
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
            metadata["mssql:v"] = "1";
            metadata["mssql:Type"] = IsFolder ? "Folder" : "File";
            return metadata;
        }

        /// <inheritdoc />
        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => _inner.FileExists(filename, cancellationToken);

        /// <inheritdoc />
        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (var child in _inner.Enumerate(cancellationToken).ConfigureAwait(false))
                yield return Create(child, _dbPrefix);
        }
    }
}
