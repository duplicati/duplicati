using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Duplicati.Library.SQLiteHelper;
using System.Runtime.CompilerServices;


#nullable enable

namespace Duplicati.Library.Main.Database.Sync;

/// <summary>
/// Local database for the sync operation.
/// </summary>
/// <remarks>
/// The sync database holds three distinct kinds of state, kept separate by purpose:
/// <list type="bullet">
/// <item><c>RemoteInventory</c> - the observed-remote cache: a snapshot of what a
/// <see cref="IBackendManager.ListAsync"/> call returned. It is the diff baseline
/// against the local source and is refreshed by listing. It is NOT written to as
/// part of recording operation intent.</item>
/// <item><c>PendingOperation</c> - the intent journal: rows are inserted BEFORE an
/// upload/update/delete is attempted and removed once it completes or is reconciled.
/// Leftover rows on resume make a crash recoverable: they say exactly what was being
/// attempted, so the program can reconcile against a fresh listing instead of
/// silently dropping state.</item>
/// <item><c>RemoteOperation</c> - an append-only audit log of completed backend calls,
/// purged by time. Distinct from <c>PendingOperation</c>: this records what happened,
/// <c>PendingOperation</c> records what is about to happen.</item>
/// </list>
/// </remarks>
public class LocalSyncDatabase : IDisposable, IBackendManagerDatabase
{
    private readonly SqliteConnection m_connection;

    /// <summary>
    /// The threshold for automatic purging of the <c>RemoteOperation</c> audit log.
    /// </summary>
    private static readonly TimeSpan AuditLogPurgeAge = TimeSpan.FromDays(30);

    /// <summary>
    /// The minimum interval between automatic purges of the <c>RemoteOperation</c>
    /// audit log. Purging on every logged operation would be a full-table scan per
    /// call; throttling to a coarse interval keeps that cost amortized. The
    /// <c>RemoteOperationTimestamp</c> index makes the purge itself cheap when it
    /// does run.
    /// </summary>
    private static readonly TimeSpan AuditLogPurgeInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// The UTC time the audit-log purge last ran, used to throttle purges to at most
    /// once per <see cref="AuditLogPurgeInterval"/>. Defaulted to <c>DateTime.MinValue</c>
    /// so the first log call does not immediately trigger a purge.
    /// </summary>
    private DateTime m_lastAuditPurgeUtc = DateTime.MinValue;

    public LocalSyncDatabase(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir!);

        m_connection = SQLiteLoader.LoadConnection(path);
        try
        {
            DatabaseUpgrader.UpgradeDatabase(m_connection, path, typeof(DatabaseSchemaMarker));
        }
        catch
        {
            m_connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs the supplied action inside a single explicit SQLite transaction, so the
    /// writes it contains commit atomically. Used to make an operation's "mark
    /// complete" writes (inventory update + pending-operation removal) all-or-nothing,
    /// preventing a crash from leaving the inventory and intent journal mutually
    /// inconsistent.
    /// </summary>
    /// <param name="action">The writes to perform inside the transaction.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the transaction has been committed.</returns>
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));

        await using var tx = await m_connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Removes every row from the <c>PendingOperation</c> intent journal.
    /// </summary>
    /// <remarks>
    /// This should only be called once leftover in-flight operations have been
    /// reconciled against a fresh remote listing. Clearing without reconciling
    /// discards the recoverability that the intent journal exists to provide.
    /// </remarks>
    public async Task ClearPendingOperationsAsync(CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"DELETE FROM ""PendingOperation"";";
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts or updates a row in the <c>RemoteInventory</c> observed-state cache.
    /// </summary>
    public async Task UpsertInventoryAsync(string relativePath, long size, DateTime lastModified, string? contentHash, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ""RemoteInventory"" (""RelativePath"", ""Size"", ""LastModified"", ""ContentHash"", ""LastVerified"")
            VALUES (@path, @size, @modified, @hash, @verified)
            ON CONFLICT(""RelativePath"") DO UPDATE SET
                ""Size""=excluded.""Size"",
                ""LastModified""=excluded.""LastModified"",
                ""ContentHash""=excluded.""ContentHash"",
                ""LastVerified""=excluded.""LastVerified"";
        ";
        cmd.Parameters.AddWithValue("@path", relativePath);
        cmd.Parameters.AddWithValue("@size", size);
        cmd.Parameters.AddWithValue("@modified", lastModified.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("@hash", (object?)contentHash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@verified", DateTime.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Records an in-flight operation in the <c>PendingOperation</c> intent journal,
    /// or updates the <c>Attempts</c> counter of an existing row for the same path.
    /// </summary>
    /// <param name="path">The relative path the operation targets on the remote destination.</param>
    /// <param name="operation">The intended operation.</param>
    /// <param name="size">The expected size of the file, if known, for verification on resume.</param>
    /// <param name="hash">The expected hash of the file, if known, for verification on resume.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    public async Task UpsertPendingOperationAsync(string path, SyncOperation operation, long? size, string? hash, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ""PendingOperation"" (""Path"", ""Operation"", ""Size"", ""Hash"", ""StartedAt"", ""Attempts"")
            VALUES (@path, @operation, @size, @hash, @startedAt, 0)
            ON CONFLICT(""Path"") DO UPDATE SET
                ""Operation""=excluded.""Operation"",
                ""Size""=excluded.""Size"",
                ""Hash""=excluded.""Hash"",
                ""StartedAt""=excluded.""StartedAt"",
                ""Attempts""=""PendingOperation"".""Attempts"" + 1;
        ";
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@operation", operation.ToString());
        cmd.Parameters.AddWithValue("@size", (object?)size ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@hash", (object?)hash ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Removes a row from the <c>RemoteInventory</c> observed-state cache.
    /// </summary>
    public async Task RemoveInventoryAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"DELETE FROM ""RemoteInventory"" WHERE ""RelativePath"" = @path;";
        cmd.Parameters.AddWithValue("@path", relativePath);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Removes a row from the <c>PendingOperation</c> intent journal, indicating
    /// that the in-flight operation has completed or been reconciled.
    /// </summary>
    public async Task RemovePendingOperationAsync(string path, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"DELETE FROM ""PendingOperation"" WHERE ""Path"" = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a single inventory item by relative path, or null if no such row exists.
    /// </summary>
    public async Task<InventoryItem?> GetInventoryItemAsync(string relativePath, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"SELECT ""RelativePath"", ""Size"", ""LastModified"", ""ContentHash"" FROM ""RemoteInventory"" WHERE ""RelativePath"" = @path;";
        cmd.Parameters.AddWithValue("@path", relativePath);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new InventoryItem
            {
                RelativePath = reader.GetString(0),
                Size = reader.GetInt64(1),
                LastModified = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                ContentHash = await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3)
            };
        }

        return null;
    }

    /// <summary>
    /// Enumerates the inventory items that are direct children of the given folder.
    /// </summary>
    /// <remarks>
    /// The folder is addressed by its relative path using '/' as the separator,
    /// matching the convention used throughout the sync handler. An empty (or null)
    /// <paramref name="folder"/> denotes the backend root and yields the top-level
    /// entries (relative paths containing no '/'). A non-empty folder yields entries
    /// whose relative path starts with <c>"{folder}/"</c> and contain no further '/'.
    /// Sub-folder entries are NOT included: only file-shaped inventory rows are
    /// returned, since the sync handler manages folders implicitly via
    /// <see cref="IBackendManager.EnsureFolderAsync"/>. This is the per-folder
    /// building block the folder-by-folder sync uses to read the local-database diff
    /// baseline under <see cref="SyncRemoteState.UseLocalState"/> without ever
    /// materializing the whole inventory into memory.
    /// </remarks>
    /// <param name="folder">The relative path of the folder to list, or null/empty for the backend root.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>An async stream of <see cref="InventoryItem"/> rows that are direct children of the folder.</returns>
    public async IAsyncEnumerable<InventoryItem> GetInventoryItemsInFolderAsync(string? folder, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The query selects inventory rows that are direct children of the folder.
        // For the root (empty folder) a direct child has no '/' in its path. For a
        // nested folder a direct child's path is exactly "{folder}/{name}" with no
        // further '/'.
        //
        // The nested-folder branch uses a sargable prefix scan so SQLite can use the
        // UNIQUE index on "RelativePath" for a range scan instead of a full table
        // scan: it matches "RelativePath LIKE @prefix || '%'" with the LIKE
        // meta-characters in the prefix escaped and ESCAPE '\' set. The "no further
        // '/'" check (which excludes sub-folder rows) is applied as a filter over the
        // candidate range rather than as a substr()-based left-prefix comparison,
        // which would have defeated the index. This is called once per folder in the
        // hot sync loop, so keeping it index-backed is important for large trees.
        // The root branch is inherently a scan (it wants all rows without a '/'), but
        // the UNIQUE index is still used for an index-only scan.
        using var cmd = m_connection.CreateCommand();
        if (string.IsNullOrEmpty(folder))
        {
            // Root: direct children have no '/'.
            cmd.CommandText = @"SELECT ""RelativePath"", ""Size"", ""LastModified"", ""ContentHash"" FROM ""RemoteInventory"" WHERE ""RelativePath"" NOT LIKE '%/%';";
        }
        else
        {
            // Nested: path starts with "{folder}/" and has no further '/' after the
            // prefix. Escape LIKE meta-characters in the prefix so a relative path
            // containing '%', '_', or '\' is matched literally.
            var prefix = folder + "/";
            var escapedPrefix = EscapeLikePattern(prefix);
            cmd.CommandText = @"
                SELECT ""RelativePath"", ""Size"", ""LastModified"", ""ContentHash""
                FROM ""RemoteInventory""
                WHERE ""RelativePath"" LIKE @prefix || '%' ESCAPE '\'
                  AND instr(substr(""RelativePath"", @prefixlen + 1), '/') = 0;";
            cmd.Parameters.AddWithValue("@prefix", escapedPrefix);
            cmd.Parameters.AddWithValue("@prefixlen", prefix.Length);
        }

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new InventoryItem
            {
                RelativePath = reader.GetString(0),
                Size = reader.GetInt64(1),
                LastModified = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                ContentHash = await reader.IsDBNullAsync(3, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(3)
            };
        }
    }

    /// <summary>
    /// Escapes the LIKE meta-characters (<c>%</c>, <c>_</c>) and the escape
    /// character (<c>\</c>) in a pattern so it is matched literally when used with
    /// <c>ESCAPE '\'</c>. This lets a caller-supplied relative path (which may
    /// contain those characters) be used safely as the fixed prefix of a sargable
    /// <c>LIKE @prefix || '%'</c> range scan without becoming a wildcard.
    /// </summary>
    /// <param name="pattern">The pattern to escape.</param>
    /// <returns>The escaped pattern, suitable for use with <c>ESCAPE '\'</c>.</returns>
    private static string EscapeLikePattern(string pattern)
    {
        // Order matters: escape the escape character first so escaping the
        // meta-characters does not itself introduce new escapes.
        return pattern.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
    }

    /// <summary>
    /// Gets a single pending operation by path, or null if no such row exists.
    /// </summary>
    public async Task<PendingOperationEntry?> GetPendingOperationAsync(string path, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"SELECT ""ID"", ""Path"", ""Operation"", ""Size"", ""Hash"", ""StartedAt"", ""Attempts"" FROM ""PendingOperation"" WHERE ""Path"" = @path;";
        cmd.Parameters.AddWithValue("@path", path);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return ReadPendingOperation(reader, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// A single row in the <c>RemoteInventory</c> observed-state cache.
    /// </summary>
    public sealed record InventoryItem
    {
        public required string RelativePath { get; set; } = "";
        public required long Size { get; set; }
        public required DateTime LastModified { get; set; }
        public required string? ContentHash { get; set; }
    }

    /// <summary>
    /// A single row in the <c>PendingOperation</c> intent journal.
    /// </summary>
    public sealed record PendingOperationEntry
    {
        public required long ID { get; set; }
        public required string Path { get; set; } = "";
        public required SyncOperation Operation { get; set; }
        public required long? Size { get; set; }
        public required string? Hash { get; set; }
        public required DateTime StartedAt { get; set; }
        public required int Attempts { get; set; }
    }

    /// <summary>
    /// Returns true if the <c>RemoteInventory</c> observed-state cache has any rows.
    /// </summary>
    public async Task<bool> HasAnyInventoryAsync(CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM ""RemoteInventory"";";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    /// <summary>
    /// Returns true if the <c>PendingOperation</c> intent journal has any rows,
    /// i.e. there are in-flight operations left over from a previous run that
    /// should be reconciled before proceeding.
    /// </summary>
    public async Task<bool> HasAnyPendingOperationsAsync(CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"SELECT COUNT(*) FROM ""PendingOperation"";";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken)) > 0;
    }

    /// <summary>
    /// Enumerates every row in the <c>RemoteInventory</c> observed-state cache.
    /// </summary>
    public async IAsyncEnumerable<InventoryItem> GetInventoryAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"SELECT ""RelativePath"", ""Size"", ""LastModified"", ""ContentHash"" FROM ""RemoteInventory"";";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return new InventoryItem
            {
                RelativePath = reader.GetString(0),
                Size = reader.GetInt64(1),
                LastModified = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                ContentHash = await reader.IsDBNullAsync(3, cancellationToken) ? null : reader.GetString(3)
            };
        }
    }

    /// <summary>
    /// Enumerates every row in the <c>PendingOperation</c> intent journal, for
    /// reconciliation against a fresh remote listing on resume.
    /// </summary>
    public async IAsyncEnumerable<PendingOperationEntry> GetPendingOperationsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"SELECT ""ID"", ""Path"", ""Operation"", ""Size"", ""Hash"", ""StartedAt"", ""Attempts"" FROM ""PendingOperation"";";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            yield return ReadPendingOperation(reader, cancellationToken);
        }
    }

    /// <summary>
    /// Logs a completed backend operation to the append-only <c>RemoteOperation</c> audit log,
    /// purging entries older than 30 days.
    /// </summary>
    public async Task LogRemoteOperationAsync(string operation, string path, string? data, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO ""RemoteOperation"" (""Timestamp"", ""Operation"", ""Path"", ""Data"")
            VALUES (@timestamp, @operation, @path, @data);
        ";
        cmd.Parameters.AddWithValue("@timestamp", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(DateTime.UtcNow));
        cmd.Parameters.AddWithValue("@operation", operation);
        cmd.Parameters.AddWithValue("@path", path);
        cmd.Parameters.AddWithValue("@data", (object?)data ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Throttle the audit-log purge so we don't run a DELETE on every logged
        // operation. The RemoteOperationTimestamp index makes the purge itself cheap
        // when it does run; the throttle amortizes it to at most once per interval.
        var nowUtc = DateTime.UtcNow;
        if (nowUtc - m_lastAuditPurgeUtc >= AuditLogPurgeInterval)
        {
            m_lastAuditPurgeUtc = nowUtc;
            await PurgeLogDataAsync(nowUtc - AuditLogPurgeAge, cancellationToken);
        }
    }

    /// <summary>
    /// Purges audit-log entries older than the given threshold.
    /// </summary>
    public async Task PurgeLogDataAsync(DateTime threshold, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = @"DELETE FROM ""RemoteOperation"" WHERE ""Timestamp"" < @threshold;";
        cmd.Parameters.AddWithValue("@threshold", Library.Utility.Utility.NormalizeDateTimeToEpochSeconds(threshold));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// A handle to a temporary table created on the sync database. Disposing it drops
    /// the table. Used by the sync handler to hold large path/size lists off the heap
    /// on memory-constrained devices instead of materializing them in process memory.
    /// </summary>
    public sealed class TempTable : IAsyncDisposable
    {
        private readonly SqliteConnection m_connection;
        private readonly string m_name;
        private bool m_disposed;

        /// <summary>
        /// The name of the temporary table, suitable for use in SQL.
        /// </summary>
        public string Name => m_name;

        internal TempTable(SqliteConnection connection, string name)
        {
            m_connection = connection;
            m_name = name;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            if (m_disposed)
                return;
            m_disposed = true;
            try
            {
                using var cmd = m_connection.CreateCommand();
                cmd.CommandText = $@"DROP TABLE IF EXISTS ""{m_name}"";";
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
            catch
            {
                // Best-effort cleanup; the temp table lives only for this run.
            }
        }
    }

    /// <summary>
    /// Creates a temporary table with the given column definition and returns a handle
    /// that drops it on dispose. The caller owns the table's lifetime.
    /// </summary>
    /// <param name="columnDefinition">The body of the CREATE TABLE statement, e.g. <c>"RelativePath TEXT NOT NULL"</c>.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>A <see cref="TempTable"/> handle.</returns>
    public async Task<TempTable> CreateTempTableAsync(string columnDefinition, CancellationToken cancellationToken)
    {
        var name = $"SyncTemp-{Library.Utility.Utility.GetHexGuid()}";
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"CREATE TEMPORARY TABLE ""{name}"" ({columnDefinition});";
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return new TempTable(m_connection, name);
    }

    /// <summary>
    /// Inserts rows into the four-column LocalFiles temp table in chunks, mirroring the
    /// <see cref="TemporaryDbValueList"/> chunked-insert approach so large file sets do
    /// not exceed SQLite's parameter limit. The caller drains its streaming source into a
    /// bounded buffer and flushes via this method so the whole local file set is never
    /// resident in process memory at once.
    /// </summary>
    /// <param name="tableName">The temp table to insert into. Must have the columns <c>RelativePath</c>, <c>AbsolutePath</c>, <c>Size</c>, <c>LastModified</c>.</param>
    /// <param name="values">The (relativePath, absolutePath, size, modifiedUtc) rows to insert.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    public async Task InsertLocalFilesAsync(string tableName, IEnumerable<(string RelativePath, string AbsolutePath, long Size, DateTime ModifiedUtc)> values, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        foreach (var slice in values.Chunk(Local.LocalDatabase.CHUNK_SIZE))
        {
            var parameterFragments = slice.Select((_, i) =>
            {
                var n = Library.Utility.Utility.FormatInvariantValue(i);
                return $"(@p{n}, @a{n}, @s{n}, @m{n})";
            }).ToArray();
            // INSERT OR IGNORE so the UNIQUE constraint on RelativePath deduplicates: the
            // first row seen for a path wins and later duplicates are silently skipped. The
            // table is expected to have a UNIQUE constraint on RelativePath for this to apply.
            var sql = $@"INSERT OR IGNORE INTO ""{tableName}"" (""RelativePath"", ""AbsolutePath"", ""Size"", ""LastModified"") VALUES {string.Join(", ", parameterFragments)};";
            cmd.SetCommandAndParameters(sql);
            for (int i = 0; i < slice.Length; i++)
            {
                var n = Library.Utility.Utility.FormatInvariantValue(i);
                cmd.SetParameterValue($"@p{n}", slice[i].RelativePath);
                cmd.SetParameterValue($"@a{n}", slice[i].AbsolutePath);
                cmd.SetParameterValue($"@s{n}", slice[i].Size);
                cmd.SetParameterValue($"@m{n}", slice[i].ModifiedUtc.ToString("O"));
            }
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A row from the computed sync plan, describing one local file and the operation
    /// to perform against the remote destination.
    /// </summary>
    public sealed record SyncPlanRow
    {
        /// <summary>The relative path on the remote destination.</summary>
        public required string RelativePath { get; set; }
        /// <summary>The absolute path of the local source file to upload from.</summary>
        public required string AbsolutePath { get; set; }
        /// <summary>The size of the local source file.</summary>
        public required long Size { get; set; }
        /// <summary>The last-modified time (UTC) of the local source file.</summary>
        public required DateTime LastModifiedUtc { get; set; }
        /// <summary>The operation to perform.</summary>
        public required SyncOperation Operation { get; set; }
        /// <summary>
        /// The content hash from the remote inventory, when present. Only populated for
        /// <see cref="SyncOperation.Update"/> rows when the remote inventory actually
        /// stored a hash; null otherwise.
        /// </summary>
        public required string? RemoteContentHash { get; set; }

        /// <summary>
        /// True when this update row was emitted only because a hash re-check was
        /// requested (size and mtime are unchanged but a remote hash exists). The
        /// caller must compute the local hash and skip the upload if it matches the
        /// <see cref="RemoteContentHash"/>; otherwise it performs a real update.
        /// </summary>
        public bool HashRecheckOnly { get; set; }
    }

    /// <summary>
    /// Streams the upload portion of the sync plan: local files that are not present in
    /// the remote inventory. This is the <c>LEFT JOIN ... WHERE remote IS NULL</c> half
    /// of the diff, computed in the database so neither side has to be resident in memory.
    /// </summary>
    /// <param name="localFilesTable">The temp table populated with local entries.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>An async stream of <see cref="SyncPlanRow"/> rows.</returns>
    public async IAsyncEnumerable<SyncPlanRow> GetUploadPlanAsync(string localFilesTable, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT lf.""RelativePath"", lf.""AbsolutePath"", lf.""Size"", lf.""LastModified""
            FROM ""{localFilesTable}"" lf
            LEFT JOIN ""RemoteInventory"" ri ON ri.""RelativePath"" = lf.""RelativePath""
            WHERE ri.""RelativePath"" IS NULL
            ORDER BY lf.""RelativePath"";
        ";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new SyncPlanRow
            {
                RelativePath = reader.GetString(0),
                AbsolutePath = reader.GetString(1),
                Size = reader.GetInt64(2),
                LastModifiedUtc = DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                Operation = SyncOperation.Upload,
                RemoteContentHash = null
            };
        }
    }

    /// <summary>
    /// Streams the update portion of the sync plan: local files that are present in
    /// the remote inventory but whose size or last-modified time differs. When
    /// <paramref name="verifyHash"/> is set, rows whose size and mtime are unchanged
    /// but whose remote inventory carries a content hash are also emitted, so the
    /// caller can re-check the hash against the local file's current content. A
    /// listing rebuild stores a null <c>ContentHash</c>, so those rows are naturally
    /// excluded unless the inventory actually has a hash to compare against.
    /// </summary>
    /// <param name="localFilesTable">The temp table populated with local entries.</param>
    /// <param name="verifyHash">Whether hash-based comparison is requested by the user.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>An async stream of <see cref="SyncPlanRow"/> rows.</returns>
    public async IAsyncEnumerable<SyncPlanRow> GetUpdatePlanAsync(string localFilesTable, bool verifyHash, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // The hash-recheck branch emits rows where size is unchanged and the local
        // mtime is not newer than the remote (i.e. remote >= local), matching the
        // original in-memory diff which only flagged a hash mismatch when the size was
        // equal and the local file was NOT newer than the remote. Including the
        // local-older case here is important: a backend may stamp the remote file with
        // a later mtime than the local source, and the original code still hash-checked
        // such rows. These rows may turn out to be unchanged once the local hash is
        // computed; the caller is responsible for skipping them in that case.
        var hashClause = verifyHash
            ? @" OR (ri.""Size"" = lf.""Size"" AND ri.""LastModified"" >= lf.""LastModified"" AND ri.""ContentHash"" IS NOT NULL)"
            : string.Empty;
        using var cmd = m_connection.CreateCommand();
        // HashRecheckOnly is true when the row was emitted solely because of the hash
        // clause: size is equal AND the remote mtime is not older than the local mtime
        // (so neither the size-differ nor the local-newer clauses matched). The caller
        // uses this flag to decide whether to compute the local hash before uploading.
        cmd.CommandText = $@"
            SELECT lf.""RelativePath"", lf.""AbsolutePath"", lf.""Size"", lf.""LastModified"", ri.""ContentHash"",
                   (ri.""Size"" = lf.""Size"" AND ri.""LastModified"" >= lf.""LastModified"") AS ""HashOnly""
            FROM ""{localFilesTable}"" lf
            INNER JOIN ""RemoteInventory"" ri ON ri.""RelativePath"" = lf.""RelativePath""
            WHERE ri.""Size"" <> lf.""Size""
               OR ri.""LastModified"" < lf.""LastModified""
               {hashClause}
            ORDER BY lf.""RelativePath"";
        ";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new SyncPlanRow
            {
                RelativePath = reader.GetString(0),
                AbsolutePath = reader.GetString(1),
                Size = reader.GetInt64(2),
                LastModifiedUtc = DateTime.Parse(reader.GetString(3)).ToUniversalTime(),
                Operation = SyncOperation.Update,
                RemoteContentHash = await reader.IsDBNullAsync(4, cancellationToken).ConfigureAwait(false) ? null : reader.GetString(4),
                HashRecheckOnly = reader.GetBoolean(5)
            };
        }
    }

    /// <summary>
    /// Streams the delete portion of the sync plan: remote inventory entries that have
    /// no matching local file and no outstanding pending-operation intent row. Pending
    /// uploads/updates are excluded here so a delete never races its own upload; those
    /// paths are reconciled via the intent journal instead.
    /// </summary>
    /// <param name="localFilesTable">The temp table populated with local entries.</param>
    /// <param name="cancellationToken">Cancellation token to monitor for cancellation requests.</param>
    /// <returns>An async stream of <see cref="SyncPlanRow"/> rows (with <see cref="SyncOperation.Delete"/> and empty <see cref="SyncPlanRow.AbsolutePath"/>).</returns>
    public async IAsyncEnumerable<SyncPlanRow> GetDeletePlanAsync(string localFilesTable, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT ri.""RelativePath"", ri.""Size"", ri.""LastModified""
            FROM ""RemoteInventory"" ri
            LEFT JOIN ""{localFilesTable}"" lf ON lf.""RelativePath"" = ri.""RelativePath""
            LEFT JOIN ""PendingOperation"" po ON po.""Path"" = ri.""RelativePath""
            WHERE lf.""RelativePath"" IS NULL
              AND po.""Path"" IS NULL
            ORDER BY ri.""RelativePath"";
        ";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return new SyncPlanRow
            {
                RelativePath = reader.GetString(0),
                AbsolutePath = string.Empty,
                Size = reader.GetInt64(1),
                LastModifiedUtc = DateTime.Parse(reader.GetString(2)).ToUniversalTime(),
                Operation = SyncOperation.Delete,
                RemoteContentHash = null
            };
        }
    }

    /// <summary>
    /// Returns the count of rows in the given temp table, for plan logging.
    /// </summary>
    public async Task<long> CountRowsAsync(string tableName, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"SELECT COUNT(*) FROM ""{tableName}"";";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// Counts the upload portion of the sync plan: local files absent from the remote
    /// inventory. Used for the plan summary log without materializing the rows.
    /// </summary>
    public async Task<long> CountUploadPlanAsync(string localFilesTable, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*)
            FROM ""{localFilesTable}"" lf
            LEFT JOIN ""RemoteInventory"" ri ON ri.""RelativePath"" = lf.""RelativePath""
            WHERE ri.""RelativePath"" IS NULL;
        ";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// Counts the update portion of the sync plan, mirroring <see cref="GetUpdatePlanAsync"/>.
    /// </summary>
    public async Task<long> CountUpdatePlanAsync(string localFilesTable, bool verifyHash, CancellationToken cancellationToken)
    {
        var hashClause = verifyHash
            ? @" OR (ri.""Size"" = lf.""Size"" AND ri.""LastModified"" >= lf.""LastModified"" AND ri.""ContentHash"" IS NOT NULL)"
            : string.Empty;
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*)
            FROM ""{localFilesTable}"" lf
            INNER JOIN ""RemoteInventory"" ri ON ri.""RelativePath"" = lf.""RelativePath""
            WHERE ri.""Size"" <> lf.""Size""
               OR ri.""LastModified"" < lf.""LastModified""
               {hashClause};
        ";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// Counts the delete portion of the sync plan, mirroring <see cref="GetDeletePlanAsync"/>.
    /// </summary>
    public async Task<long> CountDeletePlanAsync(string localFilesTable, CancellationToken cancellationToken)
    {
        using var cmd = m_connection.CreateCommand();
        cmd.CommandText = $@"
            SELECT COUNT(*)
            FROM ""RemoteInventory"" ri
            LEFT JOIN ""{localFilesTable}"" lf ON lf.""RelativePath"" = ri.""RelativePath""
            LEFT JOIN ""PendingOperation"" po ON po.""Path"" = ri.""RelativePath""
            WHERE lf.""RelativePath"" IS NULL
              AND po.""Path"" IS NULL;
        ";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), null);
    }

    /// <summary>
    /// Maps a backup-flavored <see cref="RemoteVolumeState"/> onto the sync intent journal.
    /// </summary>
    /// <remarks>
    /// The <see cref="IBackendManagerDatabase"/> interface was designed around the backup
    /// database's rich volume state machine. Sync only cares about two of those states,
    /// so this method translates the relevant ones into <see cref="SyncOperation"/> intent
    /// rows and ignores the states that have no sync meaning.
    /// </remarks>
    private static SyncOperation? MapToSyncOperation(RemoteVolumeState state)
        => state switch
        {
            RemoteVolumeState.Uploading => SyncOperation.Upload,
            RemoteVolumeState.Deleting => SyncOperation.Delete,
            // Uploaded/Verified/Temporary/Deleted are backup-internal states that have
            // no meaning for the sync intent journal; the sync handler records its own
            // intent rows directly and does not rely on these.
            _ => null
        };

    /// <summary>
    /// Updates the intent journal in response to a backend-manager-flushed volume update.
    /// Maps the backup <see cref="RemoteVolumeState"/> onto a <see cref="SyncOperation"/>
    /// where possible; states with no sync meaning are no-ops.
    /// </summary>
    public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string? hash, CancellationToken cancellationToken)
    {
        var op = MapToSyncOperation(state);
        if (!op.HasValue)
            return Task.CompletedTask;
        return UpsertPendingOperationAsync(name, op.Value, size <= 0 ? (long?)null : size, hash, cancellationToken);
    }

    /// <summary>
    /// Updates the intent journal in response to a backend-manager-flushed volume update.
    /// The backup-specific parameters (<paramref name="suppressCleanup"/>,
    /// <paramref name="deleteGraceTime"/>, <paramref name="setArchived"/>) have no sync
    /// meaning and are ignored.
    /// </summary>
    public Task UpdateRemoteVolumeAsync(string name, RemoteVolumeState state, long size, string? hash, bool suppressCleanup, TimeSpan deleteGraceTime, bool? setArchived, CancellationToken cancellationToken)
        => UpdateRemoteVolumeAsync(name, state, size, hash, cancellationToken);

    /// <summary>
    /// Renames a remote file in both the observed-state cache and the intent journal,
    /// preserving the in-flight operation (if any) under the new name.
    /// </summary>
    public async Task RenameRemoteFileAsync(string oldname, string newname, CancellationToken cancellationToken)
    {
        var item = await GetInventoryItemAsync(oldname, cancellationToken);
        if (item != null)
        {
            await RemoveInventoryAsync(oldname, cancellationToken);
            await UpsertInventoryAsync(newname, item.Size, item.LastModified, item.ContentHash, cancellationToken);
        }
        else
        {
            // Keep behavior: ensure the old name is gone and a placeholder exists under the new name.
            await RemoveInventoryAsync(oldname, cancellationToken);
            await UpsertInventoryAsync(newname, 0, DateTime.UtcNow, null, cancellationToken);
        }

        var pending = await GetPendingOperationAsync(oldname, cancellationToken);
        if (pending != null)
        {
            await RemovePendingOperationAsync(oldname, cancellationToken);
            await UpsertPendingOperationAsync(newname, pending.Operation, pending.Size, pending.Hash, cancellationToken);
        }
    }

    /// <summary>
    /// Removes the given paths from the intent journal. Used by the backend manager
    /// flush path to drop intent rows once the corresponding operation has been
    /// confirmed on the remote destination.
    /// </summary>
    public async Task RemoveRemoteVolumesAsync(IEnumerable<string> names, CancellationToken cancellationToken)
    {
        foreach (var name in names)
            await RemovePendingOperationAsync(name, cancellationToken);
    }

    /// <summary>
    /// Commits the current transaction.
    /// </summary>
    /// <remarks>
    /// This is a no-op for the sync database: callers that need atomic multi-statement
    /// writes use <see cref="ExecuteInTransactionAsync"/>, which manages its own
    /// transaction. This method exists only to satisfy the
    /// <see cref="IBackendManagerDatabase"/> contract shared with the backup database,
    /// whose backend-manager flush path calls it after removing deleted volumes.
    /// </remarks>
    public Task CommitAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads a <see cref="PendingOperationEntry"/> from the current row of a data reader.
    /// </summary>
    private static PendingOperationEntry ReadPendingOperation(SqliteDataReader reader, CancellationToken cancellationToken)
    {
        return new PendingOperationEntry
        {
            ID = reader.GetInt64(0),
            Path = reader.GetString(1),
            Operation = (SyncOperation)Enum.Parse(typeof(SyncOperation), reader.GetString(2)),
            Size = reader.IsDBNull(3) ? null : reader.GetInt64(3),
            Hash = reader.IsDBNull(4) ? null : reader.GetString(4),
            StartedAt = Library.Utility.Utility.EPOCH.AddSeconds(reader.GetInt64(5)),
            Attempts = reader.GetInt32(6)
        };
    }

    public void Dispose()
    {
        m_connection.Dispose();
    }
}
