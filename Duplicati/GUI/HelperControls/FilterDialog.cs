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
                    new System.Text.RegularExpressions.Regex(FilterText.Text);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Strings.FilterDialog.InvalidRegExpMessage, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
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

        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
                if (IsRegExp.Checked)
                    FilterText.Text = Duplicati.Library.Core.FilenameFilter.ConvertGlobbingToRegExp(Duplicati.Library.Core.Utility.AppendDirSeparator(folderBrowserDialog.SelectedPath));
                else
                    FilterText.Text = Duplicati.Library.Core.Utility.AppendDirSeparator(folderBrowserDialog.SelectedPath);
        }

        private void BrowseFileButton_Click(object sender, EventArgs e)
        {
            if (openFileDialog.ShowDialog(this) == DialogResult.OK)
                if (IsRegExp.Checked)
                    FilterText.Text = Duplicati.Library.Core.FilenameFilter.ConvertGlobbingToRegExp(openFileDialog.FileName);
                else
                    FilterText.Text = openFileDialog.FileName;
        }

        private void HelpImage_Click(object sender, EventArgs e)
        {
            Duplicati.Library.Core.UrlUtillity.OpenUrl("http://code.google.com/p/duplicati/wiki/FilterUsage");
        }

        private void HelpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            HelpImage_Click(sender, e);
        }
    }
}