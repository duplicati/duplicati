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
            m_settings["When:HasWarned"] = m_hasWarned;

            if (args.Direction == PageChangedDirection.Back)
                return;
            
            if (!m_hasWarned && !EnableRepeat.Checked)
            {
                DateTime newtime = OffsetDate.Value.Date.Add(OffsetTime.Value.TimeOfDay);
                string b = null;
                if (DateTime.Now > newtime)
                    b = Strings.SelectWhen.TimeIsInThePastWarning;
                else if (DateTime.Now > newtime.AddMinutes(10))
                    b = Strings.SelectWhen.TimeOccursShortlyWarning;

                if (b != null)
                {
                    if (MessageBox.Show(this, b + " " + Strings.SelectWhen.NoRepetitionWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
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
                        MessageBox.Show(this, Strings.SelectWhen.TooShortDurationError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            m_settings["When:HasWarned"] = m_hasWarned;

            m_wrapper.BackupTimeOffset = OffsetDate.Value.Date.Add(OffsetTime.Value.TimeOfDay);
            if (EnableRepeat.Checked)
                m_wrapper.RepeatInterval = RepeatInterval.Value;
            else
                m_wrapper.RepeatInterval = "";

            if ((bool)m_settings["Advanced:Incremental"])
                args.NextPage = new Wizard_pages.Add_backup.IncrementalSettings();
            else if ((bool)m_settings["Advanced:Throttle"])
                args.NextPage = new Wizard_pages.Add_backup.ThrottleOptions();
            else if ((bool)m_settings["Advanced:Filters"])
                args.NextPage = new Wizard_pages.Add_backup.EditFilters();
            else if ((bool)m_settings["Advanced:Filenames"])
                args.NextPage = new Wizard_pages.Add_backup.GeneratedFilenameOptions();
            else if ((bool)m_settings["Advanced:Overrides"])
                args.NextPage = new Wizard_pages.Add_backup.SettingOverrides();
            else
                args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        void SelectWhen_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_valuesAutoLoaded)
            {
                OffsetDate.Value = m_wrapper.BackupTimeOffset;
                OffsetTime.Value = m_wrapper.BackupTimeOffset;
                RepeatInterval.Value = m_wrapper.RepeatInterval;
                EnableRepeat.Checked = !string.IsNullOrEmpty(m_wrapper.RepeatInterval);
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
