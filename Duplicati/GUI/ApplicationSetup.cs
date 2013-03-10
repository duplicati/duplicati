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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using System.Data.LightDatamodel;
using Duplicati.Datamodel;
using System.Globalization;
using Duplicati.Library.Utility;

namespace Duplicati.GUI
{
    public partial class ApplicationSetup : Form
    {
        private IDataFetcherWithRelations m_connection;
        private ApplicationSettings m_settings;
        private bool m_isUpdating = false;

        //These variables handle the worker thread size calculation
        private object m_lock = new object();
        private System.Threading.Thread m_thread = null;
        private bool m_restartCalculator = false;

        public ApplicationSetup()
        {
            InitializeComponent();

            RecentDuration.SetIntervals(new List<KeyValuePair<string, string>>(
                new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>(Strings.Common.OneWeek, "1W"),
                    new KeyValuePair<string, string>(Strings.Common.TwoWeeks, "2W"),
                    new KeyValuePair<string, string>(Strings.Common.OneMonth, "1M"),
                    new KeyValuePair<string, string>(Strings.Common.ThreeMonths, "3M"),
                }));

            StartupDelayDuration.SetIntervals(new List<KeyValuePair<string, string>>(
                new KeyValuePair<string, string>[]
                {
                    new KeyValuePair<string, string>(Strings.Common.NoMinutes, "0"),
                    new KeyValuePair<string, string>(Strings.Common.OneMinute, "1m"),
                    new KeyValuePair<string, string>(Strings.Common.FiveMinutes, "5m"),
                    new KeyValuePair<string, string>(Strings.Common.FifteenMinutes, "15m"),
                    new KeyValuePair<string, string>(Strings.Common.ThirtyMinutes, "30m"),
                }));

            BalloonNotificationLevel.Items.AddRange(new object[] {
                new ComboBoxItemPair<ApplicationSettings.NotificationLevel>(Strings.ApplicationSetup.BalloonNotification_Off, ApplicationSettings.NotificationLevel.Off),
                new ComboBoxItemPair<ApplicationSettings.NotificationLevel>(Strings.ApplicationSetup.BalloonNotification_Errors, ApplicationSettings.NotificationLevel.Errors),
                new ComboBoxItemPair<ApplicationSettings.NotificationLevel>(Strings.ApplicationSetup.BalloonNotification_Warnings, ApplicationSettings.NotificationLevel.Warnings),
                new ComboBoxItemPair<ApplicationSettings.NotificationLevel>(Strings.ApplicationSetup.BalloonNotification_Start, ApplicationSettings.NotificationLevel.Start),
                new ComboBoxItemPair<ApplicationSettings.NotificationLevel>(Strings.ApplicationSetup.BalloonNotification_StartAndStop, ApplicationSettings.NotificationLevel.StartAndStop),
                new ComboBoxItemPair<ApplicationSettings.NotificationLevel>(Strings.ApplicationSetup.BalloonNotification_Continous, ApplicationSettings.NotificationLevel.Continous),
            });

            if (Program.TraylessMode)
                BalloonNotificationLevel.Enabled = label8.Enabled = false;

            LanguageSelection.Items.Clear();
            LanguageSelection.Items.Add(new ComboBoxItemPair<CultureInfo>(string.Format(Strings.ApplicationSetup.DefaultLanguage, Library.Utility.Utility.DefaultCulture.DisplayName), Library.Utility.Utility.DefaultCulture));

            System.Text.RegularExpressions.Regex cix = new System.Text.RegularExpressions.Regex("[A-z][A-z](\\-[A-z][A-z])?");

            foreach (string f in System.IO.Directory.GetDirectories(Application.StartupPath))
                if (cix.Match(System.IO.Path.GetFileName(f)).Length == System.IO.Path.GetFileName(f).Length)
                    try
                    {
                        CultureInfo ci = CultureInfo.GetCultureInfo(System.IO.Path.GetFileName(f));
                        LanguageSelection.Items.Add(new ComboBoxItemPair<CultureInfo>(ci.DisplayName, ci));
                    }
                    catch
                    {
                    }

            bool hasEnglish = false;
            foreach (ComboBoxItemPair<CultureInfo> c in LanguageSelection.Items)
                if (c.Value.Name.Equals("en-US", StringComparison.InvariantCultureIgnoreCase))
                    hasEnglish = true;

            if (!hasEnglish)
            {
                CultureInfo ci = CultureInfo.GetCultureInfo("en-US");
                LanguageSelection.Items.Add(new ComboBoxItemPair<CultureInfo>(ci.DisplayName, ci));
            }

            try
            {
                EncryptionModule.Items.Clear();

                foreach (Library.Interface.IEncryption e in Library.DynamicLoader.EncryptionLoader.Modules)
                    EncryptionModule.Items.Add(new ComboBoxItemPair<Library.Interface.IEncryption>(e.DisplayName, e));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.ApplicationSetup.EncryptionModuleLoadError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            this.Icon = Properties.Resources.TrayNormal;

#if DEBUG
            this.Text += " (DEBUG)";
#endif

        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Font = SystemFonts.MessageBoxFont;
        }

        private void RecentDuration_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_settings == null)
                return;

            try { Library.Utility.Timeparser.ParseTimeSpan(RecentDuration.Value); }
            catch { return; }

            m_settings.RecentBackupDuration = RecentDuration.Value;
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            if (UseCommonPassword.Checked && !CommonPassword.VerifyPasswordIfChanged())
                return;

            foreach (TabPage tab in TabContainer.TabPages)
                if (tab.Tag as Library.Interface.ISettingsControl != null)
                {
                    try
                    {
                        if (!(tab.Tag as Library.Interface.ISettingsControl).Validate(tab.Controls[0]))
                            return;

                        (tab.Tag as Library.Interface.ISettingsControl).Leave(tab.Controls[0]);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(this, string.Format(Strings.ApplicationSetup.SaveExtensionError, ex), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        return;
                    }
                }


            //Commit all changes made to the context
            m_connection.CommitAllRecursive();

            System.Globalization.CultureInfo newCI = System.Threading.Thread.CurrentThread.CurrentUICulture;

            try
            {
                if (string.IsNullOrEmpty(m_settings.DisplayLanguage))
                    newCI = Library.Utility.Utility.DefaultCulture;
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
            }


            this.DialogResult = DialogResult.OK;
            this.Close();
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
            try
            {
                this.Text = string.Format(Strings.ApplicationSetup.DialogTitle, License.VersionNumbers.Version);

                m_isUpdating = true;

                m_connection = new DataFetcherNested(Program.DataConnection);
                m_settings = new ApplicationSettings(m_connection);

                RecentDuration.Value = m_settings.RecentBackupDuration;
                TempPath.Text = m_settings.TempPath;

                UseCommonPassword.Checked = m_settings.UseCommonPassword;
                CommonPassword.Text = CommonPassword.InitialPassword = m_settings.CommonPassword;
                CommonPassword.AskToEnterNewPassword = !string.IsNullOrEmpty(CommonPassword.Text);

                if (EncryptionModule.Items.Count > 0)
                {
                    bool foundEncryption = false;
                    int defaultIndex = 0;
                    for (int i = 0; i < EncryptionModule.Items.Count; i++)
                        if (((ComboBoxItemPair<Library.Interface.IEncryption>)EncryptionModule.Items[i]).Value.FilenameExtension == m_settings.CommonPasswordEncryptionModule)
                        {
                            foundEncryption = true;
                            EncryptionModule.SelectedIndex = i;
                            break;
                        }
                        else if (((ComboBoxItemPair<Library.Interface.IEncryption>)EncryptionModule.Items[i]).Value.FilenameExtension == "aes")
                            defaultIndex = i;

                    if (!foundEncryption)
                        EncryptionModule.SelectedIndex = defaultIndex;
                }
                else
                {
                    PasswordDefaultsGroup.Enabled = false;
                }


                SignatureCacheEnabled.Checked = m_settings.SignatureCacheEnabled;
                SignatureCachePath.Text = m_settings.SignatureCachePath;
                CalculateSignatureCacheSize();

                StartupDelayDuration.Value = m_settings.StartupDelayDuration;
                ThreadPriorityPicker.SelectedPriority = m_settings.ThreadPriorityOverride;
                Bandwidth.UploadLimit = m_settings.UploadSpeedLimit;
                Bandwidth.DownloadLimit = m_settings.DownloadSpeedLimit;

                HideDonateButton.Checked = m_settings.HideDonateButton;

                BalloonNotificationLevel.SelectedItem = null;
                foreach(ComboBoxItemPair<ApplicationSettings.NotificationLevel> p in BalloonNotificationLevel.Items)
                    if (p.Value == m_settings.BallonNotificationLevel)
                    {
                        BalloonNotificationLevel.SelectedItem = p;
                        break;
                    }

                if (string.IsNullOrEmpty(m_settings.DisplayLanguage))
                    LanguageSelection.SelectedIndex = 0;
                else
                {
                    try
                    {
                        LanguageSelection.SelectedIndex = -1;
                        System.Globalization.CultureInfo cci = System.Globalization.CultureInfo.GetCultureInfo(m_settings.DisplayLanguage);
                        for(int i = 0; i < LanguageSelection.Items.Count; i++)
                            if (((ComboBoxItemPair<CultureInfo>)LanguageSelection.Items[i]).Value == cci)
                            {
                                LanguageSelection.SelectedIndex = i;
                                break;
                            }
                    }
                    catch
                    {
                        LanguageSelection.SelectedIndex = -1;
                    }
                }

                try
                {
                    foreach (Library.Interface.ISettingsControl ic in Library.DynamicLoader.SettingsControlLoader.Modules)
                    {
                        Control c = ic.GetControl(m_settings.CreateDetachedCopy(), Datamodel.SettingExtension.GetExtensions(m_connection, ic.Key));
                        c.Dock = DockStyle.Fill;
                        TabPage tab = new TabPage();
                        tab.Text = ic.PageTitle;
                        tab.ToolTipText = ic.PageDescription;
                        tab.Controls.Add(c);
                        tab.Tag = ic;
                        TabContainer.TabPages.Add(tab);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Strings.ApplicationSetup.SettingControlsLoadError, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                //Place the license page last
                TabContainer.TabPages.Remove(LicenseTab);
                TabContainer.TabPages.Insert(TabContainer.TabPages.Count, LicenseTab);

                string licensePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "licenses");
                List<Duplicati.License.LicenseEntry> licenses = Duplicati.License.LicenseReader.ReadLicenses(licensePath);
                licenses.Insert(0, new Duplicati.License.LicenseEntry("Duplicati", System.IO.Path.Combine(licensePath, "duplicati-url.txt"), System.IO.Path.Combine(licensePath, "license.txt")));
                licenses.Insert(0, new Duplicati.License.LicenseEntry("Acknowledgements", System.IO.Path.Combine(licensePath, "duplicati-url.txt"), System.IO.Path.Combine(licensePath, "acknowledgements.txt")));

                LicenseSections.Items.Clear();
                LicenseSections.Items.AddRange(licenses.ToArray());
                LicenseSections.SelectedIndex = -1;
                LicenseSections.SelectedIndex = 0;
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void UseCommonPassword_CheckedChanged(object sender, EventArgs e)
        {
            CommonPassword.Enabled = UseCommonPassword.Checked;
            EncryptionModule.Enabled = UseCommonPassword.Checked;

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
                e.Result = string.Format(Strings.ApplicationSetup.CacheSize, Library.Utility.Utility.FormatSizeString(Library.Utility.Utility.GetDirectorySize(System.Environment.ExpandEnvironmentVariables((string)e.Argument), null)));
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

        private void LanguageSelection_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            if (LanguageSelection.SelectedIndex == 0)
                m_settings.DisplayLanguage = "";
            else if (LanguageSelection.SelectedIndex > 0)
                m_settings.DisplayLanguage = ((ComboBoxItemPair<CultureInfo>)LanguageSelection.SelectedItem).Value.Name;
        }

        private void StartupDelayDuration_ValueChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_settings == null)
                return;

            try { Library.Utility.Timeparser.ParseTimeSpan(StartupDelayDuration.Value); }
            catch { }

            m_settings.StartupDelayDuration = StartupDelayDuration.Value;
        }

        private void ThreadPriorityPicker_SelectedPriorityChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.ThreadPriorityOverride = ThreadPriorityPicker.SelectedPriority;
        }

        private void Bandwidth_UploadLimitChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.UploadSpeedLimit = Bandwidth.UploadLimit;
        }

        private void Bandwidth_DownloadLimitChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.DownloadSpeedLimit = Bandwidth.DownloadLimit;
        }

        private void EncryptionModule_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            if (EncryptionModule.SelectedItem as ComboBoxItemPair<Library.Interface.IEncryption> != null)
                m_settings.CommonPasswordEncryptionModule = ((ComboBoxItemPair<Library.Interface.IEncryption>)EncryptionModule.SelectedItem).Value.FilenameExtension;

        }

        public static Dictionary<string, string> GetApplicationSettings(IDataFetcher connection)
        {
            Dictionary<string, string> env = new ApplicationSettings(connection).CreateDetachedCopy();

            //If there are any control extensions, let them modify the environement
            foreach (Library.Interface.ISettingsControl ic in Library.DynamicLoader.SettingsControlLoader.Modules)
                ic.BeginEdit(env, SettingExtension.GetExtensions(connection, ic.Key));

            return env;
        }

        public static void SaveExtensionSettings(IDataFetcher connection, IDictionary<string, string> env)
        {
            //If there are any control extensions, let them modify the environement
            foreach (Library.Interface.ISettingsControl ic in Library.DynamicLoader.SettingsControlLoader.Modules)
                ic.EndEdit(env, SettingExtension.GetExtensions(connection, ic.Key));
        }

        private void HideDonateButton_CheckedChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            m_settings.HideDonateButton = HideDonateButton.Checked;
        }

        private void LicenseSections_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (LicenseSections.SelectedItem as Duplicati.License.LicenseEntry != null)
            {
                Duplicati.License.LicenseEntry l = LicenseSections.SelectedItem as Duplicati.License.LicenseEntry;
                LicenseText.Text = l.License;
                LicenseLink.Visible = !string.IsNullOrEmpty(l.Url);
            }
        }

        private void LicenseLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            if (LicenseSections.SelectedItem as Duplicati.License.LicenseEntry != null)
            {
                Duplicati.License.LicenseEntry l = LicenseSections.SelectedItem as Duplicati.License.LicenseEntry;
                if (!string.IsNullOrEmpty(l.Url))
                    Duplicati.Library.Utility.UrlUtillity.OpenUrl(l.Url);
            }
        }

        private void BalloonNotificationLevel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            if (BalloonNotificationLevel.SelectedItem as ComboBoxItemPair<ApplicationSettings.NotificationLevel> != null)
                m_settings.BallonNotificationLevel = (BalloonNotificationLevel.SelectedItem as ComboBoxItemPair<ApplicationSettings.NotificationLevel>).Value;
        }

        private void groupBox1_Enter(object sender, EventArgs e)
        {

        }

        private void panel1_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}
