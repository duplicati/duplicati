using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler.Monitor.SQL.Properties
{
    internal sealed partial class Settings
    {
        /// <summary>
        /// XML that contains encrypted ConnectionString for PhoneHome
        /// </summary>
        public static string MonitorConnection = 
            Duplicati.Scheduler.Data.HistoryDataSet.GetAppData("SQLMonitorConnection.xml");
        public string BackupsConnectionString 
        {
            get 
            {
                return new Duplicati.Scheduler.Data.ConnectionFromXML(
                    @"Data Source="+Environment.MachineName+@"\SQLEXPRESS;Initial Catalog=Backups;Integrated Security=True")
                    .ConnectionString(MonitorConnection);
            }
        }
    }
}
