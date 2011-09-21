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
        private List<FilterDialog.FilterEntry> GetFilterList(bool includeDynamicFilters)
        {
            List<FilterDialog.FilterEntry> filters = new List<FilterDialog.FilterEntry>();
            foreach (ListViewItem lvi in listView.Items)
                filters.Add(lvi.Tag as FilterDialog.FilterEntry);

            if (includeDynamicFilters && !string.IsNullOrEmpty(m_dynamicFilter))
            {
                List<KeyValuePair<bool, string>> tmp1 = Library.Utility.FilenameFilter.DecodeFilter(m_dynamicFilter);
                List<FilterDialog.FilterEntry> tmp2 = new List<FilterDialog.FilterEntry>();
                foreach (KeyValuePair<bool, string> k in tmp1)
                    tmp2.Add(new FilterDialog.FilterEntry(k.Key, k.Value, null));
                tmp2.AddRange(filters);
                filters = tmp2;
            }

            return filters;
        }

        private string EncodeAsFilter(List<FilterDialog.FilterEntry> items)
        {
            List<KeyValuePair<bool, string>> r = new List<KeyValuePair<bool, string>>();
            foreach (FilterDialog.FilterEntry f in items)
                r.Add(new KeyValuePair<bool, string>(f.Include, f.Filter));

            return Duplicati.Library.Utility.FilenameFilter.EncodeAsFilter(r);
        }

        /// <summary>
        /// Gets or sets the filter string as displayed in the UI
        /// </summary>
        [DefaultValue("")]
        public string FilterXml
        {
            get { return EncodeFilterAsXml(GetFilterList(false)); }
            set { m_filter = value; RefreshList(); FilenameTester_TextChanged(null, null); }
        }


        private string EncodeFilterAsXml(List<FilterDialog.FilterEntry> list)
        {
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement("root"));

            foreach (FilterDialog.FilterEntry f in list)
            {
                System.Xml.XmlNode filter = root.AppendChild(doc.CreateElement("filter"));
                filter.Attributes.Append(doc.CreateAttribute("include")).Value = f.Include.ToString();
                filter.Attributes.Append(doc.CreateAttribute("filter")).Value = f.Filter;
                filter.Attributes.Append(doc.CreateAttribute("globbing")).Value = f.Globbing;
            }

            return doc.OuterXml;
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
            foreach (FilterDialog.FilterEntry f in DecodeFilter(m_filter))
                listView.Items.Add(f.CreateListViewItem());

            if (listView.Items.Count > 0)
                listView.Items[0].Selected = true;
        }

        private List<FilterDialog.FilterEntry> DecodeFilter(string filter)
        {
            List<FilterDialog.FilterEntry> res = new List<FilterDialog.FilterEntry>();
            if (string.IsNullOrEmpty(filter))
                return res;

            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(filter);
            foreach (System.Xml.XmlNode n in doc.SelectNodes("root/filter"))
            {
                res.Add(new FilterDialog.FilterEntry(
                    bool.Parse(n.Attributes["include"].Value),
                    n.Attributes["filter"].Value,
                    n.Attributes["globbing"].Value
                ));
            }

            return res;
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
                if (filename.StartsWith(Library.Utility.Utility.AppendDirSeparator(s), Library.Utility.Utility.ClientFilenameStringComparision))
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

            if (basepath.Equals(filename, Library.Utility.Utility.ClientFilenameStringComparision))
            {
                SetTooltipMessage(string.Format(Strings.FilterEditor.FolderIsRootFolder, filename));
                TestResults.Image = imageList.Images[0];
                TestResults.Visible = true;
                return;
            }


            //Build a list of parent folders to check
            List<string> parentFolders = new List<string>();
            string folder = filename;
            while (folder.Length > basepath.Length)
            {
                folder = System.IO.Path.GetDirectoryName(folder);
                parentFolders.Add(Duplicati.Library.Utility.Utility.AppendDirSeparator(folder));
            }

            //Work from the source towards the path
            parentFolders.Reverse();

            //Test if any of the parent folders are excluded
            string compact = EncodeAsFilter(GetFilterList(true));
            Library.Utility.FilenameFilter fn = new Duplicati.Library.Utility.FilenameFilter(Duplicati.Library.Utility.FilenameFilter.DecodeFilter(compact));
            Library.Utility.IFilenameFilter match;
            foreach (string s in parentFolders)
            {
                //Skip rootpath
                if (basepath.Equals(s, Library.Utility.Utility.ClientFilenameStringComparision))
                    continue;

                if (!fn.ShouldInclude(basepath, s, out match))
                {
                    SetTooltipMessage(string.Format(Strings.FilterEditor.FolderIsExcluded, s, ((Library.Utility.RegularExpressionFilter)match).Expression.ToString()));
                    TestResults.Image = imageList.Images[1];
                    TestResults.Visible = true;
                    return;
                }
            }

            //Update image based on the inclusion status
            TestResults.Image = imageList.Images[fn.ShouldInclude(basepath, filename, out match) ? 0 : 1];

            //Set the tooltip to inform the user of the result
            if (match == null)
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsIncludedDefault, filename));
            else if (match.Include)
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsIncluded, filename, ((Library.Utility.RegularExpressionFilter)match).Expression.ToString()));
            else
                SetTooltipMessage(string.Format(Strings.FilterEditor.FileIsExcluded, filename, ((Library.Utility.RegularExpressionFilter)match).Expression.ToString()));

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
            if (listView.SelectedItems.Count == 1 && listView.SelectedItems[0].Tag is FilterDialog.FilterEntry)
            {
                FilterDialog dlg = new FilterDialog((FilterDialog.FilterEntry)listView.SelectedItems[0].Tag);
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    listView.SelectedItems[0].Tag = dlg.Filter;
                    listView.SelectedItems[0].Text = dlg.Filter.DisplayValue;
                    listView.SelectedItems[0].ImageKey = dlg.Filter.ImageKey;
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
            FilterDialog dlg = new FilterDialog(new FilterDialog.FilterEntry(false, null, null));
            if (dlg.ShowDialog(this) == DialogResult.OK)
            {
                listView.Items.Add(dlg.Filter.CreateListViewItem());
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
            Duplicati.Library.Utility.UrlUtillity.OpenUrl("http://code.google.com/p/duplicati/wiki/FilterUsage");
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
                string path = Duplicati.Library.Utility.Utility.AppendDirSeparator(folderBrowserDialog.SelectedPath);
                FilterDialog.FilterEntry fe = new FilterDialog.FilterEntry(true, Duplicati.Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(path), path);
                listView.Items.Add(fe.CreateListViewItem());
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
                string path = Duplicati.Library.Utility.Utility.AppendDirSeparator(folderBrowserDialog.SelectedPath);
                FilterDialog.FilterEntry fe = new FilterDialog.FilterEntry(false, Duplicati.Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(path), path);
                listView.Items.Add(fe.CreateListViewItem());
                FilenameTester_TextChanged(sender, e);
            }
        }

        private void btnTestSearch_Click(object sender, EventArgs e) {
            TestSearchSelection dlg = new TestSearchSelection();
            dlg.Paths = BasePath;
            dlg.Filters = GetFilterList(false);
            dlg.DynamicFilters = m_dynamicFilter;

            if (dlg.ShowDialog() == DialogResult.OK)
            {
                listView.Items.Clear();
                foreach (var filter in dlg.Filters)
                    listView.Items.Add(filter.CreateListViewItem());
            }
                    
        }
    }
}
