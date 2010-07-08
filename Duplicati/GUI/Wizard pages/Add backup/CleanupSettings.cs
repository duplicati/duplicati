#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
using Duplicati.Library.Core;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class CleanupSettings : WizardControl
    {
        private bool m_warnedClean = false;

        private WizardSettingsWrapper m_wrapper;

        public CleanupSettings()
            : base(Strings.IncrementalSettings.PageTitle, Strings.IncrementalSettings.PageDescription)
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
            m_settings["Incremental:WarnedClean"] = m_warnedClean;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (EnableCleanupDuration.Checked)
            {
                try
                {
                    if (Timeparser.ParseTimeSpan(CleanupDuration.Value).TotalDays < 1)
                    {
                        MessageBox.Show(this, Strings.IncrementalSettings.TooShortCleanupDurationDay , Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            if (!m_warnedClean && !(EnableCleanupDuration.Checked || EnableFullBackupClean.Checked))
            {
                if (MessageBox.Show(this, Strings.IncrementalSettings.DisabledCleanupWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_warnedClean = true;
            }

            m_settings["Incremental:WarnedClean"] = m_warnedClean;

            m_wrapper.MaxFullBackups = EnableFullBackupClean.Checked ? (int)CleanFullBackupCount.Value : 0;
            m_wrapper.BackupExpireInterval = EnableCleanupDuration.Checked ? CleanupDuration.Value : "";
            m_wrapper.IgnoreFileTimestamps = IgnoreTimestamps.Checked;

            //Don't set args.NextPage, it runs on a list
        }


        void IncrementalSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_valuesAutoLoaded)
            {
                if (m_wrapper.MaxFullBackups > 0)
                {
                    CleanFullBackupCount.Value = m_wrapper.MaxFullBackups;
                    EnableFullBackupClean.Checked = true;
                }
                else
                {
                    CleanFullBackupCount.Value = 0;
                    EnableFullBackupClean.Checked = false;
                }

                CleanupDuration.Value = m_wrapper.BackupExpireInterval;
                EnableCleanupDuration.Checked = !string.IsNullOrEmpty(m_wrapper.BackupExpireInterval);
                if (!EnableCleanupDuration.Checked)
                    CleanupDuration.Value = m_wrapper.FullBackupInterval;

                IgnoreTimestamps.Checked = m_wrapper.IgnoreFileTimestamps;
            }

            if (m_settings.ContainsKey("Incremental:WarnedClean"))
                m_warnedClean = (bool)m_settings["Incremental:WarnedClean"];
        }

        private void EnableFullBackupClean_CheckedChanged(object sender, EventArgs e)
        {
            CleanFullBackupCount.Enabled = EnableFullBackupClean.Checked;
            m_warnedClean = false;
        }

        private void EnableCleanupDuration_CheckedChanged(object sender, EventArgs e)
        {
            CleanupDuration.Enabled = EnableCleanupDuration.Checked;
            m_warnedClean = false;
        }

        private void CleanFullBackupCount_ValueChanged(object sender, EventArgs e)
        {
            m_warnedClean = false; 
        }

        private void CleanupDuration_ValueChanged(object sender, EventArgs e)
        {
            m_warnedClean = false;
        }

    }
}
