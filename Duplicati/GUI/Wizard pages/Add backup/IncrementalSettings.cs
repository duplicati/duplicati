#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
    public partial class IncrementalSettings : WizardControl
    {
        private bool m_warnedFull = false;
        private bool m_warnedClean = false;
        
        private Schedule m_schedule;

        public IncrementalSettings()
            : base("Select incremental options", "To avoid large backups, Duplicati can back up only files that have changed. Each backup is much smaller, but all files are still avalible.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(IncrementalSettings_PageEnter);
            base.PageLeave += new PageChangeHandler(IncrementalSettings_PageLeave);

            List<KeyValuePair<string, string>> ix = new List<KeyValuePair<string, string>>();
            ix.Add(new KeyValuePair<string, string>("One day", "1D"));
            ix.Add(new KeyValuePair<string, string>("One week", "1W"));
            ix.Add(new KeyValuePair<string, string>("Two weeks", "2W"));
            ix.Add(new KeyValuePair<string, string>("One month", "1M"));
            CleanupDuration.SetIntervals(ix);
        }

        void IncrementalSettings_PageLeave(object sender, PageChangedArgs args)
        {

            SaveSettings();

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (FullBackups.Checked)
            {
                try
                {
                    if (Timeparser.ParseTimeSpan(FullDuration.Value).TotalMinutes < 10)
                    {
                        MessageBox.Show(this, "The duration entered is less than ten minutes. This will give very poor system performance.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        args.Cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The full backup duration entered is not valid: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            if (EnableCleanupDuration.Checked)
            {
                try
                {
                    if (Timeparser.ParseTimeSpan(CleanupDuration.Value).TotalMinutes < 10)
                    {
                        MessageBox.Show(this, "The cleanup duration entered is less than ten minutes. This will give very poor system performance.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        args.Cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The cleanup duration entered is not valid: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            if (!m_warnedFull && !FullBackups.Checked)
            {
                if (MessageBox.Show(this, "You have disabled full backups. Incremental backups are faster, but rely on the presence of a full backup.\nDisabling full backups may result in a very lengthy restoration process, and may cause a restore to fault.\nDo you want to continue without full backups?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_warnedFull = true;
            }

            if (!m_warnedClean && !(EnableCleanupDuration.Checked || EnableFullBackupClean.Checked))
            {
                if (MessageBox.Show(this, "You have disabled full backups. Incremental backups are faster, but rely on the presence of a full backup.\nDisabling full backups may result in a very lengthy restore process, and may cause a restore to fault.\nDo you want to continue without full backups?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_warnedClean = true;
            }

            SaveSettings();

            if ((bool)m_settings["Advanced:Throttle"])
                args.NextPage = new Wizard_pages.Add_backup.ThrottleOptions();
            else if ((bool)m_settings["Advanced:Filters"])
                args.NextPage = new Wizard_pages.Add_backup.FilterEditor();
            else
                args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        private void SaveSettings()
        {
            if (FullBackups.Checked)
                m_schedule.FullAfter = FullDuration.Value;
            else
                m_schedule.FullAfter = null;

            if (EnableFullBackupClean.Checked)
                m_schedule.KeepFull = (int)CleanFullBackupCount.Value;
            else
                m_schedule.KeepFull = 0;

            if (EnableCleanupDuration.Checked)
                m_schedule.KeepTime = CleanupDuration.Value;
            else
                m_schedule.KeepTime = null;

            m_settings["Incremental:WarnedFull"] = m_warnedFull;
            m_settings["Incremental:WarnedClean"] = m_warnedClean;
        }

        void IncrementalSettings_PageEnter(object sender, PageChangedArgs args)
        {
            m_schedule = (Schedule)m_settings["Schedule"];

            if (!m_valuesAutoLoaded)
            {

                FullDuration.Value = m_schedule.FullAfter;
                FullBackups.Checked = !string.IsNullOrEmpty(m_schedule.FullAfter);
                if (m_schedule.KeepFull > 0)
                {
                    CleanFullBackupCount.Value = m_schedule.KeepFull;
                    EnableFullBackupClean.Checked = true;
                }
                else
                {
                    CleanFullBackupCount.Value = 0;
                    EnableFullBackupClean.Checked = false;
                }

                CleanupDuration.Value = m_schedule.KeepTime;
                EnableCleanupDuration.Checked = !string.IsNullOrEmpty(m_schedule.KeepTime);
            }

            if (m_settings.ContainsKey("Incremental:WarnedFull"))
                m_warnedFull = (bool)m_settings["Incremental:WarnedFull"];
            if (m_settings.ContainsKey("Incremental:WarnedClean"))
                m_warnedClean = (bool)m_settings["Incremental:WarnedClean"];
        }

        private void FullBackups_CheckedChanged(object sender, EventArgs e)
        {
            FullSettings.Enabled = FullBackups.Checked;
            m_warnedFull = false;
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

        private void FullDuration_ValueChanged(object sender, EventArgs e)
        {
            m_warnedFull = false;
        }
    }
}
