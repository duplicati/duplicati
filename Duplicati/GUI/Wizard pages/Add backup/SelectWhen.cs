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
    public partial class SelectWhen : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        private bool m_hasWarned;
        private Schedule m_schedule;

        public SelectWhen()
        {
            InitializeComponent();
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get { return "Select when the backup should run"; }
        }

        string IWizardControl.HelpText
        {
            get { return "On this page you may set up when the backup is run. Automatically repeating the backup ensure that you have a backup, without requiring any action from you."; }
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
                OffsetDate.Value = m_schedule.When;
                OffsetTime.Value = m_schedule.When;
                RepeatInterval.Text = m_schedule.Repeat;
                EnableRepeat.Checked = !string.IsNullOrEmpty(m_schedule.Repeat);
            }
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
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
                        cancel = true;
                        return;
                    }
                    m_hasWarned = true;
                }
            }

            if (EnableRepeat.Checked)
            {
                try
                {
                    TimeSpan sp = Timeparser.ParseTimeSpan(RepeatInterval.Text);
                    if (sp.TotalMinutes < 5)
                    {
                        MessageBox.Show(this, "The duration entered is less than five minutes. That is not acceptable.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                        cancel = true;
                        return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The duration entered is not valid.\nError message: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    cancel = true;
                    return;
                }
            }

            m_schedule.When = OffsetDate.Value.Date.Add(OffsetTime.Value.TimeOfDay);
            if (EnableRepeat.Checked)
                m_schedule.Repeat = RepeatInterval.Text;
            else
                m_schedule.Repeat = null;
        }

        #endregion

        private void OffsetDate_ValueChanged(object sender, EventArgs e)
        {
            m_hasWarned = false;
        }

        private void OffsetTime_ValueChanged(object sender, EventArgs e)
        {
            m_hasWarned = false;
        }

        #region IScheduleBased Members

        public void Setup(Duplicati.Datamodel.Schedule schedule)
        {
            m_schedule = schedule;
        }

        #endregion
    }
}
