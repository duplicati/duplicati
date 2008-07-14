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

namespace Duplicati.Wizard_pages.Add_backup
{
    public partial class IncrementalSettings : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        private bool m_warnedFull = false;
        private bool m_warnedClean = false;
        
        private Schedule m_schedule;

        public IncrementalSettings()
        {
            InitializeComponent();
        }

        public void Setup(Schedule schedule)
        {
            m_schedule = schedule;
            if (m_schedule != null && !m_schedule.RelationManager.ExistsInDb(m_schedule))
            {
                m_schedule.Repeat = "1M";
                m_schedule.KeepFull = 4;
                m_schedule.KeepTime = "";
            }
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Select incremental options"; }
        }

        string IWizardControl.HelpText
        {
            get { return "To avoid large backups, Duplicati can back up only files that have changed. Each backup is much smaller, but all files are still avalible."; }
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
            if (m_schedule != null)
            {
                FullDuration.Text = m_schedule.FullAfter;
                FullBackups.Enabled = !string.IsNullOrEmpty(m_schedule.Repeat);
                CleanFullBackupCount.Value = m_schedule.KeepFull;
                EnableFullBackupClean.Checked = m_schedule.KeepFull > 0;
                CleanupDuration.Text = m_schedule.KeepTime;
                EnableCleanupDuration.Checked = !string.IsNullOrEmpty(m_schedule.KeepTime);
            }
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (FullBackups.Checked)
            {
                try
                {
                    if (Timeparser.ParseTimeSpan(FullDuration.Text).TotalMinutes < 10)
                    {
                        MessageBox.Show(this, "The duration entered is less than ten minutes. This will give very poor system performance.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        cancel = true;
                        return;
                    }
                } 
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The full backup duration entered is not valid: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    cancel = true;
                    return;
                }
            }

            if (EnableCleanupDuration.Checked)
            {
                try
                {
                    if (Timeparser.ParseTimeSpan(CleanupDuration.Text).TotalMinutes < 10)
                    {
                        MessageBox.Show(this, "The cleanup duration entered is less than ten minutes. This will give very poor system performance.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The cleanup duration entered is not valid: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    cancel = true;
                    return;
                }
            }

            if (!m_warnedFull && !FullBackups.Checked)
            {
                if (MessageBox.Show(this, "You have disabled full backups. Incremental backups are faster, but rely on the presence of a full backup.\nDisabling full backups may result in a very lengthy restoration process, and may cause a restore to fault.\nDo you want to continue without full backups?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    cancel = true;
                    return;
                }
                m_warnedFull = true;
            }

            if (!m_warnedClean && !(EnableCleanupDuration.Checked || EnableFullBackupClean.Checked))
            {
                if (MessageBox.Show(this, "You have disabled full backups. Incremental backups are faster, but rely on the presence of a full backup.\nDisabling full backups may result in a very lengthy restore process, and may cause a restore to fault.\nDo you want to continue without full backups?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                {
                    cancel = true;
                    return;
                }
                m_warnedClean = true;
            }

            if (m_schedule != null)
            {
                if (FullBackups.Checked)
                    m_schedule.FullAfter = FullDuration.Text;
                else
                    m_schedule.FullAfter = null;

                if (EnableFullBackupClean.Checked)
                    m_schedule.KeepFull = (int)CleanFullBackupCount.Value;
                else
                    m_schedule.KeepFull = 0;

                if (EnableCleanupDuration.Checked)
                    m_schedule.KeepTime = CleanupDuration.Text;
                else
                    m_schedule.KeepTime = null;
            }

        }


        #endregion

        private void FullBackups_CheckedChanged(object sender, EventArgs e)
        {
            FullSettings.Enabled = FullBackups.Checked;
            m_warnedFull = false;
        }

        private void EnableFullBackupClean_CheckedChanged(object sender, EventArgs e)
        {
            CleanFullBackupCount.Enabled = CleanFullBackupHelptext.Enabled = EnableFullBackupClean.Checked;
            m_warnedClean = false;
        }

        private void EnableCleanupDuration_CheckedChanged(object sender, EventArgs e)
        {
            CleanupDuration.Enabled = CleanupDurationHelptext.Enabled = EnableCleanupDuration.Checked;
            m_warnedClean = false;
        }

        private void FullDuration_TextChanged(object sender, EventArgs e)
        {
            m_warnedFull = false;
        }

        private void CleanFullBackupCount_ValueChanged(object sender, EventArgs e)
        {
            m_warnedClean = false; 
        }

        private void CleanupDuration_TextChanged(object sender, EventArgs e)
        {
            m_warnedClean = false;
        }
    }
}
