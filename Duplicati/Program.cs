#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati
{
    static class Program
    {
        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static IDataFetcherCached DataConnection;

        /// <summary>
        /// This is the TrayIcon instance
        /// </summary>
        public static NotifyIcon TrayIcon;

        /// <summary>
        /// This is the lock to be used before manipulating the shared resources
        /// </summary>
        public static object MainLock = new object();

        public static Icon NeutralIcon;
        public static Icon ErrorIcon;
        public static Icon WarningIcon;
        public static Icon RunningIcon;

        public static Bitmap NormalImage;
        public static Bitmap WorkingImage;
        public static Bitmap ErrorImage;
        public static Bitmap WarningImage;

        public static ServiceStatus StatusDialog;
        public static ServiceSetup SetupDialog;

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
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if DEBUG
            string dbpath = System.IO.Path.Combine(Application.StartupPath, "Duplicati.sqlite");
#else
            string dbpath = System.IO.Path.Combine(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Application.ProductName), "Duplicati.sqlite");
#endif
            System.Data.SQLite.SQLiteConnection con = new System.Data.SQLite.SQLiteConnection();

            //This also opens the db for us :)
            DatabaseUpgrader.UpgradeDatebase(con, dbpath);
            DataConnection = new DataFetcherThreadSafe(MainLock, new DataFetcherCached(new SQLiteDataProvider(con)));

            System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();

            NeutralIcon = new Icon(asm.GetManifestResourceStream(typeof(Program), "Icons.TrayOK.ico"));
            ErrorIcon = new Icon(asm.GetManifestResourceStream(typeof(Program), "Icons.TrayError.ico"));
            RunningIcon = new Icon(asm.GetManifestResourceStream(typeof(Program), "Icons.TrayWorking.ico"));
            WarningIcon = new Icon(asm.GetManifestResourceStream(typeof(Program), "Icons.TrayWarning.ico"));

            NormalImage = new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Status OK.png"));
            WorkingImage = new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Status Working2.png"));
            ErrorImage = new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Status Error.png")); 
            WarningImage = new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Status Warning.png"));

            TrayIcon = new NotifyIcon();
            TrayIcon.ContextMenuStrip = new ContextMenuStrip();
            TrayIcon.Icon = NeutralIcon;

            TrayIcon.ContextMenuStrip.Items.Add("Status", new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Status.ico")), new EventHandler(Status_Clicked));

            TrayIcon.ContextMenuStrip.Items.Add("Setup", new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Time.ico")), new EventHandler(Settings_Clicked));

            TrayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

            TrayIcon.ContextMenuStrip.Items.Add("Settings", new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Settings.ico")), new EventHandler(Setup_Clicked));

            TrayIcon.ContextMenuStrip.Items.Add(new ToolStripSeparator());

            TrayIcon.ContextMenuStrip.Items.Add("Quit", new Bitmap(asm.GetManifestResourceStream(typeof(Program), "Icons.Close.ico")), new EventHandler(Quit_Clicked));

            TrayIcon.ContextMenuStrip.Items[0].Font = new Font(TrayIcon.ContextMenuStrip.Items[0].Font, FontStyle.Bold);

            ApplicationSettings = new ApplicationSettings(DataConnection);
            DuplicityRunner runner = new DuplicityRunner(Application.StartupPath, null);

            WorkThread = new WorkerThread<IDuplicityTask>(new WorkerThread<IDuplicityTask>.ProcessItemDelegate(runner.ExecuteTask));

            Scheduler = new Scheduler(DataConnection, WorkThread, MainLock);

            WorkThread.CompletedWork += new EventHandler(WorkThread_CompletedWork);
            WorkThread.StartingWork += new EventHandler(WorkThread_StartingWork);

            DataConnection.AfterDataConnection += new DataConnectionEventHandler(DataConnection_AfterDataConnection);

            TrayIcon.Text = "Duplicati ready";

            TrayIcon.DoubleClick += new EventHandler(TrayIcon_DoubleClick);
            TrayIcon.Visible = true;

            Application.Run();

            Scheduler.Terminate(true);
            WorkThread.Terminate(true);
            TrayIcon.Visible = false;
        }

        static void DataConnection_AfterDataConnection(object sender, DataActions action)
        {
            Scheduler.Reschedule();
        }

        static void WorkThread_StartingWork(object sender, EventArgs e)
        {
            TrayIcon.Icon = RunningIcon;
            TrayIcon.Text = "Duplicati running " + (WorkThread.CurrentTask == null ? "" : WorkThread.CurrentTask.Schedule.Name);
        }

        static void WorkThread_CompletedWork(object sender, EventArgs e)
        {
            TrayIcon.Icon = NeutralIcon;
            TrayIcon.Text = "Duplicati ready";
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
            ShowSetup();
        }

        private static void TrayIcon_DoubleClick(object sender, EventArgs e)
        {
            //ShowStatus();
            ShowWizard();
        }

        public static void ShowStatus()
        {
            if (StatusDialog == null || !StatusDialog.Visible)
                StatusDialog = new ServiceStatus();

            StatusDialog.Show();
            StatusDialog.Activate();
        }

        public static void ShowSettings()
        {
            if (SetupDialog == null || !SetupDialog.Visible)
                SetupDialog = new ServiceSetup();

            SetupDialog.Show();
            SetupDialog.Activate();
        }

        public static void ShowSetup()
        {
            lock (MainLock)
            {
                ApplicationSetup dlg = new ApplicationSetup();
                dlg.ShowDialog();
            }
        }

        public static void ShowWizard()
        {
            System.Windows.Forms.Wizard.Dialog dlg = new System.Windows.Forms.Wizard.Dialog();
            System.Windows.Forms.Wizard.IWizardControl[] pages = new System.Windows.Forms.Wizard.IWizardControl[] 
            {
                new Wizard_pages.MainPage(),
                new Wizard_pages.Add_backup.SelectFiles(),
                new Wizard_pages.Add_backup.SelectWhen(),
                new Wizard_pages.Add_backup.IncrementalSettings(),
                new Wizard_pages.Add_backup.PasswordSettings()
            };

            dlg.Pages.Clear();
            dlg.Pages.AddRange(pages);
            dlg.Text = "Duplicati setup wizard";
            dlg.DefaultImage = Program.NeutralIcon.ToBitmap();

            dlg.ShowDialog();
        }

    }
}