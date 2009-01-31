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
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class SelectName : WizardControl
    {
        private Schedule m_schedule;

        public SelectName()
            : base("Enter a name for the backup", "On this page you can enter a name for the backup, so you can find and modify it later")
        {
            InitializeComponent();
            BackupFolder.treeView.HideSelection = false;

            base.PageEnter += new PageChangeHandler(SelectName_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectName_PageLeave);
        }

        void SelectName_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["Name:Backup"] = BackupFolder.SelectedFolder;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (BackupName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter a name for the backup", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            Schedule[] tmp = ((System.Data.LightDatamodel.IDataFetcher)m_settings["Connection"]).GetObjects<Schedule>("Name LIKE ? AND Path Like ?", BackupName.Text, BackupFolder.SelectedFolder);
            if ((tmp.Length == 1 && tmp[0] != m_schedule) || tmp.Length > 1)
            {
                MessageBox.Show(this, "There already exists a backup with that name", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (m_schedule == null)
            {
                m_schedule = ((System.Data.LightDatamodel.IDataFetcher)m_settings["Connection"]).Add<Schedule>();
                m_schedule.Tasks.Add(((System.Data.LightDatamodel.IDataFetcher)m_settings["Connection"]).Add<Task>());
            }

            m_schedule.Name = BackupName.Text;
            m_schedule.Path = BackupFolder.SelectedFolder;

            m_settings["Schedule"] = m_schedule;

            SetupDefaults();

            args.NextPage = new SelectFiles();
        }

        void SelectName_PageEnter(object sender, PageChangedArgs args)
        {
            if (!m_settings.ContainsKey("Schedule"))
                m_schedule = null;
            else
                m_schedule = (Schedule)m_settings["Schedule"];

            if (m_settings.ContainsKey("Name:Path"))
                BackupFolder.SelectedFolder = (string)m_settings["Name:Path"];
            else
                BackupFolder.SelectedFolder = "";

            if (m_schedule != null && m_schedule.ExistsInDb)
                BackupName.Text = m_schedule.Name;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            BackupFolder.SelectedFolder = null;
            //BackupFolder.Focus();
            BackupFolder.AddFolder(null).BeginEdit();
        }

        /// <summary>
        /// The purpose of this function is to set the default
        /// settings on the new backup.
        /// </summary>
        private void SetupDefaults()
        {
            //TODO: These settings should be read from a file, 
            //so they are customizable by the end user

            m_schedule.FullAfter = "1M";
            m_schedule.KeepFull = 4;
            m_schedule.KeepTime = "";
            m_schedule.Repeat = "1D";
            m_schedule.VolumeSize = "5MB";
            //Run each day at 13:00 (1 pm)
            m_schedule.When = DateTime.Now.Date.AddHours(13);

            //TODO: Probably not a good idea to hide the fact that the backup needs this key to be restored!
            //TODO: !!!! Decide how to represent this to the user
            m_schedule.Tasks[0].Encryptionkey = Duplicati.Library.Core.KeyGenerator.GenerateKey(32, 40);
        }
    }
}
