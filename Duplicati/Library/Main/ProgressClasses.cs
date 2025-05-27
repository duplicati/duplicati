// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// Interface for receiving messages from the Duplicati operations
    /// </summary>
    public interface IMessageSink : Logging.ILogDestination
    {
        /// <summary>
        /// Handles an event from the backend
        /// </summary>
        /// <param name="action">The backend action.</param>
        /// <param name="type">The event type.</param>
        /// <param name="path">The target path.</param>
        /// <param name="size">The size of the element.</param>
        void BackendEvent(BackendActionType action, BackendEventType type, string path, long size);

        /// <summary>
        /// Sets the backend progress update object
        /// </summary>
        /// <value>The backend progress update object</value>
        void SetBackendProgress(IBackendProgress progress);

        /// <summary>
        /// Sets the operation progress update object
        /// </summary>
        /// <value>The operation progress update object</value>
        void SetOperationProgress(IOperationProgress progress);
    }

    /// <summary>
    /// Helper class to allow setting multiple message sinks on a single controller
    /// </summary>
    public class MultiMessageSink : IMessageSink
    {
        /// <summary>
        /// The sinks in this instance
        /// </summary>
        private IMessageSink[] m_sinks;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Main.MultiMessageSink"/> class.
        /// </summary>
        /// <param name="sinks">The sinks to use.</param>
        public MultiMessageSink(params IMessageSink[] sinks)
        {
            m_sinks = (sinks ?? new IMessageSink[0]).Where(x => x != null).ToArray();
        }

        /// <summary>
        /// Appends a new sink to the list
        /// </summary>
        /// <param name="sink">The sink to append.</param>
        public void Append(IMessageSink sink)
        {
            if (sink == null)
                return;

            var na = new IMessageSink[m_sinks.Length + 1];
            Array.Copy(m_sinks, na, m_sinks.Length);
            na[na.Length - 1] = sink;
            m_sinks = na;
        }

        public void SetBackendProgress(IBackendProgress progress)
        {
            foreach (var s in m_sinks)
                s.SetBackendProgress(progress);
        }

        public void SetOperationProgress(IOperationProgress progress)
        {
            foreach (var s in m_sinks)
                s.SetOperationProgress(progress);
        }

        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size)
        {
            foreach (var s in m_sinks)
                s.BackendEvent(action, type, path, size);
        }

        public void WriteMessage(LogEntry entry)
        {
            foreach (var s in m_sinks)
                s.WriteMessage(entry);
        }
    }

    /// <summary>
    /// State of a single backend action.
    /// </summary>
    /// <param name="Action">The type of action being performed.</param>
    /// <param name="Path">The path being operated on.</param>
    /// <param name="Size">The size of the file being transferred.</param>
    /// <param name="Progress">The current number of transferred bytes.</param>
    /// <param name="BytesPerSecond">The transfer speed in bytes per second, -1 for unknown.</param>
    /// <param name="IsBlocking">A value indicating if the backend action is blocking operation progress.</param>
    public record BackendActionProgress(
        BackendActionType Action,
        string Path,
        long Size,
        long Progress,
        long BytesPerSecond,
        bool IsBlocking);

    /// <summary>
    /// Backend progress update object.
    /// The engine updates these statistics very often,
    /// so an event based system would take up too many resources.
    /// Instead, this interface allows the client to poll
    /// for updates as often as desired.
    /// </summary>
    public interface IBackendProgress
    {
        /// <summary>
        /// Returns a snapshot of the current backend progress.
        /// </summary>
        /// <returns>A list of backend action progress items.</returns>
        BackendActionProgress[] GetActiveTransfers();
    }

    /// <summary>
    /// Interface for updating the backend progress
    /// </summary>
    internal interface IBackendProgressUpdater
    {
        /// <summary>
        /// Register the start of a new action
        /// </summary>
        /// <param name="action">The action that is starting</param>
        /// <param name="path">The path being operated on</param>
        /// <param name="size">The size of the file being transferred</param>
        void StartAction(BackendActionType action, string path, long size);
        /// <summary>
        /// Ends an active transfer action
        /// </summary>
        /// <param name="action">The action that is ending</param>
        /// <param name="path">The path being operated on</param>
        void EndAction(BackendActionType action, string path);
        /// <summary>
        /// Updates the current progress
        /// </summary>
        /// <param name="path">The path being operated on</param>
        /// <param name="progress">The current number of transferred bytes</param>
        void UpdateProgress(string path, long progress);

        /// <summary>
        /// Sets a flag indicating if the backend operation is blocking progress
        /// </summary>
        /// <param name="isBlocking">If set to <c>true</c> the backend is blocking.</param>
        void SetBlocking(bool isBlocking);
    }

    /// <summary>
    /// Combined interface for the backend progress updater and the backend progress item
    /// </summary>
    internal interface IBackendProgressUpdaterAndReporter : IBackendProgressUpdater, IBackendProgress
    {
    }

    /// <summary>
    /// Backend progress updater instance
    /// </summary>
    internal class BackendProgressUpdater : IBackendProgressUpdaterAndReporter
    {
        /// <summary>
        /// Lock object to provide snapshot-like access to the data
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// A value indicating when the last blocking was done
        /// </summary>
        private DateTime m_blockingSince;

        /// <summary>
        /// Information about an active transfer
        /// </summary>
        /// <param name="Filename">The filename of the transfer</param>
        /// <param name="Started">When the transfer started</param>
        /// <param name="Type">The type of the transfer</param>
        /// <param name="Size">The size of the transfer</param>
        private sealed record TransferInfo(string Filename, DateTime Started, BackendActionType Type, long Size);

        /// <summary>
        /// The number of progress events to keep for each transfer
        /// </summary>
        private const int MaxProgressEvents = 30;

        /// <summary>
        /// If the backend manager is blocking progress, wait this long before considering it as blocking progress
        /// </summary>
        private static readonly TimeSpan BlockingWaitTime = TimeSpan.FromSeconds(5);

        /// <summary>
        /// A single recorded progress event
        /// </summary>
        private struct ProgressEvent
        {
            /// <summary>
            /// The time in seconds since the epoch when the event was recorded
            /// </summary>
            public long When;
            /// <summary>
            /// The number of bytes transferred at the time of the event
            /// </summary>
            public long Progress;
        }

        /// <summary>
        /// The information for the active transfers
        /// </summary>
        private Dictionary<string, TransferInfo> m_activeTransferInfo = new();
        /// <summary>
        /// The active transfer progress
        /// </summary>
        /// <remarks>
        /// The list is sorted by the time of the event, so the last item is always the most recent progress.
        /// </remarks>
        private Dictionary<string, List<ProgressEvent>> m_activeTransferProgress = new();

        /// <inheritdoc />
        public void StartAction(BackendActionType action, string path, long size)
        {
            lock (m_lock)
                m_activeTransferInfo.Add(path, new TransferInfo(path, DateTime.UtcNow, action, size));
        }

        /// <inheritdoc />
        public void UpdateProgress(string path, long progress)
        {
            lock (m_lock)
            {
                var ts = (long)(DateTime.UtcNow - DateTime.UnixEpoch).TotalSeconds;
                if (!m_activeTransferProgress.TryGetValue(path, out var pg))
                    m_activeTransferProgress[path] = pg = new List<ProgressEvent>();

                if (pg.Count == 0 || pg.Last().When != ts)
                    pg.Add(new ProgressEvent { When = ts, Progress = progress });
                else
                    pg[^1] = new ProgressEvent { When = ts, Progress = progress };

                // Each bucket is 1 second, so we keep the last MaxProgressEvents seconds of progress
                var cutoff = ts - MaxProgressEvents;

                // Remove old progress events
                while (pg.Count > MaxProgressEvents || (pg.Count > 0 && pg[0].When < cutoff))
                {
                    if (pg.Count > 0)
                        pg.RemoveAt(0);
                    else
                        break; // No more progress events to remove
                }
            }
        }

        /// <inheritdoc />
        public void EndAction(BackendActionType action, string path)
        {
            lock (m_lock)
            {
                if (m_activeTransferProgress.ContainsKey(path))
                {
                    m_activeTransferProgress.Remove(path);
                    m_activeTransferInfo.Remove(path);
                }
            }
        }

        /// <inheritdoc />
        public BackendActionProgress[] GetActiveTransfers()
        {
            lock (m_lock)
            {
                return m_activeTransferInfo.Values.OrderBy(x => x.Started).Select(x =>
                {
                    var pg = m_activeTransferProgress.GetValueOrDefault(x.Filename) ?? [];
                    var speed = -1L;
                    if (pg.Count > 1)
                    {
                        var start = pg.FirstOrDefault();
                        var end = pg.LastOrDefault();
                        speed = (end.Progress - start.Progress) / (end.When - start.When);
                    }

                    return new BackendActionProgress(
                            x.Type,
                            x.Filename,
                            x.Size,
                            pg.Count > 0 ? pg.Last().Progress : 0, // Use the last recorded progress or 0 if no progress
                            speed,
                            m_blockingSince > DateTime.MinValue && (DateTime.UtcNow - m_blockingSince) > BlockingWaitTime
                        );
                }).ToArray();
            }
        }

        /// <summary>
        /// Sets a flag indicating if the backend operation is blocking progress
        /// </summary>
        /// <param name="isBlocking">If set to <c>true</c> the backend is blocking.</param>
        public void SetBlocking(bool isBlocking)
        {
            lock (m_lock)
                m_blockingSince = isBlocking ? DateTime.UtcNow : new DateTime(0);
        }
    }

    public delegate void PhaseChangedDelegate(OperationPhase phase, OperationPhase previousPhase);

    /// <summary>
    /// Operation progress update object.
    /// The engine updates these statistics very often,
    /// so an event based system would take up too many resources.
    /// Instead, this interface allows the client to poll
    /// for updates as often as desired.
    /// </summary>
    public interface IOperationProgress
    {
        /// <summary>
        /// Update the phase, progress, filesprocessed, filesizeprocessed, filecount, filesize and countingfiles.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="progress">Progress.</param>
        /// <param name="filesprocessed">Filesprocessed.</param>
        /// <param name="filesizeprocessed">Filesizeprocessed.</param>
        /// <param name="filecount">Filecount.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="countingfiles">True if the filecount and filesize is incomplete, false otherwise</param>
        void UpdateOverall(out OperationPhase phase, out float progress, out long filesprocessed, out long filesizeprocessed, out long filecount, out long filesize, out bool countingfiles);

        /// <summary>
        /// Update the filename, filesize, and fileoffset.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="fileoffset">Fileoffset.</param>
        void UpdateFile(out string filename, out long filesize, out long fileoffset, out bool filecomplete);

        /// <summary>
        /// Occurs when the phase has changed
        /// </summary>
        event PhaseChangedDelegate PhaseChanged;
    }

    /// <summary>
    /// Interface for updating the backend progress
    /// </summary>
    internal interface IOperationProgressUpdater
    {
        void UpdatePhase(OperationPhase phase);
        void UpdateProgress(float progress);
        void StartFile(string filename, long size);
        void UpdateFileProgress(long offset);
        void UpdatefileCount(long filecount, long filesize, bool done);
        void UpdatefilesProcessed(long count, long size);
    }

    internal interface IOperationProgressUpdaterAndReporter : IOperationProgressUpdater, IOperationProgress
    {
    }

    internal class OperationProgressUpdater : IOperationProgressUpdaterAndReporter
    {
        private readonly object m_lock = new object();

        private OperationPhase m_phase;
        private float m_progress;
        private string m_curfilename;
        private long m_curfilesize;
        private long m_curfileoffset;
        private bool m_curfilecomplete;

        private long m_filesprocessed;
        private long m_filesizeprocessed;
        private long m_filecount;
        private long m_filesize;

        private bool m_countingFiles;

        public event PhaseChangedDelegate PhaseChanged;

        public void UpdatePhase(OperationPhase phase)
        {
            OperationPhase prev_phase;
            lock (m_lock)
            {
                prev_phase = m_phase;
                m_phase = phase;
                m_curfilename = null;
                m_curfilesize = 0;
                m_curfileoffset = 0;
                m_curfilecomplete = false;
            }

            if (prev_phase != phase && PhaseChanged != null)
                PhaseChanged(phase, prev_phase);
        }

        public void UpdateProgress(float progress)
        {
            lock (m_lock)
                m_progress = progress;
        }

        public void StartFile(string filename, long size)
        {
            lock (m_lock)
            {
                m_curfilename = filename;
                m_curfilesize = size;
                m_curfileoffset = 0;
                m_curfilecomplete = false;
            }
        }

        public void UpdateFileProgress(long offset)
        {
            lock (m_lock)
                m_curfileoffset = offset;
        }

        public void UpdatefileCount(long filecount, long filesize, bool done)
        {
            lock (m_lock)
            {
                m_filecount = filecount;
                m_filesize = filesize;
                m_countingFiles = !done;
            }
        }

        public void UpdatefilesProcessed(long count, long size)
        {
            lock (m_lock)
            {
                m_filesprocessed = count;
                m_filesizeprocessed = size;
                m_curfilecomplete = true;
            }
        }

        /// <summary>
        /// Update the phase, progress, filesprocessed, filesizeprocessed, filecount, filesize and countingfiles.
        /// </summary>
        /// <param name="phase">Phase.</param>
        /// <param name="progress">Progress.</param>
        /// <param name="filesprocessed">Filesprocessed.</param>
        /// <param name="filesizeprocessed">Filesizeprocessed.</param>
        /// <param name="filecount">Filecount.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="countingfiles">True if the filecount and filesize is incomplete, false otherwise</param>
        public void UpdateOverall(out OperationPhase phase, out float progress, out long filesprocessed, out long filesizeprocessed, out long filecount, out long filesize, out bool countingfiles)
        {
            lock (m_lock)
            {
                phase = m_phase;
                filesize = m_filesize;
                progress = m_progress;
                filesprocessed = m_filesprocessed;
                filesizeprocessed = m_filesizeprocessed;
                filecount = m_filecount;
                countingfiles = m_countingFiles;
            }
        }

        /// <summary>
        /// Update the filename, filesize, and fileoffset.
        /// </summary>
        /// <param name="filename">Filename.</param>
        /// <param name="filesize">Filesize.</param>
        /// <param name="fileoffset">Fileoffset.</param>
        public void UpdateFile(out string filename, out long filesize, out long fileoffset, out bool filecomplete)
        {
            lock (m_lock)
            {
                filename = m_curfilename;
                filesize = m_curfilesize;
                fileoffset = m_curfileoffset;
                filecomplete = m_curfilecomplete;
            }
        }
    }
}

