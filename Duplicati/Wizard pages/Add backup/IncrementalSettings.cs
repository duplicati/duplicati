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
    public partial class IncrementalSettings : UserControl, IWizardControl
    {
        public IncrementalSettings()
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
            get { return "Select incremental options"; }
        }

        string IWizardControl.HelpText
        {
            get { return "To avoid backuping up every single file each time, Duplicati can back up the files that have changed since the last run. It is still possible to restore all files, but the storage requirement is much lower."; }
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
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
        }


        #endregion
    }
}
