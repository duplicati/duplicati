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

            Library.Backend.IBackend selectedBackend = null;
            foreach (RadioButton button in BackendList.Controls)
                if (button.Checked && button.Tag is Library.Backend.IBackend)
                    selectedBackend = button.Tag as Library.Backend.IBackend;

            if (selectedBackend == null)
            {
                MessageBox.Show(this, Strings.SelectBackend.NoActionSelected, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            m_wrapper.Backend = selectedBackend.ProtocolKey;
            if (selectedBackend is Library.Backend.IBackendGUI)
                args.NextPage = new Backends.GUIContainer(selectedBackend as Library.Backend.IBackendGUI);
            else
                args.NextPage = new Backends.RawContainer(selectedBackend);
        }


        void SelectBackend_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            int top = 0;
            BackendList.Controls.Clear();

            foreach (Library.Backend.IBackend backend in Library.Backend.BackendLoader.LoadedBackends)
            {
                RadioButton button = new RadioButton();
                button.Text = backend.DisplayName;
                toolTips.SetToolTip(button, backend.Description);
                button.Left = 0;
                button.Top = top;
                button.Tag = backend;
                button.CheckedChanged += new EventHandler(Item_CheckChanged);

                button.Checked = (backend.ProtocolKey == m_wrapper.Backend);

                top += button.Height + 5;
                BackendList.Controls.Add(button);
            }

            Item_CheckChanged(null, null);

            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                Question.Text = Strings.SelectBackend.RestoreSetupTitle;

        }

        private void Item_CheckChanged(object sender, EventArgs e)
        {
            Library.Backend.IBackend selectedBackend = null;
            foreach (RadioButton button in BackendList.Controls)
                if (button.Checked && button.Tag is Library.Backend.IBackend)
                    selectedBackend = button.Tag as Library.Backend.IBackend;

            m_owner.NextButton.Enabled = selectedBackend != null;
        }

        private void RadioButton_DoubleClick(object sender, EventArgs e)
        {
            m_owner.NextButton.PerformClick();
        }
    }
}
