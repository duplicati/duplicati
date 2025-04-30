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
    /// The log target handler for the controller, which sends to multiple log targets
    /// </summary>
    public class ControllerMultiLogTarget : Logging.ILogDestination, IDisposable
    {
        /// <summary>
        /// The list of log targets to handle
        /// </summary>
        private readonly List<Tuple<ILogDestination, LogMessageType, Library.Utility.IFilter>> m_targets = new List<Tuple<ILogDestination, LogMessageType, Library.Utility.IFilter>>();

        /// <summary>
        /// Initializes a new instance of the <see cref="T:Duplicati.Library.Main.ControllerMultiLogTarget"/> class.
        /// </summary>
        /// <param name="target">The log target.</param>
        /// <param name="loglevel">The minimum log level to consider</param>
        /// <param name="filter">The log filter.</param>
        public ControllerMultiLogTarget(ILogDestination target, Logging.LogMessageType loglevel, Library.Utility.IFilter filter)
        {
            AddTarget(target, loglevel, filter);
        }

        /// <summary>
        /// Adds the target to the list.
        /// </summary>
        /// <param name="target">The log target.</param>
        /// <param name="loglevel">The minimum log level to consider</param>
        /// <param name="filter">The log filter.</param>
        public void AddTarget(ILogDestination target, LogMessageType loglevel, Library.Utility.IFilter filter)
        {
            if (target == null)
                return;

            m_targets.Add(new Tuple<ILogDestination, LogMessageType, Library.Utility.IFilter>(target, loglevel, filter ?? new Library.Utility.FilterExpression()));
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
                (m.Item1 as IDisposable)?.Dispose();
            m_targets.Clear();
        }

        /// <summary>
        /// Gets the minimum log level of all the targets
        /// </summary>
        public LogMessageType MinimumLevel
            => m_targets.Select(x => x.Item2).DefaultIfEmpty(LogMessageType.Error).Min();

        /// <summary>
        /// Writes the message to all the destinations.
        /// </summary>
        /// <param name="entry">Entry.</param>
        public void WriteMessage(LogEntry entry)
        {
            foreach (var e in m_targets)
            {
                var found = e.Item3.Matches(entry.FilterTag, out var result, out var match);

                // If there is a filter match, use that
                if (found)
                {
                    if (!result)
                        continue;
                }
                else
                {
                    // Otherwise, filter by log-level
                    if (entry.Level < e.Item2)
                        continue;
                }

                // If we get here, write the message
                e.Item1.WriteMessage(entry);
            }
        }
    }
}
