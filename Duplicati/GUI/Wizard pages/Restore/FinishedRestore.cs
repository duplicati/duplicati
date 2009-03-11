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

namespace Duplicati.GUI.Wizard_pages.Restore
{
    public partial class FinishedRestore : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;
        private HelperControls.WaitForOperation m_waitdlg;

        public FinishedRestore()
            : base("Ready to restore files", "Duplicati is now ready to restore your files.")
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
                m_waitdlg = new Duplicati.GUI.HelperControls.WaitForOperation();
                m_waitdlg.Setup(new DoWorkEventHandler(Restore), "Restoring files, please wait...");
                m_waitdlg.ShowDialog();
                m_owner.CancelButton.PerformClick();
                m_waitdlg = null;
                args.Cancel = true;
                return;
            }
        }

        void FinishedRestore_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            Summary.Text =
                "Action: Restore backup\r\n" +
                "Backup: " + m_wrapper.ScheduleName + "\r\n" +
                "Date:   " + (m_wrapper.RestoreTime.Ticks == 0 ? "most recent" : m_wrapper.RestoreTime.ToString()) + "\r\n" +
                "Folder: " + m_wrapper.RestorePath + "\r\n";

            args.TreatAsLast = true;
        }

        private void Restore(object sender, DoWorkEventArgs args)
        {
            Schedule s = Program.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID);

            RestoreTask task = new RestoreTask(s, m_wrapper.RestorePath, m_wrapper.RestoreFilter, m_wrapper.RestoreTime);
            Dictionary<string, string> options = new Dictionary<string, string>();
            task.GetOptions(options);
            if (options.ContainsKey("filter"))
                options.Remove("filter");
            using (Library.Main.Interface i = new Duplicati.Library.Main.Interface(task.SourcePath, options))
            {
                i.OperationProgress += new Duplicati.Library.Main.OperationProgressEvent(i_OperationProgress);
                i.Restore(task.TargetPath);
            }
        }

        void i_OperationProgress(Duplicati.Library.Main.Interface caller, Duplicati.Library.Main.DuplicatiOperation operation, int progress, int subprogress, string message)
        {
            if (m_waitdlg.InvokeRequired)
                m_waitdlg.Invoke(new Duplicati.Library.Main.OperationProgressEvent(i_OperationProgress), caller, operation, progress, subprogress, message);
            else
                m_waitdlg.Text = message;
        }


    }
}
