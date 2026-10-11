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
    /// The root entry of the MSSQL virtual hierarchy, mounted at <c>\\duplicati\mssql\</c>.
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
                { "mssql:v", MSSQLSourceProvider.METADATA_VERSION },
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
    /// Enumerating it yields one folder per instance; the default (unnamed)
    /// instance uses the name <see cref="MSSQLSourceProvider.DEFAULT_INSTANCE_NAME"/>.
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
                { "mssql:v", MSSQLSourceProvider.METADATA_VERSION },
                { "mssql:Type", "Server" },
                { "mssql:Name", server },
                { "mssql:Server", server },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var instance in _databases.GroupBy(MSSQLSourceProvider.GetInstanceName, StringComparer.OrdinalIgnoreCase).OrderBy(x => x.Key))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new MSSQLInstanceEntry(Path, server, instance.Key, instance.ToList(), _snapshotService);
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A virtual folder representing a SQL Server instance on a server.
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
                { "mssql:v", MSSQLSourceProvider.METADATA_VERSION },
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
    /// placed directly in the database folder under their own names
    /// (<c>C:\Data\db.mdf</c> becomes <c>&lt;database&gt;\db.mdf</c>), numbered when names clash.
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
                { "mssql:v", MSSQLSourceProvider.METADATA_VERSION },
                { "mssql:Type", "Database" },
                { "mssql:Name", _database.Database },
                { "mssql:Server", _database.Server },
                { "mssql:Instance", MSSQLSourceProvider.GetInstanceName(_database) },
                { "mssql:Database", _database.Database },
            });

        /// <inheritdoc />
        public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (_snapshotService == null)
                throw new InvalidOperationException("Cannot enumerate MSSQL database files without a snapshot service");

            var itemMetadata = new Dictionary<string, string?>
            {
                { "mssql:Server", _database.Server },
                { "mssql:Instance", MSSQLSourceProvider.GetInstanceName(_database) },
                { "mssql:Database", _database.Database },
            };

            // A data path inside another data path would otherwise be produced twice
            var dataPathEntries = new List<ISourceProviderEntry>();
            foreach (var dataPath in VirtualSourcePath.RemoveNestedPaths(_database.DataPaths))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var isFolder = dataPath.EndsWith(System.IO.Path.DirectorySeparatorChar) || _snapshotService.DirectoryExists(dataPath);
                var entry = _snapshotService.GetFilesystemEntry(dataPath, isFolder);
                if (entry == null)
                {
                    Logging.Log.WriteVerboseMessage(LOGTAG, "MsSqlDataPathMissing", null, "MSSQL data path not found, skipping: {0}", dataPath);
                    continue;
                }

                dataPathEntries.Add(entry);
            }

            // The data paths are named together, so clashing names can be numbered
            foreach (var entry in VirtualMappedEntry.MapDataPaths(this.Path, dataPathEntries, MSSQLSourceProvider.METADATA_PREFIX, MSSQLSourceProvider.METADATA_VERSION, itemMetadata))
                yield return entry;

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
}
