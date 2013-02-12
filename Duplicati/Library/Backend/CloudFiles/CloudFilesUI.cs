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

namespace Duplicati.Library.Backend
{
    public partial class CloudFilesUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string ACCESS_KEY = "Access Key";
        private const string CONTAINER_NAME = "Container name";
        private const string USE_UK_ACCOUNT = "UK Account";
        private const string AUTH_URL = "Auth URL";
        private const string HASTESTED = "UI: Has tested";
        private const string INITIALPASSWORD = "UI: Temp password";

        private const string SIGNUP_PAGE_US = "https://www.rackspacecloud.com/signup";
        private const string SIGNUP_PAGE_UK = "http://www.rackspace.co.uk/cloud-hosting/cloud-files/";
        private IDictionary<string, string> m_options;
        private bool m_hasTested;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        public CloudFilesUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;

            Servernames.Items.Clear();
            foreach (KeyValuePair<string, string> p in CloudFiles.KNOWN_CLOUDFILES_PROVIDERS)
                Servernames.Items.Add(new Utility.ComboBoxItemPair<string>(string.Format("{0} ({1})", p.Key, p.Value), p.Value));
        }

        private CloudFilesUI()
        {
            InitializeComponent();
        }

        internal bool Save(bool validate)
        {
            Save();

            if (!validate)
                return true;

            if (!ValidateForm(true))
                return false;

            if (!m_hasTested)
                switch (MessageBox.Show(this, Interface.CommonStrings.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        TestConnection_Click(null, null);
                        if (!m_hasTested)
                            return false;
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        return false;
                }

            Save();

            m_options.Remove(INITIALPASSWORD);

            return true;
        }

        private void Save()
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            m_options.Clear();
            m_options[HASTESTED] = m_hasTested.ToString();

            m_options[USERNAME] = Username.Text;
            m_options[ACCESS_KEY] = API_KEY.Text;
            m_options[CONTAINER_NAME] = ContainerName.Text;
            if (Servernames.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                m_options[AUTH_URL] = Servernames.Text;
            else
                m_options[AUTH_URL] = (Servernames.SelectedItem as Utility.ComboBoxItemPair<string>).Value;

            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;

            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        void CloudFilesUI_Load(object sender, EventArgs args)
        {
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            if (m_options.ContainsKey(ACCESS_KEY))
                API_KEY.Text = m_options[ACCESS_KEY];
            if (m_options.ContainsKey(CONTAINER_NAME))
                ContainerName.Text = m_options[CONTAINER_NAME];

            if (m_options.ContainsKey(AUTH_URL))
            {
                Servernames.Text = m_options[AUTH_URL];
            }
            else if (m_options.ContainsKey(USE_UK_ACCOUNT))
            {
                bool useUK;
                if (bool.TryParse(m_options[USE_UK_ACCOUNT], out useUK))
                    Servernames.Text = CloudFiles.AUTH_URL_UK;
                else
                    Servernames.Text = CloudFiles.AUTH_URL_US;
            }
            else
            {
                Servernames.Text = CloudFiles.AUTH_URL_US;
            }
            

            if (!m_options.ContainsKey(INITIALPASSWORD))
                m_options[INITIALPASSWORD] = m_options.ContainsKey(ACCESS_KEY) ? m_options[ACCESS_KEY] : "";
            API_KEY.AskToEnterNewPassword = !string.IsNullOrEmpty(m_options[INITIALPASSWORD]);
            API_KEY.InitialPassword = m_options[INITIALPASSWORD];

            if (!m_options.ContainsKey(HASTESTED) || !bool.TryParse(m_options[HASTESTED], out m_hasTested))
                m_hasTested = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
        }

        private bool ValidateForm(bool checkForBucket)
        {
            string servername;
            if (Servernames.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                servername = Servernames.Text;
            else
                servername = (Servernames.SelectedItem as Utility.ComboBoxItemPair<string>).Value;

            if (string.IsNullOrEmpty(servername))
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyAuthUrlError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servernames.Focus(); }
                catch { }
                
                return false;
            }

            Uri tmp;
            if (!Uri.TryCreate(servername, UriKind.Absolute, out tmp))
            {
                MessageBox.Show(this, string.Format(Strings.CloudFilesUI.InvalidAuthUrlError, servername), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servernames.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyCloudFilesIDError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (API_KEY.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyAPIKeyError , Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (ContainerName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyContainerNameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private void SignUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Duplicati.Library.Utility.UrlUtillity.OpenUrl(Servernames.Text.Equals(CloudFiles.AUTH_URL_UK) ? SIGNUP_PAGE_UK : SIGNUP_PAGE_US);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm(true))
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    Save();

                    Dictionary<string, string> options = new Dictionary<string, string>();
                    string destination = GetConfiguration(m_options, options);

                    bool existingBackup = false;
                    using (CloudFiles cf = new CloudFiles(destination, options))
                        foreach (Interface.IFileEntry n in cf.List())
                            if (n.Name.StartsWith("duplicati-"))
                            {
                                existingBackup = true;
                                break;
                            }

                    bool isUiAdd = string.IsNullOrEmpty(m_uiAction) || string.Equals(m_uiAction, "add", StringComparison.InvariantCultureIgnoreCase);
                    if (existingBackup && isUiAdd)
                    {
                        if (MessageBox.Show(this, string.Format(Interface.CommonStrings.ExistingBackupDetectedQuestion), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button3) != DialogResult.Yes)
                            return;
                    }
                    else
                    {
                        MessageBox.Show(this, Interface.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    m_hasTested = true;
                }
                catch (Interface.FolderMissingException)
                {
                    switch (MessageBox.Show(this, Strings.CloudFilesUI.CreateMissingContainer, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                    {
                        case DialogResult.Yes:
                            CreateContainer.PerformClick();
                            TestConnection.PerformClick();
                            return;
                        default:
                            return;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.Cursor = c;
                }
            }
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void API_KEY_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void ContainerName_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void UKAccount_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        public static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["cloudfiles-username"] = guiOptions[USERNAME];
            if (guiOptions.ContainsKey(ACCESS_KEY) && !string.IsNullOrEmpty(guiOptions[ACCESS_KEY]))
                commandlineOptions["cloudfiles-accesskey"] = guiOptions[ACCESS_KEY];

            if (guiOptions.ContainsKey(AUTH_URL))
            {
                commandlineOptions["cloudfiles-authentication-url"] = guiOptions[AUTH_URL];
            }
            else
            {
                bool useUK;
                if (guiOptions.ContainsKey(USE_UK_ACCOUNT) && bool.TryParse(guiOptions[USE_UK_ACCOUNT], out useUK) && useUK)
                    commandlineOptions["cloudfiles-uk-account"] = "";
            }

            if (!guiOptions.ContainsKey(CONTAINER_NAME))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, CONTAINER_NAME));

            return "cloudfiles://" + guiOptions[CONTAINER_NAME];
        }

        public static string PageTitle
        {
            get { return Strings.CloudFilesUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.CloudFilesUI.PageDescription; }
        }

        private void CreateContainer_Click(object sender, EventArgs e)
        {
            if (ValidateForm(false))
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    Save();

                    Dictionary<string, string> options = new Dictionary<string, string>();
                    string destination = GetConfiguration(m_options, options);

                    CloudFiles cf = new CloudFiles(destination, options);
                    cf.CreateFolder();

                    MessageBox.Show(this, Interface.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
                this.Cursor = c;
            }

        }

        private delegate void SetComboTextDelegate(ComboBox el, string text);
        private void SetComboText(ComboBox el, string text)
        {
            el.Text = text;
        }

        private void Servernames_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Servernames.SelectedItem as Utility.ComboBoxItemPair<string> != null)
                BeginInvoke(new SetComboTextDelegate(SetComboText), Servernames, (Servernames.SelectedItem as Utility.ComboBoxItemPair<string>).Value);
        }

        private void Servernames_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

    }
}
