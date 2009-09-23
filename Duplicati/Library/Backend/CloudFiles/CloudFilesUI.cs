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

namespace Duplicati.Library.Backend
{
    public partial class CloudFilesUI : UserControl
    {
        private const string USERNAME = "Username";
        private const string ACCESS_KEY = "Access Key";
        private const string CONTAINER_NAME = "Container name";
        private const string HASTESTED = "UI: Has tested";

        //TODO: Change this
        private const string LOGIN_PAGE = "https://www.rackspacecloud.com/signup";
        private IDictionary<string, string> m_options;
        private bool m_hasTested;

        public CloudFilesUI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
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
                switch (MessageBox.Show(this, Backend.CommonStrings.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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
            return true;
        }

        private void Save()
        {
            m_options.Clear();
            m_options[HASTESTED] = m_hasTested.ToString();

            m_options[USERNAME] = AWS_ID.Text;
            m_options[ACCESS_KEY] = AWS_KEY.Text;
            m_options[CONTAINER_NAME] = BucketName.Text;
        }

        void CloudFilesUI_Load(object sender, EventArgs args)
        {
            if (m_options.ContainsKey(USERNAME))
                AWS_ID.Text = m_options[USERNAME];
            if (m_options.ContainsKey(ACCESS_KEY))
                AWS_KEY.Text = m_options[ACCESS_KEY];
            if (m_options.ContainsKey(CONTAINER_NAME))
                BucketName.Text = m_options[CONTAINER_NAME];


            if (!m_options.ContainsKey(HASTESTED) || !bool.TryParse(m_options[HASTESTED], out m_hasTested))
                m_hasTested = false;
        }

        private bool ValidateForm(bool checkForBucket)
        {
            if (AWS_ID.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyAWSIDError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (AWS_KEY.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyAWSKeyError , Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (BucketName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.CloudFilesUI.EmptyBucketnameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            return true;
        }

        private void SignUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Duplicati.GUI.UrlUtillity.OpenUrl(LOGIN_PAGE);
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

                    CloudFiles cf = new CloudFiles(destination, options);
                    cf.Test();

                    MessageBox.Show(this, Backend.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Backend.FolderMissingException)
                {
                    switch (MessageBox.Show(this, Strings.CloudFilesUI.CreateMissingBucket, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    this.Cursor = c;
                }
            }
        }

        private void AWS_ID_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void AWS_KEY_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void BucketName_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        private void UseEuroBuckets_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
        }

        public static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(USERNAME) && !string.IsNullOrEmpty(guiOptions[USERNAME]))
                commandlineOptions["cloudfiles-username"] = guiOptions[USERNAME];
            if (guiOptions.ContainsKey(ACCESS_KEY) && !string.IsNullOrEmpty(guiOptions[ACCESS_KEY]))
                commandlineOptions["cloudfiles-accesskey"] = guiOptions[ACCESS_KEY];

            if (!guiOptions.ContainsKey(CONTAINER_NAME))
                throw new Exception(string.Format(Backend.CommonStrings.ConfigurationIsMissingItemError, CONTAINER_NAME));

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

        private void CreateBucket_Click(object sender, EventArgs e)
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

                    MessageBox.Show(this, Backend.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
                this.Cursor = c;
            }

        }
    }
}
