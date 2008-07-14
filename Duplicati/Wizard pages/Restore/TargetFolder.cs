using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.Wizard_pages.Restore
{
    public partial class TargetFolder : UserControl, IWizardControl
    {
        public TargetFolder()
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
            get { return "Select restore folder"; }
        }

        string IWizardControl.HelpText
        {
            get { return "Please select the folder into which the backup will be restored"; }
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
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            string targetpath = TargetPath.Text;

            try
            {
                if (targetpath.Trim().Length == 0)
                {
                    MessageBox.Show(this, "You must enter a folder to backup to", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    cancel = true;
                    return;
                }

                if (!System.IO.Path.IsPathRooted(targetpath))
                {
                    MessageBox.Show(this, "You must enter the full path of the folder", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    cancel = true;
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

        }

        #endregion

        private void TargetFolder_Load(object sender, EventArgs e)
        {

        }

        private void BrowseFolder_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                TargetPath.Text = folderBrowserDialog.SelectedPath;
        }

        public string SelectedFolder { get { return TargetPath.Text; } }
    }
}
