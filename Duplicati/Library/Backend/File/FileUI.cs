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

namespace Duplicati.Library.Backend
{
    public partial class FileUI : UserControl
    {
        IDictionary<string, string> m_options;

        private const string DESTINATION_FOLDER = "Destination";
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string CHECKED_EMPTY = "UI: Checked empty";
        private const string INITIALPASSWORD = "UI: Temp password";

        private bool m_hasCheckedEmpty = false;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        public FileUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        private FileUI()
        {
            InitializeComponent();
        }

        internal bool Save(bool validate)
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            string targetpath;
            if (UsePath.Checked)
                targetpath = TargetFolder.Text;
            else
                targetpath = TargetDrive.Text + "\\" + Folder.Text;

            if (m_hasCheckedEmpty && m_options.ContainsKey(DESTINATION_FOLDER) && targetpath != m_options[DESTINATION_FOLDER])
                m_hasCheckedEmpty = false;


            m_options.Clear();
            m_options[CHECKED_EMPTY] = m_hasCheckedEmpty.ToString();
            m_options[DESTINATION_FOLDER] = targetpath;

            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;

            if (UseCredentials.Checked)
            {
                m_options[USERNAME] = Username.Text;
                m_options[PASSWORD] = Password.Text;
            }
            else
            {
                m_options[USERNAME] = "";
                m_options[PASSWORD] = "";
            }

            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);

            if (!validate)
                return false;

            if (UseCredentials.Checked)
            {
                if (!Duplicati.Library.Backend.File.PreAuthenticate(targetpath, Username.Text, Password.Text, true))
                {
                    MessageBox.Show(this, Strings.FileUI.AuthenticationError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
            }

            try
            {
                if (targetpath.Trim().Length == 0)
                {
                    MessageBox.Show(this, Strings.FileUI.EmptyFoldernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                if (!System.IO.Path.IsPathRooted(targetpath))
                {
                    MessageBox.Show(this, Strings.FileUI.NonRootedPathError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return false;
                }

                if (!System.IO.Directory.Exists(targetpath))
                {
                    switch (MessageBox.Show(this, Strings.FileUI.CreateFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                    {
                        case DialogResult.Yes:
                            System.IO.Directory.CreateDirectory(targetpath);
                            break;
                        case DialogResult.Cancel:
                            return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.FileUI.FolderValidationError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(m_uiAction))
                {
                    if (string.Equals(m_uiAction, "add", StringComparison.InvariantCultureIgnoreCase))
                    {
                        string[] files = System.IO.Directory.GetFileSystemEntries(targetpath);
                        string[] duplicati_files = System.IO.Directory.GetFiles(targetpath, "duplicati-*");

                        if (duplicati_files.Length > 0)
                            if (MessageBox.Show(this, string.Format(Interface.CommonStrings.ExistingBackupDetectedQuestion), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                                return false;

                        if (!m_hasCheckedEmpty && files.Length > 0)
                            if (MessageBox.Show(this, Strings.FileUI.FolderNotEmptyWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                                return false;
                    }
                    else if (string.Equals(m_uiAction, "restore", StringComparison.InvariantCultureIgnoreCase))
                    {
                        if (System.IO.Directory.GetFileSystemEntries(targetpath).Length == 0)
                            if (MessageBox.Show(this, Strings.FileUI.FolderEmptyError, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                                return false;
                    }

                    m_hasCheckedEmpty = true;
                    m_options[CHECKED_EMPTY] = "true";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.FileUI.FolderValidationError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            m_options.Remove(INITIALPASSWORD);

            return true;
        }

        private void RescanDrives()
        {
            TargetDrive.Items.Clear();

            if (!Library.Utility.Utility.IsClientLinux)
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

        void FileUI_Load(object sender, EventArgs args)
        {
            RescanDrives();

            UsePath.Checked = true;
            if (m_options.ContainsKey(DESTINATION_FOLDER))
                TargetFolder.Text = m_options[DESTINATION_FOLDER];

            UseCredentials.Checked = m_options.ContainsKey(USERNAME) && !string.IsNullOrEmpty(m_options[USERNAME]);
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];

            if (!m_options.ContainsKey(INITIALPASSWORD))
                m_options[INITIALPASSWORD] = m_options.ContainsKey(PASSWORD) ? m_options[PASSWORD] : "";
            Password.AskToEnterNewPassword = !string.IsNullOrEmpty(m_options[INITIALPASSWORD]);
            Password.InitialPassword = m_options[INITIALPASSWORD];

            if (!m_options.ContainsKey(CHECKED_EMPTY) || !bool.TryParse(m_options[CHECKED_EMPTY], out m_hasCheckedEmpty))
                m_hasCheckedEmpty = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
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
                MessageBox.Show(this, Strings.FileUI.NoRemovableDrivesError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UsePath.Checked = true;
            }
        }

        public static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["ftp-username"] = guiOptions[USERNAME];
            if (guiOptions.ContainsKey(PASSWORD) && !string.IsNullOrEmpty(guiOptions[PASSWORD]))
                commandlineOptions["ftp-password"] = guiOptions[PASSWORD];

            if (!guiOptions.ContainsKey(DESTINATION_FOLDER))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, DESTINATION_FOLDER));

            return "file://" + guiOptions[DESTINATION_FOLDER];
        }

        public static string PageTitle
        {
            get { return Strings.FileUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.FileUI.PageDescription; }
        }
    }
}
