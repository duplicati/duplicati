using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler.Monitor.SQL
{
    /// <summary>
    /// Support for the remote database
    /// </summary>
    /// <remarks>
    /// The goal here was to hit the server as little as possible.
    /// So, the data is not parsed from its XML and only the changes are sent.
    /// </remarks>
    public partial class SQLHistoryDataSet
    {
        /// <summary>
        /// Get the time that the last update was done for this user/machine/entryType
        /// </summary>
        /// <param name="aEntryKind">Kind to search for</param>
        /// <returns>Last time or Time.Min if never</returns>
        public static DateTime Latest(EntryKind aEntryKind)
        {
            SQLHistoryDataSetTableAdapters.HistoryTableAdapter TableAdapter = new SQLHistoryDataSetTableAdapters.HistoryTableAdapter();
            DateTime? re = TableAdapter.LatestQuery(Duplicati.Scheduler.Utility.User.UserName, Environment.MachineName, aEntryKind.ToString());
            if (re == null) return DateTime.MinValue;
            return (DateTime)re;
        }
        /// <summary>
        /// Update a record
        /// </summary>
        /// <param name="aUserName">Domain/User</param>
        /// <param name="aMachineName">Yeah</param>
        /// <param name="aEntryType">Kind of entry to update</param>
        /// <param name="aModificationDate">Entry time</param>
        /// <param name="aContent">XML to send</param>
        public static void Update(string aUserName, string aMachineName, EntryKind aEntryType, DateTime aModificationDate, string aContent)
        {
            SQLHistoryDataSetTableAdapters.HistoryTableAdapter TableAdapter = new SQLHistoryDataSetTableAdapters.HistoryTableAdapter();
            SQLHistoryDataSet.HistoryDataTable Table = TableAdapter.GetDataBy(aUserName, aMachineName, aEntryType.ToString(), aModificationDate);
            HistoryRow Row = null;
            if (Table.Count == 0)
                Row = Table.AddHistoryRow(aUserName, aMachineName, aEntryType.ToString(), aModificationDate, aContent);
            else
                Row = ((HistoryRow)Table.Rows[0]);
            Row.XmlContent = aContent;
            TableAdapter.Update(Table);
            //Console.WriteLine(Row.EntryType + ":" + Row.XmlContent);
        }
        /// <summary>
        /// Entry may be one of these, note that Schedule includes Settings
        /// </summary>
        public enum EntryKind
        {
            Schedule,
            History,
            Log,
            None
        };
        /// <summary>
        /// Row tools
        /// </summary>
        public partial class HistoryRow
        {
            /// <summary>
            /// Convert string to EntryKind
            /// </summary>
            public EntryKind ToEntryKind(string aType)
            {
                if (System.Enum.IsDefined(typeof(EntryKind), aType)) return EntryKind.None;
                return (EntryKind)System.Enum.Parse(typeof(EntryKind), aType);
            }
            /// <summary>
            /// Sets Entry Type
            /// </summary>
            public EntryKind Entry
            {
                get { return ToEntryKind(this.EntryType); }
                set { this.EntryType = value.ToString(); }
            }
        }
    }
}
