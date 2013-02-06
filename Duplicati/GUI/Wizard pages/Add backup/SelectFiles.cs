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
using System.Windows.Forms.Wizard;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class SelectFiles : WizardControl
    {
        private const int COLLAPSED_GROUP_SIZE = 24;
        private const int GRID_SPACING = 8;

        private WorkerThread<string> m_calculator;
        private object m_lock = new object();
        private Dictionary<string, long> m_sizes;
        private IButtonControl m_acceptButton;

        private WizardSettingsWrapper m_wrapper;

        private string m_myPictures;
        private string m_myMusic;
        private string m_desktop;
        private string m_appData;
        private string m_myDocuments;

        private string[] m_specialFolders;

        public SelectFiles()
            : base(Strings.SelectFiles.PageTitle, Strings.SelectFiles.PageDescription)
        {
            InitializeComponent();
            m_sizes = new Dictionary<string, long>(Library.Utility.Utility.IsFSCaseSensitive ? StringComparer.CurrentCulture : StringComparer.CurrentCultureIgnoreCase );

            m_myPictures = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            m_myMusic = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            m_desktop = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            m_appData = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            m_myDocuments = Library.Utility.Utility.AppendDirSeparator(System.Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            FolderTooltip.SetToolTip(IncludeDocuments, m_myDocuments);
            FolderTooltip.SetToolTip(IncludeMusic, m_myMusic);
            FolderTooltip.SetToolTip(IncludeImages, m_myPictures);
            FolderTooltip.SetToolTip(IncludeDesktop, m_desktop);
            FolderTooltip.SetToolTip(IncludeSettings, m_appData);

            m_specialFolders = new string[] { 
                m_myDocuments, 
                m_myMusic, 
                m_myPictures, 
                m_appData, 
                m_desktop
            };

            base.PageEnter += new PageChangeHandler(SelectFiles_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectFiles_PageLeave);
        }

        void m_owner_Cancelled(object sender, CancelEventArgs e)
        {
            StopCalculator();
        }

        private void SaveSettings()
        {
            m_settings["Files:Sizes"] = m_sizes;
            
            m_wrapper.SelectFilesUI.Version = 2;
            m_wrapper.SelectFilesUI.UseSimpleMode = DocumentsRadio.Checked;
            m_wrapper.SelectFilesUI.IncludeDocuments = IncludeDocuments.Checked;
            m_wrapper.SelectFilesUI.IncludeMusic = IncludeMusic.Checked;
            m_wrapper.SelectFilesUI.IncludeImages = IncludeImages.Checked;
            m_wrapper.SelectFilesUI.IncludeDesktop = IncludeDesktop.Checked;
            m_wrapper.SelectFilesUI.IncludeSettings = IncludeSettings.Checked;

            List<string> folders = new List<string>();
            foreach (HelperControls.FolderPathEntry entry in InnerControlContainer.Controls)
            {
                string path = entry.SelectedPath.Trim();
                if (!string.IsNullOrEmpty(path))
                    folders.Add(Library.Utility.Utility.AppendDirSeparator(path));
            }

            folders.Reverse();
            m_wrapper.SourcePath = string.Join(System.IO.Path.PathSeparator.ToString(), folders.ToArray());
        }

        void SelectFiles_PageLeave(object sender, PageChangedArgs args)
        {
            m_owner.Cancelled -= new CancelEventHandler(m_owner_Cancelled);
            SaveSettings();

            if (args.Direction == PageChangedDirection.Back)
            {
                StopCalculator();
                return;
            }

            if (DocumentsRadio.Checked)
            {
                if (!(IncludeDesktop.Checked || IncludeDocuments.Checked || IncludeImages.Checked || IncludeMusic.Checked || IncludeSettings.Checked))
                {
                    MessageBox.Show(this, Strings.SelectFiles.NoFilesSelectedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                //Ensure that the right path is inserted at backup time
                m_wrapper.SourcePath = "";
            }
            else
            {
                List<string> folders = new List<string>();
                foreach (HelperControls.FolderPathEntry entry in InnerControlContainer.Controls)
                {
                    string path = entry.SelectedPath.Trim();
                    if (!string.IsNullOrEmpty(path))
                        folders.Add(Library.Utility.Utility.AppendDirSeparator(path));
                }

                for (int i = 0; i < folders.Count; i++)
                {
                    while (true)
                        if (!System.IO.Directory.Exists(folders[i]))
                        {
                            DialogResult res = MessageBox.Show(this, string.Format(Strings.SelectFiles.FolderDoesNotExistWarning, folders[i]), Application.ProductName, MessageBoxButtons.AbortRetryIgnore, MessageBoxIcon.Warning);
                            if (res == DialogResult.Abort)
                            {
                                args.Cancel = true;
                                return;
                            }
                            else if (res == DialogResult.Ignore)
                                break;
                        }
                        else
                            break;

                    for (int j = i + 1; j < folders.Count; j++)
                    {
                        if (folders[i].Equals(folders[j], Library.Utility.Utility.ClientFilenameStringComparision))
                        {
                            MessageBox.Show(this, string.Format(Strings.SelectFiles.DuplicateFolderError, folders[i]), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            args.Cancel = true;
                            return;
                        }

                        if (folders[i].StartsWith(folders[j]))
                        {
                            MessageBox.Show(this, string.Format(Strings.SelectFiles.RelatedFoldersError, folders[i], folders[j]), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            args.Cancel = true;
                            return;
                        }

                        if (folders[j].StartsWith(folders[i]))
                        {
                            MessageBox.Show(this, string.Format(Strings.SelectFiles.RelatedFoldersError, folders[j], folders[i]), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                            args.Cancel = true;
                            return;
                        }
                    }
                }

                if (folders.Count == 0)
                {
                    MessageBox.Show(this, Strings.SelectFiles.NoFilesSelectedError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    args.Cancel = true;
                    return;
                }

                folders.Reverse();
                m_wrapper.SourcePath = string.Join(System.IO.Path.PathSeparator.ToString(), folders.ToArray());
            }

            StopCalculator();

            args.NextPage = new PasswordSettings();
        }

        private void UpgradeFromVersionOne()
        {
            //Upgrade from the previous design with one source folder and multiple filters
            MessageBox.Show(this, Strings.SelectFiles.UpgradeWarning, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);

            string p = Library.Utility.Utility.AppendDirSeparator(m_wrapper.SourcePath);
            System.Xml.XmlDocument doc = new System.Xml.XmlDocument();
            doc.LoadXml(m_wrapper.EncodedFilterXml);

            List<KeyValuePair<bool, string>> filters = new List<KeyValuePair<bool,string>>();
            foreach (System.Xml.XmlNode n in doc.SelectNodes("root/filter"))
                filters.Add(new KeyValuePair<bool, string>(bool.Parse(n.Attributes["include"].Value), n.Attributes["filter"].Value));

            //See what folders are included with the current setup
            Library.Utility.FilenameFilter filter = new Duplicati.Library.Utility.FilenameFilter(filters);
            IncludeDocuments.Checked = filter.ShouldInclude(p, m_myDocuments);
            IncludeImages.Checked = filter.ShouldInclude(p, m_myPictures);
            IncludeMusic.Checked = filter.ShouldInclude(p, m_myMusic);
            IncludeDesktop.Checked = filter.ShouldInclude(p, m_desktop);
            IncludeSettings.Checked = filter.ShouldInclude(p, m_appData);

            //Remove any filters relating to the special folders
            for (int i = 0; i < filters.Count; i++)
                foreach (string s in m_specialFolders)
                    if (s.StartsWith(p))
                    {
                        if (filters[i].Value == Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(s.Substring(p.Length - 1) + "*"))
                        {
                            filters.RemoveAt(i);
                            i--;
                            break;
                        }
                    }

            //Remove any "exclude all" filters
            for (int i = 0; i < filters.Count; i++)
                if (filters[i].Key == false && filters[i].Value == ".*")
                {
                    filters.RemoveAt(i);
                    i--;
                }

            //See if there are extra filters that are not supported
            bool unsupported = false;
            foreach (KeyValuePair<bool, string> f in filters)
                if (f.Value.StartsWith(Library.Utility.FilenameFilter.ConvertGlobbingToRegExp(System.IO.Path.DirectorySeparatorChar.ToString())))
                {
                    unsupported = true;
                    break;
                }

            //If none of the special folders are included, we don't support the setup with simple mode
            unsupported |= !(IncludeDocuments.Checked | IncludeImages.Checked | IncludeMusic.Checked | IncludeDesktop.Checked | IncludeSettings.Checked);

            InnerControlContainer.Controls.Clear();
            AddFolderControl().SelectedPath = m_wrapper.SourcePath;

            //Make sure the extra filters are not included
            if (!unsupported)
            {
                doc = new System.Xml.XmlDocument();
                System.Xml.XmlNode root = doc.AppendChild(doc.CreateElement("root"));
                foreach (KeyValuePair<bool, string> f in filters)
                {
                    System.Xml.XmlNode n = root.AppendChild(doc.CreateElement("filter"));
                    n.Attributes.Append(doc.CreateAttribute("include")).Value = f.Key.ToString();
                    n.Attributes.Append(doc.CreateAttribute("filter")).Value = f.Value;
                    n.Attributes.Append(doc.CreateAttribute("globbing")).Value = "";
                }

                m_wrapper.EncodedFilterXml = doc.OuterXml;
            }

            if (unsupported)
                FolderRadio.Checked = true;
            else
                DocumentsRadio.Checked = true;
        }

        void SelectFiles_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            m_owner.Cancelled += new CancelEventHandler(m_owner_Cancelled);

            if (m_settings.ContainsKey("Files:Sizes"))
                m_sizes = (Dictionary<string, long>)m_settings["Files:Sizes"];

            if (!m_valuesAutoLoaded)
            {
                if (m_wrapper.SelectFilesUI.Version < 2)
                {
                    //Either upgrade or fresh copy
                    if (string.IsNullOrEmpty(m_wrapper.SourcePath))
                    {
                        //Set defaults
                        DocumentsRadio.Checked = true;
                        IncludeDocuments.Checked = true;
                        IncludeImages.Checked = true;
                        IncludeMusic.Checked = false;
                        IncludeDesktop.Checked = true;
                        IncludeSettings.Checked = false;

                    }
                    else
                        UpgradeFromVersionOne();
                }
                else
                {
                    //Multifolder version
                    if (m_wrapper.SelectFilesUI.UseSimpleMode)
                    {
                        IncludeDocuments.Checked = m_wrapper.SelectFilesUI.IncludeDocuments;
                        IncludeImages.Checked = m_wrapper.SelectFilesUI.IncludeImages;
                        IncludeMusic.Checked = m_wrapper.SelectFilesUI.IncludeMusic;
                        IncludeDesktop.Checked = m_wrapper.SelectFilesUI.IncludeDesktop;
                        IncludeSettings.Checked = m_wrapper.SelectFilesUI.IncludeSettings;
                        DocumentsRadio.Checked = true;
                    }
                    else
                    {
                        FolderRadio.Checked = true;
                    }
                }
            }

            //Always populate the list
            InnerControlContainer.Controls.Clear();
            if (!string.IsNullOrEmpty(m_wrapper.SourcePath))
                foreach (string s in m_wrapper.SourcePath.Split(System.IO.Path.PathSeparator))
                    if (!string.IsNullOrEmpty(s))
                        AddFolderControl().SelectedPath = s;

            Rescan();

            //Make sure we resize correctly
            TargetType_CheckedChanged(null, null);
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

        private void StopCalculator()
        {
            if (m_calculator != null)
            {
                m_calculator.ClearQueue(true);
                m_calculator.CompletedWork -= new EventHandler(m_calculator_CompletedWork);
                m_calculator.Terminate(false);
                m_calculator = null;
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
                    List<string> inclFolders = new List<string>();
                    
                    lock (m_lock)
                    {
                        if (m_sizes.ContainsKey(m_myDocuments) && IncludeDocuments.Checked)
                            s += FindActualSize(m_myDocuments);
                        if (m_sizes.ContainsKey(m_myMusic) && IncludeMusic.Checked)
                            s += FindActualSize(m_myMusic);
                        if (m_sizes.ContainsKey(m_myPictures) && IncludeImages.Checked)
                            s += FindActualSize(m_myPictures);
                        if (m_sizes.ContainsKey(m_appData) && IncludeSettings.Checked)
                            s += FindActualSize(m_appData);
                        if (m_sizes.ContainsKey(m_desktop) && IncludeDesktop.Checked)
                            s += FindActualSize(m_desktop);
                    }

                }
                else
                {
                    lock(m_lock)
                        foreach (HelperControls.FolderPathEntry entry in InnerControlContainer.Controls)
                        {
                            string path = entry.SelectedPath.Trim();

                            if (m_sizes.ContainsKey(path))
                            {
                                s += m_sizes[path];
                                entry.FolderSize = Library.Utility.Utility.FormatSizeString(m_sizes[path]);
                            }
                        }
                }
                
                if (m_calculator.CurrentTasks.Count == 0 && !m_calculator.Active)
                    totalSize.Text = string.Format(Strings.SelectFiles.FinalSizeCalculated, Library.Utility.Utility.FormatSizeString(s));
                else
                    totalSize.Text = string.Format(Strings.SelectFiles.PartialSizeCalculated, Library.Utility.Utility.FormatSizeString(s));

                lock(m_lock)
                {
                    if (m_sizes.ContainsKey(m_myMusic))
                        myMusicSize.Text = Library.Utility.Utility.FormatSizeString(FindActualSize(m_myMusic));
                    if (m_sizes.ContainsKey(m_myPictures))
                        myPicturesSize.Text = Library.Utility.Utility.FormatSizeString(FindActualSize(m_myPictures));
                    if (m_sizes.ContainsKey(m_desktop))
                        desktopSize.Text = Library.Utility.Utility.FormatSizeString(FindActualSize(m_desktop));
                    if (m_sizes.ContainsKey(m_appData))
                        appdataSize.Text = Library.Utility.Utility.FormatSizeString(FindActualSize(m_appData));
                    if (m_sizes.ContainsKey(m_myDocuments))
                        myDocumentsSize.Text = Library.Utility.Utility.FormatSizeString(FindActualSize(m_myDocuments));
                }
            }
        }

        /// <summary>
        /// Calculates the size of a folder, excluding sizes of excluded folders.
        /// This function is always called with the lock held
        /// </summary>
        /// <param name="path">The path to find the size for</param>
        /// <param name="exclFolders">The list of excluded folders</param>
        /// <returns>The size of the folder</returns>
        private long FindActualSize(string path)
        {
            long size = m_sizes[path];
            foreach (string f in m_specialFolders)
                if (f != path && f.StartsWith(path) && m_sizes.ContainsKey(f))
                    size -= m_sizes[f];

            return size;
        }

        private void Rescan()
        {
            lock (m_lock)
            {
                StartCalculator();
                m_calculator.ClearQueue(true);

                if (DocumentsRadio.Checked)
                {
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
                }
                else
                {
                    foreach (HelperControls.FolderPathEntry e in InnerControlContainer.Controls)
                    {
                        string s = e.SelectedPath.Trim();
                        if (s.Length > 0 && !m_sizes.ContainsKey(s))
                            m_calculator.AddTask(s);
                    }
                }

                m_calculator_CompletedWork(null, null);
            }

        }

        private void TargetType_CheckedChanged(object sender, EventArgs e)
        {
            DocumentGroup.Enabled = DocumentsRadio.Checked;
            FolderGroup.Enabled = FolderRadio.Checked;

            if (DocumentsRadio.Checked)
            {
                FolderGroup.Height = COLLAPSED_GROUP_SIZE;
                FolderGroup.Top = FolderRadio.Top = LayoutControlPanel.Height - FolderGroup.Height;

                DocumentGroup.Height = LayoutControlPanel.Height - FolderGroup.Height - GRID_SPACING;

                if (m_acceptButton != null)
                {
                    m_owner.Dialog.AcceptButton = m_acceptButton;
                    m_acceptButton = null;
                }
            }
            else
            {
                DocumentGroup.Height = COLLAPSED_GROUP_SIZE;
                FolderGroup.Top = FolderRadio.Top = DocumentGroup.Height + GRID_SPACING;
                FolderGroup.Height = LayoutControlPanel.Height - FolderGroup.Top;

                if (m_acceptButton == null)
                {
                    m_acceptButton = m_owner.Dialog.AcceptButton;
                    m_owner.Dialog.AcceptButton = null;
                }
            }

            Rescan();
            EnsureLastFolderEntryIsEmpty();
        }

        private void CalculateFolderSize(string folder)
        {
            lock (m_lock)
                if (m_sizes.ContainsKey(folder))
                    return;

            //Calculate outside lock
            long size = Duplicati.Library.Utility.Utility.GetDirectorySize(folder, null);
            lock (m_lock)
                m_sizes[folder] = size;
        }

        private void FolderPathEntry_Leave(object sender, EventArgs e)
        {
            lock (m_lock)
            {
                HelperControls.FolderPathEntry entry = (HelperControls.FolderPathEntry)sender;

                string path = entry.SelectedPath.Trim();

                if (path.Length > 0 && !m_sizes.ContainsKey(path))
                {
                    StartCalculator();
                    m_calculator.AddTask(path);
                    m_calculator_CompletedWork(null, null);
                }
                else if (entry.SelectedPath.Trim() == "")
                    entry.FolderSize = "";
                else if (m_sizes.ContainsKey(path))
                    m_calculator_CompletedWork(null, null);
            }
        }

        private void SelectFiles_VisibleChanged(object sender, EventArgs e)
        {
            if (!this.Visible)
                StopCalculator();

            if (!this.Visible && m_acceptButton != null && m_owner != null)
            {
                m_owner.Dialog.AcceptButton = m_acceptButton;
                m_acceptButton = null;
            }

        }

        private HelperControls.FolderPathEntry AddFolderControl()
        {
            //Just use the last one if it is empty anyway
            if (InnerControlContainer.Controls.Count > 0)
            {
                HelperControls.FolderPathEntry lastEntry = (HelperControls.FolderPathEntry)InnerControlContainer.Controls[0];
                if (lastEntry.SelectedPath.Trim().Length == 0)
                    return lastEntry;
            }

            try
            {
                InnerControlContainer.SuspendLayout();
                HelperControls.FolderPathEntry entry = new Duplicati.GUI.HelperControls.FolderPathEntry();
                entry.FolderBrowserDialog = folderBrowserDialog;
                entry.SelectedPathChanged += new EventHandler(FolderPathEntry_SelectedPathChanged);
                entry.DeleteButton_Clicked += new EventHandler(FolderPathEntry_DeleteButtonClicked);
                entry.SelectedPathLeave += new EventHandler(FolderPathEntry_Leave);
                entry.Height = entry.MinimumSize.Height + 2;

                InnerControlContainer.Controls.Add(entry);
                entry.Dock = DockStyle.Top;
                entry.BringToFront();
                return entry;
            }
            finally
            {
                InnerControlContainer.ResumeLayout();
            }
        }

        void FolderPathEntry_SelectedPathChanged(object sender, EventArgs e)
        {
            ((HelperControls.FolderPathEntry)sender).FolderSize = Strings.SelectFiles.CalculatingSizeMarker;
            EnsureLastFolderEntryIsEmpty();
        }

        void FolderPathEntry_DeleteButtonClicked(object sender, EventArgs e)
        {
            HelperControls.FolderPathEntry entry = (HelperControls.FolderPathEntry)sender;
            InnerControlContainer.Controls.Remove(entry);
            entry.SelectedPathChanged -= new EventHandler(FolderPathEntry_SelectedPathChanged);
            entry.DeleteButton_Clicked -= new EventHandler(FolderPathEntry_DeleteButtonClicked);
            entry.SelectedPathLeave -= new EventHandler(FolderPathEntry_Leave);
            EnsureLastFolderEntryIsEmpty();
        }

        /// <summary>
        /// Helper function that ensures that the last folder entry is always empty so the user may enter text in it
        /// </summary>
        private void EnsureLastFolderEntryIsEmpty()
        {
            if (InnerControlContainer.Controls.Count == 0)
                AddFolderControl().Focus();
            else
            {
                HelperControls.FolderPathEntry lastEntry = (HelperControls.FolderPathEntry)InnerControlContainer.Controls[0];
                if (lastEntry.SelectedPath.Trim().Length == 0)
                {
                    //If we have a duplicate empty, remove the non-focused one
                    if (InnerControlContainer.Controls.Count > 1 && ((HelperControls.FolderPathEntry)InnerControlContainer.Controls[1]).SelectedPath.Trim().Length == 0)
                        FolderPathEntry_DeleteButtonClicked(InnerControlContainer.Controls[(lastEntry.Focused ? 0 : 1)], null);
                }
                else
                    AddFolderControl();
            }
        }
    }
}
