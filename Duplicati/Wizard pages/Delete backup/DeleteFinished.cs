using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.Wizard_pages.Delete_backup
{
    public partial class DeleteFinished : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        Schedule m_schedule;

        public DeleteFinished()
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
            get { return "Delete backup"; }
        }

        string IWizardControl.HelpText
        {
            get { return "You are now ready to delete the backup. Please note that you cannot restore the backup ever again"; }
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
                "Action: Delete backup\r\n" +
                "Name: " + m_schedule.Name;
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
    }
}
