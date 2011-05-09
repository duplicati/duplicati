#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        private WizardSettingsWrapper m_wrapper = null;

        public TargetFolder()
            : base(Strings.TargetFolder.PageTitle, Strings.TargetFolder.PageDescription)
        {
            InitializeComponent();

            base.PageLeave += new PageChangeHandler(TargetFolder_PageLeave);
            base.PageEnter += new PageChangeHandler(TargetFolder_PageEnter);
        }

        void TargetFolder_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            if (PartialRestore.Checked)
                PartialRestore_CheckedChanged(null, null);
        }

        void TargetFolder_PageLeave(object sender, PageChangedArgs args)
        {
            m_wrapper.RestoreTargetFolders = backupFileList.TargetFolders;
            m_wrapper.RestoreFileSelection = backupFileList.CheckedFiles;
            
            if (args.Direction == PageChangedDirection.Back)
                return;

            string[] targetpaths;
            Dictionary<string, string> filesInFolder = new Dictionary<string,string>();

            if (PartialRestore.Checked && backupFileList.TargetFolders.Count > 1)
            {
                targetpaths = backupFileList.TargetSuggestions;
                for (int i = 0; i < targetpaths.Length; i++)
                    if (string.IsNullOrEmpty(targetpaths[i]))
                    {
                        if (!string.IsNullOrEmpty(TargetPath.Text) && TargetPath.Text.Trim().Length != 0)
                            targetpaths[i] = Library.Utility.Utility.AppendDirSeparator(TargetPath.Text) + i.ToString();
                    }

                foreach (string s in m_wrapper.RestoreFileSelection)
                {
                    int index = int.Parse(s.Substring(0, s.IndexOf(System.IO.Path.DirectorySeparatorChar)));
                    if (string.IsNullOrEmpty(targetpaths[index]))
                    {
                        MessageBox.Show(this, Strings.TargetFolder.NoFolderError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        args.Cancel = true;
                        return;
                    }
                    filesInFolder[targetpaths[index]] = null;
                }
            }
            else
            {
                if (TargetPath.Text.Trim().Length == 0)
                {
                    MessageBox.Show(this, Strings.TargetFolder.NoFolderError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                targetpaths = new string[] { TargetPath.Text.Trim() };
                filesInFolder[TargetPath.Text] = null;
            }

            //if the filelist is loaded, we can verify the file length
            if (!Library.Utility.Utility.IsClientLinux)
            {
                if (backupFileList.LoadedFileList != null && backupFileList.LoadedFileList.Count != 0)
                {
                    long maxPath = 0;
                    List<string> files = PartialRestore.Checked ? m_wrapper.RestoreFileSelection : backupFileList.LoadedFileList;

                    if (backupFileList.TargetFolders.Count > 1)
                    {
                        string[] restorefolders = PartialRestore.Checked ? targetpaths : backupFileList.TargetSuggestions;
                        foreach (string s in files)
                        {
                            int sepIndx = s.IndexOf(System.IO.Path.DirectorySeparatorChar) + 1;
                            int index = int.Parse(s.Substring(0, s.IndexOf(System.IO.Path.DirectorySeparatorChar)));
                            if (index >= 0 && index < restorefolders.Length && !string.IsNullOrEmpty(restorefolders[index]))
                                maxPath = Math.Max(restorefolders[index].Length + s.Length + 1 - sepIndx, maxPath);
                        }
                    }
                    else
                    {
                        foreach (string s in files)
                            maxPath = Math.Max(TargetPath.Text.Length + s.Length + 1, maxPath);
                    }


                    if (maxPath > 245)
                    {
                        if (MessageBox.Show(this, string.Format(Strings.TargetFolder.PathTooLongWarning, maxPath), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                        {
                            args.Cancel = true;
                            return;
                        }
                    }

                }
            }

            bool anyValids = false;
            for (int i = 0; i < targetpaths.Length; i++)
            {
                string targetpath = targetpaths[i] ?? "";

                try
                {
                    //Skip the verification for folders with no target files
                    if (!filesInFolder.ContainsKey(targetpath))
                    {
                        targetpaths[i] = "";
                        continue;
                    }

                    if (targetpath.Trim().Length == 0)
                    {
                        MessageBox.Show(this, Strings.TargetFolder.NoFolderError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        args.Cancel = true;
                        return;
                    }

                    if (!System.IO.Path.IsPathRooted(targetpath))
                    {
                        MessageBox.Show(this, string.Format(Strings.TargetFolder.FolderPathIsRelativeError, targetpath), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        args.Cancel = true;
                        return;
                    }

                    if (!System.IO.Directory.Exists(targetpath))
                    {
                        switch (MessageBox.Show(this, string.Format(Strings.TargetFolder.CreateNewFolderWarning, targetpath), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                        {
                            case DialogResult.Yes:
                                System.IO.Directory.CreateDirectory(targetpath);
                                break;
                            case DialogResult.Cancel:
                                args.Cancel = true;
                                return;
                        }
                    }

                    if (System.IO.Directory.GetFileSystemEntries(targetpath).Length > 0)
                        if (MessageBox.Show(this, string.Format(Strings.TargetFolder.FolderIsNotEmptyWarning, targetpath), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                        {
                            args.Cancel = true;
                            return;
                        }

                    anyValids = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Strings.TargetFolder.FolderValidationError, targetpath, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            if ( !anyValids || (PartialRestore.Checked && m_wrapper.RestoreFileSelection.Count == 0))
            {
                MessageBox.Show(this, Strings.TargetFolder.NoFilesSelectedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (PartialRestore.Checked)
            {
                m_wrapper.DisplayRestorePath = string.Join(System.IO.Path.PathSeparator.ToString(), targetpaths);
                m_wrapper.RestoreFilter = string.Join(System.IO.Path.PathSeparator.ToString(), backupFileList.CheckedFiles.ToArray());
            }
            else
            {
                m_wrapper.DisplayRestorePath = targetpaths[0];
                m_wrapper.RestoreFilter = "";
            }
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

            if (PartialRestore.Checked && m_wrapper != null)
            {
                if (m_wrapper.RestoreFileList == null)
                    m_wrapper.RestoreFileList = new List<string>();

                if (m_wrapper.RestoreTargetFolders == null)
                    m_wrapper.RestoreTargetFolders = new List<string>();

                if (backupFileList.LoadedFileList == null || backupFileList.LoadedFileList.Count == 0)
                    backupFileList.LoadFileList(m_wrapper.DataConnection.GetObjectById<Schedule>(m_wrapper.ScheduleID), m_wrapper.RestoreTime, m_wrapper.RestoreFileList, m_wrapper.RestoreTargetFolders, TargetPath.Text);
            }
        }

        private void TargetPath_TextChanged(object sender, EventArgs e)
        {
            backupFileList.DefaultTarget = TargetPath.Text;
        }

        private void backupFileList_FileListLoaded(object sender, EventArgs e)
        {
            if (m_wrapper.RestoreFileSelection != null && m_wrapper.RestoreFileSelection.Count > 0)
                backupFileList.CheckedFiles = m_wrapper.RestoreFileSelection;
        }
    }
}
