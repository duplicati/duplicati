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
    public partial class SelectWhen : WizardControl
    {
        private bool m_hasWarned;
        private Schedule m_schedule;

        public SelectWhen()
            : base("Select when the backup should run", "On this page you may set up when the backup is run. Automatically repeating the backup ensure that you have a backup, without requiring any action from you.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SelectWhen_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectWhen_PageLeave);
        }

        void SelectWhen_PageLeave(object sender, PageChangedArgs args)
        {
            m_schedule.When = OffsetDate.Value.Date.Add(OffsetTime.Value.TimeOfDay);
            if (EnableRepeat.Checked)
                m_schedule.Repeat = RepeatInterval.Value;
            else
                m_schedule.Repeat = null;

            m_settings["When:HasWarned"] = m_hasWarned;

            if (args.Direction == PageChangedDirection.Back)
                return;
            
            if (!m_hasWarned && !EnableRepeat.Checked)
            {
                DateTime newtime = OffsetDate.Value.Date.Add(OffsetTime.Value.TimeOfDay);
                string b = null;
                if (DateTime.Now > newtime)
                    b = "The time you entered is int the past.";
                else if (DateTime.Now > newtime.AddMinutes(10))
                    b = "The time you entered will occur shortly.";

                if (b != null)
                {
                    if (MessageBox.Show(this, b + " You have no repetition set, so the backup will never run.\nThis is fine if you only want to run the backup manually, but it is not reccomended.\nDo you want to continue?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }
                    m_hasWarned = true;
                }
            }

            if (EnableRepeat.Checked)
            {
                try
                {
                    TimeSpan sp = Timeparser.ParseTimeSpan(RepeatInterval.Value);
                    if (sp.TotalMinutes < 5)
                    {
                        MessageBox.Show(this, "The duration entered is less than five minutes. That is not acceptable.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        args.Cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The duration entered is not valid.\nError message: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    args.Cancel = true;
                    return;
                }
            }

            m_settings["When:HasWarned"] = m_hasWarned;

            if ((bool)m_settings["Advanced:Incremental"])
                args.NextPage = new Wizard_pages.Add_backup.IncrementalSettings();
            else if ((bool)m_settings["Advanced:Throttle"])
                args.NextPage = new Wizard_pages.Add_backup.ThrottleOptions();
            else if ((bool)m_settings["Advanced:Filters"])
                args.NextPage = new Wizard_pages.Add_backup.FilterEditor();
            else
                args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        void SelectWhen_PageEnter(object sender, PageChangedArgs args)
        {
            m_schedule = (Schedule)m_settings["Schedule"];

            if (!m_valuesAutoLoaded)
            {
                OffsetDate.Value = m_schedule.When;
                OffsetTime.Value = m_schedule.When;
                RepeatInterval.Value = m_schedule.Repeat;
                EnableRepeat.Checked = !string.IsNullOrEmpty(m_schedule.Repeat);
            }

            if (m_settings.ContainsKey("When:HasWarned"))
                m_hasWarned = (bool)m_settings["When:HasWarned"];
        }

        private void OffsetDate_ValueChanged(object sender, EventArgs e)
        {
            m_hasWarned = false;
        }

        private void OffsetTime_ValueChanged(object sender, EventArgs e)
        {
            m_hasWarned = false;
        }

        private void RepeatInterval_ValueChanged(object sender, EventArgs e)
        {

        }
    }
}
