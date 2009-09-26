#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Windows.Forms;
using System.Data.LightDatamodel;
using System.Drawing;
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    static class Program
    {
        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static IDataFetcherWithRelations DataConnection;

        /// <summary>
        /// This is the TrayIcon instance
        /// </summary>
        public static NotifyIcon TrayIcon;

        /// <summary>
        /// This is the lock to be used before manipulating the shared resources
        /// </summary>
        public static object MainLock = new object();

        public static ServiceStatus StatusDialog;
        public static WizardHandler Wizard;

        public static ApplicationSettings ApplicationSettings;

        /// <summary>
        /// This is the scheduling thread
        /// </summary>
        public static Scheduler Scheduler;
        
        /// <summary>
        /// This is the working thread
        /// </summary>
        public static WorkerThread<IDuplicityTask> WorkThread;

        /// <summary>
        /// The path to the file that contains the current database
        /// </summary>
        public static string DatabasePath;

        /// <summary>
        /// The actual runner, do not call directly. Only used for events.
        /// </summary>
        public static DuplicatiRunner Runner;

        /// <summary>
        /// The runtime loaded type for System.Data.SQLite.SQLiteCommand
        /// </summary>
        public static Type SQLiteCommandType;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            SingleInstance singleInstance = null;

            try
            {
                try
                {
#if DEBUG
                    //debug mode uses a lock file located in the app folder
                    singleInstance = new SingleInstance(Application.ProductName, System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location));
#else
                    //release mode uses the systems "Application Data" folder
                    singleInstance = new SingleInstance(Application.ProductName);
#endif
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(Strings.Program.StartupFailure, ex.ToString()), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                if (!singleInstance.IsFirstInstance)
                {
                    //Linux shows this output
                    Console.WriteLine(Strings.Program.AnotherInstanceDetected);
                    return;
                }

                singleInstance.SecondInstanceDetected += new SingleInstance.SecondInstanceDelegate(singleInstance_SecondInstanceDetected);

                SQLiteCommandType = LoadCorrectSQLiteAssembly();

#if DEBUG
                DatabasePath = System.IO.Path.Combine(Application.StartupPath, "Duplicati.sqlite");
#else
                DatabasePath = System.IO.Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.ProductName), "Duplicati.sqlite");
#endif
                Version sqliteVersion = new Version((string)SQLiteCommandType.GetProperty("SQLiteVersion").GetValue(null, null));
                if (sqliteVersion < new Version(3, 6, 3))
                {
                    //The official Mono SQLite provider is also broken with less than 3.6.3
                    MessageBox.Show(string.Format(Strings.Program.WrongSQLiteVersion, sqliteVersion, "3.6.3"), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

                System.Data.IDbConnection con = (System.Data.IDbConnection)Activator.CreateInstance(SQLiteCommandType);

                try
                {
                    if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(DatabasePath)))
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(DatabasePath));

                    //This also opens the db for us :)
                    DatabaseUpgrader.UpgradeDatebase(con, DatabasePath);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(Strings.Program.DatabaseOpenError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                DataConnection = new DataFetcherWithRelations(new SQLiteDataProvider(con));

                if (!string.IsNullOrEmpty(new Datamodel.ApplicationSettings(DataConnection).DisplayLanguage))
                    try
                    {
                        System.Threading.Thread.CurrentThread.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo(new Datamodel.ApplicationSettings(DataConnection).DisplayLanguage);
                        System.Threading.Thread.CurrentThread.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo(new Datamodel.ApplicationSettings(DataConnection).DisplayLanguage);
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show(string.Format(Strings.Program.LanguageSelectionError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        //This is non-fatal, just keep running with system default language
                    }

                TrayIcon = new NotifyIcon();
                TrayIcon.ContextMenuStrip = new ContextMenuStrip();
                TrayIcon.Icon = Properties.Resources.TrayNormal;

                TrayIcon.ContextMenuStrip.Items.Add(Strings.Program.MenuStatus, Properties.Resources.StatusMenuIcon, new EventHandler(Status_Clicked));
                TrayIcon.ContextMenuStrip.Items.Add(Strings.Program.MenuWizard, Properties.Resources.WizardMenuIcon, new EventHandler(Setup_Clicked));
                TrayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

                TrayIcon.ContextMenuStrip.Items.Add(Strings.Program.MenuSettings, Properties.Resources.SettingsMenuIcon, new EventHandler(Settings_Clicked));
                TrayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

                TrayIcon.ContextMenuStrip.Items.Add(Strings.Program.MenuQuit, Properties.Resources.CloseMenuIcon, new EventHandler(Quit_Clicked));
                TrayIcon.ContextMenuStrip.Items[0].Font = new Font(TrayIcon.ContextMenuStrip.Items[0].Font, FontStyle.Bold);

                ApplicationSettings = new ApplicationSettings(DataConnection);
                Runner = new DuplicatiRunner();

                WorkThread = new WorkerThread<IDuplicityTask>(new WorkerThread<IDuplicityTask>.ProcessItemDelegate(Runner.ExecuteTask));

                Scheduler = new Scheduler(DataConnection, WorkThread, MainLock);

                WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
                WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);

                DataConnection.AfterDataConnection += new DataConnectionEventHandler(DataConnection_AfterDataConnection);

                TrayIcon.Text = Strings.Program.TrayStatusReady;

                TrayIcon.DoubleClick += new EventHandler(TrayIcon_DoubleClick);
                TrayIcon.Visible = true;

                long count = 0;
                lock (MainLock)
                    count = Program.DataConnection.GetObjects<Schedule>().Length;

                if (count == 0)
                {
                    //TODO: shows the wrong icon in the taskbar... Should run under Application.Run() ...
                    ShowWizard();
                }
                else
                    handleCommandlineArguments(args);                

                Application.Run();
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Strings.Program.SeriousError, ex.ToString()), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            if (Scheduler != null)
                Scheduler.Terminate(true);
            if (WorkThread != null)
                WorkThread.Terminate(true);
            if(TrayIcon != null)
                TrayIcon.Visible = false;
            if (singleInstance != null)
                singleInstance.Dispose();
        }

        private static bool handleCommandlineArguments(string[] _args)
        {
            List<string> args = new List<string>(_args);
            Dictionary<string, string> options = CommandLine.CommandLineParser.ExtractOptions(args);
            if (args.Count == 2 && args[0].ToLower().Trim() == "run-backup")
            {
                Schedule[] schedules = Program.DataConnection.GetObjects<Schedule>("Name LIKE ?", args[1].Trim());
                if (schedules == null || schedules.Length == 0)
                {
                    MessageBox.Show(string.Format(Strings.Program.NamedBackupNotFound, args[1]), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                else if (schedules.Length > 1)
                {
                    MessageBox.Show(string.Format(Strings.Program.MultipleNamedBackupsFound, args[1], schedules.Length), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                if (options.ContainsKey("full"))
                    Program.WorkThread.AddTask(new FullBackupTask(schedules[0]));
                else
                    Program.WorkThread.AddTask(new IncrementalBackupTask(schedules[0]));
                
                return true;
            }

            if (args.Count == 1 && args[0] == "show-status")
            {
                ShowStatus();
                return true;
            }


            return false;
        }

        private static void singleInstance_SecondInstanceDetected(string[] commandlineargs)
        {
            if (handleCommandlineArguments(commandlineargs))
                return;

            //TODO: This actually blocks the app thread, and thus may pile up remote invocations
            ShowWizard();
        }

        private static void DataConnection_AfterDataConnection(object sender, DataActions action)
        {
            if (action == DataActions.Insert || action == DataActions.Update)
                Scheduler.Reschedule();
        }

        static void WorkThread_StartingWork(object sender, EventArgs e)
        {
            TrayIcon.Icon = Properties.Resources.TrayWorking;
            TrayIcon.Text = string.Format(Strings.Program.TrayStatusRunning, WorkThread.CurrentTask == null ? "" : WorkThread.CurrentTask.Schedule.Name);
        }

        static void WorkThread_CompletedWork(object sender, EventArgs e)
        {
            TrayIcon.Icon = Properties.Resources.TrayNormal;
            TrayIcon.Text = Strings.Program.TrayStatusReady;
        }

        private static void Quit_Clicked(object sender, EventArgs e)
        {
            Application.Exit();
        }

        private static void Settings_Clicked(object sender, EventArgs e)
        {
            ShowSettings();
        }

        private static void Status_Clicked(object sender, EventArgs e)
        {
            ShowStatus();
        }

        private static void Setup_Clicked(object sender, EventArgs e)
        {
            ShowWizard();
        }

        private static void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            ShowStatus();
        }

        public static void ShowStatus()
        {
            //TODO: Guard against calls from other threads
            if (StatusDialog == null || !StatusDialog.Visible)
                StatusDialog = new ServiceStatus();

            StatusDialog.Show();
            StatusDialog.Activate();
        }

        public static void ShowWizard()
        {
            //TODO: Guard against calls from other threads
            if (Wizard == null || !Wizard.Visible)
                Wizard = new WizardHandler();
            
            Wizard.Show();
        }

        public static void ShowSettings()
        {
            //TODO: Guard against calls from other threads
            lock (MainLock)
            {
                ApplicationSetup dlg = new ApplicationSetup();
                dlg.ShowDialog();
            }
        }
        
        /// <summary>
        /// Returns a localized name for a task type
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        public static string LocalizeTaskType(DuplicityTaskType type)
        {
            switch (type)
            {
                case DuplicityTaskType.FullBackup:
                    return Strings.TaskType.FullBackup;
                case DuplicityTaskType.IncrementalBackup:
                    return Strings.TaskType.IncrementalBackup;
                case DuplicityTaskType.ListActualFiles:
                    return Strings.TaskType.ListActualFiles;
                case DuplicityTaskType.ListBackupEntries:
                    return Strings.TaskType.ListBackupEntries;
                case DuplicityTaskType.ListBackups:
                    return Strings.TaskType.ListBackups;
                case DuplicityTaskType.ListFiles:
                    return Strings.TaskType.ListFiles;
                case DuplicityTaskType.RemoveAllButNFull:
                    return Strings.TaskType.RemoveAllButNFull;
                case DuplicityTaskType.RemoveOlderThan:
                    return Strings.TaskType.RemoveOlderThan;
                case DuplicityTaskType.Restore:
                    return Strings.TaskType.Restore;
                case DuplicityTaskType.RestoreSetup:
                    return Strings.TaskType.RestoreSetup;
                default:
                    return type.ToString();
            }
        }


        /// <summary>
        /// A helper method to load the correct SQLite assembly for the current architecture
        /// </summary>
        public static Type LoadCorrectSQLiteAssembly()
        {
            string filename = "System.Data.SQLite.dll";
            string basePath = System.IO.Path.Combine(Application.StartupPath, "SQLite");
            string assemblyPath = System.IO.Path.Combine(basePath, "pinvoke");

            if (System.Environment.OSVersion.Platform == PlatformID.Win32NT || System.Environment.OSVersion.Platform == PlatformID.Win32Windows)
            {
                if (IntPtr.Size == 8 || (IntPtr.Size == 4 && Is32BitProcessOn64BitProcessor()))
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win64"), filename)))
                        assemblyPath = System.IO.Path.Combine(basePath, "win64");
                }
                else
                {
                    if (System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(basePath, "win32"), filename)))
                        assemblyPath = System.IO.Path.Combine(basePath, "win32");
                }
            }

            return System.Reflection.Assembly.LoadFile(System.IO.Path.Combine(assemblyPath, filename)).GetType("System.Data.SQLite.SQLiteConnection");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CallingConvention = System.Runtime.InteropServices.CallingConvention.Winapi)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool IsWow64Process([System.Runtime.InteropServices.In] IntPtr hProcess, [System.Runtime.InteropServices.Out] out bool lpSystemInfo);

        private static bool Is32BitProcessOn64BitProcessor()
        {
            try
            {
                bool retVal;
                IsWow64Process(System.Diagnostics.Process.GetCurrentProcess().Handle, out retVal);
                return retVal;
            }
            catch
            {
                return false; //In case the OS is old enough not to have the Wow64 function
            }
        }

    }
}