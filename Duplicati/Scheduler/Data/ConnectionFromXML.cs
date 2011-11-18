using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;

namespace Duplicati.Scheduler.Data
{
    public class ConnectionFromXML
    {
        public DataTable ConnectionTable = new DataTable("Connection");
        private DataColumn StringCol = new DataColumn("String", typeof(string));
        public ConnectionFromXML()
        {
            this.ConnectionTable.Columns.Add(StringCol);
        }
        public ConnectionFromXML(string aDefault)
            : this()
        {
            Default = aDefault;
        }
        public static string Default { get; set; }
        public string ConnectionString(string aXMLFile)
        {
            try 
	        {
                this.ConnectionTable.ReadXml(aXMLFile);
	        }
	        catch (Exception Ex)
	        {
                Console.WriteLine(Ex.Message);
                return Default;
	        }
            if (this.ConnectionTable.Rows.Count == 0) return Default;
            return System.Text.ASCIIEncoding.ASCII.GetString(
                Utility.Tools.Unprotect(
                        System.Convert.FromBase64String(
                        (string)this.ConnectionTable.Rows[0][StringCol])));
        }
        public void SaveConnection(string aXMLFile, string aConnectionString)
        {
            this.ConnectionTable.Rows.Clear();
            this.ConnectionTable.Rows.Add(
                System.Convert.ToBase64String(
                    Utility.Tools.Protect(System.Text.ASCIIEncoding.ASCII.GetBytes(aConnectionString))));
            this.ConnectionTable.WriteXml(aXMLFile);
        }
    }
}
