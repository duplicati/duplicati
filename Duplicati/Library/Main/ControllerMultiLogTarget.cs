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

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Logging;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// The log target handler for the controller, which sends to multiple log targets
    /// </summary>
    public class ControllerMultiLogTarget : ILogDestination, ILogFilter, IDisposable
    {
        /// <summary>
        /// The target entry for the log target
        /// </summary>
        /// <param name="Target">The log target.</param>
        /// <param name="Level">The minimum log level to consider</param>
        /// <param name="Filter">The log filter.</param>
        private sealed record TargetEntry(ILogDestination Target, LogMessageType Level, Library.Utility.IFilter Filter);

        /// <summary>
        /// The list of log targets to handle
        /// </summary>
        private readonly List<TargetEntry> m_targets = new List<TargetEntry>();

        /// <summary>
        /// The filter used to suppress warnings
        /// </summary>
        private readonly HashSet<string>? m_supressWarningFilter;

        /// <summary>
        /// The result the messages are collected in, which a nested result replaces
        /// </summary>
        private readonly ILogDestination m_primaryTarget;

        /// <summary>
        /// The nested result that collects the messages in place of <see cref="m_primaryTarget"/>, or null
        /// </summary>
        private ILogDestination? m_nestedResult;

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/> class.
        /// </summary>
        /// <param name="target">The log target.</param>
        /// <param name="loglevel">The minimum log level to consider</param>
        /// <param name="filter">The log filter.</param>
        public ControllerMultiLogTarget(ILogDestination target, Logging.LogMessageType loglevel, Library.Utility.IFilter? filter, HashSet<string>? supressWarningFilter)
        {
            m_supressWarningFilter = supressWarningFilter;
            m_primaryTarget = target;
            AddTarget(target, loglevel, filter);
        }

        /// <summary>
        /// Collects the messages in a result nested in the one this target collects for, until the returned
        /// instance is disposed. The messages still reach the other destinations and the outer log scopes, such
        /// as the server log, but not the outer result, so an operation that runs inside another is reported on
        /// its own.
        /// </summary>
        /// <param name="nestedResult">The nested result to collect the messages in</param>
        /// <returns>The instance that restores the outer result when disposed</returns>
        public IDisposable CollectInNestedResult(ILogDestination nestedResult)
        {
            if (m_nestedResult != null)
                throw new InvalidOperationException("Messages are already collected in a nested result");

            m_nestedResult = nestedResult;
            return new NestedResultScope(this);
        }

        /// <summary>
        /// Restores the outer result when disposed
        /// </summary>
        /// <param name="Parent">The target to restore</param>
        private sealed record NestedResultScope(ControllerMultiLogTarget Parent) : IDisposable
        {
            public void Dispose() => Parent.m_nestedResult = null;
        }

        /// <summary>
        /// Adds the target to the list.
        /// </summary>
        /// <param name="target">The log target.</param>
        /// <param name="loglevel">The minimum log level to consider</param>
        /// <param name="filter">The log filter.</param>
        public void AddTarget(ILogDestination? target, LogMessageType loglevel, Library.Utility.IFilter? filter)
        {
            if (target == null)
                return;

            m_targets.Add(new TargetEntry(target, loglevel, filter ?? new Library.Utility.FilterExpression()));
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/> so the garbage collector can reclaim the
        /// memory that the <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/> was occupying.</remarks>
		public void Dispose()
        {
            foreach (var m in m_targets)
                (m.Target as IDisposable)?.Dispose();
            m_targets.Clear();
        }

        /// <summary>
        /// Gets the minimum log level of all the targets
        /// </summary>
        public LogMessageType MinimumLevel
            => m_targets.Select(x => x.Level).DefaultIfEmpty(LogMessageType.Error).Min();

        /// <summary>
        /// Writes the message to all the destinations.
        /// </summary>
        /// <param name="entry">Entry.</param>
        public void WriteMessage(LogEntry entry)
        {
            entry = ApplyWarningSuppression(entry);

            foreach (var e in m_targets)
            {
                if (!TargetAccepts(e, entry))
                    continue;

                // If we get here, write the message, to the nested result in place of the outer one
                var nestedResult = m_nestedResult;
                if (nestedResult != null && ReferenceEquals(e.Target, m_primaryTarget))
                    nestedResult.WriteMessage(entry);
                else
                    e.Target.WriteMessage(entry);
            }
        }

        /// <summary>
        /// Checks if a message would be written to at least one of the destinations
        /// </summary>
        /// <param name="entry">The entry to examine</param>
        /// <returns><c>true</c> if the message is written; <c>false</c> otherwise</returns>
        public bool Accepts(LogEntry entry)
        {
            entry = ApplyWarningSuppression(entry);

            foreach (var e in m_targets)
                if (TargetAccepts(e, entry) && (e.Target is not ILogFilter targetFilter || targetFilter.Accepts(entry)))
                    return true;

            return false;
        }

        /// <summary>
        /// Changes a suppressed warning message into an information message
        /// </summary>
        /// <param name="entry">The entry to examine</param>
        /// <returns>The entry to write</returns>
        private LogEntry ApplyWarningSuppression(LogEntry entry)
        {
            if (m_supressWarningFilter != null && entry.Level == LogMessageType.Warning && m_supressWarningFilter.Contains(entry.Id))
                return new LogEntry(entry.Message, entry.Arguments, LogMessageType.Information, entry.Tag, entry.Id, entry.Exception);

            return entry;
        }

        /// <summary>
        /// Checks if the filter and log level of a target lets the message through
        /// </summary>
        /// <param name="target">The target to check</param>
        /// <param name="entry">The entry to examine</param>
        /// <returns><c>true</c> if the target should get the message; <c>false</c> otherwise</returns>
        private static bool TargetAccepts(TargetEntry target, LogEntry entry)
        {
            // If there is a filter match, use that, otherwise filter by log-level
            if (target.Filter.Matches(entry.FilterTag, out var result, out var _))
                return result;

            return entry.Level >= target.Level;
        }
    }
}
