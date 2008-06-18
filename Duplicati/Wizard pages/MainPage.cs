using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;

namespace Duplicati.Wizard_pages
{
    public partial class MainPage : UserControl, IWizardControl
    {
        private IWizardForm m_owner;

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
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
        }


        private void UpdateButtonState()
        {
            if (m_owner != null)
                m_owner.NextButton.Enabled = CreateNew.Checked | Edit.Checked | Restore.Checked;
        }

        #endregion

        private void Radio_CheckedChanged(object sender, EventArgs e)
        {
            UpdateButtonState();
        }
    }
}
