using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.Wizard_pages.Add_backup
{
    public partial class FinishedAdd : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        private Schedule m_schedule;

        public FinishedAdd()
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
            get { return "Ready to add backup"; }
        }

        string IWizardControl.HelpText
        {
            get { return "You have now entered all the required data, and can now create the backup."; }
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
                "Action: Add new backup\r\n" +
                "When: " + m_schedule.When.ToString() + "\r\n";
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
        }

        #endregion

        #region IScheduleBased Members

        public void Setup(Duplicati.Datamodel.Schedule schedule)
        {
            m_schedule = schedule;
        }

        #endregion
    }
}
