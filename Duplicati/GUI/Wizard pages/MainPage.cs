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

namespace Duplicati.GUI.Wizard_pages
{
    public partial class MainPage : UserControl, IWizardControl
    {
        private IWizardForm m_owner;

        public enum Action
        {
            Unknown,
            Add,
            Edit,
            Delete,
            Restore,
            Backup,
            Rearrange
        }

        public MainPage()
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
            get { return "Welcome to the Duplicati Wizard"; }
        }

        string IWizardControl.HelpText
        {
            get { return "Please select the action you want to perform below"; }
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
            m_owner = owner;
            UpdateButtonState();

            this.Controls.Remove(ShowAdvanced);
            m_owner.ButtonPanel.Controls.Add(ShowAdvanced);
            ShowAdvanced.Top = m_owner.CancelButton.Top;
            ShowAdvanced.Left = m_owner.ButtonPanel.Width - m_owner.CancelButton.Right;
            ShowAdvanced.Visible = true;
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            m_owner.ButtonPanel.Controls.Remove(ShowAdvanced);
            this.Controls.Add(ShowAdvanced);
            ShowAdvanced.Visible = false;
        }

        #endregion

        private void UpdateButtonState()
        {
            if (m_owner != null)
                m_owner.NextButton.Enabled = CreateNew.Checked | Edit.Checked | Restore.Checked | Backup.Checked | Remove.Checked;
        }

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            UpdateButtonState();
        }

        public Action SelectedAction
        {
            get
            {
                if (CreateNew.Checked)
                    return Action.Add;
                else if (Edit.Checked)
                    return Action.Edit;
                else if (Restore.Checked)
                    return Action.Restore;
                else if (Backup.Checked)
                    return Action.Backup;
                else if (Remove.Checked)
                    return Action.Delete;
                else
                    return Action.Unknown;
                    
            }
        }

        private void ShowAdvanced_Click(object sender, EventArgs e)
        {
            Program.ShowSetup();
            m_owner.Dialog.DialogResult = DialogResult.Cancel;
            m_owner.Dialog.Close();
        }
    }
}
