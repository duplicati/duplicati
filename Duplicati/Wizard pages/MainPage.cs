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
        public MainPage()
        {
            InitializeComponent();
        }

        #region IWizardControl Members

        public new Control Control
        {
            get { return this; }
        }

        public string Title
        {
            get { return "Welcome to the Duplicati Wizard"; }
        }

        public string HelpText
        {
            get { return "Please select the action you want to perform below"; }
        }

        public Image Image
        {
            get { return null; }
        }

        public bool FullSize
        {
            get { return false; }
        }

        public void Displayed(IWizardForm owner)
        {
            
        }

        #endregion
    }
}
