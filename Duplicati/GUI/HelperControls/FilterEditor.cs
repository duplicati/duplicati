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
    public partial class FilterEditor : UserControl
    {
        private string m_basepath = "";
        private string m_filter = "";

        public FilterEditor()
        {
            InitializeComponent();
        }

        private List<KeyValuePair<bool, string>> GetFilterList()
        {
            List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();
            foreach (ListViewItem lvi in listView.Items)
                filters.Add(new KeyValuePair<bool, string>(lvi.ImageIndex == 0, lvi.Text));

            return filters;
        }

        public string Filter
        {
            get { return Library.Core.FilenameFilter.EncodeAsFilter(GetFilterList()); }
            set { m_filter = value; RefreshList(); FilenameTester_TextChanged(null, null); }
        }

        public string BasePath
        {
            get { return m_basepath; }
            set { m_basepath = value; FilenameTester_TextChanged(null, null); }
        }

        private void RefreshList()
        {
            listView.Items.Clear();
            foreach (KeyValuePair<bool, string> f in Library.Core.FilenameFilter.DecodeFilter(m_filter))
                listView.Items.Add(f.Value, f.Key ? 0 : 1);

            if (listView.Items.Count > 0)
                listView.Items[0].Selected = true;
        }

        private void FilenameTester_TextChanged(object sender, EventArgs e)
        {
            if (FilenameTester.Text.Trim().Length == 0 || string.IsNullOrEmpty(m_basepath))
            {
                SetTooltipMessage("");
                TestResults.Visible = false;
                return;
            }

            string filename = FilenameTester.Text;

            if (filename.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.InvalidCharsInFilenameError, filename));
                TestResults.Visible = false;
                return;
            }

            if (!System.IO.Path.IsPathRooted(filename))
                filename = System.IO.Path.Combine(m_basepath, filename);

            if (!filename.StartsWith(m_basepath))
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.FilenameIsNotInRootFolder, filename, m_basepath));
                TestResults.Visible = false;
                return;
            }

            List<string> parentFolders = new List<string>();
            string folder = filename;
            string folder_cmp = folder;
            while (folder_cmp.Length > m_basepath.Length)
            {
                folder = System.IO.Path.GetDirectoryName(folder);
                folder_cmp = Duplicati.Library.Core.Utility.AppendDirSeperator(folder);
                if (!folder_cmp.Equals(m_basepath, Library.Core.Utility.IsFSCaseSensitive ? StringComparison.CurrentCulture : StringComparison.CurrentCultureIgnoreCase))
                    parentFolders.Add(folder_cmp);
                else
                    break;
            }

            parentFolders.Reverse();

            Library.Core.FilenameFilter fn = new Duplicati.Library.Core.FilenameFilter(GetFilterList());
            Library.Core.IFilenameFilter match;
            foreach(string s in parentFolders)
                if (!fn.ShouldInclude(m_basepath, s, out match))
                {
                    SetTooltipMessage(string.Format(Strings.FilterEditor.FolderIsExcluded, s, ((Library.Core.RegularExpressionFilter)match).Expression.ToString()));
                    TestResults.Image = imageList1.Images[1];
                    TestResults.Visible = true;
                    return;
                }

            TestResults.Image = imageList1.Images[fn.ShouldInclude(m_basepath, filename, out match) ? 0 : 1];

            if (match == null)
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsIncludedDefault, filename));
            else if (match.Include)
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsIncluded, filename, ((Library.Core.RegularExpressionFilter)match).Expression.ToString()));
            else
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsExcluded, filename, ((Library.Core.RegularExpressionFilter)match).Expression.ToString()));

            TestResults.Visible = true;
        }

        private void SetTooltipMessage(string message)
        {
            toolTip1.SetToolTip(FilenameTester, message);
            toolTip1.SetToolTip(label1, message);
            toolTip1.SetToolTip(TestResults, message);
            toolTip1.Show(message, TestResults, 5000);
        }

        private void listView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RemoveFilterButton.Enabled =
            EditFilterButton.Enabled = listView.SelectedItems.Count == 1;

            MoveFilterUpButton.Enabled =
            MoveFilterTopButton.Enabled = listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Index != 0;

            MoveFilterDownButton.Enabled =
            MoveFilterBottomButton.Enabled = listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Index != listView.Items.Count - 1;
        }

        private void RemoveFilterButton_Click(object sender, EventArgs e)
        {
            while (listView.SelectedItems.Count > 0)
                listView.Items.Remove(listView.SelectedItems[0]);
            FilenameTester_TextChanged(sender, e);
        }

        private void EditFilterButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                FilterDialog dlg = new FilterDialog(new KeyValuePair<bool, string>(listView.SelectedItems[0].ImageIndex == 0, listView.SelectedItems[0].Text));
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    listView.SelectedItems[0].Text = dlg.Filter.Value;
                    listView.SelectedItems[0].ImageIndex = dlg.Filter.Key ? 0 : 1;
                    FilenameTester_TextChanged(sender, e);
                }
            }
        }

        private void MoveFilterUpButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(index - 1, lvi);
                lvi.Selected = true;
                FilenameTester_TextChanged(sender, e);
            }
        }

        private void MoveFilterDownButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(index + 1, lvi);
                lvi.Selected = true;
                FilenameTester_TextChanged(sender, e);
            }
        }

        private void MoveFilterTopButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(0, lvi);
                lvi.Selected = true;
                FilenameTester_TextChanged(sender, e);
            }
        }

        private void MoveFilterBottomButton_Click(object sender, EventArgs e)
        {
            if (listView.SelectedItems.Count == 1)
            {
                ListViewItem lvi = listView.SelectedItems[0];
                int index = listView.SelectedItems[0].Index;
                listView.Items.RemoveAt(index);
                listView.Items.Insert(listView.Items.Count, lvi);
                lvi.Selected = true;
                FilenameTester_TextChanged(sender, e);
            }
        }

        private void AddFilterButton_Click(object sender, EventArgs e)
        {
            FilterDialog dlg = new FilterDialog(new KeyValuePair<bool, string>(true, ""));
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                listView.Items.Add(dlg.Filter.Value, dlg.Filter.Key ? 0 : 1);
                FilenameTester_TextChanged(sender, e);
            }
        }

        private void FilterEditor_Load(object sender, EventArgs e)
        {
            if (listView.Items.Count != 0)
                listView.Items[0].Selected = true;
        }

        private void listView_DoubleClick(object sender, EventArgs e)
        {
            if (EditFilterButton.Enabled)
                EditFilterButton.PerformClick();
        }
    }
}
