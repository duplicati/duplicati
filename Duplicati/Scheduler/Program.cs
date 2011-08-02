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
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// The Duplicati Scheduler - Uses the Task Scheduler to run Duplicati Backups
    /// </summary>
    static class Program
    {
        /// <summary>
        /// The name of the mother package (all hail)
        /// </summary>
        public const string Package = "Duplicati";
        /// <summary>
        /// The official name of this
        /// </summary>
        public const string Name = "Duplicati.Scheduler";
        /// <summary>
        /// The base name of the named pipe to the executive
        /// </summary>
        public const string PipeBaseName = "Duplicati.Pipe";
        /// <summary>
        /// The name of the thread that advertises the pipe server
        /// </summary>
        public const string PipeServerThreadName = "Duplicati.PipeServer";
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Only allow one of me by locking a mutex
            bool SingleInstance = false;
            using (System.Threading.Mutex Single = new System.Threading.Mutex(true, Name, out SingleInstance))
            {
                if (SingleInstance)
                {
#if !DEBUG
                    try
#endif
                    {
                        // Debug log?
                        Utility.Tools.TryCatch((Action)delegate()
                        {
                            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DUPERRORLOG")))
                            {
                                System.Diagnostics.Debug.Listeners.Add(new System.Diagnostics.TextWriterTraceListener(System.IO.File.CreateText(Environment.GetEnvironmentVariable("DEPERRLOG"))));
                                Console.SetOut(System.IO.File.AppendText(Environment.GetEnvironmentVariable("DEPERRLOG")));
                            }
                        });
                        // Set up the application
                        Application.EnableVisualStyles();
                        Application.SetCompatibleTextRenderingDefault(false);
                        // throw new Exception("It's all gone horribly wrong!");
                        // TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST TEST 
                        //Utility.Su.Impersonate("User", Environment.UserDomainName, "asd");  // TEST
                        //Environment.SetEnvironmentVariable("TMP", "C:\\temp"); // TEST

                        // Check if user can access the database
                        if (System.IO.File.Exists(Duplicati.Scheduler.Data.SchedulerDataSet.DefaultPath()) &&
                            new System.IO.FileInfo(Duplicati.Scheduler.Data.SchedulerDataSet.DefaultPath()).IsReadOnly &&
                            MessageBox.Show("The database file, "+Duplicati.Scheduler.Data.SchedulerDataSet.DefaultPath()+" is not writable by "+Utility.User.UserName+"; continue?", "READONLY", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation) == DialogResult.No) 
                            return;

                        Application.Run(new Scheduler());
                    }
#if !DEBUG
                    catch (Exception Ex)
                    {
                        Utility.Tools.ProcessError("Duplicati", Name, Ex);
                    }
#endif
                }
            }
        }
    }
}
