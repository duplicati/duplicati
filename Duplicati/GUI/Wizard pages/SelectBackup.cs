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
using System.Data.LightDatamodel;
using Duplicati.Datamodel;

namespace Duplicati.Wizard_pages
{
    public partial class SelectBackup : UserControl, IWizardControl
    {
        private MainPage.Action m_selectType;

        public SelectBackup()
        {
            InitializeComponent();
            BackupList.treeView.HideSelection = false;
        }

        #region IWizardControl Members

        Control IWizardControl.Control
        {
            get { return this; }
        }

        string IWizardControl.Title
        {
            get
            {
                switch (m_selectType)
                {
                    case MainPage.Action.Backup:
                        return "Select the backup to run now";
                    case MainPage.Action.Delete:
                        return "Select the backup to remove";
                    case MainPage.Action.Edit:
                        return "Select the backup to modify";
                    case MainPage.Action.Restore:
                        return "Select the backup to restore";
                    default:
                        return "Unknown action";
                }
            }
        }

        string IWizardControl.HelpText
        {
            get 
            {
                switch (m_selectType)
                {
                    case MainPage.Action.Backup:
                        return "In the list below, select the backup you want to run immediately";
                    case MainPage.Action.Delete:
                        return "In the list below, select the backup you want to delete";
                    case MainPage.Action.Edit:
                        return "In the list below, select the backup you want to modify";
                    case MainPage.Action.Restore:
                        return "In the list below, select the backup you want to restore";
                    default:
                        return "Unknown action";
                }
            }
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
            if (BackupList.SelectedBackup == null)
            {
                MessageBox.Show(this, "You must select a backup before you can continue", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                cancel = true;
                return;
            }

        }

        #endregion

        public void Setup(IDataFetcher connection, MainPage.Action selectType)
        {
            m_selectType = selectType;
            BackupList.Setup(connection, false, false);
        }

        public MainPage.Action SelectType
        {
            get { return m_selectType; }
            set { m_selectType = value; }
        }

        public Schedule SelectedBackup { get { return BackupList.SelectedBackup; } }
    }
}
