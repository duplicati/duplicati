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
                if (!AskToResumeIfPaused())
                {
                    e.Cancel = true;
                    return;
                }

                Schedule schedule = wrapper.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID);

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
                if (!AskToResumeIfPaused())
                {
                    e.Cancel = true;
                    return;
                }

                Schedule schedule = wrapper.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID);
                if (wrapper.ForceFull)
                    Program.WorkThread.AddTask(new FullBackupTask(schedule));
                else
                    Program.WorkThread.AddTask(new IncrementalBackupTask(schedule));

            }
            else if (m_form.CurrentPage is Wizard_pages.Delete_backup.DeleteFinished)
            {
                Schedule schedule = wrapper.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID);

                if (Program.WorkThread.Active)
                {
                    try
                    {
                        //TODO: It's not safe to access the values like this, 
                        //because the runner thread might interfere
                        if (Program.WorkThread.CurrentTask.Schedule.ID == schedule.ID)
                        {
                            bool paused = Program.LiveControl.State == LiveControls.LiveControlState.Paused;
                            Program.LiveControl.Pause();
                            if (MessageBox.Show(m_form.Dialog, Strings.WizardHandler.StopRunningBackupQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) != DialogResult.Yes)
                            {
                                e.Cancel = true;
                                if (!paused)
                                    Program.LiveControl.Resume();
                                return;
                            }

                            bool repaused = Program.LiveControl.State == LiveControls.LiveControlState.Paused;
                            Program.LiveControl.Pause();
                            if (Program.WorkThread.CurrentTask.Schedule.ID == schedule.ID)
                                Program.Runner.Terminate();

                            Cursor prevCursor = m_form.Dialog.Cursor;

                            try
                            {
                                m_form.Dialog.Cursor = Cursors.WaitCursor;
                                for (int i = 0; i < 10; i++)
                                    if (Program.WorkThread.Active)
                                    {
                                        IDuplicityTask t = Program.WorkThread.CurrentTask;
                                        if (t != null && t.Schedule.ID == schedule.ID)
                                            System.Threading.Thread.Sleep(1000);
                                        else
                                            break;
                                    }
                                    else
                                        break;
                            }
                            finally
                            {
                                try { m_form.Dialog.Cursor = prevCursor; }
                                catch { }
                            }

                            if (Program.WorkThread.Active)
                            {
                                IDuplicityTask t = Program.WorkThread.CurrentTask;
                                if (t == null && t.Schedule.ID == schedule.ID)
                                {
                                    MessageBox.Show(m_form.Dialog, Strings.WizardHandler.UnableToStopBackupError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                                    e.Cancel = true;
                                    return;
                                }
                            }

                            if (!paused || !repaused)
                                Program.LiveControl.Resume();
                        }
                    }
                    catch(Exception ex)
                    {
                        MessageBox.Show(m_form.Dialog, string.Format(Strings.WizardHandler.StopBackupError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        e.Cancel = true;
                        return;
                    }
                }

                List<IDataClass> items = new List<IDataClass>();
                IList<Duplicati.Datamodel.Log> tmp = schedule.Task.Logs;
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


        private bool AskToResumeIfPaused()
        {
            if (Program.LiveControl.State == LiveControls.LiveControlState.Paused)
            {
                DialogResult res = MessageBox.Show(m_form.Dialog, Strings.WizardHandler.ResumeNowQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel);
                if (res == DialogResult.Cancel)
                    return false;
                else if (res == DialogResult.Yes)
                    Program.LiveControl.Resume();
            }

            return true;
        }
    }
}
