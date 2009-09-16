using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.Wizard_pages
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

        public void Setup(IList<Library.Backend.ICommandLineArgument> switches, IDictionary<string, string> options)
        {
            if (!m_unsupported)
            {
                OverrideTable.Rows.Clear();
                foreach (Library.Backend.ICommandLineArgument arg in switches)
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
            Library.Backend.ICommandLineArgument arg = (Library.Backend.ICommandLineArgument)r["argument"];

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
