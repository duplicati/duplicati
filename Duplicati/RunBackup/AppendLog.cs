#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
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
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Logging
{
    /// <summary>
    /// Writes log messages to a file, file is opened, appended and closed each write
    /// </summary>
    public class AppendLog : ILog, IDisposable
    {
        // Using a colon followed by a ETX will make this easier to parse, yet still readable
        public static string Separator = ":" + (char)3;
        // Use the round-trip date formatter
        public const string DateFormat = "o";
        // The name of the file
        public string LogFileName { get; set; }
        // Just a little extra to put in the messages
        private string itsNote = string.Empty;
        /// <summary>
        /// Constructs a new log destination, writing to the Log database
        /// </summary>
        /// <param name="filename">The file to write to</param>
        public AppendLog(string aFileName, string aNote)
        {
            LogFileName = aFileName;
            itsNote = aNote;
        }
        /// <summary>
        /// Returns a DateTime given a log date or DateTime.MaxValue if can not parse
        /// </summary>
        /// <param name="aLogDate">Log date</param>
        /// <returns>DateTime or DateTime.MaxValue if can not parse</returns>
        public static DateTime ParseLogDate(string aLogDate)
        {
            DateTime Result = DateTime.MaxValue;
            DateTime.TryParse(aLogDate, null, System.Globalization.DateTimeStyles.RoundtripKind, out Result);
            return Result;
        }
        /// <summary>
        /// Represents a log entry as a class
        /// </summary>
        public class LogEntry
        {
            public DateTime Date { get; set; }
            public string Job { get; set; }
            public Duplicati.Library.Logging.LogMessageType Type { get; set; }
            public string Message { get; set; }
            public string ExMessage { get; set; }
            public LogEntry(string aLine)
            {
                string[] Parts = aLine.Split(new string[] { Duplicati.Library.Logging.AppendLog.Separator }, StringSplitOptions.None);
                if (Parts.Length < 5) return;
                Date = Duplicati.Library.Logging.AppendLog.ParseLogDate(Parts[0]);
                Job = Parts[1];
                Type = (Duplicati.Library.Logging.LogMessageType)System.Enum.Parse(typeof(Duplicati.Library.Logging.LogMessageType), Parts[2]);
                Message = Parts[3];
                ExMessage = Parts[4];
            }
        }
        /// <summary>
        /// Convert a log file into a XML
        /// </summary>
        /// <param name="aLogFile">Log file name</param>
        /// <returns>XML text</returns>
        public static string LogFileToXML(string aLogFile)
        {
            var Parser = from string Line in System.IO.File.ReadAllLines(aLogFile) select new LogEntry(Line);
            System.Xml.Linq.XElement XML = new System.Xml.Linq.XElement("Log", 
                from LogEntry qL in Parser
                select new System.Xml.Linq.XElement("Log",
                    new System.Xml.Linq.XAttribute("File", System.IO.Path.GetFileName(aLogFile)),
                    new System.Xml.Linq.XElement("Date", qL.Date),
                    new System.Xml.Linq.XElement("Job", qL.Job),
                    new System.Xml.Linq.XElement("Type", qL.Type),
                    new System.Xml.Linq.XElement("Message", qL.Message),
                    new System.Xml.Linq.XElement("ExMessage", qL.ExMessage)
                    ));
            return XML.ToString();
        }
        /// <summary>
        /// A bindable list of log entries
        /// </summary>
        public class LogList : System.ComponentModel.BindingList<Duplicati.Library.Logging.AppendLog.LogEntry>
        {
            public LogList(string[] aContents)
                : base((from string qS in aContents select new Duplicati.Library.Logging.AppendLog.LogEntry(qS)).ToArray())
            {
            }
        }
        #region ILog Members
        /// <summary>
        /// The function called when a message is logged
        /// </summary>
        /// <param name="message">The message logged</param>
        /// <param name="type">The type of message logged</param>
        /// <param name="exception">An exception, may be null</param>
        public virtual void WriteMessage(string aMessage, Duplicati.Library.Logging.LogMessageType aType, Exception aException)
        {
            // Never, I don't care what
            if (aType == LogMessageType.Profiling) return;
            if (aMessage.Contains("is partial from byte offset")) return; // Silly message confuses users
            // Multiple messages may be sent separated by newline
            foreach(string Line in aMessage.Split(new char[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string Message = string.Join(Separator, new string[]
                    {
                        DateTime.Now.ToString(DateFormat),
                        itsNote,
                        aType.ToString(),
                        Line,
                        aException == null ? string.Empty : aException.ToString()
                    });
                bool OK = false;
                try
                {
                    System.IO.File.AppendAllText(LogFileName, Message + "\r\n"); // Just for notepad...
                    OK = true;
                }
                catch (Exception Ex)
                {
                    Console.WriteLine(LogFileName+":"+Ex.ToString());
                }
                if (!OK)
                    Console.WriteLine(Message);
            }
            if (EventLogged != null) EventLogged(aMessage, aType, aException);
        }

        /// <summary>
        /// An event that is raised when a message is logged
        /// </summary>
        public event EventLoggedDelgate EventLogged;

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Frees up all internally held resources
        /// </summary>
        public void Dispose()
        {
        }

        #endregion
    }
}
