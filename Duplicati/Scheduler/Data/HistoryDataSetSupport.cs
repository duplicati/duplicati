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
using System.Text;

namespace Duplicati.Scheduler.Data
{
    /// <summary>
    /// Provides some tools used with the history dataset
    /// </summary>
    public partial class HistoryDataSet
    {
        /// <summary>
        /// The name of the XML file (no path)
        /// </summary>
        public const string DefaultName = "History.xml";
        /// <summary>
        /// The XML file path without the file name
        /// </summary>
        public static string DefaultPath()
        {
            return GetAppData(DefaultName);
        }
        /// <summary>
        /// The application path and filename, making sure it exists
        /// </summary>
        public static string GetAppData(string aFileName)
        {
            string Result = Utility.User.GetApplicationDirectory(@"Duplicati");
            if (!System.IO.Directory.Exists(Result)) System.IO.Directory.CreateDirectory(Result);
            return System.IO.Path.Combine(Result, aFileName);
        }
        /// <summary>
        /// Load the database from the deefault XML file
        /// </summary>
        /// <returns>null if OK, else the exception</returns>
        public Exception Load()
        {
            return Load(string.Empty);
        }
        /// <summary>
        /// Load the database from a file (if empty, use the default)
        /// </summary>
        /// <param name="aFullPath">File to load or default if null or empty</param>
        /// <returns>null if OK, else the exception</returns>
        public Exception Load(string aFullPath)
        {
            if (string.IsNullOrEmpty(aFullPath)) aFullPath = DefaultPath();
            if (!System.IO.File.Exists(aFullPath)) return new Exception("File not found: " + aFullPath);
            this.Clear();
            Exception Result = null;
            try
            {
                this.ReadXml(aFullPath);
            }
            catch (Exception Ex)
            {
                Result = Ex;
            }
            return Result;
        }
        /// <summary>
        /// Saves the dataset to the default XML
        /// </summary>
        /// <returns>null if OK, else the exception</returns>
        public Exception Save()
        {
            return Save(GetAppData(DefaultName));
        }
        /// <summary>
        /// Saves the dataset to the passed file
        /// </summary>
        /// <param name="aFullPath">XML file to load</param>
        /// <returns>null if OK, else the exception</returns>
        public Exception Save(string aFullPath)
        {
            Exception Result = null;
            try
            {
                this.WriteXml(aFullPath);
            }
            catch (Exception Ex)
            {
                Result = Ex;
            }
            return Result;
        }
        /// <summary>
        /// Tools for the data table
        /// </summary>
        public partial class HistoryDataTable 
        {
            /// <summary>
            /// Get all rows for a job
            /// </summary>
            /// <param name="aJob">Job to load</param>
            /// <returns>Rows or empty if none</returns>
            public HistoryRow[] GetHistory(string aJob)
            {
                return this.Where(qR => qR.Name.Equals(aJob)).ToArray();
            }
            /// <summary>
            /// Gives all rows for a job older than a time span
            /// </summary>
            /// <param name="aJob">Job to load</param>
            /// <param name="aYoungerThan">TimeSpan older than which rows will not be loaded</param>
            /// <returns></returns>
            public HistoryRow[] GetHistory(string aJob, TimeSpan aYoungerThan)
            {
                DateTime Later = DateTime.Now - aYoungerThan;
                return GetHistory(aJob).Where(qR => qR.ActionDate > Later).ToArray();
            }
            /// <summary>
            /// Returns the last FULL backup for a job or null
            /// </summary>
            /// <param name="aJob">Job to load</param>
            /// <returns>the last FULL backup for a job or null</returns>
            public DateTime LastFull(string aJob)
            {
                return GetHistory(aJob).Where(qF => qF.Full && qF.Success).Select(qD => qD.ActionDate).DefaultIfEmpty(DateTime.MinValue).Max();
            }
            /// <summary>
            /// Returns the number of runs since the last full backup
            /// </summary>
            /// <param name="aJob">Job to load</param>
            /// <returns>The number of runs since the last full backup</returns>
            public int NumberSinceLastFull(string aJob)
            {
                DateTime Last = LastFull(aJob);
                if (Last == DateTime.MinValue) return 0;
                return NumberSinceLastFull(aJob, Last);
            }
            /// <summary>
            /// Returns the number of incremntal runs since a date
            /// </summary>
            /// <param name="aJob">Job to run</param>
            /// <param name="aLastFull">Date</param>
            /// <returns>The number of incremntal runs since a date</returns>
            public int NumberSinceLastFull(string aJob, DateTime aLastFull)
            {
                return GetHistory(aJob).Where(qF => !qF.Full && qF.Success && qF.ActionDate > aLastFull).Count();
            }
            /// <summary>
            /// Deletes all rows for a job
            /// </summary>
            /// <param name="aJob"></param>
            public void DeleteJob(string aJob)
            {
                foreach (HistoryRow Row in GetHistory(aJob))
                    Row.Delete();
            }
        }
        /// <summary>
        /// Data row tools
        /// </summary>
        public partial class HistoryRow
        {
            /// <summary>
            /// Update a stats row
            /// </summary>
            /// <param name="aOutput">Duplicati output (Result) string</param>
            public void UpdateStats(string aOutput)
            {
                StatsRow[] Stats = this.GetStatsRows();
                StatsRow sRow = null;
                if (Stats == null || Stats.Length == 0)
                    sRow = ((StatsDataTable)this.tableHistory.ChildRelations["History_Stats"].ChildTable).AddStatsRow(this,
                        this.ActionDate, 0, 0, 0, 0, 0L, 0L, 0L, 0, 0, 0, this.ActionDate, this.ActionDate, string.Empty,
                        0, 0, 0, string.Empty, null);
                else
                    sRow = Stats[0];
                // Parse the stats out of the results
                foreach (string Line in aOutput.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int Colon = Line.IndexOf(':');
                    if (Colon > 0)
                    {
                        string Field = Line.Substring(0, Colon).Trim();
                        string Value = Line.Substring(Colon + 1).Trim();
                        if (sRow.Table.Columns.Contains(Field))
                            sRow[Field] = System.Convert.ChangeType(Value, sRow.Table.Columns[Field].DataType);
                    }
                }
            }
            /// <summary>
            /// Update a History row
            /// </summary>
            /// <param name="aAction">Action done</param>
            /// <param name="aFull">Was it full</param>
            /// <param name="aSuccess">OK</param>
            /// <param name="aStatus">Status</param>
            /// <param name="aChecksum">Checksun used</param>
            /// <param name="aCheckMod">Module used</param>
            public void Update(string aAction, bool aFull, bool aSuccess, string aStatus, byte[] aChecksum, string aCheckMod)
            {
                this.Action = aAction;
                this.Full = aFull;
                this.Success = aSuccess;
                this.CheckMod = aCheckMod;
                this.Checksum = aChecksum;
                this.UpdateStats(aStatus);
            }
            /// <summary>
            /// Delete a history row
            /// </summary>
            public new void Delete()
            {
                foreach (StatsRow sRow in from StatsRow qS in this.GetStatsRows() select qS)
                    sRow.Delete();
                base.Delete();
            }
        }
    }
}
