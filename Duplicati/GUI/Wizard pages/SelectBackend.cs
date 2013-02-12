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

namespace Duplicati.GUI.Wizard_pages
{
    public partial class SelectBackend : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SelectBackend()
            : base(Strings.SelectBackend.PageTitle, Strings.SelectBackend.PageHelptext)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(SelectBackend_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectBackend_PageLeave);
            base.PageDisplay += new PageChangeHandler(SelectBackend_PageDisplay);
        }

        void SelectBackend_PageDisplay(object sender, PageChangedArgs args)
        {
            Item_CheckChanged(null, null);

            //If there is just one backend, skip this page
            if (BackendList.Controls.Count == 1)
            {
                if (args.Direction == PageChangedDirection.Next)
                {
                    ((RadioButton)BackendList.Controls[0]).Checked = true;
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

        void SelectBackend_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            Library.Interface.IBackend selectedBackend = null;
            foreach (RadioButton button in BackendList.Controls)
                if (button.Checked && button.Tag is Library.Interface.IBackend)
                    selectedBackend = button.Tag as Library.Interface.IBackend;

            if (selectedBackend == null)
            {
                MessageBox.Show(this, Strings.SelectBackend.NoActionSelected, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            //If the user chooses another backend, we need to clear the settings,
            // so items like the tested flag are not set
            if (m_wrapper.Backend != selectedBackend.ProtocolKey)
                m_wrapper.BackendSettings.Clear();

            m_wrapper.Backend = selectedBackend.ProtocolKey;

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
            {
                if (m_wrapper.ShowAdvancedRestoreOptions)
                    args.NextPage = new Add_backup.SettingOverrides();
                else
                    args.NextPage = new Restore.SelectBackupVersion();
            }
            else if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
            {
                if (m_wrapper.ShowAdvancedRestoreOptions)
                    args.NextPage = new Add_backup.SettingOverrides();
                else
                    args.NextPage = new RestoreSetup.FinishedRestoreSetup();
            }
            else
                args.NextPage = new Add_backup.AdvancedOptions();

            //Create the appropriate GUI for the backend settings
            if (selectedBackend is Library.Interface.IBackendGUI)
                args.NextPage = new GUIContainer(args.NextPage, selectedBackend as Library.Interface.IGUIControl);
            else
                args.NextPage = new Backends.RawContainer(args.NextPage, selectedBackend, m_wrapper.BackendSettings);
        }


        void SelectBackend_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            int top = 0;
            BackendList.Controls.Clear();

            //Sort backends by display name
            SortedList<string, Library.Interface.IBackend> lst = new SortedList<string, Duplicati.Library.Interface.IBackend>();
            foreach (Library.Interface.IBackend backend in Library.DynamicLoader.BackendLoader.Backends)
                lst.Add(backend.DisplayName.Trim().ToLower(), backend);

            foreach (Library.Interface.IBackend backend in lst.Values)
            {
                DoubleClickRadioButton button = new DoubleClickRadioButton();
                button.AutoSize = true;
                button.Text = backend.DisplayName;
                toolTips.SetToolTip(button, backend.Description);
                button.Left = 0;
                button.Top = top;
                button.Tag = backend;
                button.CheckedChanged += new EventHandler(Item_CheckChanged);
                button.DoubleClick += new EventHandler(button_DoubleClick);

                button.Checked = (backend.ProtocolKey == m_wrapper.Backend);

                top += button.Height + 5;
                BackendList.Controls.Add(button);
            }

            Item_CheckChanged(null, null);

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup || m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
                Question.Text = Strings.SelectBackend.RestoreSetupTitle;

        }

        void button_DoubleClick(object sender, EventArgs e)
        {
            try { m_owner.NextButton.PerformClick(); }
            catch { }
        }

        private void Item_CheckChanged(object sender, EventArgs e)
        {
            Library.Interface.IBackend selectedBackend = null;
            foreach (RadioButton button in BackendList.Controls)
                if (button.Checked && button.Tag is Library.Interface.IBackend)
                    selectedBackend = button.Tag as Library.Interface.IBackend;

            m_owner.NextButton.Enabled = selectedBackend != null;
        }

        private void RadioButton_DoubleClick(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
