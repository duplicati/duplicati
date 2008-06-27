using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;

namespace Duplicati.Wizard_pages.Backends.File
{
    public partial class FileOptions : UserControl, IWizardControl, Wizard_pages.Interfaces.ITaskBased
    {
        private Duplicati.Datamodel.Backends.File m_file;
        private bool m_isUpdating = false;

        public FileOptions()
        {
            InitializeComponent();
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Backup storage options"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you can select where to store the backup data."; }
        }

        Image IWizardControl.Image
        {
            get { return null; }
        }

        bool IWizardControl.FullSize
        {
            get { return false; }
        }

        void IWizardControl.Enter(IWizardForm owner)
        {
            if (TargetDrive.Items.Count == 0)
            {
                for (char i = 'A'; i < 'Z'; i++)
                {
                    System.IO.DriveInfo di = new System.IO.DriveInfo(i.ToString());
                    if (di.DriveType == System.IO.DriveType.Removable)
                        TargetDrive.Items.Add(i.ToString() + ":");
                }
            }


            try
            {
                m_isUpdating = true;

                UsePath.Checked = true;
                TargetFolder.Text = m_file.DestinationFolder;
                
                UseCredentials.Checked = !string.IsNullOrEmpty(m_file.Username);
                Username.Text = m_file.Username;
                Password.Text = m_file.Password;
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            string targetpath;
            if (UsePath.Checked)
                targetpath = TargetFolder.Text;
            else
                targetpath = TargetDrive.Text + "\\" + Folder.Text;
            
            try
            {
                if (!System.IO.Directory.Exists(targetpath))
                {
                    switch (MessageBox.Show(this, "The selected folder does not exist. Do you want to create it?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                    {
                        case DialogResult.Yes:
                            System.IO.Directory.CreateDirectory(targetpath);
                            break;
                        case DialogResult.Cancel:
                            cancel = true;
                            return;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "An error occured while verifying the destination. Please make sure it exists and is accessible.\nError message: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                cancel = true;
                return;
            }

            try
            {
                if (System.IO.Directory.GetFileSystemEntries(targetpath).Length > 0)
                    if (MessageBox.Show(this, "The selected folder is not empty. Do you want to use it anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        cancel = true;
                        return;
                    }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "An error occured while verifying the destination. Please make sure it exists and is accessible.\nError message: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                cancel = true;
                return;
            }

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
        }

        #endregion

        private void UseCredentials_CheckedChanged(object sender, EventArgs e)
        {
            Credentials.Enabled = UseCredentials.Checked;

            if (m_isUpdating)
                return;

            if (UseCredentials.Checked)
            {
                MessageBox.Show(this, "This feature is not supported in the current version of Duplicati. You may enter the information now, and it may be used in later versions of Duplicati.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
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

            if (TargetDrive.Enabled && TargetDrive.Items.Count == 0)
            {
                MessageBox.Show(this, "No removable drives were found on your system. Please enter the path manually.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                UsePath.Checked = true;
            }

        }

        #region ITaskBased Members

        public void Setup(Duplicati.Datamodel.Task task)
        {
            m_file = new Duplicati.Datamodel.Backends.File(task);
        }

        #endregion
    }
}
