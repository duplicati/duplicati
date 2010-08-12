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
    /// <summary>
    /// The filter editor is used to graphically edit a set of rules for including or excluding files and folders
    /// </summary>
    public partial class FilterEditor : UserControl
    {
        private string[] m_basepath;
        private string m_filter = "";
        private string m_dynamicFilter;


        /// <summary>
        /// Initializes a new instance of the <see cref="FilterEditor"/> class.
        /// </summary>
        public FilterEditor()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Gets the current filter list.
        /// </summary>
        /// <param name="includeDynamicFilters">if set to <c>true</c> include dynamic filters.</param>
        /// <returns>A list of filters</returns>
        private List<KeyValuePair<bool, string>> GetFilterList(bool includeDynamicFilters)
        {
            List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();
            foreach (ListViewItem lvi in listView.Items)
                filters.Add(new KeyValuePair<bool, string>(lvi.ImageIndex == 0, lvi.Text));

            if (includeDynamicFilters && !string.IsNullOrEmpty(m_dynamicFilter))
            {
                List<KeyValuePair<bool, string>> tmp = Library.Core.FilenameFilter.DecodeFilter(m_dynamicFilter);
                tmp.AddRange(filters);
                filters = tmp;
            }

            return filters;
        }

        /// <summary>
        /// Gets or sets the filter string as displayed in the UI
        /// </summary>
        [DefaultValue("")]
        public string Filter
        {
            get { return Library.Core.FilenameFilter.EncodeAsFilter(GetFilterList(false)); }
            set { m_filter = value; RefreshList(); FilenameTester_TextChanged(null, null); }
        }


        /// <summary>
        /// Gets or sets the dynamic filter, which is applied to the test area, but not visible in the list
        /// </summary>
        [DefaultValue("")]
        public string DynamicFilter
        {
            get { return m_dynamicFilter; }
            set { m_dynamicFilter = value; FilenameTester_TextChanged(null, null); }
        }

        /// <summary>
        /// Gets or sets the list of source folders
        /// </summary>
        [DefaultValue(null)]
        public string[] BasePath
        {
            get { return m_basepath; }
            set { m_basepath = value; FilenameTester_TextChanged(null, null); }
        }

        /// <summary>
        /// Refreshes the UI with the values found in the filter variable
        /// </summary>
        private void RefreshList()
        {
            listView.Items.Clear();
            foreach (KeyValuePair<bool, string> f in Library.Core.FilenameFilter.DecodeFilter(m_filter))
                listView.Items.Add(f.Value, f.Key ? 0 : 1);

            if (listView.Items.Count > 0)
                listView.Items[0].Selected = true;
        }

        /// <summary>
        /// Handles the TextChanged event of the FilenameTester control.
        /// Used to update the status and tooltip to show if the current path is included or excluded
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void FilenameTester_TextChanged(object sender, EventArgs e)
        {
            //Basic sanity check
            if (FilenameTester.Text.Trim().Length == 0 || m_basepath == null || m_basepath.Length == 0 || (m_basepath.Length == 1 && string.IsNullOrEmpty(m_basepath[0])))
            {
                SetTooltipMessage("");
                TestResults.Visible = false;
                return;
            }

            string filename = FilenameTester.Text;
            
            //Prevent exceptions based on invalid input
            if (filename.IndexOfAny(System.IO.Path.GetInvalidPathChars()) >= 0)
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.InvalidCharsInFilenameError, filename));
                TestResults.Visible = false;
                return;
            }

            try
            {
                //Certain paths cause exceptions anyway
                System.IO.Path.GetFullPath(filename);
            }
            catch
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.InvalidCharsInFilenameError, filename));
                TestResults.Visible = false;
                return;
            }

            //System.IO.Path.IsPathRooted returns true for filenames starting with a backslash on windows
            if (System.IO.Path.GetFullPath(filename) != filename)
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.FilepathIsNotAbsoluteError, filename));
                TestResults.Visible = false;
                return;
            }

            //Find the source folder that matches the path entered
            string basepath = null;
            foreach (string s in m_basepath)
                if (filename.StartsWith(Library.Core.Utility.AppendDirSeperator(s), Library.Core.Utility.ClientFilenameStringComparision))
                {
                    basepath = s;
                    break;
                }

            if (string.IsNullOrEmpty(basepath))
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.FilenameIsNotInAnyRootFolder, filename, m_basepath));
                TestResults.Visible = false;
                return;
            }

            //Build a list of parent folders to check
            List<string> parentFolders = new List<string>();
            string folder = filename;
            while (folder.Length > basepath.Length)
            {
                folder = System.IO.Path.GetDirectoryName(folder);
                parentFolders.Add(Duplicati.Library.Core.Utility.AppendDirSeperator(folder));
            }

            //Work from the source towards the path
            parentFolders.Reverse();

            //Test if any of the parent folders are excluded
            Library.Core.FilenameFilter fn = new Duplicati.Library.Core.FilenameFilter(GetFilterList(true));
            Library.Core.IFilenameFilter match;
            foreach (string s in parentFolders)
                if (!fn.ShouldInclude(basepath, s, out match))
                {
                    SetTooltipMessage(string.Format(Strings.FilterEditor.FolderIsExcluded, s, ((Library.Core.RegularExpressionFilter)match).Expression.ToString()));
                    TestResults.Image = imageList.Images[1];
                    TestResults.Visible = true;
                    return;
                }

            //Update image based on the inclusion status
            TestResults.Image = imageList.Images[fn.ShouldInclude(basepath, filename, out match) ? 0 : 1];

            //Set the tooltip to inform the user of the result
            if (match == null)
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsIncludedDefault, filename));
            else if (match.Include)
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsIncluded, filename, ((Library.Core.RegularExpressionFilter)match).Expression.ToString()));
            else
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsExcluded, filename, ((Library.Core.RegularExpressionFilter)match).Expression.ToString()));

            TestResults.Visible = true;
        }

        /// <summary>
        /// Sets the tooltip message.
        /// </summary>
        /// <param name="message">The message to show as the tooltip.</param>
        private void SetTooltipMessage(string message)
        {
            toolTip.SetToolTip(FilenameTester, message);
            toolTip.SetToolTip(label1, message);
            toolTip.SetToolTip(TestResults, message);
            if (TestResults != null && this.Visible)
                toolTip.Show(message, TestResults, 5000);
        }

        /// <summary>
        /// Handles the SelectedIndexChanged event of the listView control.
        /// Used to update the enabled state of the buttons.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void listView_SelectedIndexChanged(object sender, EventArgs e)
        {
            RemoveFilterButton.Enabled =
            EditFilterButton.Enabled = listView.SelectedItems.Count == 1;

            MoveFilterUpButton.Enabled =
            MoveFilterTopButton.Enabled = listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Index != 0;

            MoveFilterDownButton.Enabled =
            MoveFilterBottomButton.Enabled = listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Index != listView.Items.Count - 1;
        }

        /// <summary>
        /// Handles the Click event of the RemoveFilterButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void RemoveFilterButton_Click(object sender, EventArgs e)
        {
            while (listView.SelectedItems.Count > 0)
                listView.Items.Remove(listView.SelectedItems[0]);
            FilenameTester_TextChanged(sender, e);
        }

        /// <summary>
        /// Handles the Click event of the EditFilterButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
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

        /// <summary>
        /// Handles the Click event of the MoveFilterUpButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
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

        /// <summary>
        /// Handles the Click event of the MoveFilterDownButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
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

        /// <summary>
        /// Handles the Click event of the MoveFilterTopButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
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

        /// <summary>
        /// Handles the Click event of the MoveFilterBottomButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
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

        /// <summary>
        /// Handles the Click event of the AddFilterButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void AddFilterButton_Click(object sender, EventArgs e)
        {
            FilterDialog dlg = new FilterDialog(new KeyValuePair<bool, string>(true, ""));
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                listView.Items.Add(dlg.Filter.Value, dlg.Filter.Key ? 0 : 1);
                FilenameTester_TextChanged(sender, e);
            }
        }

        /// <summary>
        /// Handles the Load event of the FilterEditor control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void FilterEditor_Load(object sender, EventArgs e)
        {
            if (listView.Items.Count != 0)
                listView.Items[0].Selected = true;
        }

        /// <summary>
        /// Handles the DoubleClick event of the listView control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void listView_DoubleClick(object sender, EventArgs e)
        {
            if (EditFilterButton.Enabled)
                EditFilterButton.PerformClick();
        }

        /// <summary>
        /// Handles the Click event of the help button
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void HelpButton_Click(object sender, EventArgs e)
        {
            UrlUtillity.OpenUrl("http://code.google.com/p/duplicati/wiki/FilterUsage");
        }


        /// <summary>
        /// Handles the Click event of the IncludeFolderButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void IncludeFolderButton_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.Description = Strings.FilterEditor.IncludeFolderBrowseTitle;
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                listView.Items.Add(Duplicati.Library.Core.FilenameFilter.ConvertGlobbingToRegExp(Duplicati.Library.Core.Utility.AppendDirSeperator(folderBrowserDialog.SelectedPath)), 0);
                FilenameTester_TextChanged(sender, e);
            }
        }

        /// <summary>
        /// Handles the Click event of the ExcludeFolderButton control.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="e">The <see cref="System.EventArgs"/> instance containing the event data.</param>
        private void ExcludeFolderButton_Click(object sender, EventArgs e)
        {
            folderBrowserDialog.Description = Strings.FilterEditor.ExcludeFolderBrowseTitle;
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                listView.Items.Add(Duplicati.Library.Core.FilenameFilter.ConvertGlobbingToRegExp(Duplicati.Library.Core.Utility.AppendDirSeperator(folderBrowserDialog.SelectedPath)), 1);
                FilenameTester_TextChanged(sender, e);
            }
        }
    }
}
