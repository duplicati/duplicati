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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Data.LightDatamodel;
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    public partial class ApplicationSetup : Form
    {
        private IDataFetcherCached m_connection;
        private ApplicationSettings m_settings;
        private bool m_isUpdating = false;

        //These variables handle the worker thread size calculation
        private object m_lock = new object();
        private System.Threading.Thread m_thread = null;
        private bool m_restartCalculator = false;

        public ApplicationSetup()
        {
            InitializeComponent();
            m_connection = new DataFetcherNested(Program.DataConnection);
            m_settings = new ApplicationSettings(m_connection);

            RecentDuration.SetIntervals(new List<KeyValuePair<string, string>>(
                new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>("One week", "1W"),
                    new KeyValuePair<string, string>("Two weeks", "2W"),
                    new KeyValuePair<string, string>("One month", "1M"),
                    new KeyValuePair<string, string>("Three months", "3M"),
                }));

            try
            {
                m_isUpdating = true;
                RecentDuration.Value = m_settings.RecentBackupDuration;
                GPGPath.Text = m_settings.GPGPath;
                SFTPPath.Text = m_settings.SFtpPath;
                TempPath.Text = m_settings.TempPath;

                UseCommonPassword.Checked = m_settings.UseCommonPassword;
                CommonPassword.Text = m_settings.CommonPassword;
                CommonPasswordUseGPG.Checked = m_settings.CommonPasswordUseGPG;

                SignatureCacheEnabled.Checked = m_settings.SignatureCacheEnabled;
                SignatureCachePath.Text = m_settings.SignatureCachePath;
                CalculateSignatureCacheSize();
            }
            finally
            {
                m_isUpdating = false;
            }

            this.Icon = Properties.Resources.TrayNormal;
        }

        private bool TestForFiles(string folder, params string[] files)
        {
            try
            {
                foreach(string file in files)
                    if (!System.IO.File.Exists(System.IO.Path.Combine(folder, file)))
                        if (MessageBox.Show(this, "The folder selected does not contain the file: " + file + ".\r\nDo you want to use that folder anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                            return false;
            }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, "An exception occured while examining the folder: "+ ex.Message + ".\r\nDo you want to use that folder anyway?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                    return false;
            }

            return true;
        }

        private void BrowseGPG_Click(object sender, EventArgs e)
        {
            if (BrowseGPGDialog.ShowDialog(this) == DialogResult.OK)
                GPGPath.Text = BrowseGPGDialog.FileName;
        }

        private void RecentDuration_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.RecentBackupDuration = RecentDuration.Value;
        }

        private void GPGPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.GPGPath = GPGPath.Text;
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            m_connection.CommitRecursive(m_connection.GetObjects<ApplicationSetting>());

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void BrowseSFTP_Click(object sender, EventArgs e)
        {
            if (BrowseSFTPDialog.ShowDialog(this) == DialogResult.OK)
                SFTPPath.Text = BrowseSFTPDialog.FileName;
        }

        private void SFTPPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.SFtpPath = SFTPPath.Text;
        }

        private void RecentDuration_ValueChanged(object sender, EventArgs e)
        {
            RecentDuration_TextChanged(sender, e);
        }

        private void TempPathBrowse_Click(object sender, EventArgs e)
        {
            if (BrowseTempPath.ShowDialog(this) == DialogResult.OK)
                TempPath.Text = BrowseTempPath.SelectedPath;
        }

        private void TempPath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;
            m_settings.TempPath = TempPath.Text;
        }

        private void ApplicationSetup_Load(object sender, EventArgs e)
        {

        }

        private void UseCommonPassword_CheckedChanged(object sender, EventArgs e)
        {
            PasswordPanel.Enabled = UseCommonPassword.Checked;

            if (m_isUpdating)
                return;

            m_settings.UseCommonPassword = UseCommonPassword.Checked;

            if (m_settings.UseCommonPassword)
                m_settings.CommonPassword = CommonPassword.Text;
            else
                m_settings.CommonPassword = ""; //Clear it from DB
        }

        private void CommonPassword_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.CommonPassword = CommonPassword.Text;
        }

        private void CommonPasswordUseGPG_CheckedChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.CommonPasswordUseGPG = CommonPasswordUseGPG.Checked;
        }

        private void SignatureCachePathBrowse_Click(object sender, EventArgs e)
        {
            if (BrowseSignatureCachePath.ShowDialog(this) == DialogResult.OK)
                SignatureCachePath.Text = BrowseSignatureCachePath.SelectedPath;

        }

        private void SignatureCachePath_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.SignatureCachePath = SignatureCachePath.Text;
            CalculateSignatureCacheSize();
        }

        private void SignatureCacheEnabled_CheckedChanged(object sender, EventArgs e)
        {
            SignatureCachePath.Enabled = SignatureCachePathBrowse.Enabled = SignatureCacheEnabled.Checked;

            if (m_isUpdating)
                return;

            m_settings.SignatureCacheEnabled = SignatureCacheEnabled.Checked;
            CalculateSignatureCacheSize();
        }

        private void CacheSizeCalculator_DoWork(object sender, DoWorkEventArgs e)
        {
            lock (m_lock)
                m_thread = System.Threading.Thread.CurrentThread;

            try
            {
                e.Result = "Cache size: " + Library.Core.Utility.FormatSizeString(Library.Core.Utility.GetDirectorySize(System.Environment.ExpandEnvironmentVariables((string)e.Argument), null));
            }
            catch (System.Threading.ThreadAbortException)
            {
                System.Threading.Thread.ResetAbort();
                e.Cancel = true;
            }
            finally
            {
                lock (m_lock)
                    m_thread = null;
            }

        }

        private void CacheSizeCalculator_RunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (e.Cancelled)
                CacheSizeLabel.Text = "Cancelled";
            else if (e.Error != null)
                CacheSizeLabel.Text = e.Error.Message;
            else
                CacheSizeLabel.Text = (string)e.Result;

            if (m_restartCalculator)
                CalculateSignatureCacheSize();
        }

        private void CalculateSignatureCacheSize()
        {
            CacheSizeLabel.Text = "Calculating cache size ...";

            lock (m_lock)
                if (CacheSizeCalculator.IsBusy)
                {
                    m_restartCalculator = true;
                    m_thread.Abort();
                }
                else
                {
                    m_restartCalculator = false;
                    if (SignatureCacheEnabled.Checked)
                        CacheSizeCalculator.RunWorkerAsync(SignatureCachePath.Text);
                    else
                        CacheSizeLabel.Text = "";
                }
        }

        private void ClearCacheButton_Click(object sender, EventArgs e)
        {
            try
            {
                string path = System.Environment.ExpandEnvironmentVariables(SignatureCachePath.Text);
                if (MessageBox.Show(this, "Delete signature files in the folder: \n" + path, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                Library.Main.Interface.RemoveSignatureFiles(path);
                CalculateSignatureCacheSize();
            }
            catch(Exception ex)
            {
                MessageBox.Show(this, "An error occured: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ApplicationSetup_FormClosing(object sender, FormClosingEventArgs e)
        {
            lock(m_lock)
                if (m_thread != null)
                {
                    m_restartCalculator = false;
                    m_thread.Abort();
                }
        }

    }
}