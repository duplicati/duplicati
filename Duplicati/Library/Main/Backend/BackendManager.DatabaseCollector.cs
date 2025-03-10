using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Duplicati.Library.Logging;
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
    private class DatabaseCollector
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType<DatabaseCollector>();
        /// <summary>
        /// The database to use
        /// </summary>
        private readonly Lazy<LocalDatabase> m_db;
        /// <summary>
        /// The lock object for the database queue
        /// </summary>
        private readonly object m_dbqueuelock = new object();
        /// <summary>
        /// The queue of database operations
        /// </summary>
        private List<IRemoteOperationEntry> m_dbqueue = [];

        /// <summary>
        /// Interface for database entries
        /// </summary>
        private interface IRemoteOperationEntry { }

        /// <summary>
        /// Logs a database update after a remote file operation
        /// </summary>
        /// <param name="Remotename">The remote name of the volume</param>
        /// <param name="State">The new state of the volume</param>
        /// <param name="Size">The new size of the volume</param>
        /// <param name="Hash">The new hash of the volume</param>
        private sealed record RemoteVolumeUpdate(string Remotename, RemoteVolumeState State, long Size, string? Hash) : IRemoteOperationEntry;

        /// <summary>
        /// Logs a rename of a remote file
        /// </summary>
        /// <param name="Oldname">The old name of the file</param>
        /// <param name="Newname">The new name of the file</param>
        private sealed record RenameRemoteVolume(string Oldname, string Newname) : IRemoteOperationEntry;

        /// <summary>
        /// The number of in-flight uploads
        /// </summary>
        private int m_inFlightUploads = 0;

        /// <summary>
        /// Creates a new database collector
        /// </summary>
        /// <param name="dbManager">The database connection manager to use</param>
        public DatabaseCollector(DatabaseConnectionManager dbManager)
        {
            m_db = new Lazy<LocalDatabase>(() => new LocalDatabase(dbManager, null));
        }

        /// <summary>
        /// Marks the operation as started
        /// </summary>
        /// <param name="operation">The operation that was started</param>
        public void StartedOperation(PendingOperationBase operation)
        {
            if (operation is PutOperation && Interlocked.Increment(ref m_inFlightUploads) > 1)
                lock (m_dbqueuelock)
                    m_db.Value.TerminatedWithActiveUploads = true;
        }

        /// <summary>
        /// Marks the operation as finished
        /// </summary>
        /// <param name="operation">The operation that was finished</param>
        public void FinishedOperation(PendingOperationBase operation)
        {
            if (operation is PutOperation && Interlocked.Decrement(ref m_inFlightUploads) == 0)
                lock (m_dbqueuelock)
                    m_db.Value.TerminatedWithActiveUploads = false;
        }

        /// <summary>
        /// Marks the operation as failed
        /// </summary>
        /// <param name="operation">The operation that failed</param>
        /// <param name="ex">The exception that caused the failure</param>
        public void FailedOperation(PendingOperationBase operation, Exception ex)
        {
            // In case the upload failed completely, we do not decrement the counter,
            // as the operation will fail, but we log the exception message
            if (operation is PutOperation putOp)
                lock (m_dbqueuelock)
                    m_db.Value.LogRemoteOperation("put", putOp.RemoteFilename, ex.ToString(), null);
        }

        /// <summary>
        /// Logs an operation performed on the remote destination
        /// </summary>
        /// <param name="action">The action of the operation</param>
        /// <param name="file">The file of the operation</param>
        /// <param name="result">The result of the operation</param>
        public void LogRemoteOperation(string action, string file, string? result)
        {
            lock (m_dbqueuelock)
                m_db.Value.LogRemoteOperation(action, file, result, null);
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
            lock (m_dbqueuelock)
                m_dbqueue.Add(new RemoteVolumeUpdate(remotename, state, size, hash));
        }

        /// <summary>
        /// Logs a remote file rename
        /// </summary>
        /// <param name="oldname">The old name of the file</param>
        /// <param name="newname">The new name of the file</param>
        public void LogRemoteVolumeRenamed(string oldname, string newname)
        {
            lock (m_dbqueuelock)
                m_dbqueue.Add(new RenameRemoteVolume(oldname, newname));
        }

        /// <summary>
        /// Drops all pending messages from the queue
        /// </summary>
        public void ClearPendingMessages()
        {
            lock (m_dbqueuelock)
                m_dbqueue = [];
        }

        /// <summary>
        /// Flushes all messages to the database
        /// </summary>
        /// <param name="db">The database to write pending messages to</param>
        /// <param name="transaction">The transaction to use, if any</param>
        /// <returns>Whether any messages were flushed</returns>
        public bool FlushPendingMessages(LocalDatabase db, System.Data.IDbTransaction? transaction)
        {
            List<IRemoteOperationEntry> entries;
            lock (m_dbqueuelock)
                if (m_dbqueue.Count == 0)
                    return false;
                else
                {
                    entries = m_dbqueue;
                    m_dbqueue = [];
                }

            // Collect removed volumes for final db cleanup.
            var volsRemoved = new HashSet<string>();

            // As we replace the list, we can now freely access the elements without locking
            foreach (var e in entries)
                if (e is RemoteVolumeUpdate update && update.State == RemoteVolumeState.Deleted)
                {
                    db.UpdateRemoteVolume(update.Remotename, RemoteVolumeState.Deleted, update.Size, update.Hash, true, TimeSpan.FromHours(2), transaction);
                    volsRemoved.Add(update.Remotename);
                }
                else if (e is RemoteVolumeUpdate dbUpdate)
                    db.UpdateRemoteVolume(dbUpdate.Remotename, dbUpdate.State, dbUpdate.Size, dbUpdate.Hash, transaction);
                else if (e is RenameRemoteVolume rename)
                    db.RenameRemoteFile(rename.Oldname, rename.Newname, transaction);
                else if (e != null)
                    Log.WriteErrorMessage(LOGTAG, "InvalidQueueElement", null, "Queue had element of type: {0}, {1}", e.GetType(), e);

            // Finally remove volumes from DB.
            if (volsRemoved.Count > 0)
                db.RemoveRemoteVolumes(volsRemoved);

            return true;
        }

        /// <summary>
        /// Flushes all messages to the log after stopping the processing
        /// </summary>
        public void FlushMessagesToLog()
        {
            if (m_dbqueue.Count == 0)
                return;

            string message;
            lock (m_dbqueuelock)
                message = string.Join("\n", m_dbqueue.Select(e => e switch
                {
                    RemoteVolumeUpdate update => $"Update: {update.Remotename} State: {update.State} Size: {update.Size} Hash: {update.Hash}",
                    RenameRemoteVolume rename => $"Rename: {rename.Oldname} -> {rename.Newname}",
                    _ => $"InvalidQueueElement: {e.GetType()} {e}"
                }));

            Log.WriteWarningMessage(LOGTAG, "FlushingMessagesToLog", null, message);

        }
    }
}