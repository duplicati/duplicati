#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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

namespace Duplicati.GUI.HelperControls
{
    public partial class CommandLineOptionGrid : UserControl
    {
        private bool m_unsupported = false;

        public CommandLineOptionGrid()
        {
            try
            {
                InitializeComponent();
            }
            catch (NotImplementedException)
            {
                OptionsGrid.Enabled = false;
                MessageBox.Show(this, Strings.CommandLineOptionsGrid.PageNotSupportedWarning, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                m_unsupported = true;
            }
        }

        public bool Unsupported { get { return m_unsupported; } }

        public void Setup(IList<Library.Interface.ICommandLineArgument> switches, IList<Library.Interface.ICommandLineArgument> extras, IDictionary<string, string> options)
        {
            if (!m_unsupported)
            {
                OverrideTable.Rows.Clear();
                foreach (IList<Library.Interface.ICommandLineArgument> sw in new IList<Library.Interface.ICommandLineArgument>[] { switches, extras })
                    if (sw != null)
                        foreach (Library.Interface.ICommandLineArgument arg in sw)
                        {
                            DataRow dr = OverrideTable.NewRow();
                            dr["Enabled"] = options.ContainsKey(arg.Name);
                            dr["argument"] = arg;
                            dr["Name"] = arg.Name;
                            if (options.ContainsKey(arg.Name))
                                dr["Value"] = options[arg.Name];
                            OverrideTable.Rows.Add(dr);
                        }
            }
        }

        public Dictionary<string, string> GetConfiguration()
        {
            Dictionary<string, string> opts = new Dictionary<string, string>();
            if (!m_unsupported)
                foreach (DataRow dr in OverrideTable.Rows)
                    if ((bool)dr["Enabled"])
                        opts.Add((string)dr["Name"], (string)dr["Value"]);

            return opts;
        }

        public DataSet DataSet
        {
            get { return BaseDataSet; }
            set 
            {
                if (!m_unsupported)
                {
                    BaseDataSet = value;
                    OptionsGrid.DataSource = BaseDataSet;
                    OverrideTable = BaseDataSet.Tables["OverrideTable"];
                    OptionsGrid.DataMember = "OverrideTable";
                }
            }
        }

        private void OptionsGrid_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            DataRow r = OverrideTable.Rows[e.RowIndex];
            Library.Interface.ICommandLineArgument arg = (Library.Interface.ICommandLineArgument)r["argument"];

            InfoLabel.Text = string.Format(Strings.CommandLineOptionsGrid.InfoLabelFormat, arg.Typename, arg.ShortDescription, arg.LongDescription);
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
