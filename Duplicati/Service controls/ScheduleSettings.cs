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
using Duplicati.Datamodel;

namespace Duplicati.Service_controls
{
    public partial class ScheduleSettings : UserControl
    {
        private Schedule m_schedule;
        private bool m_isUpdating = false;

        public ScheduleSettings()
        {
            InitializeComponent();
        }

        public void Setup(Schedule schedule)
        {
            try
            {
                m_isUpdating = true;
                WeekdayPicker.BeginUpdate();
                m_schedule = schedule;
                OffsetDate.Value = m_schedule.When;
                OffsetTime.Value = m_schedule.When;

                RepeatCheck.Checked = !string.IsNullOrEmpty(m_schedule.Repeat);
                RepeatInterval.Text = m_schedule.Repeat;

                for (int i = 0; i < WeekdayPicker.Items.Count; i++)
                    WeekdayPicker.SetItemChecked(i, false);

                foreach (string s in m_schedule.Weekdays.Split(','))
                    switch (s.ToLower().Trim())
                    {
                        case "mon":
                            WeekdayPicker.SetItemChecked(4, true);
                            break;
                        case "tue":
                            WeekdayPicker.SetItemChecked(5, true);
                            break;
                        case "wed":
                            WeekdayPicker.SetItemChecked(6, true);
                            break;
                        case "thu":
                            WeekdayPicker.SetItemChecked(7, true);
                            break;
                        case "fri":
                            WeekdayPicker.SetItemChecked(8, true);
                            break;
                        case "sat":
                            WeekdayPicker.SetItemChecked(9, true);
                            break;
                        case "sun":
                            WeekdayPicker.SetItemChecked(3, true);
                            break;
                    }

                UpdateCompositeDays();

                KeepNFullCheckbox.Checked = m_schedule.KeepFull > 0;
                CleanupFullCount.Value = Math.Max(Math.Min(m_schedule.KeepFull, CleanupFullCount.Maximum), CleanupFullCount.Minimum);

                KeepIntervalCheckbox.Checked = !string.IsNullOrEmpty(m_schedule.KeepTime);
                CleanupDuration.Text = m_schedule.KeepTime;

                ForceFullBackup.Checked = !string.IsNullOrEmpty(m_schedule.FullAfter);
                ForceFullBackupTimespan.Text = m_schedule.FullAfter;
            }
            finally
            {
                WeekdayPicker.EndUpdate();
                m_isUpdating = false;
            }
        }

        private void UpdateCompositeDays()
        {
            WeekdayPicker.SetItemChecked(2, WeekdayPicker.GetItemChecked(3) && WeekdayPicker.GetItemChecked(9));

            bool allChecked = true;
            for (int i = 4; i < 8 && allChecked; i++)
                allChecked &= WeekdayPicker.GetItemChecked(i);

            WeekdayPicker.SetItemChecked(1, allChecked);

            WeekdayPicker.SetItemChecked(0, WeekdayPicker.CheckedItems.Count == WeekdayPicker.Items.Count - 1);
        }

        private bool Interval_TextChanged(object sender, EventArgs e)
        {
            try
            {
                TimeSpan ts = Timeparser.ParseTimeSpan((sender as TextBox).Text);
                if (ts.TotalMinutes < 5)
                    throw new Exception("The given interval is less than five minutes. This will lead to very bad performance.");
            }
            catch (Exception ex)
            {
                errorProvider.SetError(sender as TextBox, ex.Message);
                return false;
            }

            errorProvider.SetError((sender as TextBox), "");
            return true;
        }

        private void OffsetDate_ValueChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            m_schedule.When = OffsetDate.Value.Date + m_schedule.When.TimeOfDay;
        }

        private void OffsetTime_ValueChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            m_schedule.When = m_schedule.When.Date.Add(OffsetTime.Value.TimeOfDay);
        }

        private void RepeatInterval_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            RepeatCheck.Checked = (RepeatInterval.Text.Trim().Length > 0);
            if (Interval_TextChanged(sender, e))
                m_schedule.Repeat = RepeatInterval.Text;
            
        }

        private void WeekdayPicker_ItemCheck(object sender, ItemCheckEventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            try
            {
                m_isUpdating = true;
                WeekdayPicker.BeginUpdate();
                if (e.Index == 0) //All days
                    for (int i = 1; i < WeekdayPicker.Items.Count; i++)
                        WeekdayPicker.SetItemChecked(i, e.NewValue == CheckState.Checked);
                else if (e.Index == 1) //Weekdays
                    for (int i = 4; i < 9; i++)
                        WeekdayPicker.SetItemChecked(i, e.NewValue == CheckState.Checked);
                else if (e.Index == 2) //Weekends
                {
                    WeekdayPicker.SetItemChecked(3, e.NewValue == CheckState.Checked);
                    WeekdayPicker.SetItemChecked(9, e.NewValue == CheckState.Checked);
                }

                WeekdayPicker.SetItemChecked(e.Index, e.NewValue == CheckState.Checked);

                UpdateCompositeDays();

            }
            finally
            {
                m_isUpdating = false;
                WeekdayPicker.EndUpdate();
            }
        }

        private void CleanupDuration_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            KeepIntervalCheckbox.Checked = (CleanupDuration.Text.Trim().Length > 0);
            if (Interval_TextChanged(sender, e))
                m_schedule.KeepTime = CleanupDuration.Text;

        }

        private void ForceFullBackupTimespan_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            ForceFullBackup.Checked = (ForceFullBackupTimespan.Text.Trim().Length > 0);
            if (Interval_TextChanged(sender, e))
                m_schedule.FullAfter = ForceFullBackupTimespan.Text;

        }

        private void CleanupFullCount_ValueChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_schedule == null)
                return;

            m_schedule.KeepFull = Convert.ToInt32(CleanupFullCount.Value);
            KeepNFullCheckbox.Checked = m_schedule.KeepFull > 0;
        }
    }
}
