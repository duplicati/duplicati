using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;

namespace Duplicati.Wizard_pages.Add_backup
{
    public partial class SelectWhen : UserControl, IWizardControl
    {
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

        void IWizardControl.Displayed(IWizardForm owner)
        {
            
        }

        #endregion
    }
}
