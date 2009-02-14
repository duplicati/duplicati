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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.Wizard_pages
{
    public partial class FirstLaunch : System.Windows.Forms.Wizard.WizardControl
    {
        public FirstLaunch()
            : base("Welcome to Duplicati", "Since this is your first use of Duplicati on this machine, this wizard will guide you through a Duplicati setup")
        {
            InitializeComponent();
            base.PageDisplay += new System.Windows.Forms.Wizard.PageChangeHandler(FirstLaunch_PageDisplay);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(FirstLaunch_PageLeave);
        }

        void FirstLaunch_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (!CreateNew.Checked && !Restore.Checked)
            {
                MessageBox.Show(this, "You must select an action", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel;
                args.NextPage = null;
                return;
            }

            WizardSettingsWrapper wrapper = new WizardSettingsWrapper(m_settings);

            if (CreateNew.Checked)
            {
                //If there are no existing backups, the mainpage just selects add, and sets the defaults
                args.NextPage = new MainPage();
                wrapper.PrimayAction = WizardSettingsWrapper.MainAction.Add;
            }
            else
            {
                args.NextPage = new SelectBackend();
                wrapper.PrimayAction = WizardSettingsWrapper.MainAction.RestoreSetup;
            }

        }

        void FirstLaunch_PageDisplay(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_owner.NextButton.Enabled =
                CreateNew.Checked | Restore.Checked;
        }

        private void CreateNew_CheckedChanged(object sender, EventArgs e)
        {
            FirstLaunch_PageDisplay(sender, null);
        }

        private void Restore_CheckedChanged(object sender, EventArgs e)
        {
            FirstLaunch_PageDisplay(sender, null);
        }
    }
}

