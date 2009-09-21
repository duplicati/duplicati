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

            base.PageEnter += new PageChangeHandler(FinishedRestoreSetup_PageEnter);
            base.PageLeave += new PageChangeHandler(FinishedRestoreSetup_PageLeave);
        }

        void FinishedRestoreSetup_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            HelperControls.WaitForOperation dlg = new Duplicati.GUI.HelperControls.WaitForOperation();
            dlg.Setup(new DoWorkEventHandler(Restore), Strings.FinishedRestoreSetup.RestoreWaitDialogTitle);
            if (dlg.ShowDialog() != DialogResult.OK)
            {
                if (dlg.Error != null)
                    MessageBox.Show(this, string.Format(Strings.FinishedRestoreSetup.RestoreFailedError, dlg.Error.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

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

            using (Library.Core.TempFolder tf = new Duplicati.Library.Core.TempFolder())
            {
                RestoreSetupTask task = new RestoreSetupTask(s, tf);
                Dictionary<string, string> options = new Dictionary<string, string>();
                string destination  = task.GetConfiguration(options);
                Library.Main.Interface.RestoreControlFiles(destination, task.LocalPath, options);

                string filename = System.IO.Path.Combine(tf, System.IO.Path.GetFileName(Program.DatabasePath));
                if (System.IO.File.Exists(filename))
                    System.IO.File.Copy(filename, Program.DatabasePath, true);
                else
                    throw new Exception(Strings.FinishedRestoreSetup.SetupFileMissingError);

                Program.DataConnection.ClearCache();
                Program.Scheduler.Reschedule();
            }
        }
    }
}
