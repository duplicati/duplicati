using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;


namespace Duplicati.Wizard_pages.RunNow
{
    public partial class RunNowFinished : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        Schedule m_schedule;

        public RunNowFinished()
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
            get { return "Ready to run backup"; }
        }

        string IWizardControl.HelpText
        {
            get { return "You are now ready to run the backup"; }
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
            Summary.Text =
                "Action: Run backup now\r\n" +
                "Name:   " + m_schedule.Name;
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
        }

        #endregion

        #region IScheduleBased Members

        public void Setup(Schedule schedule)
        {
            m_schedule = schedule;
        }

        #endregion

        public bool ForceFullBackup { get { return ForceFull.Checked; } }
    }
}
