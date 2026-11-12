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

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.SQLiteHelper;
using Microsoft.Data.Sqlite;

namespace Duplicati.Library.ChangeTracking;

/// <summary>
/// A recording session, as stored in the tracking database
/// </summary>
/// <param name="Id">The session id</param>
/// <param name="Token">The token handed to the backup run that opened the session</param>
/// <param name="BackupId">The backup the session belongs to</param>
/// <param name="SettingsHash">The hash of the backup settings the session was recorded under</param>
/// <param name="StartedUtc">When the session was opened</param>
/// <param name="State">The session state</param>
/// <param name="OverflowReason">Why the session cannot be trusted, or null</param>
public sealed record SessionInfo(long Id, string Token, string BackupId, string SettingsHash, DateTime StartedUtc, SessionState State, string? OverflowReason);

/// <summary>
/// The state of a recording session
/// </summary>
public enum SessionState
{
    /// <summary>The session receives new observations</summary>
    Recording = 0,
    /// <summary>The session was closed and is being handed to a backup</summary>
    Handoff = 1,
}

/// <summary>
/// The SQLite database that holds the observed changes between backups.
/// It is recreated when the server starts, because the server cannot know
/// what happened while it was not running, so there is no migration logic.
/// All access is serialized; there is one writer and one reader.
/// </summary>
public sealed class ChangeTrackingDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// The path to the database file
    /// </summary>
    public string Path { get; }

    private ChangeTrackingDatabase(string path, SqliteConnection connection)
    {
        Path = path;
        _connection = connection;
    }

    /// <summary>
    /// Deletes any existing database at the path and creates a new one
    /// </summary>
    /// <param name="path">The path to the database file</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The new database</returns>
    public static async Task<ChangeTrackingDatabase> CreateAsync(string path, CancellationToken cancellationToken)
    {
        foreach (var suffix in new[] { "", "-journal", "-wal", "-shm" })
        {
            var file = path + suffix;
            if (File.Exists(file))
                File.Delete(file);
        }

        var connection = await SQLiteLoader.LoadConnectionAsync(path).ConfigureAwait(false);
        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE ""Session"" (
                    ""ID"" INTEGER PRIMARY KEY AUTOINCREMENT,
                    ""Token"" TEXT NOT NULL,
                    ""BackupID"" TEXT NOT NULL,
                    ""SettingsHash"" TEXT NOT NULL,
                    ""Started"" INTEGER NOT NULL,
                    ""State"" INTEGER NOT NULL,
                    ""OverflowReason"" TEXT NULL
                );
                CREATE INDEX ""SessionBackupID"" ON ""Session"" (""BackupID"", ""State"");
                CREATE TABLE ""SessionBaseToken"" (
                    ""SessionID"" INTEGER NOT NULL,
                    ""Token"" TEXT NOT NULL,
                    PRIMARY KEY (""SessionID"", ""Token"")
                ) WITHOUT ROWID;
                CREATE TABLE ""Change"" (
                    ""SessionID"" INTEGER NOT NULL,
                    ""Path"" TEXT NOT NULL,
                    ""Kind"" INTEGER NOT NULL,
                    ""Observed"" INTEGER NOT NULL,
                    PRIMARY KEY (""SessionID"", ""Path"")
                ) WITHOUT ROWID;
            ";
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new ChangeTrackingDatabase(path, connection);
    }

    private SqliteCommand CreateCommand(string sql, SqliteTransaction? transaction)
    {
        var cmd = _connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        return cmd;
    }

    /// <summary>
    /// Opens a new recording session for a backup
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <param name="settingsHash">The hash of the backup settings</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The id of the new session</returns>
    public async Task<long> StartSessionAsync(string backupId, string settingsHash, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            var (id, _) = await InsertSessionAsync(backupId, settingsHash, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return id;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<(long Id, string Token)> InsertSessionAsync(string backupId, string settingsHash, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        // The token identifies the fileset of the run that opens the session; the session
        // lists what changed since that run's snapshot, so the token is also its base
        var token = Guid.NewGuid().ToString("N");
        long id;
        await using (var cmd = CreateCommand(@"
            INSERT INTO ""Session"" (""Token"", ""BackupID"", ""SettingsHash"", ""Started"", ""State"", ""OverflowReason"")
            VALUES (@Token, @BackupId, @SettingsHash, @Started, @State, NULL);
            SELECT last_insert_rowid();", transaction))
        {
            cmd.Parameters.AddWithValue("@Token", token);
            cmd.Parameters.AddWithValue("@BackupId", backupId);
            cmd.Parameters.AddWithValue("@SettingsHash", settingsHash);
            cmd.Parameters.AddWithValue("@Started", DateTime.UtcNow.Ticks);
            cmd.Parameters.AddWithValue("@State", (int)SessionState.Recording);
            id = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? throw new InvalidOperationException("No session id returned"));
        }

        await using (var cmd = CreateCommand(@"INSERT INTO ""SessionBaseToken"" (""SessionID"", ""Token"") VALUES (@SessionId, @Token)", transaction))
        {
            cmd.Parameters.AddWithValue("@SessionId", id);
            cmd.Parameters.AddWithValue("@Token", token);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        return (id, token);
    }

    /// <summary>
    /// Closes the recording session of a backup, if any, and opens a new one, in one transaction
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <param name="settingsHash">The hash of the backup settings for the new session</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The id of the closed session, or null if none was recording, and the id and token of the new session</returns>
    public async Task<(long? ClosedSessionId, long NewSessionId, string NewToken)> RotateAsync(string backupId, string settingsHash, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            long? closed = null;
            await using (var cmd = CreateCommand(@"SELECT ""ID"" FROM ""Session"" WHERE ""BackupID"" = @BackupId AND ""State"" = @State ORDER BY ""ID"" DESC LIMIT 1", transaction))
            {
                cmd.Parameters.AddWithValue("@BackupId", backupId);
                cmd.Parameters.AddWithValue("@State", (int)SessionState.Recording);
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                if (result is long id)
                    closed = id;
            }

            if (closed != null)
            {
                await using var cmd = CreateCommand(@"UPDATE ""Session"" SET ""State"" = @State WHERE ""ID"" = @Id", transaction);
                cmd.Parameters.AddWithValue("@State", (int)SessionState.Handoff);
                cmd.Parameters.AddWithValue("@Id", closed.Value);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            var (created, token) = await InsertSessionAsync(backupId, settingsHash, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return (closed, created, token);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Records that a session cannot be trusted
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="reason">The reason</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public async Task MarkOverflowedAsync(long sessionId, string reason, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = CreateCommand(@"UPDATE ""Session"" SET ""OverflowReason"" = @Reason WHERE ""ID"" = @Id AND ""OverflowReason"" IS NULL", null);
            cmd.Parameters.AddWithValue("@Reason", reason);
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reads a session
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The session, or null if it does not exist</returns>
    public async Task<SessionInfo?> GetSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = CreateCommand(@"SELECT ""ID"", ""Token"", ""BackupID"", ""SettingsHash"", ""Started"", ""State"", ""OverflowReason"" FROM ""Session"" WHERE ""ID"" = @Id", null);
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                return null;

            return new SessionInfo(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
                (SessionState)reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetString(6));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Writes buffered changes to a session in one transaction. A path already in the
    /// session has the kinds merged and the observation time raised.
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="changes">The changes to write</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of paths in the session after the write</returns>
    public async Task<long> WriteChangesAsync(long sessionId, IReadOnlyDictionary<string, BufferedChange> changes, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var cmd = CreateCommand(@"
                INSERT INTO ""Change"" (""SessionID"", ""Path"", ""Kind"", ""Observed"")
                VALUES (@SessionId, @Path, @Kind, @Observed)
                ON CONFLICT (""SessionID"", ""Path"") DO UPDATE SET
                    ""Kind"" = ""Kind"" | excluded.""Kind"",
                    ""Observed"" = MAX(""Observed"", excluded.""Observed"")", transaction))
            {
                cmd.Parameters.AddWithValue("@SessionId", sessionId);
                var pathParam = cmd.Parameters.Add("@Path", SqliteType.Text);
                var kindParam = cmd.Parameters.Add("@Kind", SqliteType.Integer);
                var observedParam = cmd.Parameters.Add("@Observed", SqliteType.Integer);

                foreach (var (path, change) in changes)
                {
                    pathParam.Value = path;
                    kindParam.Value = (long)change.Kind;
                    observedParam.Value = change.ObservedUtcTicks;
                    await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            var count = await CountChangesAsync(sessionId, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return count;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<long> CountChangesAsync(long sessionId, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(@"SELECT COUNT(*) FROM ""Change"" WHERE ""SessionID"" = @SessionId", transaction);
        cmd.Parameters.AddWithValue("@SessionId", sessionId);
        return (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? 0L);
    }

    /// <summary>
    /// Counts the paths in a session
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of paths</returns>
    public async Task<long> CountChangesAsync(long sessionId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CountChangesAsync(sessionId, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Streams the paths of a session. The database is locked for other callers until
    /// the enumeration completes, so a caller must not enumerate two sessions at once.
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The recorded changes</returns>
    public async IAsyncEnumerable<RecordedChange> ReadChangesAsync(long sessionId, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = CreateCommand(@"SELECT ""Path"", ""Kind"", ""Observed"" FROM ""Change"" WHERE ""SessionID"" = @SessionId", null);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                yield return new RecordedChange(reader.GetString(0), (ChangeKind)reader.GetInt64(1), reader.GetInt64(2));
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Gets the tokens of the filesets a session's paths are relative to: the token of the
    /// run that opened it, and those of sessions merged into it, which started earlier and
    /// so list everything since their own run as well
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The tokens</returns>
    public async Task<string[]> GetBaseTokensAsync(long sessionId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = CreateCommand(@"SELECT ""Token"" FROM ""SessionBaseToken"" WHERE ""SessionID"" = @SessionId", null);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            var result = new List<string>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                result.Add(reader.GetString(0));
            return result.ToArray();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Copies the paths observed at or after a time from one session into another,
    /// keeping the source. Paths already in the target have the kinds merged.
    /// </summary>
    /// <param name="fromSessionId">The session to copy from</param>
    /// <param name="toSessionId">The session to copy into</param>
    /// <param name="sinceUtcTicks">The earliest observation time to copy</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of copied paths</returns>
    public async Task<long> CopyObservedSinceAsync(long fromSessionId, long toSessionId, long sinceUtcTicks, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await CopyAsync(fromSessionId, toSessionId, sinceUtcTicks, null, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<long> CopyAsync(long fromSessionId, long toSessionId, long sinceUtcTicks, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using var cmd = CreateCommand(@"
            INSERT INTO ""Change"" (""SessionID"", ""Path"", ""Kind"", ""Observed"")
            SELECT @ToSessionId, ""Path"", ""Kind"", ""Observed""
            FROM ""Change""
            WHERE ""SessionID"" = @FromSessionId AND ""Observed"" >= @Since
            ON CONFLICT (""SessionID"", ""Path"") DO UPDATE SET
                ""Kind"" = ""Kind"" | excluded.""Kind"",
                ""Observed"" = MAX(""Observed"", excluded.""Observed"")", transaction);
        cmd.Parameters.AddWithValue("@ToSessionId", toSessionId);
        cmd.Parameters.AddWithValue("@FromSessionId", fromSessionId);
        cmd.Parameters.AddWithValue("@Since", sinceUtcTicks);
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes the paths of a session last observed before a time
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="utcTicks">The earliest observation time to keep</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The number of deleted paths</returns>
    public async Task<long> DeleteChangesObservedBeforeAsync(long sessionId, long utcTicks, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = CreateCommand(@"DELETE FROM ""Change"" WHERE ""SessionID"" = @SessionId AND ""Observed"" < @Before", null);
            cmd.Parameters.AddWithValue("@SessionId", sessionId);
            cmd.Parameters.AddWithValue("@Before", utcTicks);
            return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Moves every path of one session into another and deletes the source session
    /// </summary>
    /// <param name="fromSessionId">The session to merge</param>
    /// <param name="toSessionId">The session to merge into</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public async Task MergeSessionIntoAsync(long fromSessionId, long toSessionId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await CopyAsync(fromSessionId, toSessionId, long.MinValue, transaction, cancellationToken).ConfigureAwait(false);

            // The target now also lists everything since the source's base
            await using (var cmd = CreateCommand(@"
                INSERT OR IGNORE INTO ""SessionBaseToken"" (""SessionID"", ""Token"")
                SELECT @ToSessionId, ""Token"" FROM ""SessionBaseToken"" WHERE ""SessionID"" = @FromSessionId", transaction))
            {
                cmd.Parameters.AddWithValue("@ToSessionId", toSessionId);
                cmd.Parameters.AddWithValue("@FromSessionId", fromSessionId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await DeleteSessionAsync(fromSessionId, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task DeleteSessionAsync(long sessionId, SqliteTransaction? transaction, CancellationToken cancellationToken)
    {
        await using (var cmd = CreateCommand(@"DELETE FROM ""Change"" WHERE ""SessionID"" = @Id", transaction))
        {
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var cmd = CreateCommand(@"DELETE FROM ""SessionBaseToken"" WHERE ""SessionID"" = @Id", transaction))
        {
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var cmd = CreateCommand(@"DELETE FROM ""Session"" WHERE ""ID"" = @Id", transaction))
        {
            cmd.Parameters.AddWithValue("@Id", sessionId);
            await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a session and its paths
    /// </summary>
    /// <param name="sessionId">The session id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public async Task DeleteSessionAsync(long sessionId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await DeleteSessionAsync(sessionId, transaction, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deletes every session of a backup
    /// </summary>
    /// <param name="backupId">The backup id</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public async Task DeleteSessionsForBackupAsync(string backupId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var cmd = CreateCommand(@"DELETE FROM ""Change"" WHERE ""SessionID"" IN (SELECT ""ID"" FROM ""Session"" WHERE ""BackupID"" = @BackupId)", transaction))
            {
                cmd.Parameters.AddWithValue("@BackupId", backupId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var cmd = CreateCommand(@"DELETE FROM ""SessionBaseToken"" WHERE ""SessionID"" IN (SELECT ""ID"" FROM ""Session"" WHERE ""BackupID"" = @BackupId)", transaction))
            {
                cmd.Parameters.AddWithValue("@BackupId", backupId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var cmd = CreateCommand(@"DELETE FROM ""Session"" WHERE ""BackupID"" = @BackupId", transaction))
            {
                cmd.Parameters.AddWithValue("@BackupId", backupId);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
            _lock.Dispose();
        }
    }
}
