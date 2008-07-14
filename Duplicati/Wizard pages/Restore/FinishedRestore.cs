using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.Wizard_pages.Restore
{
    public partial class FinishedRestore : UserControl, IWizardControl
    {
        public FinishedRestore()
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
            get { return "Ready to restore files"; }
        }

        string IWizardControl.HelpText
        {
            get { return "Duplicati is now ready to restore your files."; }
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

        public void Setup(Schedule schedule, DateTime backup, string target)
        {
            Summary.Text = 
                "Action: Restore backup\r\n" +
                "Backup: " + schedule.Name + "\r\n" +
                "Date:   " + (backup.Ticks == 0 ? "most recent" : backup.ToString()) + "\r\n" +
                "Folder: " + target + "\r\n";
        }
    }
}
