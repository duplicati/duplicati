using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler.Data
{
    /// <summary>
    /// Plugin interface maintains the monitor data
    /// </summary>
    public interface IMonitorPlugin
    {
        /// <summary>
        /// Unique name of this plugin (will appear on Scheduler menu)
        /// </summary>
        string Name { get; }
        /// <summary>
        /// Update a log entry
        /// </summary>
        /// <param name="aLogDate">Date</param>
        /// <param name="aLogContent">XML content</param>
        void UpdateLog(DateTime aLogDate, string aLogContent);
        /// <summary>
        /// Update the scheduler record
        /// </summary>
        /// <param name="aScheduler">XML file to load</param>
        void UpdateScheduler(string aScheduler);
        /// <summary>
        /// Update History
        /// </summary>
        void UpdateHistory(string aHistory);
        /// <summary>
        /// Dialog Form that configures the monitor
        /// </summary>
        /// <returns>Dialog Result</returns>
        System.Windows.Forms.DialogResult Configure();
    }
}
