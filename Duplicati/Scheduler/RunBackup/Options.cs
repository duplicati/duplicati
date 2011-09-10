#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Duplicati.Scheduler.RunBackup
{
    /// <summary>
    /// Manipulate Options to be Duplicati ready
    /// </summary>
    public class Options : Dictionary<string, string>
    {
        /// <summary>
        /// The backup destination 
        /// </summary>
        public string Target { get; set; }
        /// <summary>
        /// The backup source URI
        /// </summary>
        public string[] Source { get; set; }
        /// <summary>
        /// How long to keep log files (==0 = forever)
        /// </summary>
        public int LogFileMaxAgeDays { get; set; }
        /// <summary>
        /// Is this backup 'full', false = 'incremental'
        /// </summary>
        public bool Full { get; set; }
        /// <summary>
        /// Results from attempted mapping of network drives
        /// </summary>
        public string MapResults { get; set; }
        /// <summary>
        /// The Checksum
        /// </summary>
        public byte[] Checksum { get; private set; }
        /// <summary>
        /// CheckMod
        /// </summary>
        public string CheckMod { get; private set; }
        public string[] DisabledMonitors { get; private set; }
        /// <summary>
        /// Parse Options ready for Duplicati backup run
        /// </summary>
        /// <param name="aJob">Job name</param>
        /// <param name="aXML">Job database XML file name</param>
        public Options(string aJob, string aXML)
        {
            // Get options from XML
            using (Duplicati.Scheduler.Data.SchedulerDataSet sds = new Duplicati.Scheduler.Data.SchedulerDataSet())
            {
                // Let this throw...
                Exception Ex = sds.Load(aXML);  // Load database from XML
                if (Ex != null) Library.Logging.Log.WriteMessage(aXML + ":" + Ex.Message, Duplicati.Library.Logging.LogMessageType.Error);
                // Fetch turned off monitors
                this.DisabledMonitors = sds.Settings.DisabledMonitors;
                // Find the job
                Duplicati.Scheduler.Data.SchedulerDataSet.JobsRow Row = sds.Jobs.FindByName(aJob);
                if (Row == null) throw new ArgumentException("No such job: " + aJob);
                // Get source, destination, options
                Target = Row.Destination;
                Source = Row.Source.Split(System.IO.Path.PathSeparator);
                foreach (KeyValuePair<string, string> kvp in sds.Jobs.GetOptions(aJob))
                    this.Add(kvp.Key, kvp.Value);
                // Get that log limit
                LogFileMaxAgeDays = sds.Settings.Values.LogFileAgeDays;
                // Look for the log file
                if (this.ContainsKey("log-file"))
                    this.Remove("log-file");
                // Map network drives
                if (Row.MapDrives)
                {
                    foreach (Duplicati.Scheduler.Data.SchedulerDataSet.DriveMapsRow dRow in Row.GetDriveMapsRows())
                    {
                        System.IO.DriveInfo di = new System.IO.DriveInfo(dRow.DriveLetter);
                        if (di != null && !di.IsReady)
                            MapResults += Duplicati.Scheduler.Utility.User.Run("net.exe", "use " + di.Name + ' ' + dRow.UNC) + '\n';
                    }
                }
                // Take care of the filters
                if (!string.IsNullOrEmpty(Row.Filter))
                    this["filter"] = ProcessFilter(Row.Filter);
                this["full-if-sourcefolder-changed"] = true.ToString();
                // OK, deal with the 'FULL'
                if (this.ContainsKey("full"))
                    Full = bool.Parse(this["full"]);
                else if (Row.FullOnly || (Row.FullRepeatDays <= 0 && Row.FullAfterN <= 0))
                    Full = true;
                else
                    Full = IsFull(aJob, Row.FullAfterN, Row.FullRepeatDays);
                this["full"] = Full.ToString();
                this["full-if-sourcefolder-changed"] = true.ToString();
                if (this.ContainsKey("Destination")) this.Remove("Destination");
                if (Row.MaxAgeDays > 0)
                    this["delete-older-than"] = Row.MaxAgeDays.ToString() + 'd';
                if (Row.MaxFulls > 0)
                    this["delete-all-but-n-full"] = Row.MaxFulls.ToString();
                if (Row.AutoCleanup || Row.MaxAgeDays > 0 || Row.MaxFulls > 0)
                {
                    this["auto-cleanup"] = true.ToString();
                    this["force"] = true.ToString();
                }
                this["encryption-module"] = this.CheckMod = Row.CheckMod;
                if (!this.ContainsKey("volsize")) this["volsize"] = "50MB";
            }
            this["log-level"] = Duplicati.Library.Logging.Log.LogLevel.ToString(); // Don't mess with the log level.
            // uh hmmm
            if (this.ContainsKey("Checksum"))
            {
                Checksum = System.Convert.FromBase64String(this["Checksum"]);
                // Unprotect will only work if process is the same user as it was protected; which this one should be.
                this["passphrase"] = System.Text.ASCIIEncoding.ASCII.GetString(Duplicati.Scheduler.Utility.Tools.Unprotect(this.Checksum));
                this.Remove("Checksum");
            }
        }
        /// <summary>
        /// Determine if this is a full backup by looking back
        /// </summary>
        /// <remarks>
        /// This looks in the history database at the last full backup for this job.
        /// If that date is older than the aRepeatDays parameter, then the backup is full.
        /// If not and the aNumberRuns parameter is zero, the backup is full
        /// If not the backup is full if the number of runs since the last full run is greater than aNumberRuns
        /// </remarks>
        /// <param name="aJob">Job name</param>
        /// <param name="aNumberRuns">The number of incremental runs before a full (0=infinate)</param>
        /// <param name="aRepeatDays">The number of days between full runs</param>
        /// <returns>true 'full', false = 'incremental'</returns>
        public static bool IsFull(string aJob, int aNumberRuns, int aRepeatDays)
        {
            bool Result = true;
            try
            {
                // Get the history file
                Duplicati.Scheduler.Data.HistoryDataSet hds = new Duplicati.Scheduler.Data.HistoryDataSet();
                hds.Load();
                // Get the last full run
                DateTime LastFull = hds.History.LastFull(aJob);
                // If it is past the number of days, it is full
                if (LastFull.AddDays(aRepeatDays) >= DateTime.Now) return true;
                // Always full of NumberRuns == 0
                if (aNumberRuns <= 0) return true;
                // Otherwise if the number of incremental is greater than the number of runs...
                return (hds.History.NumberSinceLastFull(aJob, LastFull) > aNumberRuns);
            }
            catch (Exception Ex)
            {
                Debug.WriteLine(Ex);    // Ignore errors just return true
            }
            return Result;
        }
        /// <summary>
        /// Turns filters in the "[+-]filter;..." format to Duplicati "[ei]:filter;..." format
        /// </summary>
        /// <param name="aFilterString">"[+-]filter;..." formatted filters</param>
        /// <returns>Duplicati "[ei]:filter;..." format filters</returns>
        public static string ProcessFilter(string aFilterString)
        {
            return string.IsNullOrEmpty(aFilterString) ? string.Empty :
                ("\0" + aFilterString)     // Leading null is so 1st line will match
                .Replace("\0-", System.IO.Path.PathSeparator.ToString() + "e:")
                .Replace("\0+", System.IO.Path.PathSeparator.ToString() + "i:");
        }

    }
}
