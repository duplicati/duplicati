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

namespace Duplicati.GUI.Wizard_pages.Backends.File
{
    public partial class FileOptions : WizardControl
    {
        private Duplicati.Datamodel.Backends.File m_file;
        private bool m_isUpdating = false;

        public FileOptions()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            m_autoFillValues = false;

            base.PageEnter += new PageChangeHandler(FileOptions_PageEnter);
            base.PageLeave += new PageChangeHandler(FileOptions_PageLeave);
        }

        void FileOptions_PageLeave(object sender, PageChangedArgs args)
        {
            SaveDialogSettings();

            if (args.Direction == PageChangedDirection.Back)
                return;

            string targetpath;
            if (UsePath.Checked)
                targetpath = TargetFolder.Text;
            else
                targetpath = TargetDrive.Text + "\\" + Folder.Text;

            if (UseCredentials.Checked)
                if (!Duplicati.Library.Backend.File.PreAuthenticate(targetpath, Username.Text, Password.Text))
                {
                    MessageBox.Show(this, "Failed to authenticate using the given credentials.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }

            try
            {
                if (targetpath.Trim().Length == 0)
                {
                    MessageBox.Show(this, "You must enter a folder to backup to", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                if (!System.IO.Path.IsPathRooted(targetpath))
                {
                    MessageBox.Show(this, "You must enter the full path of the folder", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                if (!System.IO.Directory.Exists(targetpath))
                {
                    switch (MessageBox.Show(this, "The selected folder does not exist. Do you want to create it?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                    {
                        case DialogResult.Yes:
                            System.IO.Directory.CreateDirectory(targetpath);
                            break;
                        case DialogResult.Cancel:
                            args.Cancel = true;
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "An error occured while verifying the destination. Please make sure it exists and is accessible.\nError message: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                args.Cancel = true;
                return;
            }

            try
            {
                if (System.IO.Directory.GetFileSystemEntries(targetpath).Length > 0 && !((Schedule)m_settings["Schedule"]).Tasks[0].ExistsInDb)
                    if (MessageBox.Show(this, "The selected folder is not empty. Do you want to use it anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "An error occured while verifying the destination. Please make sure it exists and is accessible.\nError message: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                args.Cancel = true;
                return;
            }

            SaveSettings();
            args.NextPage = new Add_backup.AdvancedOptions();
        }

        private void SaveSettings()
        {
            m_file.DestinationFolder = TargetFolder.Text;

            if (UseCredentials.Checked)
            {
                m_file.Username = Username.Text;
                m_file.Password = Password.Text;
            }
            else
            {
                m_file.Username = null;
                m_file.Password = null;
            }

            m_file.SetService();
        }

        private void RescanDrives()
        {
            TargetDrive.Items.Clear();

            for (char i = 'A'; i < 'Z'; i++)
            {
                System.IO.DriveInfo di = new System.IO.DriveInfo(i.ToString());
                if (di.DriveType == System.IO.DriveType.Removable)
                    TargetDrive.Items.Add(i.ToString() + ":");
            }
        }

        void FileOptions_PageEnter(object sender, PageChangedArgs args)
        {
            m_file = new Duplicati.Datamodel.Backends.File(((Schedule)m_settings["Schedule"]).Tasks[0]);

            RescanDrives();

            try
            {
                m_isUpdating = true;

                if (!LoadDialogSettings())
                {
                    UsePath.Checked = true;
                    TargetFolder.Text = m_file.DestinationFolder;

                    UseCredentials.Checked = !string.IsNullOrEmpty(m_file.Username);
                    Username.Text = m_file.Username;
                    Password.Text = m_file.Password;
                }
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void UseCredentials_CheckedChanged(object sender, EventArgs e)
        {
            Credentials.Enabled = UseCredentials.Checked;

            if (m_isUpdating)
                return;
        }

        private void UsePath_CheckedChanged(object sender, EventArgs e)
        {
            TargetFolder.Enabled = BrowseTargetFolder.Enabled = UsePath.Checked;
        }

        private void BrowseTargetFolder_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                TargetFolder.Text = folderBrowserDialog.SelectedPath;
        }

        private void UseDisk_CheckedChanged(object sender, EventArgs e)
        {
            TargetDrive.Enabled = Folder.Enabled = FolderLabel.Enabled = UseDisk.Checked;

            if (m_isUpdating)
                return;

            RescanDrives();

            if (TargetDrive.Enabled && TargetDrive.Items.Count == 0)
            {
                MessageBox.Show(this, "No removable drives were found on your system. Please enter the path manually.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UsePath.Checked = true;
            }

        }
    }
}
