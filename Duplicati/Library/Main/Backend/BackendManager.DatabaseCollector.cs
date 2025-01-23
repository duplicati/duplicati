using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database;

namespace Duplicati.Library.Main.Backend;

#nullable enable

partial class BackendManager
{
    /// <summary>
    /// A class to collect database operations and flush them in a single transaction
    /// </summary>
    private class DatabaseCollector
    {
        /// <summary>
        /// The log tag for this class
        /// </summary>
        private static readonly string LOGTAG = Log.LogTagFromType<DatabaseCollector>();
        /// <summary>
        /// The lock object for the database queue
        /// </summary>
        private readonly object m_dbqueuelock = new object();
        /// <summary>
        /// The queue of database operations
        /// </summary>
        private List<IDbEntry> m_dbqueue = [];

        /// <summary>
        /// Interface for database entries
        /// </summary>
        private interface IDbEntry { }

        /// <summary>
        /// A database remmote operation entry
        /// </summary>
        private class DbOperation : IDbEntry
        {
            /// <summary>
            /// The action of the operation
            /// </summary>
            public required string Action { get; init; }
            /// <summary>
            /// The file of the operation
            /// </summary>
            public required string File { get; init; }
            /// <summary>
            /// The result of the operation
            /// </summary>
            public required string? Result { get; init; }
        }

        /// <summary>
        /// A database update entry
        /// </summary>
        private class DbUpdate : IDbEntry
        {
            /// <summary>
            /// The remote name of the volume
            /// </summary>
            public required string Remotename { get; init; }
            /// <summary>
            /// The new state of the volume
            /// </summary>
            public required RemoteVolumeState State { get; init; }
            /// <summary>
            /// The new size of the volume
            /// </summary>
            public required long Size { get; init; }
            /// <summary>
            /// The new hash of the volume
            /// </summary>
            public required string? Hash { get; init; }
        }

        /// <summary>
        /// A database rename entry
        /// </summary>
        private class DbRename : IDbEntry
        {
            /// <summary>
            /// The old name of the file
            /// </summary>
            public required string Oldname { get; init; }
            /// <summary>
            /// The new name of the file
            /// </summary>
            public required string Newname { get; init; }
        }

        /// <summary>
        /// Logs a database operation
        /// </summary>
        /// <param name="action">The action of the operation</param>
        /// <param name="file">The file of the operation</param>
        /// <param name="result">The result of the operation</param>
        public void LogDbOperation(string action, string file, string? result)
        {
            lock (m_dbqueuelock)
                m_dbqueue.Add(new DbOperation() { Action = action, File = file, Result = result });
        }

        /// <summary>
        /// Logs a remote file update
        /// </summary>
        /// <param name="remotename">The remote name of the volume</param>
        /// <param name="state">The new state of the volume</param>
        /// <param name="size">The new size of the volume</param>
        /// <param name="hash">The new hash of the volume</param>
        public void LogDbUpdate(string remotename, RemoteVolumeState state, long size, string? hash)
        {
            lock (m_dbqueuelock)
                m_dbqueue.Add(new DbUpdate() { Remotename = remotename, State = state, Size = size, Hash = hash });
        }

        /// <summary>
        /// Logs a remote file rename
        /// </summary>
        /// <param name="oldname">The old name of the file</param>
        /// <param name="newname">The new name of the file</param>
        public void LogDbRename(string oldname, string newname)
        {
            lock (m_dbqueuelock)
                m_dbqueue.Add(new DbRename() { Oldname = oldname, Newname = newname });
        }

        /// <summary>
        /// Drops all pending messages from the queue
        /// </summary>
        public void ClearDbMessages()
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
        public bool FlushDbMessages(LocalDatabase db, System.Data.IDbTransaction? transaction)
        {
            List<IDbEntry> entries;
            lock (m_dbqueuelock)
                if (m_dbqueue.Count == 0)
                    return false;
                else
                {
                    entries = m_dbqueue;
                    m_dbqueue = [];
                }

            // collect removed volumes for final db cleanup.
            var volsRemoved = new HashSet<string>();

            //As we replace the list, we can now freely access the elements without locking
            foreach (var e in entries)
                if (e is DbOperation operation)
                    db.LogRemoteOperation(operation.Action, operation.File, operation.Result, transaction);
                else if (e is DbUpdate update && update.State == RemoteVolumeState.Deleted)
                {
                    db.UpdateRemoteVolume(update.Remotename, RemoteVolumeState.Deleted, update.Size, update.Hash, true, TimeSpan.FromHours(2), transaction);
                    volsRemoved.Add(update.Remotename);
                }
                else if (e is DbUpdate dbUpdate)
                    db.UpdateRemoteVolume(dbUpdate.Remotename, dbUpdate.State, dbUpdate.Size, dbUpdate.Hash, transaction);
                else if (e is DbRename rename)
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
                    DbOperation operation => $"Operation: {operation.Action} File: {operation.File} Result: {operation.Result}",
                    DbUpdate update => $"Update: {update.Remotename} State: {update.State} Size: {update.Size} Hash: {update.Hash}",
                    DbRename rename => $"Rename: {rename.Oldname} -> {rename.Newname}",
                    _ => $"InvalidQueueElement: {e.GetType()} {e}"
                }));

            Log.WriteWarningMessage(LOGTAG, "FlushingMessagesToLog", null, message);

        }
    }
}