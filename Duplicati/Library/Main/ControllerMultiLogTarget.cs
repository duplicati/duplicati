//  Copyright (C) 2018, The Duplicati Team
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
