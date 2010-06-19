using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Library.Backend
{
    public partial class SSHCommonOptions : UserControl
    {
        public const string SFTP_PATH = "SFTP Path";
        public const string DEFAULT_MANAGED = "Default Managed";
        public const string PATH_TESTED = "SFTP Path Tested";

        private IDictionary<string, string> m_applicationSettings;
        private IDictionary<string, string> m_options;

        private bool m_pathTested = false;

        private SSHCommonOptions()
        {
            InitializeComponent();
        }

        public SSHCommonOptions(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
            : this()
        {
            m_applicationSettings = applicationSettings;
            m_options = options;
        }

        public bool Save(bool validate)
        {
            if (validate && !m_pathTested)
            {
                if (string.IsNullOrEmpty(SFTPPath.Text))
                {
                    if (MessageBox.Show(this, Strings.SSHCommonOptions.UseEmptyPathWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return false;
                }
                else
                {
                    try
                    {
                        if (Core.Utility.LocateFileInSystemPath(SFTPPath.Text) == null && MessageBox.Show(this, Strings.SSHCommonOptions.LocatingSftpError, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                            return false;
                    }
                    catch (Exception ex)
                    {
                        if (MessageBox.Show(this, string.Format(Strings.SSHCommonOptions.SftpProgramNotFoundError, ex), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                            return false;
                    }
                }

                m_pathTested = true;
            }

            m_options[SFTP_PATH] = SFTPPath.Text;
            m_options[DEFAULT_MANAGED] = UseManagedAsDefault.Checked.ToString();
            m_options[PATH_TESTED] = m_pathTested.ToString();

            return true;
        }

        private void SSHCommonOptions_Load(object sender, EventArgs e)
        {
            try
            {
                SFTPPath.Items.Clear();
                SFTPPath.Items.Add(ExecutableFilename);

                string autofind = Core.Utility.LocateFileInSystemPath(ExecutableFilename);
                if (!string.IsNullOrEmpty(autofind))
                {
                    BrowseForSFTPDialog.FileName = System.IO.Path.GetFileName(autofind);
                    BrowseForSFTPDialog.InitialDirectory = System.IO.Path.GetDirectoryName(autofind);
                    SFTPPath.Items.Add(autofind);
                    toolTip.SetToolTip(SFTPPath, string.Format(Strings.SSHCommonOptions.SftpAutodetectedInfo, autofind));
                }
                else
                {
                    toolTip.SetToolTip(SFTPPath, Strings.SSHCommonOptions.SftpNotAutodetectedWarning);
                }

                if (m_options.ContainsKey(SFTP_PATH))
                    SFTPPath.Text = m_options[SFTP_PATH];

                bool b;
                if (!m_options.ContainsKey(DEFAULT_MANAGED) || !bool.TryParse(m_options[DEFAULT_MANAGED], out b))
                    b = true;

                UseManagedAsDefault.Checked = b;

                if (!m_options.ContainsKey(PATH_TESTED))
                    m_pathTested = true; //Initially true because we don't want to prompt if there has been no change
                else if (!bool.TryParse(m_options[PATH_TESTED], out m_pathTested))
                    m_pathTested = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Strings.SSHCommonOptions.LoadError, ex), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SFTPPath_TextChanged(object sender, EventArgs e)
        {
            m_pathTested = false;
        }

        private void BrowseForSFTPButton_Click(object sender, EventArgs e)
        {
            if (BrowseForSFTPDialog.ShowDialog(this) == DialogResult.OK)
                SFTPPath.Text = BrowseForSFTPDialog.FileName;
        }

        private static string ExecutableFilename { get { return Core.Utility.IsClientLinux ? "sftp" : "psftp.exe"; } }

        public static string GetConfiguration(IDictionary<string, string> applicationSettings, IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(SFTP_PATH) && !string.IsNullOrEmpty(guiOptions[SFTP_PATH]))
            {
                applicationSettings[SFTP_PATH] = guiOptions[SFTP_PATH];

                if (commandlineOptions != null)
                    commandlineOptions[SSH.SFTP_PATH_OPTION] = guiOptions[SFTP_PATH];
            }

            if (guiOptions.ContainsKey(DEFAULT_MANAGED))
            {
                applicationSettings[DEFAULT_MANAGED] = guiOptions[DEFAULT_MANAGED];
            }

            return null;
        }
    }
}
