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
using Duplicati.Datamodel;
using Duplicati.Library.Core;

namespace Duplicati.GUI.Wizard_pages.Restore
{
    public partial class SelectBackup : UserControl, IWizardControl, Interfaces.IScheduleBased
    {
        Schedule m_schedule;
        DateTime m_selectedDate = new DateTime();

        public SelectBackup()
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
            get { return "Select the backup to restore"; }
        }

        string IWizardControl.HelpText
        {
            get { return "The list below shows all the avalible backups. Select one to restore"; }
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
            BackupList.Setup(m_schedule);
        }

        void IWizardControl.Leave(IWizardForm owner, ref bool cancel)
        {
            if (BackupList.SelectedItem == null)
            {
                MessageBox.Show(this, "You must select the backup to restore", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }

            m_selectedDate = new DateTime();
            try
            {
                m_selectedDate = Timeparser.ParseDuplicityFileTime(BackupList.SelectedItem);
            }
            catch (Exception ex)
            {
                if (MessageBox.Show(this, "An error occured while parsing the time: " + ex.Message + "\r\nDo you want to try to restore the most current backup instead?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                {
                    cancel = true;
                    return;
                }
                m_selectedDate = new DateTime();
            }

        }

        #endregion

        #region IScheduleBased Members

        public void Setup(Duplicati.Datamodel.Schedule schedule)
        {
            m_schedule = schedule;
        }

        #endregion

        public DateTime SelectedBackup { get { return m_selectedDate; } }
    }
}
