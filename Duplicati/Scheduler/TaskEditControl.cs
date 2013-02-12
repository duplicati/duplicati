#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
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
using System.Windows.Forms;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// Edit a task for the task scheduler
    /// </summary>
    /// <remarks>
    /// Actually edit a trigger: 
    /// Each Job gets a Task Scheduler Trigger that is stored in the Task Scheduler database.
    /// It can be retrieved using: TaskScheduler.GetTrigger(JobRow.TaskName);
    /// The TaskName is: "DUP." + aName + '.' + UserName.Replace('\\', '.')
    /// If this is a new job, call SetTrigger with null and the user can make a new one.
    /// </remarks>
    public partial class TaskEditControl : UserControl
    {
        /// <summary>
        /// Edit a task for the task scheduler
        /// </summary>
        public TaskEditControl()
        {
            InitializeComponent();
            if (this.DesignMode) return;
            this.WhichComboBox.SelectedIndex = 0;  // Can't select default in designer FSR
            AbleMonthlyOrGroupBox(false);
            this.OnceMonthCalendar.MinDate = DateTime.Now.Date;
            this.MonthlyDaysPicker.DaysPicked = new int[] { 1 };
            this.OnceMonthCalendar.MaxDate = this.OnceMonthCalendar.MinDate.AddYears(1); // That's enuf
            foreach (TabPage P in this.PeriodTabControl.TabPages)
                P.BackColor = this.BackColor;
        }
        /// <summary>
        /// Retrieves the edited task scheduler trigger
        /// </summary>
        /// <returns></returns>
        public Microsoft.Win32.TaskScheduler.Trigger GetTrigger()
        {
            Microsoft.Win32.TaskScheduler.Trigger Result = null;
            DateTime StartBoundary = this.BeginDateTimePicker.Value;
            // The trigger type depends on the selected tab
            if (this.PeriodTabControl.SelectedTab.Equals(this.TabOnce))
            {
                Result = new Microsoft.Win32.TaskScheduler.TimeTrigger();
                StartBoundary = this.OnceMonthCalendar.SelectionStart.Date + this.BeginDateTimePicker.Value.TimeOfDay;
            }
            else if (this.PeriodTabControl.SelectedTab.Equals(this.TabDaily))
                Result = new Microsoft.Win32.TaskScheduler.DailyTrigger((short)this.DailyNumericUpDown.Value);
            else if (this.PeriodTabControl.SelectedTab.Equals(this.TabWeekly))
                Result = new Microsoft.Win32.TaskScheduler.WeeklyTrigger(GetDayOfWeek(this.WeeklyGroupBox), (short)this.WeeksNumericUpDown.Value);
            else if (this.PeriodTabControl.SelectedTab.Equals(this.TabMonthly) && this.MonthlyOrCheckBox.Checked)
                Result = new Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger(GetDayOfWeek(this.DaysGroupBox1), Microsoft.Win32.TaskScheduler.MonthsOfTheYear.AllMonths, GetWhich());
            else if (this.PeriodTabControl.SelectedTab.Equals(this.TabMonthly))
                Result = new Microsoft.Win32.TaskScheduler.MonthlyTrigger(1, Microsoft.Win32.TaskScheduler.MonthsOfTheYear.AllMonths) { DaysOfMonth = this.MonthlyDaysPicker.DaysPicked1IfEmpty, };
            else
                return null;

            Result.Enabled = this.EnableCheckBox.Checked;
            Result.StartBoundary = StartBoundary;

            return Result;
        }
        /// <summary>
        /// Convert a Trigger to XML
        /// </summary>
        /// <param name="aTrigger">Trigger to convert</param>
        /// <returns>XML text</returns>
        public static string TriggerToXml( Microsoft.Win32.TaskScheduler.Trigger aTrigger )
        {
            if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyTrigger)
            {
                Microsoft.Win32.TaskScheduler.MonthlyTrigger t = (Microsoft.Win32.TaskScheduler.MonthlyTrigger)aTrigger;
                return new System.Xml.Linq.XElement("Trigger",
                    new System.Xml.Linq.XAttribute("Type", "MonthlyTrigger"),
                    new System.Xml.Linq.XElement("Enabled", t.Enabled),
                    new System.Xml.Linq.XElement("StartBoundary", t.StartBoundary.ToString("o")),
                    new System.Xml.Linq.XElement("DayOfMonth", 1),
                    new System.Xml.Linq.XElement("MonthsOfYear", t.MonthsOfYear)
                    ).ToString();
            }
            if (aTrigger is Microsoft.Win32.TaskScheduler.TimeTrigger)
            {
                return new System.Xml.Linq.XElement("Trigger",
                    new System.Xml.Linq.XAttribute("Type", "TimeTrigger"),
                    new System.Xml.Linq.XElement("StartBoundary", 
                        ((Microsoft.Win32.TaskScheduler.TimeTrigger)aTrigger).StartBoundary.ToString("o"))).ToString();
            }
            if (aTrigger is Microsoft.Win32.TaskScheduler.DailyTrigger)
            {
                Microsoft.Win32.TaskScheduler.DailyTrigger t = (Microsoft.Win32.TaskScheduler.DailyTrigger)aTrigger;
                return new System.Xml.Linq.XElement("Trigger",
                    new System.Xml.Linq.XAttribute("Type", "DailyTrigger"),
                    new System.Xml.Linq.XElement("Enabled", t.Enabled),
                    new System.Xml.Linq.XElement("StartBoundary", t.StartBoundary.ToString("o")),
                    new System.Xml.Linq.XElement("DaysInterval", t.DaysInterval)).ToString();
            }
            if (aTrigger is Microsoft.Win32.TaskScheduler.WeeklyTrigger)
            {
                Microsoft.Win32.TaskScheduler.WeeklyTrigger t = (Microsoft.Win32.TaskScheduler.WeeklyTrigger)aTrigger;
                return new System.Xml.Linq.XElement("Trigger",
                    new System.Xml.Linq.XAttribute("Type", "WeeklyTrigger"),
                    new System.Xml.Linq.XElement("Enabled", t.Enabled),
                    new System.Xml.Linq.XElement("StartBoundary", t.StartBoundary.ToString("o")),
                    new System.Xml.Linq.XElement("DaysOfWeek", t.DaysOfWeek),
                    new System.Xml.Linq.XElement("WeeksInterval", t.WeeksInterval)
                    ).ToString();
            }
            if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger)
            {
                Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger t = (Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger)aTrigger;
                return new System.Xml.Linq.XElement("Trigger",
                    new System.Xml.Linq.XAttribute("Type", "MonthlyDOWTrigger"),
                    new System.Xml.Linq.XElement("Enabled", t.Enabled),
                    new System.Xml.Linq.XElement("StartBoundary", t.StartBoundary.ToString("o")),
                    new System.Xml.Linq.XElement("DaysOfWeek", t.DaysOfWeek),
                    new System.Xml.Linq.XElement("MonthsOfYear", t.MonthsOfYear),
                    new System.Xml.Linq.XElement("WeeksOfMonth", t.WeeksOfMonth)
                    ).ToString();
            }
            if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyTrigger)
            {
                Microsoft.Win32.TaskScheduler.MonthlyTrigger t = (Microsoft.Win32.TaskScheduler.MonthlyTrigger)aTrigger;
                return new System.Xml.Linq.XElement("Trigger",
                    new System.Xml.Linq.XAttribute("Type", "MonthlyTrigger"),
                    new System.Xml.Linq.XElement("Enabled", t.Enabled),
                    new System.Xml.Linq.XElement("StartBoundary", t.StartBoundary.ToString("o")),
                    new System.Xml.Linq.XElement("DayOfMonth", 1),
                    new System.Xml.Linq.XElement("MonthsOfYear", t.MonthsOfYear)
                    ).ToString();
            }
            return string.Empty;
        }
        /// <summary>
        /// Sets the trigger to edit
        /// </summary>
        /// <param name="aTrigger">Trigger to edit or null for a new one</param>
        public void SetTrigger(Microsoft.Win32.TaskScheduler.Trigger aTrigger)
        {
            if (aTrigger == null) // Ahh, Make a new one, default to monthly
            {
                this.EnableCheckBox.Checked = true;
                DateTime Now = DateTime.Now.AddMinutes(15);
                this.OnceMonthCalendar.MinDate = Now.Date;
                this.OnceMonthCalendar.SelectionStart = this.BeginDateTimePicker.Value = Now.Date.AddHours(Now.Hour + 1); // Set to next hour...
                this.PeriodTabControl.SelectedTab = this.TabMonthly;
                this.MonthlyOrCheckBox.Checked = false;
                this.MonthlyDaysPicker.DaysPicked = new int[] { 1 };
                return;
            }
            // Set the forms based on the type of trigger
            this.EnableCheckBox.Checked = aTrigger.Enabled;
            this.BeginDateTimePicker.Value = aTrigger.StartBoundary;
            if (aTrigger is Microsoft.Win32.TaskScheduler.TimeTrigger)
            {
                this.PeriodTabControl.SelectedTab = this.TabOnce;
            }
            else if (aTrigger is Microsoft.Win32.TaskScheduler.DailyTrigger)
            {
                this.PeriodTabControl.SelectedTab = this.TabDaily;
                Microsoft.Win32.TaskScheduler.DailyTrigger dt = (Microsoft.Win32.TaskScheduler.DailyTrigger)aTrigger;
                this.DailyNumericUpDown.Value = (decimal)dt.DaysInterval;
            }
            else if (aTrigger is Microsoft.Win32.TaskScheduler.WeeklyTrigger)
            {
                this.PeriodTabControl.SelectedTab = this.TabWeekly;
                Microsoft.Win32.TaskScheduler.WeeklyTrigger wt = (Microsoft.Win32.TaskScheduler.WeeklyTrigger)aTrigger;
                SetDayOfWeek(this.WeeklyGroupBox, wt.DaysOfWeek);
                this.WeeksNumericUpDown.Value = (decimal)wt.WeeksInterval;
            }
            else if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyTrigger)
            {
                this.PeriodTabControl.SelectedTab = this.TabMonthly;
                this.MonthlyOrCheckBox.Checked = false;
                Microsoft.Win32.TaskScheduler.MonthlyTrigger mt = (Microsoft.Win32.TaskScheduler.MonthlyTrigger)aTrigger;
                this.MonthlyDaysPicker.DaysPicked = mt.DaysOfMonth;
            }
            else if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger)
            {
                this.PeriodTabControl.SelectedTab = this.TabMonthly;
                this.MonthlyOrCheckBox.Checked = true;
                Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger mt = (Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger)aTrigger;
                SetWhich(mt.WeeksOfMonth);
            }
            PeriodTabControl_SelectedIndexChanged(null, null); // In case it didnt change...
        }
        /// <summary>
        /// Converts a short day name to a Task Scheduler DaysOfWeek
        /// </summary>
        /// <param name="aShortDay">a short day name, i.e. Mon</param>
        /// <returns>TaskScheduler.DaysOfTheWeek</returns>
        private Microsoft.Win32.TaskScheduler.DaysOfTheWeek ToDaysOfWeek(string aShortDay)
        {
            return (Microsoft.Win32.TaskScheduler.DaysOfTheWeek)
                (1 << Array.IndexOf(System.Globalization.DateTimeFormatInfo.InvariantInfo.AbbreviatedDayNames, aShortDay));
        }
        /// <summary>
        /// Get day from GroupBox
        /// </summary>
        /// <param name="aDOWBox">Box to fetch from</param>
        /// <returns>TaskScheduler.DaysOfTheWeek</returns>
        private Microsoft.Win32.TaskScheduler.DaysOfTheWeek GetDayOfWeek(GroupBox aDOWBox)
        {
            short Result = 0;
            foreach (Control C in aDOWBox.Controls)
                if (C is CheckBox && ((CheckBox)C).Checked) 
                    Result |= (short)ToDaysOfWeek(C.Text);
            return (Microsoft.Win32.TaskScheduler.DaysOfTheWeek)Result;
        }
        /// <summary>
        /// Set day in groupbox to TaskScheduler.DaysOfTheWeek
        /// </summary>
        /// <param name="aDOWBox">Days group box</param>
        /// <param name="aDOW">TaskScheduler.DaysOfTheWeek</param>
        private void SetDayOfWeek(GroupBox aDOWBox, Microsoft.Win32.TaskScheduler.DaysOfTheWeek aDOW)
        {
            foreach (Control C in aDOWBox.Controls)
                ((CheckBox)C).Checked = (aDOW & ToDaysOfWeek( C.Text )) != 0;
        }
        /// <summary>
        /// Get TaskScheduler.WhichWeek from ComboBox
        /// </summary>
        /// <returns>TaskScheduler.WhichWeek</returns>
        private Microsoft.Win32.TaskScheduler.WhichWeek GetWhich()
        {
            return (Microsoft.Win32.TaskScheduler.WhichWeek)System.Enum.Parse(typeof(Microsoft.Win32.TaskScheduler.WhichWeek), 
                (string)this.WhichComboBox.SelectedItem+"Week");
        }
        /// <summary>
        /// Sets ComboBox to TaskScheduler.WhichWeek
        /// </summary>
        /// <param name="aWitch">TaskScheduler.WhichWeek</param>
        private void SetWhich(Microsoft.Win32.TaskScheduler.WhichWeek aWitch)
        {
            this.WhichComboBox.SelectedItem = aWitch.ToString().Replace("Week", string.Empty);
        }
        /// <summary>
        /// User checked 'which' box, 
        /// </summary>
        private void MonthlyOrCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            AbleMonthlyOrGroupBox(this.MonthlyOrCheckBox.Checked);
            this.MonthlyDaysPicker.Enabled = !this.MonthlyOrCheckBox.Checked;
        }
        /// <summary>
        /// Go through all this to keep the checkbox enabled
        /// </summary>
        private void MonthlyOrGroupBox_EnabledChanged(object sender, EventArgs e)
        {
            if (!this.MonthlyOrGroupBox.Enabled)
                AbleMonthlyOrGroupBox(false);
        }
        /// <summary>
        /// Go through all this to keep the checkbox enabled
        /// </summary>
        private void AbleMonthlyOrGroupBox(bool aEnable)
        {
            this.MonthlyOrGroupBox.Enabled = true;
            foreach (Control C in this.MonthlyOrGroupBox.Controls)
                if (!C.Equals(this.MonthlyOrCheckBox)) C.Enabled = aEnable;
        }
        /// <summary>
        /// Keep calendar in the future
        /// </summary>
        private void OnceMonthCalendar_DateChanged(object sender, DateRangeEventArgs e)
        {
            if(this.OnceMonthCalendar.SelectionStart.Date < DateTime.Now.Date)
                this.OnceMonthCalendar.SelectionStart = DateTime.Now.Date;
            this.OnceMonthCalendar.MinDate = DateTime.Now.Date;
            this.BeginDateTimePicker.Value = this.OnceMonthCalendar.SelectionStart.Date + this.BeginDateTimePicker.Value.TimeOfDay;
        }
        /// <summary>
        /// Checks/Unchecks the tab control's tab images
        /// </summary>
        private void PeriodTabControl_SelectedIndexChanged(object sender, EventArgs e)
        {
            foreach(TabPage P in this.PeriodTabControl.TabPages)
                P.ImageIndex = P.Equals(this.PeriodTabControl.SelectedTab) ? 1 : 0;
        }
    }
}