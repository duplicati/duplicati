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

namespace Duplicati.Library.Encryption
{
    public partial class GPGCommonOptions : UserControl
    {
        public const string GPG_PATH = "GPG Path";
        public const string PATH_TESTED = "GPG Path Tested";

        private IDictionary<string, string> m_applicationSettings;
        private IDictionary<string, string> m_options;

        private bool m_pathTested = false;

        private GPGCommonOptions()
        {
            InitializeComponent();
        }

        public GPGCommonOptions(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
            : this()
        {
            m_applicationSettings = applicationSettings;
            m_options = options;
        }

        public bool Save(bool validate)
        {
            if (validate && !m_pathTested)
            {
                if (string.IsNullOrEmpty(GPGPath.Text))
                {
                    if (MessageBox.Show(this, Strings.GPGCommonOptions.UseEmptyPathWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return false;
                }
                else
                {
                    try
                    {
                        if (Utility.Utility.LocateFileInSystemPath(GPGPath.Text) == null && MessageBox.Show(this, Strings.GPGCommonOptions.LocatingGpgError, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                            return false;
                    }
                    catch (Exception ex)
                    {
                        if (MessageBox.Show(this, string.Format(Strings.GPGCommonOptions.GpgProgramNotFoundError, ex), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                            return false;
                    }
                }

                m_pathTested = true;
            }

            m_options[GPG_PATH] = GPGPath.Text;
            m_options[PATH_TESTED] = m_pathTested.ToString();

            return true;
        }

        private void GPGCommonOptions_Load(object sender, EventArgs e)
        {
            try
            {
                GPGPath.Items.Clear();
                GPGPath.Items.Add(ExecutableFilename);

                string autofind = Utility.Utility.LocateFileInSystemPath(ExecutableFilename);
                if (!string.IsNullOrEmpty(autofind))
                {
                    BrowseForGPGDialog.FileName = System.IO.Path.GetFileName(autofind);
                    BrowseForGPGDialog.InitialDirectory = System.IO.Path.GetDirectoryName(autofind);
                    GPGPath.Items.Add(autofind);
                    toolTip.SetToolTip(GPGPath, string.Format(Strings.GPGCommonOptions.GpgAutodetectedInfo, autofind));
                }
                else
                {
                    toolTip.SetToolTip(GPGPath, Strings.GPGCommonOptions.GpgNotAutodetectedWarning);
                }

                if (m_options.ContainsKey(GPG_PATH))
                    GPGPath.Text = m_options[GPG_PATH];


                if (!m_options.ContainsKey(PATH_TESTED))
                    m_pathTested = true; //Initially true because we don't want to prompt if there has been no change
                else if (!bool.TryParse(m_options[PATH_TESTED], out m_pathTested))
                    m_pathTested = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.GPGCommonOptions.LoadError, ex), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void BrowseForGPGPathButton_Click(object sender, EventArgs e)
        {
            if (BrowseForGPGDialog.ShowDialog(this) == DialogResult.OK)
                GPGPath.Text = BrowseForGPGDialog.FileName;
        }

        private void GPGPath_TextChanged(object sender, EventArgs e)
        {
            m_pathTested = false;
        }

        private static string ExecutableFilename { get { return Utility.Utility.IsClientLinux ? "gpg" : "gpg.exe"; } }

        public static string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(GPG_PATH) && !string.IsNullOrEmpty(guiOptions[GPG_PATH]))
            {
                applicationSettings[GPG_PATH] = guiOptions[GPG_PATH];

                //If the encryption module is not set, assume we need this module, otherwise only apply settings if the module is actually gpg
                if (commandlineOptions != null)
                    if (!applicationSettings.ContainsKey("encryption-module") || applicationSettings["encryption-module"] == "gpg")
                        commandlineOptions[GPGEncryption.COMMANDLINE_OPTIONS_PATH] = guiOptions[GPG_PATH];
            }

            return null;
        }
    }
}
