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
using System.Data;
using System.Threading;
using COSXML.Log;

#nullable enable

namespace Duplicati.Library.Main.Database;

/// <summary>
/// Slow query monitor helper
/// </summary>
internal static class SlowQueryMonitor
{
    /// <summary>
    /// The log tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SlowQueryMonitor));

    /// <summary>
    /// The current instance of the slow query monitor, based on call tree
    /// </summary>
    private static readonly AsyncLocal<SlowQueryMonitorInstance?> currentInstance = new();

    /// <summary>
    /// Starts monitoring slow queries
    /// </summary>
    /// <param name="threshold">The threshold for a query to be considered slow</param>
    /// <returns>A disposable that stops monitoring when disposed</returns>
    public static IDisposable? StartMonitoring(TimeSpan threshold)
    {
        if (threshold.TotalSeconds < 1)
        {
            Logging.Log.WriteInformationMessage(LOGTAG, "TooLowQueryThreshold", "Slow query threshold is lower than 1s, treating as a disable request and disabling slow query monitoring");
            return null;
        }

        return new Disposer(currentInstance.Value = new SlowQueryMonitorInstance(threshold));
    }

    /// <summary>
    /// Starts monitoring a query
    /// </summary>
    /// <param name="command">The command to monitor</param>
    /// <param name="operation">The operation being performed</param>
    /// <returns>A disposable that stops monitoring the query when disposed</returns>
    public static IDisposable? StartQuery(IDbCommand command, string operation)
         => currentInstance.Value?.StartQuery(command, operation);

    /// <summary>
    /// Dispose helper to simplify stopping the monitoring
    /// </summary>
    private struct Disposer : IDisposable
    {
        /// <summary>
        /// The instance to dispose
        /// </summary>
        private readonly SlowQueryMonitorInstance _instance;

        /// <summary>
        /// Creates a new instance of the disposer
        /// </summary>
        /// <param name="instance">The instance to dispose</param>
        public Disposer(SlowQueryMonitorInstance instance)
            => _instance = instance;

        /// <summary>
        /// Stops monitoring slow queries
        /// </summary>
        public void Dispose()
        {
            _instance.Dispose();
            currentInstance.Value = null!;
        }
    }

    /// <summary>
    /// Helper class to monitor slow queries by tracking active queries
    /// and logging a message if any runs for a long time.
    /// </summary>
    private sealed class SlowQueryMonitorInstance
    {
        /// <summary>
        /// Lock object to ensure thread safety.
        /// </summary>
        private readonly object _lock = new object();
        /// <summary>
        /// The threshold for a query to be considered slow.
        /// </summary>
        private readonly TimeSpan _slowThreshold;
        /// <summary>
        /// The next ID to assign to a query.
        /// </summary>
        private long _nextId;
        /// <summary>
        /// The dictionary of active queries.
        /// </summary>
        private Dictionary<long, ActiveQuery>? _activeQueries;
        /// <summary>
        /// The timer used to check for slow queries.
        /// </summary>
        private Timer? _timer;

        /// <summary>
        /// Represents an active query being tracked.
        /// </summary>
        /// <param name="Id">The query id</param>
        /// <param name="Command">The command being executed</param>
        /// <param name="Operation">The operation name</param>
        /// <param name="StartTime">The time the query started</param>
        private sealed record ActiveQuery(
            long Id,
            IDbCommand Command,
            string Operation,
            DateTime StartTime
        );

        /// <summary>
        /// Starts the slow query monitoring timer.
        /// </summary>
        public SlowQueryMonitorInstance(TimeSpan slowThreshold)
        {
            _nextId = 0;
            _activeQueries = new();
            _slowThreshold = slowThreshold;
            _timer = new Timer(CheckSlowQueries, null, slowThreshold, slowThreshold);
        }

        /// <summary>
        /// Starts tracking a query and returns a disposable scope that ends tracking when disposed.
        /// </summary>
        /// <param name="command">The command being executed.</param>
        /// <param name="operation">The operation name.</param>
        /// <returns>A disposable scope that ends tracking on dispose.</returns>
        public IDisposable StartQuery(IDbCommand command, string operation)
        {
            var id = Interlocked.Increment(ref _nextId);

            lock (_lock)
                _activeQueries?[id] = new ActiveQuery(id, command, operation, DateTime.UtcNow);

            return new QueryScope(this, id);
        }

        /// <summary>
        /// Ends tracking of a query. Called from the Scope.Dispose method.
        /// </summary>
        /// <param name="id">The query to end tracking for.</param>
        private void EndQuery(long id)
        {
            lock (_lock)
                _activeQueries?.Remove(id);
        }

        /// <summary>
        /// Checks for slow queries and logs them. Called by the timer.
        /// </summary>
        /// <param name="state">Unused state object</param>
        private void CheckSlowQueries(object? state)
        {
            try
            {
                // Get a snapshot of the active queries so we can check them without holding the lock
                List<ActiveQuery> snapshot;
                lock (_lock)
                {
                    if (_activeQueries == null || _activeQueries.Count == 0)
                        return;
                    snapshot = [.. _activeQueries.Values];
                }

                var now = DateTime.UtcNow;
                foreach (var query in snapshot)
                {
                    var elapsed = now - query.StartTime;
                    if (elapsed < _slowThreshold)
                        continue;

                    // Try to get the command text, but the object could be disposed by now
                    string? commandText;
                    try
                    {
                        commandText = query.Command.GetPrintableCommandText();
                    }
                    catch
                    {
                        commandText = "[command disposed or unavailable]";
                    }

                    Logging.Log.WriteWarningMessage(LOGTAG, "SlowQueryDetected", null,
                        "Query still running after {0:F1}s in {1}: {2}",
                        elapsed.TotalSeconds, query.Operation, commandText);
                }
            }
            catch (Exception ex)
            {
                // Just in case the logging system is broken
                try { Logging.Log.WriteWarningMessage(LOGTAG, "SlowQueryMonitorError", ex, "Error checking for slow queries"); }
                catch { }
            }
        }

        /// <summary>
        /// Stops the slow query monitoring timer.
        /// </summary>
        public void Dispose()
        {
            lock (_lock)
            {
                _timer?.Dispose();
                _timer = null;
                _activeQueries = null;
            }
        }

        /// <summary>
        /// A disposable scope that ends tracking of a query when disposed.
        /// </summary>
        private readonly struct QueryScope : IDisposable
        {
            /// <summary>
            /// The parent instance that owns the query.
            /// </summary>
            private readonly SlowQueryMonitorInstance _parent;
            /// <summary>
            /// The ID of the query to end tracking for.
            /// </summary>
            private readonly long _id;
            /// <summary>
            /// Creates a new scope that ends tracking of a query when disposed.
            /// </summary>
            /// <param name="id">The query to end tracking for.</param>
            public QueryScope(SlowQueryMonitorInstance parent, long id)
            {
                _parent = parent;
                _id = id;
            }
            /// <summary>
            /// Ends tracking of the query.
            /// </summary>
            public void Dispose() => _parent.EndQuery(_id);
        }
    }
}


