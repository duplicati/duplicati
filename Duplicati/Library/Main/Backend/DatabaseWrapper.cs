using System;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// A class to collect operations performed on the remote destination.
    /// This class is used to log operations so they can later be written to the database.
    /// The goal is to keep the database state as close as possible to the remote operation state.
    /// Since the backend communication is done in parallel with the actual operations,
    /// we store the performed operations in a queue and flush them to the database when the database is available.
    /// The main operations are responsible for flushing the messages when comitting a transaction.
    /// If the operation fails, the logged messages here should still be flushed to the database, 
    /// as they have already been performed on the remote destination.
    /// </summary>
    private class DatabaseWrapper : IDisposable
    {
        /// <summary>
        /// Lock object to ensure thread safety
        /// </summary>
        private readonly object _lock = new object();

        /// <summary>
        /// The database connection manager used for updating the database
        /// </summary>
        private readonly DatabaseConnectionManager _dbManager;

        /// <summary>
        /// The database to use
        /// </summary>
        private readonly Lazy<LocalDatabase> _db;

        /// <summary>
        /// The database to use
        /// </summary>
        private LocalDatabase DB => _db.Value;

        /// <summary>
        /// Creates a new database wrapper
        /// </summary>
        /// <param name="dbManager">The database connection manager to use</param>
        public DatabaseWrapper(DatabaseConnectionManager dbManager)
        {
            // Set up an isolated connection for the backend manager to use
            _dbManager = dbManager.CreateAdditionalConnection();
            _db = new Lazy<LocalDatabase>(() => new LocalDatabase(_dbManager, null));
        }

        /// <summary>
        /// Logs an operation performed on the remote destination
        /// </summary>
        /// <param name="action">The action of the operation</param>
        /// <param name="file">The file of the operation</param>
        /// <param name="result">The result of the operation</param>
        public void LogRemoteOperation(string action, string file, string? result)
        {
            lock (_lock)
            {
                DB.LogRemoteOperation(action, file, result, null);
                if (string.Equals(action, "put", StringComparison.OrdinalIgnoreCase))
                    DB.TerminatedWithActiveUploads = true;
            }
        }

        /// <summary>
        /// Logs a database update after a remote file operation
        /// </summary>
        /// <param name="remotename">The remote name of the volume</param>
        /// <param name="state">The new state of the volume</param>
        /// <param name="size">The new size of the volume</param>
        /// <param name="hash">The new hash of the volume</param>
        public void LogRemoteVolumeUpdated(string remotename, RemoteVolumeState state, long size, string? hash)
        {
            lock (_lock)
                if (state == RemoteVolumeState.Deleted)
                    DB.UpdateRemoteVolume(remotename, RemoteVolumeState.Deleted, size, hash, false, TimeSpan.FromHours(2), null);
                else
                    DB.UpdateRemoteVolume(remotename, state, size, hash, null);
        }

        /// <summary>
        /// Logs a remote file rename
        /// </summary>
        /// <param name="oldname">The old name of the file</param>
        /// <param name="newname">The new name of the file</param>
        public void LogRemoteVolumeRenamed(string oldname, string newname)
        {
            lock (_lock)
                DB.RenameRemoteFile(oldname, newname, null);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_db.IsValueCreated)
                _db.Value.Dispose();
            _dbManager.Dispose();
        }
    }
}