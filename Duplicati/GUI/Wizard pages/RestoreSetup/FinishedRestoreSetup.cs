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
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.RestoreSetup
{
    public partial class FinishedRestoreSetup : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public FinishedRestoreSetup()
            : base(Strings.FinishedRestoreSetup.PageTitle, Strings.FinishedRestoreSetup.PageDescription)
        {
            InitializeComponent();

            MonoSupport.FixTextBoxes(this);

            base.PageEnter += new PageChangeHandler(FinishedRestoreSetup_PageEnter);
            base.PageLeave += new PageChangeHandler(FinishedRestoreSetup_PageLeave);
        }

        void FinishedRestoreSetup_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            try
            {
                HelperControls.WaitForOperation dlg = new Duplicati.GUI.HelperControls.WaitForOperation();
                dlg.Setup(new DoWorkEventHandler(Restore), Strings.FinishedRestoreSetup.RestoreWaitDialogTitle, true);
                if (dlg.ShowDialog() != DialogResult.OK)
                {
                    if (dlg.Error != null)
                        throw dlg.Error;

                    args.Cancel = true;
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.FinishedRestoreSetup.RestoreFailedError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                args.Cancel = true;
                return;
            }
        }

        void FinishedRestoreSetup_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            List<KeyValuePair<string, string>> strings = new List<KeyValuePair<string, string>>();
            strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummaryAction, Strings.FinishedRestoreSetup.SummaryRestoreBackup));

            strings.Add(new KeyValuePair<string, string>(null, null));
            strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummarySource, m_wrapper.Backend.ToString()));

            //TODO: Figure out how to make summary

            /*switch(m_wrapper.Backend)
            {
                case WizardSettingsWrapper.BackendType.File:
                    FileSettings file = new FileSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummarySourcePath, file.Path));
                    break;
                case WizardSettingsWrapper.BackendType.FTP:
                    FTPSettings ftp = new FTPSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummarySourcePath, ftp.Server + "/" + ftp.Path));
                    break;
                case WizardSettingsWrapper.BackendType.SSH:
                    SSHSettings ssh = new SSHSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummarySourcePath, ssh.Server + "/" + ssh.Path));
                    break;
                case WizardSettingsWrapper.BackendType.S3:
                    S3Settings s3 = new S3Settings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummarySourcePath, s3.Path));
                    break;
                case WizardSettingsWrapper.BackendType.WebDav:
                    WEBDAVSettings webdav = new WEBDAVSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>(Strings.FinishedRestoreSetup.SummarySourcePath, webdav.Path));
                    break;
            }*/
            
            int maxlen = 0;
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key != null)
                    maxlen = Math.Max(maxlen, i.Key.Length);

            System.Text.StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key == null)
                    sb.Append("\r\n");
                else
                    sb.Append(i.Key + ": " + new String(' ', maxlen - i.Key.Length) + i.Value + "\r\n");

            Summary.Text = sb.ToString();

            args.TreatAsLast = true;
        }

        private void Restore(object sender, DoWorkEventArgs args)
        {
            System.Data.LightDatamodel.DataFetcherNested con = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);
            Schedule s = con.Add<Schedule>();
            s.Task = con.Add<Task>();
            m_wrapper.UpdateSchedule(s);

            using (Library.Utility.TempFolder tf = new Duplicati.Library.Utility.TempFolder())
            {
                RestoreSetupTask task = new RestoreSetupTask(s, tf);
                Dictionary<string, string> options = new Dictionary<string, string>();
                string destination  = task.GetConfiguration(options);
                Library.Main.Interface.RestoreControlFiles(destination, task.LocalPath, options);

                string filename = System.IO.Path.Combine(tf, System.IO.Path.GetFileName(Program.DatabasePath));
                if (System.IO.File.Exists(filename))
                {
                    //Connect to the downloaded database
                    using (System.Data.IDbConnection scon = (System.Data.IDbConnection)Activator.CreateInstance(SQLiteLoader.SQLiteConnectionType))
                    {
                        scon.ConnectionString = "Data Source=" + filename;

                        //Make sure encryption etc is handled correctly
                        Program.OpenDatabase(scon);

                        //Upgrade the database to the current version
                        DatabaseUpgrader.UpgradeDatabase(scon, filename);
                    }

                    //Shut down this connection
                    Program.LiveControl.Pause();
                    Program.DataConnection.ClearCache();
                    //We also need to remove any dirty objects as the ClearCache maintains those
                    foreach (System.Data.LightDatamodel.IDataClass o in Program.DataConnection.LocalCache.GetAllChanged())
                        Program.DataConnection.DiscardObject(o);
                    Program.DataConnection.Provider.Connection.Close();

                    //Replace the existing database with this one
                    System.IO.File.Copy(filename, Program.DatabasePath, true);

                    //Re-start the connection, using the new file
                    Program.DataConnection.Provider.Connection = (System.Data.IDbConnection)Activator.CreateInstance(SQLiteLoader.SQLiteConnectionType);
                    Program.DataConnection.Provider.Connection.ConnectionString = "Data Source=" + Program.DatabasePath;
                    Program.OpenDatabase(Program.DataConnection.Provider.Connection);

                    //Remove the downloaded database
                    try { System.IO.File.Delete(filename); }
                    catch { }
                }
                else
                    throw new Exception(Strings.FinishedRestoreSetup.SetupFileMissingError);

                NormalizeApplicationSettings();

                Program.Scheduler.Reschedule();
            }
        }
        
        private void NormalizeApplicationSettings() {
            //Make sure we have a startup delay, so a restart won't accidently wipe something
            bool settingsModified = false;
            Datamodel.ApplicationSettings appset = new Datamodel.ApplicationSettings(Program.DataConnection);
            TimeSpan startDelay = new TimeSpan(0);
            if (!string.IsNullOrEmpty(appset.StartupDelayDuration))
                try { startDelay = Duplicati.Library.Utility.Timeparser.ParseTimeSpan(appset.StartupDelayDuration); }
                catch { }


            if (startDelay < TimeSpan.FromMinutes(5))
            {
                appset.StartupDelayDuration = "5m";
                settingsModified = true;
            }

            if (!System.IO.Directory.Exists(appset.TempPath)) {
                appset.TempPath = "";
                settingsModified = true;
            }
            
            if (!System.IO.Directory.Exists(appset.SignatureCachePath)) {
                appset.SignatureCachePath = "";
                settingsModified = true;
            }

            if (settingsModified) {
                Program.DataConnection.CommitRecursive(Program.DataConnection.GetObjects<Datamodel.ApplicationSetting>());
            }
        }
    }
}
