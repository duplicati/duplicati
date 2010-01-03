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

namespace Duplicati.GUI.Wizard_pages
{
    public partial class MainPage : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public MainPage()
            : base(Strings.MainPage.PageTitle, Strings.MainPage.PageHelptext)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(MainPage_PageEnter);
            base.PageLeave += new PageChangeHandler(MainPage_PageLeave);
            base.PageDisplay += new PageChangeHandler(MainPage_PageDisplay);
        }

        void MainPage_PageDisplay(object sender, PageChangedArgs args)
        {
            //Skip this, as there is only one valid option
            if (Program.DataConnection.GetObjects<Datamodel.Schedule>().Length == 0)
            {
                if (args.Direction == PageChangedDirection.Next)
                {
                    CreateNew.Checked = true;
                    try { m_owner.NextButton.PerformClick(); }
                    catch { }
                }
                else
                {
                    try { m_owner.BackButton.PerformClick(); }
                    catch { }
                }
            }
        }

        void MainPage_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            UpdateButtonState();
            args.TreatAsLast = false;
        }

        void MainPage_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (CreateNew.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            else if (Edit.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Edit;
            else if (Restore.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Restore;
            else if (Backup.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.RunNow;
            else if (Remove.Checked)
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Remove;
            else
            {
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Unknown;
                args.NextPage = null;
                args.Cancel = true;
                return;
            }

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
            {
                args.NextPage = new Add_backup.SelectName();
                m_wrapper.SetupDefaults();
                m_wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            }
            else
                args.NextPage = new SelectBackup();
        }

        private void UpdateButtonState()
        {
            if (m_owner != null)
                m_owner.NextButton.Enabled = CreateNew.Checked | Edit.Checked | Restore.Checked | Backup.Checked | Remove.Checked;
        }

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            UpdateButtonState();
        }

        private void RadioButton_DoubleClick(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
