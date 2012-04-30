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
using Duplicati.Library.Utility;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class CleanupSettings : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public CleanupSettings()
            : base(Strings.CleanupSettings.PageTitle, Strings.CleanupSettings.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(IncrementalSettings_PageEnter);
            base.PageLeave += new PageChangeHandler(IncrementalSettings_PageLeave);

            List<KeyValuePair<string, string>> ix = new List<KeyValuePair<string, string>>();
            ix.Add(new KeyValuePair<string, string>(GUI.Strings.Common.OneDay, "1D"));
            ix.Add(new KeyValuePair<string, string>(GUI.Strings.Common.OneWeek, "1W"));
            ix.Add(new KeyValuePair<string, string>(GUI.Strings.Common.TwoWeeks, "2W"));
            ix.Add(new KeyValuePair<string, string>(GUI.Strings.Common.OneMonth, "1M"));
            CleanupDuration.SetIntervals(ix);
        }

        void IncrementalSettings_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (EnableCleanupDuration.Checked)
            {
                try
                {
                    if (Timeparser.ParseTimeSpan(CleanupDuration.Value).TotalDays < 1)
                    {
                        MessageBox.Show(this, Strings.CleanupSettings.TooShortCleanupDurationDay, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        args.Cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(GUI.Strings.Common.InvalidDuration, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            if (!m_wrapper.CleanupSettingsUI.HasWarnedClean && !(EnableCleanupDuration.Checked || EnableFullBackupClean.Checked))
            {
                if (MessageBox.Show(this, Strings.CleanupSettings.DisabledCleanupWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_wrapper.CleanupSettingsUI.HasWarnedClean = true;
            }

            m_wrapper.MaxFullBackups = EnableFullBackupClean.Checked ? (int)CleanFullBackupCount.Value : 0;
            m_wrapper.BackupExpireInterval = EnableCleanupDuration.Checked ? CleanupDuration.Value : "";
            m_wrapper.IgnoreFileTimestamps = IgnoreTimestamps.Checked;

            //Don't set args.NextPage, it runs on a list
        }


        void IncrementalSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            
            bool hasWarnedClean = m_wrapper.CleanupSettingsUI.HasWarnedClean;
            
            if (!m_valuesAutoLoaded)
            {
                if (m_wrapper.MaxFullBackups > 0)
                {
                    CleanFullBackupCount.Value = m_wrapper.MaxFullBackups;
                    EnableFullBackupClean.Checked = true;
                }
                else
                {
                    CleanFullBackupCount.Value = 4;
                    EnableFullBackupClean.Checked = false;
                }

                CleanupDuration.Value = m_wrapper.BackupExpireInterval;
                EnableCleanupDuration.Checked = !string.IsNullOrEmpty(m_wrapper.BackupExpireInterval);
                if (!EnableCleanupDuration.Checked)
                    CleanupDuration.Value = m_wrapper.FullBackupInterval;

                IgnoreTimestamps.Checked = m_wrapper.IgnoreFileTimestamps;
            }

            m_wrapper.CleanupSettingsUI.HasWarnedClean = hasWarnedClean;
        }

        private void EnableFullBackupClean_CheckedChanged(object sender, EventArgs e)
        {
            CleanFullBackupCount.Enabled = EnableFullBackupClean.Checked;
            ResetUserHasBeenWarned();
        }

        private void EnableCleanupDuration_CheckedChanged(object sender, EventArgs e)
        {
            CleanupDuration.Enabled = EnableCleanupDuration.Checked;
            ResetUserHasBeenWarned();
        }

        private void CleanFullBackupCount_ValueChanged(object sender, EventArgs e)
        {
            ResetUserHasBeenWarned();
        }

        private void CleanupDuration_ValueChanged(object sender, EventArgs e)
        {
            ResetUserHasBeenWarned();
        }

        private void ResetUserHasBeenWarned()
        {
            if (m_wrapper != null) m_wrapper.CleanupSettingsUI.HasWarnedClean = false;
        }
    }
}
