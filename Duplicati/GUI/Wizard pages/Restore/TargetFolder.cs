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
    public partial class TargetFolder : WizardControl
    {
        public TargetFolder()
            : base(Strings.TargetFolder.PageTitle, Strings.TargetFolder.PageDescription)
        {
            InitializeComponent();

            base.PageLeave += new PageChangeHandler(TargetFolder_PageLeave);
        }

        void TargetFolder_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            string targetpath = TargetPath.Text;

            try
            {
                if (targetpath.Trim().Length == 0)
                {
                    MessageBox.Show(this, Strings.TargetFolder.NoFolderError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                if (!System.IO.Path.IsPathRooted(targetpath))
                {
                    MessageBox.Show(this, Strings.TargetFolder.FolderPathRelativeError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                if (!System.IO.Directory.Exists(targetpath))
                {
                    switch (MessageBox.Show(this, Strings.TargetFolder.CreateFolderWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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
                MessageBox.Show(this, string.Format(Strings.TargetFolder.ValidatingFolderError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                args.Cancel = true;
                return;
            }

            try
            {
                if (System.IO.Directory.GetFileSystemEntries(targetpath).Length > 0)
                    if (MessageBox.Show(this, Strings.TargetFolder.FolderNotEmptyWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.TargetFolder.ValidatingFolderError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                args.Cancel = true;
                return;
            }

            if (PartialRestore.Checked && backupFileList.CheckedCount == 0)
            {
                MessageBox.Show(this, Strings.TargetFolder.NoFilesSelectedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            WizardSettingsWrapper wrapper = new WizardSettingsWrapper(m_settings);
            wrapper.RestorePath = targetpath;
            wrapper.RestoreFilter = PartialRestore.Checked ? string.Join(System.IO.Path.PathSeparator.ToString(), backupFileList.CheckedFiles.ToArray()) : "";
            args.NextPage = new FinishedRestore();
        }

        private void TargetFolder_Load(object sender, EventArgs e)
        {

        }

        private void BrowseFolder_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                TargetPath.Text = folderBrowserDialog.SelectedPath;
        }

        public string SelectedFolder { get { return TargetPath.Text; } }

        private void PartialRestore_CheckedChanged(object sender, EventArgs e)
        {
            PartialSettings.Enabled = PartialRestore.Checked;

            if (PartialRestore.Checked)
            {
                WizardSettingsWrapper wrapper = new WizardSettingsWrapper(m_settings);
                if (wrapper.RestoreFileList == null)
                    wrapper.RestoreFileList = new List<string>();

                backupFileList.LoadFileList(Program.DataConnection.GetObjectById<Schedule>(wrapper.ScheduleID), wrapper.RestoreTime, wrapper.RestoreFileList);
            }
        }
    }
}
