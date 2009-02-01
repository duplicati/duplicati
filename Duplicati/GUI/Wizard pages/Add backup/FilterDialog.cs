using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class FilterDialog : Form
    {
        TaskFilter m_filter;

        public FilterDialog(TaskFilter filter)
            : this()
        {
            m_filter = filter;
        }

        private FilterDialog()
        {
            InitializeComponent();
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            m_filter.Include = Inclusive.Checked;
            if (IsRegExp.Checked)
            {
                try
                {
                    System.Text.RegularExpressions.Regex re = new System.Text.RegularExpressions.Regex(Filter.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "The expression is not a valid regular expression: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                m_filter.Filter = Filter.Text;
            }
            else
                m_filter.Filter = Library.Core.FilenameFilter.ConvertGlobbingToRegExp(Filter.Text);

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void FilterDialog_Load(object sender, EventArgs e)
        {
            if (m_filter != null)
            {
                Inclusive.Checked = m_filter.Include;
                Exclusive.Checked = !m_filter.Include;
                Filter.Text = m_filter.Filter;
                if (!string.IsNullOrEmpty(m_filter.Filter))
                    IsRegExp.Checked = true;
                else
                    IsRegExp.Checked = false;
            }

            try { Filter.Focus(); }
            catch { }

        }
    }
}