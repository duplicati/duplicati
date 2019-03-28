//  Copyright (C) 2019, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Duplicati.Library.Logging;

namespace Duplicati.CommandLine.SubProcess
{
    /// <summary>
    /// Marker interface for a log proxy
    /// </summary>
    public interface ILogProxy : IDisposable
    {
        /// <summary>
        /// Test method that writes to the log destination attached to this proxy
        /// </summary>
        /// <param name="message">The log message to write.</param>
        void WriteTestMessage(string message);
        /// <summary>
        /// Writes a message to the log scope
        /// </summary>
        /// <param name="message">The log message to write.</param>
        void WriteLogMessage(string message);
    }

    /// <summary>
    /// Log destination that does not use <see cref="LogEntry"/> to pass data
    /// </summary>
    public interface ISimpleDestination
    {
        void WriteMessage(DateTime when, string message, LogMessageType level, string tag, string id, Exception ex, Dictionary<string, string> logcontext);

    }

    public class LogProxy : ILogProxy
    {
        /// <summary>
        /// The registered proxies
        /// </summary>
        private static List<LogProxy> _proxies = new List<LogProxy>();

        /// <summary>
        /// The lock instance for the proxies list
        /// </summary>
        private static object _proxies_lock = new object();

        /// <summary>
        /// Gets or sets the shared logging destination.
        /// </summary>
        private ISimpleDestination SharedDestination { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.CommandLine.SubProcess.LogProxy"/> class.
        /// </summary>
        /// <param name="destination">The log destination.</param>
        public LogProxy(ISimpleDestination destination)
        {
            SharedDestination = destination ?? throw new ArgumentNullException(nameof(destination));
            lock(_proxies_lock)
                _proxies.Add(this);
        }

        /// <summary>
        /// Releases all resource used by the <see cref="T:Duplicati.CommandLine.SubProcess.LogProxy"/> object.
        /// </summary>
        /// <remarks>Call <see cref="Dispose"/> when you are finished using the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.LogProxy"/>. The <see cref="Dispose"/> method leaves the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.LogProxy"/> in an unusable state. After calling
        /// <see cref="Dispose"/>, you must release all references to the
        /// <see cref="T:Duplicati.CommandLine.SubProcess.LogProxy"/> so the garbage collector can reclaim the memory
        /// that the <see cref="T:Duplicati.CommandLine.SubProcess.LogProxy"/> was occupying.</remarks>
        public void Dispose()
        {
            (SharedDestination as IDisposable)?.Dispose();
            SharedDestination = null;
            lock(_proxies_lock)
                _proxies.Remove(this);
        }

        /// <summary>
        /// Writes a log message to all registered proxies
        /// </summary>
        /// <param name="entry">The log entry to write.</param>
        public static void WriteMessage(LogEntry entry)
        {
            Dictionary<string, string> logcontext = null;
            if (entry.ContextKeys.Any())
            {
                logcontext = new Dictionary<string, string>();
                foreach (var k in entry.ContextKeys)
                    logcontext[k] = entry[k];
            }

            lock (_proxies_lock)
                foreach (var n in _proxies)
                    n.SharedDestination?.WriteMessage(entry.When, entry.FormattedMessage, entry.Level, entry.Tag, entry.Id, entry.Exception, logcontext);
        }

        /// <summary>
        /// Test method that writes to the log destination attached to this proxy
        /// </summary>
        /// <param name="message">The log message to write.</param>
        public void WriteTestMessage(string message)
        {
            SharedDestination?.WriteMessage(DateTime.Now, message, LogMessageType.Error, "NA", "NA", null, null);
        }

        /// <summary>
        /// Writes a message to the log scope
        /// </summary>
        /// <param name="message">The log message to write.</param>
        public void WriteLogMessage(string message)
        {
            Log.WriteErrorMessage("NA", "NA", null, message, null);
        }
    }

    /// <summary>
    /// The server side log proxy, which just writes to the current log scope
    /// </summary>
    public class ServerLogDestinationProxy : ISimpleDestination
    {
        /// <summary>
        /// Forwards a log message to the log subsystem
        /// </summary>
        /// <param name="when">The time the message was recorded.</param>
        /// <param name="message">The message itself.</param>
        /// <param name="level">The log entry level.</param>
        /// <param name="tag">The log entry tag.</param>
        /// <param name="id">The log entry id.</param>
        /// <param name="ex">An optional exception.</param>
        /// <param name="logcontext">An optional log context.</param>
        public void WriteMessage(DateTime when, string message, LogMessageType level, string tag, string id, Exception ex, Dictionary<string, string> logcontext)
        {
            var e = new LogEntry(message, null, level, tag, id, ex) { When = when };
            if (logcontext != null)
                foreach (var c in logcontext)
                    e[c.Key] = c.Value;

            Task.Run(() => Log.WriteMessage(e));
        }
    }
}
