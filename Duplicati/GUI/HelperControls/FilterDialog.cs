using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.HelperControls
{
    public partial class FilterDialog : Form
    {
        KeyValuePair<bool, string> m_filter;

        public FilterDialog(KeyValuePair<bool, string> filter)
            : this()
        {
            m_filter = filter;
        }

        public KeyValuePair<bool, string> Filter
        {
            get { return m_filter; }
        }

        private FilterDialog()
        {
            InitializeComponent();
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            string f;
            if (IsRegExp.Checked)
            {
                try
                {
                    System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(FilterText.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The expression is not a valid regular expression: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                f = FilterText.Text;
            }
            else
                f = Library.Core.FilenameFilter.ConvertGlobbingToRegExp(FilterText.Text);

            m_filter = new KeyValuePair<bool, string>(Inclusive.Checked, f);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void FilterDialog_Load(object sender, EventArgs e)
        {
            Inclusive.Checked = m_filter.Key;
            Exclusive.Checked = !m_filter.Key;
            FilterText.Text = m_filter.Value;
            IsRegExp.Checked = !string.IsNullOrEmpty(m_filter.Value);

            try { FilterText.Focus(); }
            catch { }

        }
    }
}