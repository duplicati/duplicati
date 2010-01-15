#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.GUI.Wizard_pages.Restore
{
    public partial class FinishedRestore : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;
        private HelperControls.WaitForOperation m_waitdlg;

        public FinishedRestore()
            : base(Strings.FinishedRestore.PageTitle, Strings.FinishedRestore.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(FinishedRestore_PageEnter);
            base.PageLeave += new PageChangeHandler(FinishedRestore_PageLeave);
        }

        void FinishedRestore_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!RunInBackground.Checked)
            {
                try
                {
                    m_waitdlg = new Duplicati.GUI.HelperControls.WaitForOperation();
                    m_waitdlg.Setup(new DoWorkEventHandler(Restore), Strings.FinishedRestore.RestoreWaitDialogTitle);
                    if (m_waitdlg.ShowDialog() != DialogResult.OK)
                    {
                        if (m_waitdlg.Error != null)
                            throw m_waitdlg.Error;
                        
                        args.Cancel = true;
                        return;
                    }

                    m_owner.CancelButton.PerformClick();
                    m_waitdlg = null;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Strings.FinishedRestore.RestoreFailedError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                args.Cancel = true;
                return;
            }
        }

        void FinishedRestore_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            Summary.Text = string.Format(
                Strings.FinishedRestore.SummaryText,
                m_wrapper.ScheduleName,
                (m_wrapper.RestoreTime.Ticks == 0 ? Strings.FinishedRestore.MostRecent : m_wrapper.RestoreTime.ToString()),
                m_wrapper.RestorePath
            );

            args.TreatAsLast = true;

            Schedule schedule = m_wrapper.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID);
            if (!schedule.ExistsInDb)
            {
                RunInBackground.Checked = false;
                RunInBackground.Visible = false;
            }
        }

        private void Restore(object sender, DoWorkEventArgs args)
        {
            //TODO: add a Try-Catch here
            Schedule s = m_wrapper.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID);

            RestoreTask task = new RestoreTask(s, m_wrapper.RestorePath, m_wrapper.RestoreFilter, m_wrapper.RestoreTime);
            Dictionary<string, string> options = new Dictionary<string, string>();
            string destination = task.GetConfiguration(options);
            if (options.ContainsKey("filter"))
                options.Remove("filter");

            //TODO: Should not be replicated here, but executed in the DuplicatiRunner
            ApplicationSettings appSet = new ApplicationSettings(task.Schedule.DataParent);
            if (appSet.SignatureCacheEnabled && !string.IsNullOrEmpty(appSet.SignatureCachePath))
                options["signature-cache-path"] = System.IO.Path.Combine(System.Environment.ExpandEnvironmentVariables(appSet.SignatureCachePath), task.Schedule.ID.ToString());
            
            using (Library.Main.Interface i = new Duplicati.Library.Main.Interface(destination, options))
            {
                i.OperationProgress += new Duplicati.Library.Main.OperationProgressEvent(i_OperationProgress);
                i.Restore(task.LocalPath);
            }
        }

        void i_OperationProgress(Duplicati.Library.Main.Interface caller, Duplicati.Library.Main.DuplicatiOperation operation, int progress, int subprogress, string message, string submessage)
        {
            if (m_waitdlg.InvokeRequired)
                m_waitdlg.Invoke(new Duplicati.Library.Main.OperationProgressEvent(i_OperationProgress), caller, operation, progress, subprogress, message, submessage);
            else
                m_waitdlg.Text = message;
        }


    }
}
