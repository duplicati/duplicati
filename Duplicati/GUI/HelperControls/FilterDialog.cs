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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.HelperControls
{
    public partial class FilterDialog : Form
    {
        public class FilterEntry
        {
            public FilterEntry(bool include, string filter, string globbing)
            {
                this.Include = include;
                this.Filter = filter;
                this.Globbing = globbing;
            }

            public string Filter;
            public bool Include;
            public string Globbing;

            public string DisplayValue { get { return string.IsNullOrEmpty(this.Globbing) ? this.Filter : this.Globbing; } }
            public string ImageKey
            {
                get
                {
                    return
                        (string.IsNullOrEmpty(this.Globbing) ? "regexp" : "globbing")
                        + "-" +
                        (this.Include ? "include" : "exclude");
                }
            }
            public override string ToString()
            {
                return this.DisplayValue;
            }

            public ListViewItem CreateListViewItem()
            {
                ListViewItem lvi = new ListViewItem(this.DisplayValue, this.ImageKey);
                lvi.Tag = this;
                return lvi;
            }
        }

        FilterEntry m_filter;

        public FilterDialog(FilterEntry filter)
            : this()
        {
            m_filter = filter;
        }

        public FilterEntry Filter
        {
            get { return m_filter; }
        }

        private FilterDialog()
        {
            InitializeComponent();
        }

        private void OKBtn_Click(object sender, EventArgs e)
        {
            string filter;
            string globbing;
            if (IsRegExp.Checked)
            {
                try
                {
                    new System.Text.RegularExpressions.Regex(FilterText.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Strings.FilterDialog.InvalidRegExpMessage, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                filter = FilterText.Text;
                globbing = null;
            }
            else
            {
                filter = Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(FilterText.Text);
                globbing = FilterText.Text;
            }

            m_filter = new FilterEntry(Inclusive.Checked, filter, globbing);
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void FilterDialog_Load(object sender, EventArgs e)
        {
            Inclusive.Checked = m_filter.Include;
            Exclusive.Checked = !m_filter.Include;
            FilterText.Text = m_filter.Filter;
            if (string.IsNullOrEmpty(m_filter.Globbing))
            {
                if (string.IsNullOrEmpty(m_filter.Filter))
                {
                    //New filter is default globbing
                    IsRegExp.Checked = false;
                    FilterText.Text = "";
                }
                else
                {
                    IsRegExp.Checked = true;
                    FilterText.Text = m_filter.Filter;
                }
            }
            else
            {
                IsRegExp.Checked = false;
                FilterText.Text = m_filter.Globbing;
            }

            try { FilterText.Focus(); }
            catch { }

        }

        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                if (IsRegExp.Checked)
                    FilterText.Text = Duplicati.Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(Duplicati.Library.Utility.Utility.AppendDirSeparator(folderBrowserDialog.SelectedPath));
                else
                    FilterText.Text = Duplicati.Library.Utility.Utility.AppendDirSeparator(folderBrowserDialog.SelectedPath);
        }

        private void BrowseFileButton_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                if (IsRegExp.Checked)
                    FilterText.Text = Duplicati.Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(openFileDialog.FileName);
                else
                    FilterText.Text = openFileDialog.FileName;
        }

        private void HelpImage_Click(object sender, EventArgs e)
        {
            Duplicati.Library.Utility.UrlUtillity.OpenUrl("http://code.google.com/p/duplicati/wiki/FilterUsage");
        }

        private void HelpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            HelpImage_Click(sender, e);
        }

        private void FilterText_TextChanged(object sender, EventArgs e)
        {
            OKBtn.Enabled = FilterText.Text!= null && FilterText.Text.Trim().Length > 0;
        }

        private void FilterDialog_Activated(object sender, EventArgs e)
        {
            try { FilterText.Focus(); }
            catch { }
        }
    }
}