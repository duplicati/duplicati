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
    public partial class SelectWhen : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SelectWhen()
            : base(Strings.SelectWhen.PageTitle, Strings.SelectWhen.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SelectWhen_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectWhen_PageLeave);
        }

        void SelectWhen_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!m_wrapper.SelectWhenUI.HasWarnedNoSchedule && NoScheduleRadio.Checked)
            {
                if (MessageBox.Show(this, Strings.SelectWhen.NoScheduleWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_wrapper.SelectWhenUI.HasWarnedNoSchedule = true;
            }

            if (!m_wrapper.SelectWhenUI.HasWarnedNoIncrementals && !IncrementalPeriodRadio.Checked)
            {
                string s = NeverFullRadio.Checked ? Strings.SelectWhen.OnlyIncrementalBackupsWarning : Strings.SelectWhen.OnlyFullBackupsWarning;
                if (MessageBox.Show(this, s, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                {
                    args.Cancel = true;
                    return;
                }
                m_wrapper.SelectWhenUI.HasWarnedNoIncrementals = true;
            }

            DateTime scheduledTime = OffsetDate.Value.Date.Add(OffsetTime.Value.TimeOfDay);
            TimeSpan scheduleInterval = new TimeSpan(0);

            if (ScheduleRadio.Checked)
            {
                try
                {
                    scheduleInterval = Timeparser.ParseTimeSpan(RepeatInterval.Value);
                    if (scheduleInterval.TotalMinutes < 5)
                    {
                        MessageBox.Show(this, string.Format(Strings.SelectWhen.TooShortScheduleDurationError, 5), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        args.Cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Duplicati.GUI.Strings.Common.InvalidDuration, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            TimeSpan fullDuration = new TimeSpan(0);
            if (IncrementalPeriodRadio.Checked)
            {
                try
                {
                    fullDuration = Timeparser.ParseTimeSpan(FullDuration.Value);
                    if (fullDuration.TotalMinutes < 10)
                    {
                        MessageBox.Show(this, string.Format(Strings.SelectWhen.TooShortFullDurationError, 10), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        args.Cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Duplicati.GUI.Strings.Common.InvalidDuration, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            if (ScheduleRadio.Checked && IncrementalPeriodRadio.Checked)
            {
                if (fullDuration < scheduleInterval)
                {
                    MessageBox.Show(this, Strings.SelectWhen.FullDurationShorterThanScheduleDurationError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }

                if (!m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental && fullDuration.Ticks / scheduleInterval.Ticks > 100)
                {
                    if (MessageBox.Show(this, string.Format(Strings.SelectWhen.TooManyIncrementalsWarning,fullDuration.Ticks / scheduleInterval.Ticks), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }

                    m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = true;
                }
            }

            m_wrapper.BackupTimeOffset = scheduledTime;
            if (ScheduleRadio.Checked)
            {
                m_wrapper.RepeatInterval = RepeatInterval.Value;
                List<DayOfWeek> w = new List<DayOfWeek>();
                CheckBox[] chks = new CheckBox[] { Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday };
                for (int i = 0; i < chks.Length; i++)
                    if (chks[i].Checked)
                        w.Add((DayOfWeek)i);

                m_wrapper.AllowedWeekdays = w.ToArray();

                try
                {
                    Scheduler.GetNextValidTime(m_wrapper.BackupTimeOffset, m_wrapper.RepeatInterval, m_wrapper.AllowedWeekdays);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Duplicati.GUI.Strings.Common.InvalidDuration, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }

            }
            else
            {
                m_wrapper.RepeatInterval = "";
                m_wrapper.AllowedWeekdays = null;
            }

            if (IncrementalPeriodRadio.Checked)
                m_wrapper.FullBackupInterval = FullDuration.Value;
            else if (NeverFullRadio.Checked)
                m_wrapper.FullBackupInterval = "";
            else if (AlwaysFullRadio.Checked)
                m_wrapper.FullBackupInterval = "1s"; //TODO: Is this a good way to specify this?
            
            //Don't set args.NextPage, it runs on a list
        }

        void SelectWhen_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            bool hasWarnedNoSchedule = m_wrapper.SelectWhenUI.HasWarnedNoSchedule;
            bool HasWarnedTooManyIncremental = m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental;
            bool hasWarnedNoIncrementals = m_wrapper.SelectWhenUI.HasWarnedNoIncrementals;

            DayOfWeek d = System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat.FirstDayOfWeek;
            CheckBox[] chks = new CheckBox[] { Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday };
            Label[] lbls = new Label[] { Day1Label, Day2Label, Day3Label, Day4Label, Day5Label, Day6Label, Day7Label };

            int spacing = AllowedDaysPanel.Width / 7;
            int offset = spacing / 2;

            for (int i = 0; i < 7; i++)
            {
                int index = (((int)d) + i) % 7;
                chks[index].Left = offset + (spacing * i);
                lbls[index].Text = System.Globalization.CultureInfo.CurrentUICulture.DateTimeFormat.AbbreviatedDayNames[index];
                lbls[index].Left = chks[index].Left + (chks[index].Width / 2) - (lbls[index].Width / 2);
            }


            if (!m_valuesAutoLoaded)
            {
                OffsetDate.Value = m_wrapper.BackupTimeOffset;
                OffsetTime.Value = m_wrapper.BackupTimeOffset;
                ScheduleRadio.Checked = !string.IsNullOrEmpty(m_wrapper.RepeatInterval);
                if (string.IsNullOrEmpty(m_wrapper.RepeatInterval))
                    RepeatInterval.Value = "1D";
                else
                    RepeatInterval.Value = m_wrapper.RepeatInterval;
                NoScheduleRadio.Checked = ! ScheduleRadio.Checked;

                if (m_wrapper.AllowedWeekdays == null || m_wrapper.AllowedWeekdays.Length == 0)
                {
                    foreach (CheckBox c in chks)
                        c.Checked = true;
                }
                else
                {
                    foreach (CheckBox c in chks)
                        c.Checked = false;

                    foreach (DayOfWeek day in m_wrapper.AllowedWeekdays)
                        chks[(int)day].Checked = true;
                }

                if (string.IsNullOrEmpty(m_wrapper.FullBackupInterval))
                {
                    NeverFullRadio.Checked = true;
                    FullDuration.Value = "1M";
                }
                else if (m_wrapper.FullBackupInterval.Equals("1s")) //TODO: Is this the best way?
                {
                    AlwaysFullRadio.Checked = true;
                    FullDuration.Value = "1M";
                }
                else
                {
                    IncrementalPeriodRadio.Checked = true;
                    FullDuration.Value = m_wrapper.FullBackupInterval;
                }
            }

            m_wrapper.SelectWhenUI.HasWarnedNoSchedule = hasWarnedNoSchedule;
            m_wrapper.SelectWhenUI.HasWarnedNoIncrementals = hasWarnedNoIncrementals;
            m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = HasWarnedTooManyIncremental;
        }

        private void ScheduleRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
            {
                m_wrapper.SelectWhenUI.HasWarnedNoSchedule = false;
                m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = false;
            }

            ScheduleGroup.Enabled = ScheduleRadio.Checked;
        }

        private void NoScheduleRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
                m_wrapper.SelectWhenUI.HasWarnedNoSchedule = false;
        }

        private void IncrementalPeriodRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
            {
                m_wrapper.SelectWhenUI.HasWarnedNoIncrementals = false;
                m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = false;
            }

            FullDuration.Enabled = IncrementalPeriodRadio.Checked;
        }

        private void NeverFullRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
            {
                m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = false;
                m_wrapper.SelectWhenUI.HasWarnedNoIncrementals = false;
            }
        }

        private void AlwaysFullRadio_CheckedChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
                m_wrapper.SelectWhenUI.HasWarnedNoIncrementals = false;
        }

        private void RepeatInterval_ValueChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
                m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = false;
        }

        private void AllowedDay_CheckedChanged(object sender, EventArgs e)
        {
            if (m_wrapper != null)
                m_wrapper.SelectWhenUI.HasWarnedTooManyIncremental = false;
        }
    }
}
