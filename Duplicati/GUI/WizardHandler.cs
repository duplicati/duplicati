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
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using System.Data.LightDatamodel;
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    /// <summary>
    /// This class encapsulates the control of the wizard
    /// </summary>
    public class WizardHandler
    {
        /// <summary>
        /// The main wizard form
        /// </summary>
        IWizardForm m_form;

        public WizardHandler()
        {

            m_form = new Dialog();
            m_form.Title = "Duplicati Setup Wizard";

            m_form.Pages.Clear();
            long count = 0;
            lock (Program.MainLock)
                count = Program.DataConnection.GetObjects<Schedule>().Length;

            if (count == 0)
                m_form.Pages.AddRange(new IWizardControl[] { new Wizard_pages.FirstLaunch() });
            else
                m_form.Pages.AddRange(new IWizardControl[] { new Wizard_pages.MainPage() });

            m_form.DefaultImage = Properties.Resources.Duplicati;
            m_form.Dialog.Icon = Properties.Resources.TrayNormal;
            m_form.Finished += new System.ComponentModel.CancelEventHandler(m_form_Finished);
        }

        void m_form_Finished(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Wizard_pages.WizardSettingsWrapper wrapper = new Duplicati.GUI.Wizard_pages.WizardSettingsWrapper(m_form.Settings);


            if (wrapper.PrimayAction == Duplicati.GUI.Wizard_pages.WizardSettingsWrapper.MainAction.Add || wrapper.PrimayAction == Duplicati.GUI.Wizard_pages.WizardSettingsWrapper.MainAction.Edit)
            {
                Schedule schedule;
                IDataFetcherWithRelations con = new DataFetcherNested(Program.DataConnection);

                if (wrapper.PrimayAction == Duplicati.GUI.Wizard_pages.WizardSettingsWrapper.MainAction.Add)
                    schedule = con.Add<Schedule>();
                else
                    schedule = con.GetObjectById<Schedule>(wrapper.ScheduleID);

                wrapper.UpdateSchedule(schedule);

                bool scheduleRun = wrapper.RunImmediately && wrapper.BackupTimeOffset > DateTime.Now;

                lock (Program.MainLock)
                    con.CommitAllRecursive();
                schedule = Program.DataConnection.GetObjectById<Schedule>(schedule.ID);

                if (wrapper.UseEncryptionAsDefault)
                {
                    ApplicationSettings appset = new ApplicationSettings(Program.DataConnection);
                    appset.UseCommonPassword = true;
                    appset.CommonPassword = wrapper.BackupPassword;
                    appset.CommonPasswordUseGPG = wrapper.GPGEncryption;
                    Program.DataConnection.Commit(Program.DataConnection.GetObjects<ApplicationSetting>());
                }

                if (scheduleRun)
                    Program.WorkThread.AddTask(new IncrementalBackupTask(schedule));
            }
            else if (m_form.CurrentPage is Wizard_pages.Restore.FinishedRestore)
            {
                Schedule schedule = Program.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID);
                DateTime when = wrapper.RestoreTime;
                string target = wrapper.RestorePath;
                string restoreFilter = wrapper.RestoreFilter;

                if (when.Ticks == 0)
                    Program.WorkThread.AddTask(new RestoreTask(schedule, target, restoreFilter));
                else
                    Program.WorkThread.AddTask(new RestoreTask(schedule, target, restoreFilter, when));
            }
            else if (m_form.CurrentPage is Wizard_pages.RunNow.RunNowFinished)
            {
                Schedule schedule = Program.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID);
                if (wrapper.ForceFull)
                    Program.WorkThread.AddTask(new FullBackupTask(schedule));
                else
                    Program.WorkThread.AddTask(new IncrementalBackupTask(schedule));
            }
            else if (m_form.CurrentPage is Wizard_pages.Delete_backup.DeleteFinished)
            {
                Schedule schedule = Program.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID);
                List<IDataClass> items = new List<IDataClass>();
                items.AddRange(Program.DataConnection.FindObjectRelations(schedule));
                foreach(IDataClass o in items)
                    Program.DataConnection.DeleteObject(o);

                Program.DataConnection.Commit(items.ToArray());
            }
            else if (m_form.CurrentPage is Wizard_pages.RestoreSetup.FinishedRestoreSetup)
            {
                MessageBox.Show(m_form as Form, "The previous Duplicati setup is now restored!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        public bool Visible { get { return m_form.Dialog.Visible; } }

        public void Show()
        {
            if (!(m_form as Form).Visible)
                (m_form as Form).ShowDialog();
        }

    }
}
