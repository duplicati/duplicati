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

namespace Duplicati.GUI.Wizard_pages
{
    public partial class SelectBackup : WizardControl
    {
        public SelectBackup()
            : base ("", "")
        {
            InitializeComponent();
            BackupList.treeView.HideSelection = false;

            base.PageLeave += new PageChangeHandler(SelectBackup_PageLeave);
            base.PageEnter += new PageChangeHandler(SelectBackup_PageEnter);
        }

        void SelectBackup_PageEnter(object sender, PageChangedArgs args)
        {
            BackupList.Setup((IDataFetcher)m_settings["Connection"], false, false);
            if (m_settings.ContainsKey("Schedule"))
                BackupList.SelectedBackup = (Schedule)m_settings["Schedule"];
        }

        void SelectBackup_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (BackupList.SelectedBackup == null)
            {
                MessageBox.Show(this, "You must select a backup before you can continue", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            m_settings["Schedule"] = BackupList.SelectedBackup;
            args.NextPage = new Add_backup.SelectName();
        }

        public override string Title
        {
            get
            {
                switch ((string)m_settings["Main:Action"])
                {
                    case "backup":
                        return "Select the backup to run now";
                    case "delete":
                        return "Select the backup to remove";
                    case "edit":
                        return "Select the backup to modify";
                    case "restore":
                        return "Select the backup to restore";
                    default:
                        return "Unknown action";
                }

            }
        }

        public override string HelpText
        {
            get
            {
                switch ((string)m_settings["Main:Action"])
                {
                    case "backup":
                        return "In the list below, select the backup you want to run immediately";
                    case "delete":
                        return "In the list below, select the backup you want to delete";
                    case "edit":
                        return "In the list below, select the backup you want to modify";
                    case "restore":
                        return "In the list below, select the backup you want to restore";
                    default:
                        return "Unknown action";
                }
            }
        }
    }
}
