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
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class SelectFiles : WizardControl
    {
        private WorkerThread<string> m_calculator;
        private object m_lock = new object();
        private Dictionary<string, long> m_sizes;

        private WizardSettingsWrapper m_wrapper;

        private string m_myPictures;
        private string m_myMusic;
        private string m_desktop;
        private string m_appData;
        private string m_myDocuments;

        private string[] m_specialFolders;

        private bool m_warnedFiltersChanged = true;

        public SelectFiles()
            : base(Strings.SelectFiles.PageTitle, Strings.SelectFiles.PageDescription)
        {
            InitializeComponent();
            m_sizes = new Dictionary<string, long>();

            //TODO: If any of these variables point to the same folder, bad things will happen...
            m_myPictures = Library.Core.Utility.AppendDirSeperator("!" + System.Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            m_myMusic = Library.Core.Utility.AppendDirSeperator("!" + System.Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            m_desktop = Library.Core.Utility.AppendDirSeperator("!" + System.Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
            m_appData = Library.Core.Utility.AppendDirSeperator("!" + System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            m_myDocuments = Library.Core.Utility.AppendDirSeperator("!" + System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            FolderTooltip.SetToolTip(IncludeDocuments, m_myDocuments.Substring(1));
            FolderTooltip.SetToolTip(IncludeMusic, m_myMusic.Substring(1));
            FolderTooltip.SetToolTip(IncludeImages, m_myPictures.Substring(1));
            FolderTooltip.SetToolTip(IncludeDesktop, m_desktop.Substring(1));
            FolderTooltip.SetToolTip(IncludeSettings, m_appData.Substring(1));

            m_specialFolders = new string[] { 
                m_myDocuments.Substring(1), 
                m_myMusic.Substring(1), 
                m_myPictures.Substring(1), 
                m_appData.Substring(1), 
                m_desktop.Substring(1)
            };

            base.PageEnter += new PageChangeHandler(SelectFiles_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectFiles_PageLeave);
        }

        void m_owner_Cancelled(object sender, CancelEventArgs e)
        {
            if (m_calculator != null)
                m_calculator.ClearQueue(true);
        }

        void SelectFiles_PageLeave(object sender, PageChangedArgs args)
        {
            m_owner.Cancelled -= new CancelEventHandler(m_owner_Cancelled);
            m_settings["Files:Sizes"] = m_sizes;
            m_settings["Files:WarnedFilters"] = m_warnedFiltersChanged;
            
            if (m_calculator != null)
                m_calculator.ClearQueue(true);

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (DocumentsRadio.Checked)
            {
                if (!m_warnedFiltersChanged)
                {
                    if (MessageBox.Show(this, Strings.SelectFiles.ModifiedFiltersWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                    {
                        args.Cancel = true;
                        return;
                    }

                    m_warnedFiltersChanged = true;
                }

                List<string> folders = new List<string>();
                List<string> exfolders = new List<string>();
                if (IncludeDocuments.Checked)
                    folders.Add(m_myDocuments.Substring(1));
                else
                    exfolders.Add(m_myDocuments.Substring(1));

                if (IncludeImages.Checked)
                    folders.Add(m_myPictures.Substring(1));
                else
                    exfolders.Add(m_myPictures.Substring(1));

                if (IncludeMusic.Checked)
                    folders.Add(m_myMusic.Substring(1));
                else
                    exfolders.Add(m_myMusic.Substring(1));

                if (IncludeDesktop.Checked)
                    folders.Add(m_desktop.Substring(1));
                else
                    exfolders.Add(m_desktop.Substring(1));

                if (IncludeSettings.Checked)
                    folders.Add(m_appData.Substring(1));
                else
                    exfolders.Add(m_appData.Substring(1));

                if (folders.Count == 0)
                {
                    MessageBox.Show(this, Strings.SelectFiles.NoFilesSelectedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                //Find the most common root
                string basefolder = folders[0];
                bool hasCommonParent = false;

                foreach (string f in folders)
                    if (basefolder.StartsWith(f))
                        basefolder = f;
                    else if (!f.StartsWith(basefolder))
                    {
                        string[] p1 = basefolder.Split(System.IO.Path.DirectorySeparatorChar);
                        string[] p2 = f.Split(System.IO.Path.DirectorySeparatorChar);

                        int ix = 0;
                        while (p1.Length > ix && p2.Length > ix && p1[ix] == p2[ix])
                            ix++;

                        if (ix == 0)
                        {
                            MessageBox.Show(this, Strings.SelectFiles.MultipleSourcesError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            args.Cancel = true;
                            return;
                        }

                        hasCommonParent = true;
                        basefolder = Library.Core.Utility.AppendDirSeperator(string.Join(System.IO.Path.DirectorySeparatorChar.ToString(), p1, 0, ix));
                    }

                m_wrapper.EncodedFilters = EncodeFilterString(basefolder, hasCommonParent, folders, exfolders, m_wrapper.EncodedFilters);
                m_wrapper.SourcePath = basefolder;
            }
            else
            {
                if (!System.IO.Directory.Exists(TargetFolder.Text))
                {
                    MessageBox.Show(this, string.Format(Strings.SelectFiles.FolderDoesNotExistError, TargetFolder.Text), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                m_wrapper.SourcePath = TargetFolder.Text;
            }

            if (m_calculator != null)
                m_calculator.ClearQueue(true);

            m_settings["Files:WarnedFilters"] = m_warnedFiltersChanged;
            args.NextPage = new PasswordSettings();
        }

        private string EncodeFilterString(string basefolder, bool hasCommonParent, List<string> folders, List<string> exfolders, string existingFilter)
        {
            List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();

            List<KeyValuePair<bool, string>> extras = new List<KeyValuePair<bool, string>>();
            foreach (KeyValuePair<bool, string> tf in Library.Core.FilenameFilter.DecodeFilter(existingFilter))
                if (!tf.Value.StartsWith(System.IO.Path.DirectorySeparatorChar.ToString()))
                    extras.Add(tf);

            folders.Sort();
            folders.Reverse();

            //Include selected folders
            foreach (string f in folders)
            {
                //Exclude subfolders
                foreach (string s in exfolders)
                    if (s.StartsWith(f) && (basefolder == s || s.StartsWith(basefolder)))
                        filters.Add(new KeyValuePair<bool, string>(false, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(s.Substring(basefolder.Length - 1) + "*")));

                if (hasCommonParent)
                    filters.Add(new KeyValuePair<bool, string>(true, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(f.Substring(basefolder.Length - 1) + "*")));
            }

            //Exclude everything else if they have a non-included parent
            if (hasCommonParent)
                filters.Add(new KeyValuePair<bool, string>(false, Library.Core.FilenameFilter.ConvertGlobbingToRegExp("*")));

            extras.AddRange(filters);
            return Library.Core.FilenameFilter.EncodeAsFilter(extras);
        }

        void SelectFiles_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_owner.Cancelled += new CancelEventHandler(m_owner_Cancelled);

            if (m_settings.ContainsKey("Files:Sizes"))
                m_sizes = (Dictionary<string, long>)m_settings["Files:Sizes"];
            if (m_settings.ContainsKey("Files:WarnedFilters"))
                m_warnedFiltersChanged = (bool)m_settings["Files:WarnedFilters"];

            if (!m_valuesAutoLoaded)
            {
                if (string.IsNullOrEmpty(m_wrapper.SourcePath))
                {
                    DocumentsRadio.Checked = true;
                    IncludeDocuments.Checked = true;
                    IncludeImages.Checked = true;
                    IncludeMusic.Checked = false;
                    IncludeDesktop.Checked = true;
                    IncludeSettings.Checked = false;
                }
                else
                {
                    string p = Library.Core.Utility.AppendDirSeperator(m_wrapper.SourcePath);
                    List<KeyValuePair<bool, string>> filters = Library.Core.FilenameFilter.DecodeFilter(m_wrapper.EncodedFilters);

                    bool hasCommonBase = false;
                    List<CheckBox> included = new List<CheckBox>();
                    List<CheckBox> excluded = new List<CheckBox>();

                    string startChar = Library.Core.FilenameFilter.ConvertGlobbingToRegExp(System.IO.Path.DirectorySeparatorChar.ToString());
                    Dictionary<string, CheckBox> specialFolders = new Dictionary<string,CheckBox>();

                    if (m_myDocuments.Substring(1).StartsWith(p))
                    {
                        specialFolders.Add(Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_myDocuments.Substring(p.Length) + "*"), IncludeDocuments);
                        specialFolders.Add(Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_myDocuments.Substring(1) + "*"), IncludeDocuments);
                    }
                    if (m_myPictures.Substring(1).StartsWith(p))
                    {
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_myPictures.Substring(p.Length) + "*")] = IncludeImages;
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_myPictures.Substring(1) + "*")] = IncludeImages;
                    }
                    if (m_myMusic.Substring(1).StartsWith(p))
                    {
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_myMusic.Substring(p.Length) + "*")] = IncludeMusic;
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_myMusic.Substring(1) + "*")] = IncludeMusic;
                    }
                    if (m_desktop.Substring(1).StartsWith(p))
                    {
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_desktop.Substring(p.Length) + "*")] = IncludeDesktop;
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_desktop.Substring(1) + "*")] = IncludeDesktop;
                    }
                    if (m_appData.Substring(1).StartsWith(p))
                    {
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_appData.Substring(p.Length) + "*")] = IncludeSettings;
                        specialFolders[Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_appData.Substring(1) + "*")] = IncludeSettings;
                    }

                    bool unsupported = false;

                    foreach (KeyValuePair<bool, string> s in filters)
                    {
                        hasCommonBase |= (s.Key == false && s.Value == ".*");

                        if (s.Value.StartsWith(startChar))
                        {
                            if (specialFolders.ContainsKey(s.Value))
                            {
                                if (s.Key)
                                    included.Add(specialFolders[s.Value]);
                                else
                                    excluded.Add(specialFolders[s.Value]);
                            }
                            else
                                unsupported = true;
                        }
                    }

                    string baseEncoded = Library.Core.FilenameFilter.ConvertGlobbingToRegExp(p);
                    foreach (string s in specialFolders.Keys)
                        if (!excluded.Contains(specialFolders[s]) && s.StartsWith(baseEncoded) && !included.Contains(specialFolders[s]))
                            included.Add(specialFolders[s]);

                    TargetFolder.Text = m_wrapper.SourcePath;

                    IncludeDocuments.Checked =
                        IncludeImages.Checked =
                        IncludeMusic.Checked =
                        IncludeDesktop.Checked =
                        IncludeSettings.Checked = false;

                    foreach (CheckBox c in included)
                        c.Checked = true;

                    if (unsupported || included.Count == 0)
                        FolderRadio.Checked = true;
                    else
                        DocumentsRadio.Checked = true;

                    m_warnedFiltersChanged = !unsupported;
                }
            }

            if (FolderRadio.Checked)
                TargetFolder_Leave(null, null);
            else
                Rescan();
        }

        private void StartCalculator()
        {
            if (m_calculator == null)
            {
                totalSize.Text = Strings.SelectFiles.CalculatingSize;
                m_calculator = new WorkerThread<string>(CalculateFolderSize, false);
                m_calculator.CompletedWork += new EventHandler(m_calculator_CompletedWork);
            }
        }

        void m_calculator_CompletedWork(object sender, EventArgs e)
        {

            if (this.InvokeRequired)
                this.Invoke(new EventHandler(m_calculator_CompletedWork), sender, e);
            else
            {
                if (m_calculator == null)
                    return;

                long s = 0;

                if (DocumentsRadio.Checked)
                {
                    if (m_sizes.ContainsKey(m_myDocuments) && IncludeDocuments.Checked)
                        s += m_sizes[m_myDocuments];
                    if (m_sizes.ContainsKey(m_myMusic) && IncludeMusic.Checked)
                        s += m_sizes[m_myMusic];
                    if (m_sizes.ContainsKey(m_myPictures) && IncludeImages.Checked)
                        s += m_sizes[m_myPictures];
                    if (m_sizes.ContainsKey(m_appData) && IncludeSettings.Checked)
                        s += m_sizes[m_appData];
                    if (m_sizes.ContainsKey(m_desktop) && IncludeDesktop.Checked)
                        s += m_sizes[m_desktop];
                }
                else
                    s = m_sizes.ContainsKey(TargetFolder.Text) ? m_sizes[TargetFolder.Text] : 0;
                
                if (m_calculator.CurrentTasks.Count == 0 && !m_calculator.Active)
                    totalSize.Text = string.Format(Strings.SelectFiles.FinalSizeCalculated, Library.Core.Utility.FormatSizeString(s));
                else
                    totalSize.Text = string.Format(Strings.SelectFiles.PartialSizeCalculated, Library.Core.Utility.FormatSizeString(s));

                if (m_sizes.ContainsKey(m_myMusic))
                    myMusicSize.Text = Library.Core.Utility.FormatSizeString(m_sizes[m_myMusic]);
                if (m_sizes.ContainsKey(m_myPictures))
                    myPicturesSize.Text = Library.Core.Utility.FormatSizeString(m_sizes[m_myPictures]);
                if (m_sizes.ContainsKey(m_desktop))
                    desktopSize.Text = Library.Core.Utility.FormatSizeString(m_sizes[m_desktop]);
                if (m_sizes.ContainsKey(m_appData))
                    appdataSize.Text = Library.Core.Utility.FormatSizeString(m_sizes[m_appData]);
                if (m_sizes.ContainsKey(m_myDocuments))
                    myDocumentsSize.Text = Library.Core.Utility.FormatSizeString(m_sizes[m_myDocuments]);

                if (m_sizes.ContainsKey(TargetFolder.Text))
                    customSize.Text = Library.Core.Utility.FormatSizeString(m_sizes[TargetFolder.Text]);
            }
        }

        private void Rescan()
        {
            lock (m_lock)
            {
                StartCalculator();
                if (!m_sizes.ContainsKey(m_myMusic))
                    m_calculator.AddTask(m_myMusic);
                if (!m_sizes.ContainsKey(m_myPictures))
                    m_calculator.AddTask(m_myPictures);
                if (!m_sizes.ContainsKey(m_desktop))
                    m_calculator.AddTask(m_desktop);
                if (!m_sizes.ContainsKey(m_appData))
                    m_calculator.AddTask(m_appData);
                if (!m_sizes.ContainsKey(m_myDocuments))
                    m_calculator.AddTask(m_myDocuments);
                
                m_calculator_CompletedWork(null, null);
            }
        }

        private void BrowseFolderButton_Click(object sender, EventArgs e)
        {
            if (folderBrowserDialog.ShowDialog(this) == DialogResult.OK)
            {
                TargetFolder.Text = folderBrowserDialog.SelectedPath;
                TargetFolder_Leave(null, null);
            }
        }

        private void TargetType_CheckedChanged(object sender, EventArgs e)
        {
            DocumentGroup.Enabled = DocumentsRadio.Checked;
            FolderGroup.Enabled = FolderRadio.Checked;
            if (DocumentsRadio.Checked)
                Rescan();
            else
                TargetFolder_Leave(sender, e);
        }

        private void CalculateFolderSize(string folder)
        {
            lock (m_lock)
                if (m_sizes.ContainsKey(folder))
                    return;

            long size = 0;

            Library.Core.FilenameFilter fnf = null;
            string folderkey = folder;

            //Special folders, extended logic
            if (folder.StartsWith("!"))
            {
                folder = folder.Substring(1);
                List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool,string>>();

                filters.Add(new KeyValuePair<bool, string>(true, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(folder + "*")));

                foreach (string s in m_specialFolders)
                    if (s != folder && s.StartsWith(folder))
                        filters.Add(new KeyValuePair<bool, string>(false, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(s + "*")));


                if (filters.Count > 1)
                    fnf = new Duplicati.Library.Core.FilenameFilter(filters);
            }

            //Calculate outside lock
            size = Duplicati.Library.Core.Utility.GetDirectorySize(folder, fnf);
            lock (m_lock)
                m_sizes[folderkey] = size;
        }

        private void TargetFolder_Leave(object sender, EventArgs e)
        {
            lock (m_lock)
                if (TargetFolder.Text.Trim().Length > 0 && !m_sizes.ContainsKey(TargetFolder.Text))
                {
                    StartCalculator();
                    m_calculator.ClearQueue(true);
                    m_calculator.AddTask(TargetFolder.Text);
                    m_calculator_CompletedWork(null, null);
                }
                else if (TargetFolder.Text == "")
                    totalSize.Text = "";
                else if (m_sizes.ContainsKey(TargetFolder.Text))
                {
                    StartCalculator();
                    m_calculator.ClearQueue(true);
                    m_calculator_CompletedWork(null, null);
                }
        }

        private void SelectFiles_VisibleChanged(object sender, EventArgs e)
        {
            if (!this.Visible && m_calculator != null)
            {
                m_calculator.Terminate(false);
                m_calculator = null;
            }
        }
    }
}
