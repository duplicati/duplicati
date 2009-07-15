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
        private List<System.Globalization.CultureInfo> m_supportedLanguages;

        public ApplicationSetup()
        {
            InitializeComponent();
            m_connection = new DataFetcherNested(Program.DataConnection);
            m_settings = new ApplicationSettings(m_connection);
            m_supportedLanguages = new List<System.Globalization.CultureInfo>();

            RecentDuration.SetIntervals(new List<KeyValuePair<string, string>>(
                new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>(Strings.Common.OneWeek, "1W"),
                    new KeyValuePair<string, string>(Strings.Common.TwoWeeks, "2W"),
                    new KeyValuePair<string, string>(Strings.Common.OneMonth, "1M"),
                    new KeyValuePair<string, string>(Strings.Common.ThreeMonths, "3M"),
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
                UseGPGEncryption.Checked = m_settings.CommonPasswordUseGPG;

                SignatureCacheEnabled.Checked = m_settings.SignatureCacheEnabled;
                SignatureCachePath.Text = m_settings.SignatureCachePath;
                CalculateSignatureCacheSize();

                LanguageSelection.Items.Clear();
                LanguageSelection.Items.Add(string.Format(Strings.ApplicationSetup.DefaultLanguage, System.Globalization.CultureInfo.InstalledUICulture.DisplayName));

                m_supportedLanguages.Add(System.Globalization.CultureInfo.GetCultureInfo("en"));

                System.Text.RegularExpressions.Regex cix = new System.Text.RegularExpressions.Regex("[A-z][A-z](\\-[A-z][A-z])?");

                foreach(string f in System.IO.Directory.GetDirectories(Application.StartupPath))
                    if (cix.Match(System.IO.Path.GetFileName(f)).Length == System.IO.Path.GetFileName(f).Length)
                        try 
                        {
                            m_supportedLanguages.Add(System.Globalization.CultureInfo.GetCultureInfo(System.IO.Path.GetFileName(f)));
                        }
                        catch 
                        {
                        }

                foreach (System.Globalization.CultureInfo ci in m_supportedLanguages)
                    LanguageSelection.Items.Add(ci.DisplayName);

                if (string.IsNullOrEmpty(m_settings.DisplayLanguage))
                    LanguageSelection.SelectedIndex = 0;
                else
                {
                    try
                    {
                        System.Globalization.CultureInfo cci = System.Globalization.CultureInfo.GetCultureInfo(m_settings.DisplayLanguage);
                        if (m_supportedLanguages.Contains(cci))
                            LanguageSelection.SelectedIndex = m_supportedLanguages.IndexOf(cci) + 1;
                        else
                            LanguageSelection.SelectedIndex = -1;
                    }
                    catch
                    {
                        LanguageSelection.SelectedIndex = -1;
                    }
                }
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
                        if (MessageBox.Show(this, string.Format(Strings.ApplicationSetup.FolderIsMissingFile, file), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                            return false;
            }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, string.Format(Strings.ApplicationSetup.ErrorWhileExaminingFolder, ex.Message), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
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

            System.Globalization.CultureInfo newCI = System.Threading.Thread.CurrentThread.CurrentUICulture;

            try
            {
                if (string.IsNullOrEmpty(m_settings.DisplayLanguage))
                    newCI = System.Globalization.CultureInfo.InstalledUICulture;
                else
                    newCI = System.Globalization.CultureInfo.GetCultureInfo(m_settings.DisplayLanguage);
            }
            catch
            {
            }


            if (newCI != System.Threading.Thread.CurrentThread.CurrentUICulture)
            {
                MessageBox.Show(this, Strings.ApplicationSetup.LanguageChangedWarning, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

                //We don't change here, because the application has loaded some 
                // resources already, so the switch would be partial

                /*System.Threading.Thread.CurrentThread.CurrentUICulture =
                System.Threading.Thread.CurrentThread.CurrentCulture =
                    newCI;*/
            }


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
                e.Result = string.Format(Strings.ApplicationSetup.CacheSize, Library.Core.Utility.FormatSizeString(Library.Core.Utility.GetDirectorySize(System.Environment.ExpandEnvironmentVariables((string)e.Argument), null)));
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
                CacheSizeLabel.Text = Strings.ApplicationSetup.OperationCancelled;
            else if (e.Error != null)
                CacheSizeLabel.Text = e.Error.Message;
            else
                CacheSizeLabel.Text = (string)e.Result;

            if (m_restartCalculator)
                CalculateSignatureCacheSize();
        }

        private void CalculateSignatureCacheSize()
        {
            CacheSizeLabel.Text = Strings.ApplicationSetup.CalculatingCacheSize;

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
                if (MessageBox.Show(this, string.Format(Strings.ApplicationSetup.ConfirmCacheDelete, path), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    return;

                Library.Main.Interface.RemoveSignatureFiles(path);
                CalculateSignatureCacheSize();
            }
            catch(Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.Common.GenericError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void UseAESEncryption_CheckedChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.CommonPasswordUseGPG = UseGPGEncryption.Checked;
        }

        private void UseGPGEncryption_CheckedChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.CommonPasswordUseGPG = UseGPGEncryption.Checked;
        }

        private void LanguageSelection_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            if (LanguageSelection.SelectedIndex == 0)
                m_settings.DisplayLanguage = "";
            else if (LanguageSelection.SelectedIndex > 0)
                m_settings.DisplayLanguage = m_supportedLanguages[LanguageSelection.SelectedIndex - 1].Name;
        }

    }
}