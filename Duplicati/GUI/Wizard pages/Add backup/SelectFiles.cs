#region Disclaimer / License
// Copyright (C) 2009, Kenneth Skovhede
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

        public SelectFiles()
            : base("Select files to backup", "On this page you must select the folder and files you wish to backup")
        {
            InitializeComponent();
            m_sizes = new Dictionary<string, long>();

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

        void SelectFiles_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["Files:Sizes"] = m_sizes;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (DocumentsRadio.Checked)
            {
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
                    MessageBox.Show(this, "You have not included any files.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
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
                            MessageBox.Show(this, "Due to your machine setup, it is not possible to backup\nthe selected folders in the same backup.\nTry unchecking some items, and create more than one backup.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            args.Cancel = true;
                            return;
                        }

                        hasCommonParent = true;
                        basefolder = Library.Core.Utility.AppendDirSeperator(string.Join(System.IO.Path.DirectorySeparatorChar.ToString(), p1, 0, ix));
                    }

                List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool, string>>();

                //Exclude everything if they have a non-included parent
                if (hasCommonParent)
                    filters.Add(new KeyValuePair<bool, string>(false, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(basefolder + "*")));

                //Include selected folders
                foreach (string f in folders)
                {
                    filters.Add(new KeyValuePair<bool, string>(true, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(f + "*")));

                    //Exclude subfolders
                    foreach (string s in exfolders)
                        if (s.StartsWith(f) && (basefolder == s || s.StartsWith(basefolder)))
                            filters.Add(new KeyValuePair<bool, string>(false, Library.Core.FilenameFilter.ConvertGlobbingToRegExp(s + "*")));
                }

                List<KeyValuePair<bool, string>> extras = new List<KeyValuePair<bool, string>>();
                string key = Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_wrapper.SourcePath);
                foreach(KeyValuePair<bool, string> tf in Library.Core.FilenameFilter.DecodeFilter(m_wrapper.EncodedFilters))
                    if (!tf.Value.StartsWith(key))
                        extras.Add(tf);

                if (filters.Count <= 1)
                    m_wrapper.EncodedFilters = Library.Core.FilenameFilter.EncodeAsFilter(extras);
                else
                {
                    filters.AddRange(extras);
                    m_wrapper.EncodedFilters = Library.Core.FilenameFilter.EncodeAsFilter(filters);
                }

                m_wrapper.SourcePath = basefolder;
            }
            else
            {
                if (!System.IO.Directory.Exists(TargetFolder.Text))
                {
                    MessageBox.Show(this, "The folder \"" + TargetFolder.Text + "\" does not exist.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                m_wrapper.SourcePath = TargetFolder.Text;
            }

            if (m_calculator != null)
                m_calculator.ClearQueue(true);

            args.NextPage = new PasswordSettings();
        }

        void SelectFiles_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (m_settings.ContainsKey("Files:Sizes"))
                m_sizes = (Dictionary<string, long>)m_settings["Files:Sizes"];

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

                    List<string> filters = new List<string>();
                    foreach (KeyValuePair<bool, string> tf in Library.Core.FilenameFilter.DecodeFilter(m_wrapper.EncodedFilters))
                        if (tf.Key && tf.Value.StartsWith(Library.Core.FilenameFilter.ConvertGlobbingToRegExp(m_wrapper.SourcePath)))
                            filters.Add(tf.Value);

                    List<string> included = new List<string>();
                    foreach (string s in m_specialFolders)
                        if (filters.Contains(Library.Core.FilenameFilter.ConvertGlobbingToRegExp(s + "*")))
                            included.Add(s);

                    if (included.Count == 0)
                    {
                        FolderRadio.Checked = true;
                        TargetFolder.Text = m_wrapper.SourcePath;
                    }
                    else
                    {
                        DocumentsRadio.Checked = true;
                        IncludeDocuments.Checked = included.Contains(m_myDocuments.Substring(1));
                        IncludeImages.Checked = included.Contains(m_myPictures.Substring(1));
                        IncludeMusic.Checked = included.Contains(m_myMusic.Substring(1));
                        IncludeDesktop.Checked = included.Contains(m_desktop.Substring(1));
                        IncludeSettings.Checked = included.Contains(m_appData.Substring(1));

                        if (!(IncludeDocuments.Checked || IncludeMusic.Checked || IncludeImages.Checked || IncludeDesktop.Checked || IncludeSettings.Checked))
                        {
                            FolderRadio.Checked = true;
                            TargetFolder.Text = m_wrapper.SourcePath;
                        }
                    }
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
                totalSize.Text = "Calculating size ...";
                m_calculator = new WorkerThread<string>(CalculateFolderSize);
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
                    totalSize.Text = string.Format("The selected items take up {0} of space", Library.Core.Utility.FormatSizeString(s));
                else
                    totalSize.Text = string.Format("Calculating ... (using more than {0} of space)", Library.Core.Utility.FormatSizeString(s));

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

            if (System.IO.Directory.Exists(folder))
            {
                try
                {
                    foreach (string file in Duplicati.Library.Core.Utility.EnumerateFiles(folder, fnf))
                        try { size += new System.IO.FileInfo(file).Length; }
                        catch { }
                }
                catch { }
            }

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
