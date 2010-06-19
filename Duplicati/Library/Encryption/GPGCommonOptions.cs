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
                        if (Core.Utility.LocateFileInSystemPath(GPGPath.Text) == null && MessageBox.Show(this, Strings.GPGCommonOptions.LocatingGpgError, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
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

                string autofind = Core.Utility.LocateFileInSystemPath(ExecutableFilename);
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

        private static string ExecutableFilename { get { return Core.Utility.IsClientLinux ? "gpg" : "gpg.exe"; } }

        public static string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(GPG_PATH) && !string.IsNullOrEmpty(guiOptions[GPG_PATH]))
            {
                applicationSettings[GPG_PATH] = guiOptions[GPG_PATH];

                if (commandlineOptions != null)
                    commandlineOptions[GPGEncryption.COMMANDLINE_OPTIONS_PATH] = guiOptions[GPG_PATH];
            }

            return null;
        }
    }
}
