#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
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
        private bool m_enabledClicked = false;

        private Dictionary<string, Library.Interface.ICommandLineArgument> m_optionCache = new Dictionary<string,Library.Interface.ICommandLineArgument>();

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

            MonoSupport.FixTextBoxes(this);
        }

        public bool Unsupported { get { return m_unsupported; } }

        public void Setup(IList<Library.Interface.ICommandLineArgument> switches, IList<Library.Interface.ICommandLineArgument> extras, IDictionary<string, string> options)
        {
            if (!m_unsupported)
            {
                OverrideTable.Rows.Clear();
                m_optionCache.Clear();
                
                foreach (IList<Library.Interface.ICommandLineArgument> sw in new IList<Library.Interface.ICommandLineArgument>[] { switches, extras })
                    if (sw != null)
                        foreach (Library.Interface.ICommandLineArgument arg in sw)
                        {
                            DataRow dr = OverrideTable.NewRow();
                            dr["Enabled"] = options.ContainsKey(arg.Name);
                            dr["argument"] = arg;
                            dr["validated"] = false;
                            dr["Name"] = arg.Name;
                            if (options.ContainsKey(arg.Name))
                                dr["Value"] = options[arg.Name];
                            OverrideTable.Rows.Add(dr);
                            
                            //We use this as a lookup table due to a Mono bug
                            m_optionCache[arg.Name] = arg;
                        }
            }
        }

        public Dictionary<string, string> GetConfiguration()
        {
            Dictionary<string, string> opts = new Dictionary<string, string>();
            if (!m_unsupported)
                foreach (DataRow dr in OverrideTable.Rows)
                    if ((bool)dr["Enabled"])
                        opts.Add((string)dr["Name"], (string)(dr["Value"] == DBNull.Value ? "" : dr["Value"] ?? ""));

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

                    OptionsGrid.Sort(nameDataGridViewTextBoxColumn, ListSortDirection.Ascending);
                }
            }
        }

        public Dictionary<string, Library.Interface.ICommandLineArgument> DataElementCache
        {
            get { return m_optionCache; }
            set
            {
                if (!m_unsupported)
                    m_optionCache = value;
            }
        }

        private Duplicati.Library.Interface.ICommandLineArgument GetArgument(DataRow r)
        {
            Duplicati.Library.Interface.ICommandLineArgument arg;
            object v = r["argument"];
            if (v is string)
            {
                //For some reason, the newer Mono versions store the object as a string instance instead of the object
                //To counter this, there is now a seperate argument cache
                if (r["Name"] == null || r["Name"] == DBNull.Value)
                    return null;
                m_optionCache.TryGetValue((string)r["Name"], out arg);
                return arg;
            }
            else if (v is Duplicati.Library.Interface.ICommandLineArgument)
                return (Duplicati.Library.Interface.ICommandLineArgument)v;
            else
                return null;
        }

        private DataRow FindRow(int rowIndex)
        {
            int argIndex = -1;
            foreach (DataGridViewColumn c in OptionsGrid.Columns)
                if (c.DataPropertyName == "argument")
                {
                    argIndex = c.Index;
                    break;
                }

            int nameIndex = -1;
            foreach (DataGridViewColumn c in OptionsGrid.Columns)
                if (c.DataPropertyName == "Name")
                {
                    nameIndex = c.Index;
                    break;
                }

            if (argIndex == -1)
                return null;

            object _arg = OptionsGrid.Rows[rowIndex].Cells[argIndex].Value;
            if (_arg == null)
                return null;

            Library.Interface.ICommandLineArgument arg;

            //For some reason, the newer Mono versions store the object as a string instance instead of the object
            //To counter this, there is now a seperate argument cache
            if (_arg is string)
            {
                if (nameIndex == -1)
                    return null;

                string name = OptionsGrid.Rows[rowIndex].Cells[nameIndex].Value as string;
                if (name == null)
                    return null;
                m_optionCache.TryGetValue(name, out arg);
            }
            else if (_arg is Library.Interface.ICommandLineArgument)
            {
                arg = (Library.Interface.ICommandLineArgument)OptionsGrid.Rows[rowIndex].Cells[argIndex].Value;
            }
            else
            {
                return null;
            }

            foreach (DataRow r in OverrideTable.Rows)
                if (GetArgument(r) == arg)
                    return r;

            return null;
        }

        private void OptionsGrid_RowEnter(object sender, DataGridViewCellEventArgs e)
        {
            DataRow r = FindRow(e.RowIndex);
            if (r == null)
                return;
            Library.Interface.ICommandLineArgument arg = GetArgument(r);

            string typename = arg.Typename;
            if (arg.ValidValues != null && arg.ValidValues.Length > 0)
                typename += ": " + string.Join(", ", arg.ValidValues);

            InfoLabel.Text = string.Format(Strings.CommandLineOptionsGrid.InfoLabelFormat, typename, arg.ShortDescription, arg.LongDescription);
        }

        private void OptionsGrid_RowValidating(object sender, DataGridViewCellCancelEventArgs e)
        {
            DataRow r = FindRow(e.RowIndex);
            if (r == null)
                return;

            if (e.ColumnIndex != 0)
                r["Enabled"] = r["Value"] != DBNull.Value;

            r["validated"] = false;

            if ((bool)r["Enabled"])
            {
                Library.Interface.ICommandLineArgument arg = GetArgument(r);
                string optionvalue = (string)(r["Value"] == DBNull.Value ? "" : r["Value"] ?? "");

                string validationMessage = Duplicati.Library.Main.Interface.ValidateOptionValue(arg, arg.Name, optionvalue);
                if (validationMessage != null)
                    if (MessageBox.Show(this, validationMessage, Application.ProductName, MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) == DialogResult.Cancel)
                        e.Cancel = true;

                if (!e.Cancel)
                    r["validated"] = true;
            }
        }

        private void OptionsGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 0)
                m_enabledClicked = true;
        }

        private void OptionsGrid_CellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (e == null || e.FormattedValue == null)
                return;

            if (m_enabledClicked && e.ColumnIndex == 0 && e.FormattedValue.ToString() == true.ToString())
            {
                DataRow r = FindRow(e.RowIndex);
                if (r == null)
                    return;

                Library.Interface.ICommandLineArgument arg = GetArgument(r);
                if (arg.Type == Duplicati.Library.Interface.CommandLineArgument.ArgumentType.Boolean)
                {
                    if (r["Value"] == DBNull.Value)
                    {
                        r["Value"] = "true";
                        r["Enabled"] = true; //Required or display is weird
                    }
                }
            }

            m_enabledClicked = false;
        }
    }
}
