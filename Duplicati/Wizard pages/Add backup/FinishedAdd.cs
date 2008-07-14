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
            List<KeyValuePair<string, string>> strings = new List<KeyValuePair<string,string>>();
            if (!m_schedule.RelationManager.ExistsInDb(m_schedule))
                strings.Add(new KeyValuePair<string,string>("Action", "Add new backup"));
            else
                strings.Add(new KeyValuePair<string, string>("Action", "Modify backup"));

            strings.Add(new KeyValuePair<string,string>("Source folder", m_schedule.Tasks[0].SourcePath));
            strings.Add(new KeyValuePair<string,string>("When", m_schedule.When.ToString()));
            if (!string.IsNullOrEmpty(m_schedule.Repeat))
                strings.Add(new KeyValuePair<string,string>("Repeat", m_schedule.Repeat));
            if (!string.IsNullOrEmpty(m_schedule.FullAfter))
                strings.Add(new KeyValuePair<string,string>("Full backup each", m_schedule.FullAfter));
            if (m_schedule.KeepFull > 0)
                strings.Add(new KeyValuePair<string,string>("Keep full backups", m_schedule.KeepFull.ToString()));

            if (m_schedule.Tasks != null && m_schedule.Tasks.Count == 1)
            {
                Duplicati.Datamodel.Backends.IBackend backend = m_schedule.Tasks[0].Backend;
                strings.Add(new KeyValuePair<string, string>(null, null));
                strings.Add(new KeyValuePair<string, string>("Destination", backend.FriendlyName));
                strings.Add(new KeyValuePair<string, string>("Destination path", backend.GetDestinationPath()));
            }

            int maxlen = 0;
            foreach(KeyValuePair<string, string> i in strings)
                if (i.Key != null)
                    maxlen = Math.Max(maxlen, i.Key.Length);

            System.Text.StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key == null)
                    sb.Append("\r\n");
                else
                    sb.Append(i.Key + ": " + new String(' ', maxlen - i.Key.Length) + i.Value + "\r\n"); 

            Summary.Text = sb.ToString();

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
