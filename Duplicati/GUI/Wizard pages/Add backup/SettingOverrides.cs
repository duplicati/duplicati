#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class SettingOverrides : System.Windows.Forms.Wizard.WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SettingOverrides()
            : base("Override settings", "On this page you can override all settings supported by Duplicati. This is very advanced, so be carefull!")
        {
            InitializeComponent();

            m_autoFillValues = false;
            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(SettingOverrides_PageEnter);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(SettingOverrides_PageLeave);
        }

        void SettingOverrides_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            m_wrapper.Overrides.Clear();
            foreach (DataRow dr in OverrideTable.Rows)
                if ((bool)dr["Enabled"])
                    m_wrapper.Overrides.Add((string)dr["Name"], (string)dr["Value"]);

            args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();
        }

        void SettingOverrides_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_settings.ContainsKey("Overrides:Table"))
            {
                OverrideTable.Rows.Clear();

                Library.Main.Options opt = new Library.Main.Options(new Dictionary<string, string>());
                foreach (Library.Backend.ICommandLineArgument arg in opt.SupportedCommands)
                {
                    DataRow dr = OverrideTable.NewRow();
                    dr["Enabled"] = m_wrapper.Overrides.ContainsKey(arg.Name);
                    dr["argument"] = arg;
                    dr["Name"] = arg.Name;
                    if (m_wrapper.Overrides.ContainsKey(arg.Name))
                        dr["Value"] = m_wrapper.Overrides[arg.Name];
                    OverrideTable.Rows.Add(dr);
                }

                m_settings["Overrides:Table"] = BaseDataSet;
            }
            else
            {
                BaseDataSet = (DataSet)m_settings["Overrides:Table"];
                OptionsGrid.DataSource = BaseDataSet;
                OverrideTable = BaseDataSet.Tables["OverrideTable"];
                OptionsGrid.DataMember = "OverrideTable";
            }

        }

        private void OptionsGrid_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            DataRow r = OverrideTable.Rows[e.RowIndex];
            Library.Backend.ICommandLineArgument arg = (Library.Backend.ICommandLineArgument)r["argument"];
            InfoLabel.Text = string.Format("Type: {0}. {1}. {2}", arg.Type.ToString(), arg.ShortDescription, arg.LongDescription);
        }

        private void OptionsGrid_CellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex != 0)
            {
                DataRow r = OverrideTable.Rows[e.RowIndex];
                r["Enabled"] = r["Value"] != DBNull.Value;
            }
        }
    }
}

