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
        /*public enum Pages
        {
            MainAction,
            Add_SelectName,
            Add_SelectFiles,
            Add_SelectService,
            Add_FileOptions,
            Add_FTPOptions,
            Add_S3Options,
            Add_SSHOptions,
            Add_WebDAVOptions,
            Add_AdvancedChoices,
            Add_SelectWhen,
            Add_Incremental,
            Add_Password,
            Add_Finished,

            SelectBackup,
            Restore_SelectDate,
            Restore_TargetFolder,
            Restore_Finished,
            RunNow_Finished,
            Delete_Finished,
        }*/

        /// <summary>
        /// The main wizard form
        /// </summary>
        IWizardForm m_form;

        /// <summary>
        /// The connection on which changes are made
        /// </summary>
        IDataFetcherCached m_connection;

        /// <summary>
        /// The item to be commited, for the add procedure
        /// </summary>
        Schedule m_addedItem;

        /// <summary>
        /// The item to be committed, for the edit procedure
        /// </summary>
        Schedule m_editItem;

        /// <summary>
        /// The page that produced the finish page, used to go back to it
        /// </summary>
        IWizardControl m_addFinishedPage = null;

        public WizardHandler()
        {

            m_form = new Dialog();
            m_form.Title = "Duplicati Setup Wizard";

            m_form.Pages.Clear();
            m_form.Pages.AddRange(new IWizardControl[] { new Wizard_pages.MainPage() });

            m_form.DefaultImage = Program.NeutralIcon.ToBitmap();
            m_form.Finished += new System.ComponentModel.CancelEventHandler(m_form_Finished);
        }

        void m_form_Finished(object sender, System.ComponentModel.CancelEventArgs e)
        {

            if (m_form.CurrentPage is Wizard_pages.Add_backup.FinishedAdd)
            {
                Schedule schedule = (Schedule)m_form.Settings["Schedule"];
                ((IDataFetcherCached)schedule.DataParent).CommitRecursiveWithRelations(schedule);

                if ((m_form.CurrentPage as Duplicati.GUI.Wizard_pages.Add_backup.FinishedAdd).RunNow.Checked)
                    if (m_addedItem != null)
                        Program.WorkThread.AddTask(new FullBackupTask(m_addedItem));
                    else
                        Program.WorkThread.AddTask(new IncrementalBackupTask(m_editItem));
            }
            else if (m_form.CurrentPage is Wizard_pages.Restore.FinishedRestore)
            {
                /*Schedule schedule = (m_form.Pages[(int)Pages.SelectBackup] as Wizard_pages.SelectBackup).SelectedBackup;
                DateTime backup = (m_form.Pages[(int)Pages.Restore_SelectDate] as Wizard_pages.Restore.SelectBackup).SelectedBackup;
                string target = (m_form.Pages[(int)Pages.Restore_TargetFolder] as Wizard_pages.Restore.TargetFolder).SelectedFolder;

                if (backup.Ticks == 0)
                    Program.WorkThread.AddTask(new RestoreTask(schedule, target));
                else
                    Program.WorkThread.AddTask(new RestoreTask(schedule, target, backup));*/
            }
            else if (m_form.CurrentPage is Wizard_pages.RunNow.RunNowFinished)
            {
                /*Schedule schedule = (m_form.Pages[(int)Pages.SelectBackup] as Wizard_pages.SelectBackup).SelectedBackup;
                if ((m_form.Pages[(int)Pages.RunNow_Finished] as Wizard_pages.RunNow.RunNowFinished).ForceFullBackup)
                    Program.WorkThread.AddTask(new FullBackupTask(m_editItem));
                else
                    Program.WorkThread.AddTask(new IncrementalBackupTask(m_editItem));*/
            }
            else if (m_form.CurrentPage is Wizard_pages.Delete_backup.DeleteFinished)
            {
                /*Schedule schedule = (m_form.Pages[(int)Pages.SelectBackup] as Wizard_pages.SelectBackup).SelectedBackup;
                m_connection.DeleteObject(m_editItem);
                m_connection.CommitAll();
                Program.DataConnection.CommitAll();*/
            }
        }

        public bool Visible { get { return m_form.Dialog.Visible; } }

        /*void m_form_NextPressed(object sender, PageChangedArgs args)
        {
            switch ((Pages)m_form.Pages.IndexOf(m_form.CurrentPage))
            {
                case Pages.MainAction:
                    switch (((Wizard_pages.MainPage)m_form.CurrentPage).SelectedAction)
                    {
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Add:
                            args.NextPage = m_form.Pages[(int)Pages.Add_SelectName];

                            if (m_addedItem == null || m_editItem != null)
                            {
                                m_connection = new DataFetcherNested(Program.DataConnection);
                                m_addedItem = m_connection.Add<Schedule>();
                                m_addedItem.Tasks.Add(m_connection.Add<Task>());
                                
                                m_addedItem.FullAfter = "1M";
                                m_addedItem.KeepFull = 4;
                                m_addedItem.KeepTime = "";
                                m_addedItem.Name = "New backup";
                                m_addedItem.Repeat = "1D";
                                m_addedItem.When = DateTime.Now.Date.AddHours(DateTime.Now.Hour + 1);

                                foreach (IWizardControl c in m_form.Pages)
                                {
                                    if (c as Wizard_pages.Interfaces.IScheduleBased != null)
                                        (c as Wizard_pages.Interfaces.IScheduleBased).Setup(m_addedItem);
                                    if (c as Wizard_pages.Interfaces.ITaskBased != null)
                                        (c as Wizard_pages.Interfaces.ITaskBased).Setup(m_addedItem.Tasks[0]);
                                }
                            }

                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Edit:
                            m_connection = new DataFetcherNested(Program.DataConnection);
                            args.NextPage = m_form.Pages[(int)Pages.SelectBackup];
                            (args.NextPage as Wizard_pages.SelectBackup).Setup(m_connection, Duplicati.GUI.Wizard_pages.MainPage.Action.Edit);
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Restore:
                            m_connection = new DataFetcherNested(Program.DataConnection);
                            args.NextPage = m_form.Pages[(int)Pages.SelectBackup];
                            (args.NextPage as Wizard_pages.SelectBackup).Setup(m_connection, Duplicati.GUI.Wizard_pages.MainPage.Action.Restore);
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Backup:
                            m_connection = new DataFetcherNested(Program.DataConnection);
                            args.NextPage = m_form.Pages[(int)Pages.SelectBackup];
                            (args.NextPage as Wizard_pages.SelectBackup).Setup(m_connection, Duplicati.GUI.Wizard_pages.MainPage.Action.Backup);
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Delete:
                            m_connection = new DataFetcherNested(Program.DataConnection);
                            args.NextPage = m_form.Pages[(int)Pages.SelectBackup];
                            (args.NextPage as Wizard_pages.SelectBackup).Setup(m_connection, Duplicati.GUI.Wizard_pages.MainPage.Action.Delete);
                            break;
                        //case Duplicati.Wizard_pages.MainPage.Action.Rearrange:
                        //    break;
                        default:
                            args.Cancel = true;
                            MessageBox.Show(m_form as Form, "Unknown option selected", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;
                    }
                    break;
                case Pages.Add_SelectService:
                    switch (((Wizard_pages.SelectBackend)m_form.CurrentPage).SelectedProvider)
                    {
                        case Duplicati.GUI.Wizard_pages.SelectBackend.Provider.File:
                            args.NextPage = m_form.Pages[(int)Pages.Add_FileOptions];
                            break;
                        case Duplicati.GUI.Wizard_pages.SelectBackend.Provider.FTP:
                            args.NextPage = m_form.Pages[(int)Pages.Add_FTPOptions];
                            break;
                        case Duplicati.GUI.Wizard_pages.SelectBackend.Provider.SSH:
                            args.NextPage = m_form.Pages[(int)Pages.Add_SSHOptions];
                            break;
                        case Duplicati.GUI.Wizard_pages.SelectBackend.Provider.WebDAV:
                            args.NextPage = m_form.Pages[(int)Pages.Add_WebDAVOptions];
                            break;
                        case Duplicati.GUI.Wizard_pages.SelectBackend.Provider.S3:
                            args.NextPage = m_form.Pages[(int)Pages.Add_S3Options];
                            break;
                        default:
                            args.Cancel = true;
                            MessageBox.Show(m_form as Form, "Unknown option selected", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;
                    }
                    break;

                case Pages.Add_FileOptions:
                case Pages.Add_FTPOptions:
                case Pages.Add_SSHOptions:
                case Pages.Add_WebDAVOptions:
                case Pages.Add_S3Options:
                    m_addFinishedPage = m_form.CurrentPage;
                    args.NextPage = m_form.Pages[(int)Pages.Add_Finished];
                    args.TreatAsLast = false;
                    break;

                case Pages.Add_AdvancedChoices:
                    break;
                case Pages.Add_Finished:
                    break;

                case Pages.SelectBackup:

                    m_editItem = (m_form.Pages[(int)Pages.SelectBackup] as Wizard_pages.SelectBackup).SelectedBackup;

                    foreach (IWizardControl c in m_form.Pages)
                    {
                        if (c as Wizard_pages.Interfaces.IScheduleBased != null)
                            (c as Wizard_pages.Interfaces.IScheduleBased).Setup(m_editItem);
                        if (c as Wizard_pages.Interfaces.ITaskBased != null)
                            (c as Wizard_pages.Interfaces.ITaskBased).Setup(m_editItem.Tasks[0]);
                    }

                    switch ((m_form.Pages[(int)Pages.SelectBackup] as Wizard_pages.SelectBackup).SelectType)
                    {
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Edit:
                            args.NextPage = m_form.Pages[(int)Pages.Add_SelectName];
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Backup:
                            args.NextPage = m_form.Pages[(int)Pages.RunNow_Finished];
                            args.TreatAsLast = true;
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Delete:
                            args.NextPage = m_form.Pages[(int)Pages.Delete_Finished];
                            args.TreatAsLast = true;
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Rearrange:
                            break;
                        case Duplicati.GUI.Wizard_pages.MainPage.Action.Restore:
                            args.NextPage = m_form.Pages[(int)Pages.Restore_SelectDate];
                            (m_form.Pages[(int)Pages.Restore_SelectDate] as Wizard_pages.Restore.SelectBackup).Setup(m_editItem);
                            break;
                    }
                    break;
                case Pages.Restore_TargetFolder:
                    {
                        Schedule schedule = (m_form.Pages[(int)Pages.SelectBackup] as Wizard_pages.SelectBackup).SelectedBackup;
                        DateTime backup = (m_form.Pages[(int)Pages.Restore_SelectDate] as Wizard_pages.Restore.SelectBackup).SelectedBackup;
                        string target = (m_form.Pages[(int)Pages.Restore_TargetFolder] as Wizard_pages.Restore.TargetFolder).SelectedFolder;
                        (m_form.Pages[(int)Pages.Restore_Finished] as Wizard_pages.Restore.FinishedRestore).Setup(schedule, backup, target);
                        args.TreatAsLast = true;
                    }
                    break;


            }
        }

        void m_form_BackPressed(object sender, PageChangedArgs args)
        {
            switch ((Pages)m_form.Pages.IndexOf(m_form.CurrentPage))
            {
                case Pages.Add_FileOptions:
                case Pages.Add_FTPOptions:
                case Pages.Add_SSHOptions:
                case Pages.Add_WebDAVOptions:
                case Pages.Add_S3Options:
                    args.NextPage = m_form.Pages[(int)Pages.Add_SelectService];
                    break;
                case Pages.Add_SelectFiles:
                    args.NextPage = m_form.Pages[(int)Pages.MainAction];
                    break;
                case Pages.Add_Finished:
                    if (m_addFinishedPage != null)
                        args.NextPage = m_addFinishedPage;
                    break;
                case Pages.SelectBackup:
                    args.NextPage = m_form.Pages[(int)Pages.MainAction];
                    break;
                case Pages.Add_SelectName:
                    if (m_editItem != null)
                        args.NextPage = m_form.Pages[(int)Pages.SelectBackup];
                    break;
                case Pages.RunNow_Finished:
                case Pages.Delete_Finished:
                    args.NextPage = m_form.Pages[(int)Pages.SelectBackup];
                    break;
            }
        } */

        public void Show()
        {
            (m_form as Form).ShowDialog();
        }

    }
}
