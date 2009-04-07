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

namespace Duplicati.GUI.Wizard_pages.Backends.File
{
    public partial class FileOptions : WizardControl
    {
        FileSettings m_wrapper;

        public FileOptions()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(FileOptions_PageEnter);
            base.PageLeave += new PageChangeHandler(FileOptions_PageLeave);
        }

        void FileOptions_PageLeave(object sender, PageChangedArgs args)
        {
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
                if (System.IO.Directory.GetFileSystemEntries(targetpath).Length > 0 && new WizardSettingsWrapper(m_settings).PrimayAction == WizardSettingsWrapper.MainAction.Add)
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

            m_wrapper.Path = targetpath;
            if (UseCredentials.Checked)
            {
                m_wrapper.Username = Username.Text;
                m_wrapper.Password = Password.Text;
            }
            else
            {
                m_wrapper.Username = m_wrapper.Password = "";
            }

            if (new WizardSettingsWrapper(m_settings).PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new RestoreSetup.FinishedRestoreSetup();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
        }

        private void RescanDrives()
        {
            TargetDrive.Items.Clear();


            if (System.Environment.OSVersion.Platform != PlatformID.MacOSX && System.Environment.OSVersion.Platform != PlatformID.Unix)
                for (char i = 'A'; i < 'Z'; i++)
                {
                    try
                    {
                        System.IO.DriveInfo di = new System.IO.DriveInfo(i.ToString());
                        if (di.DriveType == System.IO.DriveType.Removable)
                            TargetDrive.Items.Add(i.ToString() + ":");
                    }
                    catch { }
                }
        }

        void FileOptions_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings).FileSettings;

            RescanDrives();

            if (!m_valuesAutoLoaded )
            {
                UsePath.Checked = true;
                TargetFolder.Text = m_wrapper.Path;

                UseCredentials.Checked = !string.IsNullOrEmpty(m_wrapper.Username);
                Username.Text = m_wrapper.Username;
                Password.Text = m_wrapper.Password;
            }
        }

        private void UseCredentials_CheckedChanged(object sender, EventArgs e)
        {
            Credentials.Enabled = UseCredentials.Checked;
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

            RescanDrives();

            if (TargetDrive.Enabled && TargetDrive.Items.Count == 0)
            {
                MessageBox.Show(this, "No removable drives were found on your system. Please enter the path manually.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UsePath.Checked = true;
            }

        }
    }
}
