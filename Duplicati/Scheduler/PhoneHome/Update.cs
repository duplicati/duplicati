using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler.PhoneHome
{
    /// <summary>
    /// Maintains the server data
    /// </summary>
    public static class Update
    {
        /// <summary>
        /// Update a log entry
        /// </summary>
        /// <param name="aLogDate">Date</param>
        /// <param name="aLogContent">XML content</param>
        public static void Log(DateTime aLogDate, string aLogContent)
        {
            BackupsDataSet.Update(Duplicati.Scheduler.Utility.User.UserName, Environment.MachineName, BackupsDataSet.EntryKind.Log,
                aLogDate, aLogContent);
        }
        /// <summary>
        /// Update the scheduler record
        /// </summary>
        public static void Scheduler()
        {
            Scheduler(Duplicati.Scheduler.Data.SchedulerDataSet.DefaultPath());
        }
        /// <summary>
        /// Update the scheduler record
        /// </summary>
        /// <param name="aScheduler">XML file to load</param>
        public static void Scheduler(string aScheduler)
        {
            DateTime LastUpdate = BackupsDataSet.Latest(BackupsDataSet.EntryKind.Schedule);
            DateTime LastWrite = System.IO.File.GetLastWriteTime(aScheduler);
            if (LastUpdate < LastWrite)
                BackupsDataSet.Update(Duplicati.Scheduler.Utility.User.UserName, Environment.MachineName, BackupsDataSet.EntryKind.Schedule,
                    LastWrite, System.IO.File.ReadAllText(aScheduler));
        }
        /// <summary>
        /// Update History
        /// </summary>
        public static void History()
        {
            using (Duplicati.Scheduler.Data.HistoryDataSet hds = new Duplicati.Scheduler.Data.HistoryDataSet())
            {
                hds.Load();
                History(System.IO.File.GetLastWriteTime(Duplicati.Scheduler.Data.HistoryDataSet.DefaultPath()), hds.History);
            }
        }
        /// <summary>
        /// Update History
        /// </summary>
        /// <param name="aLastWrite">Date</param>
        /// <param name="aHistoryTable">XML file name</param>
        public static void History(DateTime aLastWrite, Duplicati.Scheduler.Data.HistoryDataSet.HistoryDataTable aHistoryTable)
        {
            DateTime LastUpdate = BackupsDataSet.Latest(BackupsDataSet.EntryKind.Schedule);
            Duplicati.Scheduler.Data.HistoryDataSet.HistoryDataTable Changes = new Duplicati.Scheduler.Data.HistoryDataSet.HistoryDataTable();
            foreach (Duplicati.Scheduler.Data.HistoryDataSet.HistoryRow Row in
                from Duplicati.Scheduler.Data.HistoryDataSet.HistoryRow qR in aHistoryTable
                where qR.ActionDate > LastUpdate
                select qR)
                Changes.ImportRow(Row);
            StringBuilder b = new StringBuilder();
            Changes.WriteXml(new System.IO.StringWriter(b));
            BackupsDataSet.Update(Duplicati.Scheduler.Utility.User.UserName, Environment.MachineName, BackupsDataSet.EntryKind.History,
                aLastWrite, b.ToString());
        }
    }
}
