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
    /// Tools for the Scheduler DataSet
    /// </summary>
    public partial class SchedulerDataSet
    {
        /// <summary>
        /// The last time we loaded the XML file
        /// </summary>
        public DateTime LastLoaded { get; set; }
        /// <summary>
        /// a Kinda good user time format
        /// </summary>
        public static string DateFormat = "ddd MM/dd/yyy hh:mm tt";
        /// <summary>
        /// Makes sure the path exists and returns it
        /// </summary>
        /// <param name="aFileName">File to combine with app path</param>
        /// <returns>Full path with file name</returns>
        public static string GetAppData(string aFileName)
        {
            string Result = Utility.User.GetApplicationDirectory( @"Duplicati" );
            if (!System.IO.Directory.Exists(Result)) System.IO.Directory.CreateDirectory(Result);
            return System.IO.Path.Combine(Result, aFileName);
        }
        /// <summary>
        /// Returns the delfault XML file name with path
        /// </summary>
        /// <returns>The delfault XML file name with path</returns>
        public static string DefaultPath() { return GetAppData(DefaultName); }
        /// <summary>
        /// The XML file last loaded
        /// </summary>
        public string XMLPath { get; set; }
        /// <summary>
        /// The default XML file without path
        /// </summary>
        public const string DefaultName = "Scheduler.xml";
        /// <summary>
        /// Load the default XML file
        /// </summary>
        /// <returns>null if OK or exception thrown</returns>
        public Exception Load()
        {
            return Load(string.Empty);
        }
        /// <summary>
        /// Checks to see if the XML file changed since we last loaded
        /// </summary>
        /// <returns>true if the file is younger than the last load</returns>
        public bool NeedsLoading()
        {
            return NeedsLoading(GetAppData(DefaultName));
        }
        /// <summary>
        /// Checks to see if the XML file changed since we last loaded
        /// </summary>
        /// <param name="aFullPath">File to check</param>
        /// <returns>true if the file is younger than the last load</returns>
        public bool NeedsLoading(string aFullPath)
        {
            if (LastLoaded == null) return false;
            DateTime LastWrite = (new System.IO.FileInfo(aFullPath)).LastWriteTime;
            return LastLoaded != LastWrite;
        }
        /// <summary>
        /// Load from a file
        /// </summary>
        /// <param name="aFullPath">File to load or use default if null or empty</param>
        /// <returns>null if OK or exception thrown</returns>
        public Exception Load(string aFullPath)
        {
            if (string.IsNullOrEmpty(aFullPath)) aFullPath = DefaultPath();
            if (!System.IO.File.Exists(aFullPath)) return new Exception("File not found: " + aFullPath);
            this.Clear();
            Exception Result = null;
            try
            {
                this.ReadXml(aFullPath);
                XMLPath = aFullPath;
                LastLoaded = (new System.IO.FileInfo(aFullPath)).LastWriteTime;
            }
            catch (Exception Ex)
            {
                Result = Ex;
            }
            return Result;
        }
        /// <summary>
        /// Saves the dataset to the default XML file
        /// </summary>
        /// <returns>null if OK or exception thrown</returns>
        public Exception Save()
        {
            return Save(GetAppData(DefaultName));
        }
        /// <summary>
        /// Loads the database
        /// </summary>
        /// <param name="aFullPath">XML file to load</param>
        /// <returns>null if OK or exception thrown</returns>
        public Exception Save(string aFullPath)
        {
            Exception Result = null;
            try
            {
                this.WriteXml(aFullPath);
                LastLoaded = (new System.IO.FileInfo(aFullPath)).LastWriteTime;
            }
            catch (Exception Ex)
            {
                Result = Ex;
            }
            return Result;
        }
        /// <summary>
        /// Tools for the row
        /// </summary>
        public partial class JobsRow
        {
            /// <summary>
            /// Returns the Options
            /// </summary>
            public Dictionary<string, string> Options
            {
                get
                {
                    Dictionary<string, string> Result = (from OptionsRow qRow in this.GetOptionsRows()
                                                         select new { Key = qRow.Parameter, Value = qRow.Value }).
                              ToDictionary(n => n.Key, n => n.Value);
                    if (GetCheckSrc() == 0 || Checksum.Length == 0) Result["no-encryption"] = string.Empty;
                    else Result["Checksum"] = System.Convert.ToBase64String(Checksum);
                    if (!string.IsNullOrEmpty(Filter))
                        Result["Filter"] = Filter;
                    return Result;
                }
            }
            /// <summary>
            /// Returns the GuiOptions
            /// </summary>
            public Dictionary<string, string> GuiOptions
            {
                get
                {
                    return (from GuiOptionsRow qRow in this.GetGuiOptionsRows()
                            select new { Key = qRow.Parameter, Value = qRow.Value }).
                            ToDictionary(n => n.Key, n => n.Value);
                }
            }
            /// <summary>
            /// This is only local and is not saved in XML - Enabled should be fetched from the trigger
            /// But, this is used for temporary storage by the editor
            /// </summary>
            public bool Enabled { get; set; }
            /// <summary>
            /// Separator used in Filters
            /// </summary>
            public const char Separator = (char)3; // ETX
            /// <summary>
            /// Filters as a string array
            /// </summary>
            public string[] FilterLines
            {
                get
                {
                    if (this.IsFilterNull() || string.IsNullOrEmpty(this.Filter)) return new string[0];
                    return this.Filter.Split(Separator);
                }
                set
                {
                    if (value == null || value.Length == 0) Filter = string.Empty;
                    else Filter = string.Join(Separator.ToString(), value);
                }
            }
            /// <summary>
            /// Save as options
            /// </summary>
            /// <param name="aOptions">Options to save</param>
            /// <returns>OK if saved</returns>
            public bool SetOptions(Dictionary<string, string> aOptions)
            {
                System.Data.DataRelation dr = this.tableJobs.ChildRelations["Jobs_Options"];
                if (dr == null) return false;
                SchedulerDataSet.OptionsDataTable ChildTable = (SchedulerDataSet.OptionsDataTable)dr.ChildTable;
                foreach (SchedulerDataSet.OptionsRow Row in this.GetOptionsRows())
                    Row.Delete();
                foreach (KeyValuePair<string, string> kvp in aOptions)
                    ChildTable.AddOptionsRow(this, kvp.Key, kvp.Value);
                return true;
            }
            /// <summary>
            /// Save as GuiOptions
            /// </summary>
            /// <param name="aOptions">Options to save</param>
            /// <returns>OK if saved</returns>
            public bool SetGuiOptions(Dictionary<string, string> aOptions)
            {
                System.Data.DataRelation dr = this.tableJobs.ChildRelations["Jobs_GuiOptions"];
                if (dr == null) return false;
                SchedulerDataSet.GuiOptionsDataTable ChildTable = (SchedulerDataSet.GuiOptionsDataTable)dr.ChildTable;
                foreach (SchedulerDataSet.GuiOptionsRow Row in this.GetGuiOptionsRows())
                    Row.Delete();
                foreach (KeyValuePair<string, string> kvp in aOptions)
                    ChildTable.AddGuiOptionsRow(this, kvp.Key, kvp.Value);
                return true;
            }
            /// <summary>
            /// Creates a task name for this job
            /// </summary>
            /// <param name="aName">Job Name</param>
            /// <param name="aUserName">User Name with Domain</param>
            /// <returns>Task Name</returns>
            public static string MakeTaskName(string aName, string aUserName)
            {
                return "DUP." + aName + '.' + aUserName.Replace('\\', '.');
            }
            /// <summary>
            /// Returns the Task Name for this job
            /// </summary>
            public string TaskName
            {
                get { return MakeTaskName(this.Name, Utility.User.UserName); }
            }
            /// <summary>
            /// Returns the settings row for this job
            /// </summary>
            private SettingsRow Settings { get { return ((SchedulerDataSet)this.Table.DataSet).Settings.Values; } }
            /// <summary>
            /// Returns the Check Source [0==none, 1==Local, 2==global]
            /// </summary>
            /// <returns>Check Source [0==none, 1==Local, 2==global]</returns>
            public int GetCheckSrc()
            {
                if (this.IsCheckSrcNull() || string.IsNullOrEmpty(this.CheckSrc) || this.CheckSrc.Equals("none", StringComparison.InvariantCultureIgnoreCase))
                    return 0;
                else if (this.CheckSrc.StartsWith("g", StringComparison.InvariantCultureIgnoreCase))
                    return 2;
                return 1;
            }
            /// <summary>
            /// Sets the check source [0==none, 1==Local, 2==global]
            /// </summary>
            /// <param name="aSet">[0==none, 1==Local, 2==global]</param>
            public void SetCheckSrc(int aSet)
            {
                this.CheckSrc = new string[] { "none", "local", "global" }[aSet & 3];
            }
            /// <summary>
            /// Returns checksum based on source
            /// </summary>
            public byte[] Checksum
            {
                get
                {
                    switch (GetCheckSrc())
                    {
                        case 0: return new byte[0];
                        case 2: return Settings.Checksum;
                        default: return LocalChecksum;
                    }
                }
                set { this.LocalChecksum = value; }
            }
            /// <summary>
            /// Returns the Check Module based on Source
            /// </summary>
            public string CheckMod
            {
                get
                {
                    switch (GetCheckSrc())
                    {
                        case 0: return string.Empty;
                        case 2: return ((SchedulerDataSet)this.Table.DataSet).Settings.Values.CheckMod;
                        default: return LocalCheckMod;
                    }
                }
                set { this.LocalCheckMod = value; }
            }
            /// <summary>
            /// Drive mappings at time of last save
            /// </summary>
            public Dictionary<string, string> DriveMaps
            {
                get
                {
                    Dictionary<string, string> Result = new Dictionary<string, string>();
                    foreach (DriveMapsRow Row in GetDriveMapsRows())
                        Result.Add(Row.DriveLetter, Row.UNC);
                    return Result;
                }
            }
            /// <summary>
            /// Creates the drive map table
            /// </summary>
            /// <param name="aDriveMap">Map to save</param>
            /// <returns>true if saved OK</returns>
            public bool SetDriveMaps(Dictionary<string, string> aDriveMap)
            {
                System.Data.DataRelation dr = this.tableJobs.ChildRelations["Jobs_DriveMaps"];
                if (dr == null) return false;
                SchedulerDataSet.DriveMapsDataTable ChildTable = (SchedulerDataSet.DriveMapsDataTable)dr.ChildTable;
                foreach (SchedulerDataSet.DriveMapsRow Row in this.GetDriveMapsRows())
                    Row.Delete();
                foreach (KeyValuePair<string, string> kvp in aDriveMap)
                    ChildTable.AddDriveMapsRow(this, kvp.Key, kvp.Value);
                return true;
            }
            /// <summary>
            /// Delete this job
            /// </summary>
            public new void Delete()
            {
                // Get rid of the options
                foreach (OptionsRow oRow in GetOptionsRows())
                    oRow.Delete();
                foreach (GuiOptionsRow oRow in GetGuiOptionsRows())
                    oRow.Delete();
                // And the maps
                foreach (DriveMapsRow dRow in GetDriveMapsRows())
                    dRow.Delete();
                // And the history
                using(HistoryDataSet hds = new HistoryDataSet())
                {
                    hds.Load();
                    hds.History.DeleteJob(this.Name);
                    foreach( HistoryDataSet.HistoryRow hRow in hds.History.Select("Name = '"+this.Name+'\''))
                        hRow.Delete();
                    hds.Save();
                }
                base.Delete();
            }
        }
        /// <summary>
        /// Data table tools
        /// </summary>
        public partial class JobsDataTable
        {
            /// <summary>
            /// Get options
            /// </summary>
            /// <param name="aName">Job name</param>
            /// <returns>Options</returns>
            public Dictionary<string, string> GetOptions(string aName)
            {
                JobsRow Row = this.FindByName(aName);
                if (Row == null) return new Dictionary<string, string>();
                return Row.Options;
            }
            /// <summary>
            /// Get GuiOptions
            /// </summary>
            /// <param name="aName">Job name</param>
            /// <returns>Options</returns>
            public Dictionary<string, string> GetGuiOptions(string aName)
            {
                JobsRow Row = this.FindByName(aName);
                if (Row == null) return new Dictionary<string, string>();
                return Row.GuiOptions;
            }
            /// <summary>
            /// Create a new row with defaults
            /// </summary>
            /// <param name="aName"></param>
            /// <returns></returns>
            public SchedulerDataSet.JobsRow NewJobsRow(string aName)
            {
                SchedulerDataSet.JobsRow Result = NewJobsRow();
                Result.Name = aName;
                Result.CheckMod = string.Empty;
                Result.Checksum = new byte[0];
                Result.Destination = string.Empty;
                Result.Filter = string.Empty;
                Result.FullOnly = false;
                Result.FullRepeatDays = 10;
                Result.FullAfterN = 8;
                Result.MaxAgeDays = 0;
                Result.MaxFulls = 4;
                Result.Source = string.Empty;
                Result.MapDrives = true;
                Result.AutoCleanup = true;
                return Result;
            }
        }
        /// <summary>
        /// Settings
        /// </summary>
        public partial class SettingsDataTable
        {
            /// <summary>
            /// Only 1st row used
            /// </summary>
            public SettingsRow Values
            {
                get
                {
                    if (this.Count == 0)
                        this.AddSettingsRow(0, new byte[0], string.Empty, true, false, string.Empty, DateTime.Now);
                    return (SettingsRow)this.Rows[0];
                }
            }
            /// <summary>
            /// Is the global password set?
            /// </summary>
            public bool UseGlobalPassword { get { return Values.CheckSrc && Values.Checksum.Length > 0; } }
            public string[] DisabledMonitors
            {
                get { return Values.DisabledMonitors.Split(';'); }
                set { Values.DisabledMonitors = string.Join(";", value); }
            }
            public void EnableMonitor(string aName, bool aEnable)
            {
                if (aEnable && DisabledMonitors.Contains(aName)) DisabledMonitors = DisabledMonitors.Where(qR => qR != aName).ToArray();
                else if (!aEnable && !DisabledMonitors.Contains(aName)) DisabledMonitors = DisabledMonitors.Concat(new string[] { aName }).ToArray();
            }
        }
        /// <summary>
        /// Options
        /// </summary>
        public partial class OptionsDataTable
        {
            /// <summary>
            /// As dictionary
            /// </summary>
            public Dictionary<string, string> OptionsDict
            {
                get { return (from OptionsRow qRow in this select new { Key = qRow.Parameter, Value = qRow.Value }).ToDictionary(n => n.Key, n => n.Value); }
            }
        }
    }
}
