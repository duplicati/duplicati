using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Library.Backend
{
    public partial class GoogleDocsUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string PASSWORD = "Password";
        private const string FOLDER = "Folder";

        private const string HAS_TESTED = "UI: Has tested";
        private const string INITIALPASSWORD = "UI: Temp password";

        private bool m_hasTested;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        private IDictionary<string, string> m_options;

        public GoogleDocsUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        private GoogleDocsUI()
        {
            InitializeComponent();
        }

        internal bool Save(bool validate)
        {
            Save();

            if (!validate)
                return true;

            if (!ValidateForm())
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

        private void GoogleDocsUI_Load(object sender, EventArgs e)
        {
            if (m_options.ContainsKey(USERNAME))
                Username.Text = m_options[USERNAME];
            if (m_options.ContainsKey(PASSWORD))
                Password.Text = m_options[PASSWORD];
            if (m_options.ContainsKey(FOLDER))
                Path.Text = m_options[FOLDER];

            if (!m_options.ContainsKey(INITIALPASSWORD))
                m_options[INITIALPASSWORD] = m_options.ContainsKey(PASSWORD) ? m_options[PASSWORD] : "";
            Password.AskToEnterNewPassword = !string.IsNullOrEmpty(m_options[INITIALPASSWORD]);
            Password.InitialPassword = m_options[INITIALPASSWORD];

            if (!m_options.ContainsKey(HAS_TESTED) || !bool.TryParse(m_options[HAS_TESTED], out m_hasTested))
                m_hasTested = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
        }

        private bool ValidateForm()
        {
            if (Path.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.GoogleDocsUI.EmptyFolderPathError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { Path.Focus(); }
                catch { }

                return false;
            }

            if (Username.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Interface.CommonStrings.EmptyUsernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { Username.Focus(); }
                catch { }

                return false;
            }

            if (Password.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Interface.CommonStrings.EmptyPasswordError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                try { Password.Focus(); }
                catch { }

                return false;
            }

            return true;
        }

        private void Save()
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            m_options.Clear();
            m_options[HAS_TESTED] = m_hasTested.ToString();
            m_options[FOLDER] = Path.Text;
            m_options[USERNAME] = Username.Text;
            m_options[PASSWORD] = Password.Text;

            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;

            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry == true)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;

                        Save();
                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string destination = GetConfiguration(m_options, options);

                        bool existingBackup = false;

                        using(GoogleDocs GoogleDocs = new GoogleDocs(destination, options))
                            foreach (Interface.IFileEntry n in GoogleDocs.List())
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
                        switch (MessageBox.Show(this, Strings.GoogleDocsUI.CreateMissingFolderQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                        {
                            case DialogResult.Yes:
                                CreateFolderButton.PerformClick();
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
        }

        private void Username_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Password_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void Path_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void CreateFolderButton_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                bool retry = true;
                while (retry == true)
                {
                    retry = false;
                    Cursor c = this.Cursor;
                    try
                    {
                        this.Cursor = Cursors.WaitCursor;
                        Save();

                        Dictionary<string, string> options = new Dictionary<string, string>();
                        string destination = GetConfiguration(m_options, options);

                        GoogleDocs GoogleDocs = new GoogleDocs(destination, options);
                        GoogleDocs.CreateFolder();

                        MessageBox.Show(this, Strings.GoogleDocsUI.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                        m_hasTested = true;
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
        }

        internal static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["ftp-username"] = guiOptions[USERNAME];

            if (guiOptions.ContainsKey(PASSWORD) && !string.IsNullOrEmpty(guiOptions[PASSWORD]))
                commandlineOptions["ftp-password"] = guiOptions[PASSWORD];

            string folder;
            guiOptions.TryGetValue(FOLDER, out folder);

            if (string.IsNullOrEmpty(folder))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, FOLDER));

            if (folder.StartsWith("/"))
                folder = folder.Substring(1);

            if (string.IsNullOrEmpty(folder))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, FOLDER));

            return "googledocs://" + guiOptions[FOLDER];
        }

        public static string PageTitle
        {
            get { return Strings.GoogleDocsUI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.GoogleDocsUI.PageDescription; }
        }

        private void SignUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Duplicati.Library.Utility.UrlUtillity.OpenUrl("https://mail.google.com/mail/signup");
        }

    }
}
