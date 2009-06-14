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
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class ThrottleOptions : System.Windows.Forms.Wizard.WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        /// <summary>
        /// Textual representation of priorities
        /// </summary>
        private readonly string[] THREAD_PRIORITIES = new string[] {
            GUI.Strings.Common.ThreadPriorityHighest,
            GUI.Strings.Common.ThreadPriorityAboveNormal,
            GUI.Strings.Common.ThreadPriorityNormal,
            GUI.Strings.Common.ThreadPriorityBelowNormal,
            GUI.Strings.Common.ThreadPriortyLowest
        };

        /// <summary>
        /// Mapping the priority to the string index
        /// </summary>
        private System.Threading.ThreadPriority[] PRIORITY_LOOKUP = new System.Threading.ThreadPriority[] {
            System.Threading.ThreadPriority.Highest,
            System.Threading.ThreadPriority.AboveNormal,
            System.Threading.ThreadPriority.Normal,
            System.Threading.ThreadPriority.BelowNormal,
            System.Threading.ThreadPriority.Lowest,
        };

        public ThrottleOptions()
            : base(Strings.ThrottleOptions.PageTitle, Strings.ThrottleOptions.PageDescription)
        {
            InitializeComponent();

            m_autoFillValues = false;

            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageLeave);
            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageEnter);
            base.PageDisplay += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageDisplay);
        }

        void ThrottleOptions_PageDisplay(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            //Reload due to annoying bug with the numerics
            if (m_valuesAutoLoaded)
                base.LoadDialogSettings();
        }

        private void LoadItem(string name, int defaultSuffix, string value)
        {
            ComboBox combo = (ComboBox)this.Controls[name + "LimitSuffix"];
            NumericUpDown number = (NumericUpDown)this.Controls[name + "LimitNumber"];

            if (this.Controls.ContainsKey(name + "LimitEnabled"))
            {
                ((CheckBox)this.Controls[name + "LimitEnabled"]).Checked = !string.IsNullOrEmpty(value) && value != "0";
                if (!((CheckBox)this.Controls[name + "LimitEnabled"]).Checked)
                {
                    number.Value = 0;
                    combo.SelectedIndex = defaultSuffix;
                    return;
                }
            }

            long size = 0;

            size = Duplicati.Library.Core.Sizeparser.ParseSize(value);
            number.Value = 0;

            if (size != 0)
            {
                for (int i = 0; i < combo.Items.Count; i++)
                    if (size < Math.Pow(2, 10 * (i + 1)))
                    {
                        combo.SelectedIndex = i;
                        number.Value = (int)(size / (long)Math.Pow(2, 10 * i));
                        break;
                    }

                if (number.Value == 0)
                    size = 0;
            }

            if (size == 0)
            {
                combo.SelectedIndex = defaultSuffix;
                number.Value = 0;
            }
        }

        void ThrottleOptions_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            if (!m_valuesAutoLoaded)
            {
                LoadItem("Upload", 1, m_wrapper.UploadSpeedLimit);
                LoadItem("Download", 1, m_wrapper.DownloadSpeedLimit);
                LoadItem("Backup", 2, m_wrapper.BackupSizeLimit);
                LoadItem("VolumeSize", 2, m_wrapper.VolumeSize);

                AsyncEnabled.Checked = m_wrapper.AsyncTransfer;
                ThreadPriority.SelectedIndex = Array.IndexOf<System.Threading.ThreadPriority>(PRIORITY_LOOKUP, Library.Core.Utility.ParsePriority(m_wrapper.ThreadPriority));
                ThreadPriorityEnabled.Checked = !string.IsNullOrEmpty(m_wrapper.ThreadPriority); 
            }
        }

        void ThrottleOptions_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            m_wrapper.UploadSpeedLimit =
                UploadLimitEnabled.Checked ? 
                UploadLimitNumber.Value.ToString() + UploadLimitSuffix.Text.Substring(0, UploadLimitSuffix.Text.Length - 2) :
                "";

            m_wrapper.DownloadSpeedLimit =
                DownloadLimitEnabled.Checked ?
                DownloadLimitNumber.Value.ToString() + DownloadLimitSuffix.Text.Substring(0, DownloadLimitSuffix.Text.Length - 2) :
                "";

            m_wrapper.BackupSizeLimit =
               BackupLimitEnabled.Checked ?
               BackupLimitNumber.Value.ToString() + BackupLimitSuffix.Text :
               "";

            m_wrapper.VolumeSize = VolumeSizeLimitNumber.Value.ToString() + VolumeSizeLimitSuffix.Text;
            m_wrapper.AsyncTransfer = AsyncEnabled.Checked;
            m_wrapper.ThreadPriority = ThreadPriorityEnabled.Checked ? PRIORITY_LOOKUP[ThreadPriority.SelectedIndex].ToString() : "";

            if ((bool)m_settings["Advanced:Filters"])
                args.NextPage = new Wizard_pages.Add_backup.EditFilters();
            else if ((bool)m_settings["Advanced:Filenames"])
                args.NextPage = new Wizard_pages.Add_backup.GeneratedFilenameOptions();
            else if ((bool)m_settings["Advanced:Overrides"])
                args.NextPage = new Wizard_pages.Add_backup.SettingOverrides();
            else
                args.NextPage = new FinishedAdd();
        }


        private void UploadLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            UploadLimitNumber.Enabled = UploadLimitSuffix.Enabled = UploadLimitEnabled.Checked;
        }

        private void DownloadLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            DownloadLimitNumber.Enabled = DownloadLimitSuffix.Enabled = DownloadLimitEnabled.Checked;
        }

        private void BackupLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            BackupLimitNumber.Enabled = BackupLimitSuffix.Enabled = BackupLimitEnabled.Checked;
        }

        private void ThreadPriorityEnabled_CheckedChanged(object sender, EventArgs e)
        {
            ThreadPriority.Enabled = ThreadPriorityEnabled.Checked;
        }
    }
}

