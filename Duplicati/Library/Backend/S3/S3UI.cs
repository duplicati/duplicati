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
    public partial class S3UI : UserControl
    {
        private const string ACCESS_ID = "AccessID";
        private const string ACCESS_KEY = "AccessKey";
        private const string BUCKET_NAME = "Bucketname";
        private const string EUROBUCKET = "UseEuroBucket";
        private const string SUBDOMAIN = "UseSubdomainStrategy";
        private const string SERVER_URL = "ServerUrl";
        private const string PREFIX = "Prefix";
        private const string HASTESTED = "UI: HasTested";

        private const string S3_PATH = "s3.amazonaws.com";

        
        private const string LOGIN_PAGE = "http://aws.amazon.com/s3";
        private IDictionary<string, string> m_options;
        private bool m_hasTested;

        public S3UI(IDictionary<string, string> options)
            : this()
        {
            m_options = options;
        }

        private S3UI()
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

            m_options[ACCESS_ID] = AWS_ID.Text;
            m_options[ACCESS_KEY] = AWS_KEY.Text;
            m_options[BUCKET_NAME] = BucketName.Text;
            m_options[EUROBUCKET] = UseEuroBuckets.Checked.ToString();
            string bucketname = m_options[BUCKET_NAME];
            if (bucketname.IndexOf("/") > 0)
                bucketname = bucketname.Substring(0, bucketname.IndexOf("/"));
            m_options[SUBDOMAIN] = (bucketname.ToLower() == bucketname).ToString();

        }

        void S3UI_Load(object sender, EventArgs args)
        {
            if (m_options.ContainsKey(ACCESS_ID))
                AWS_ID.Text = m_options[ACCESS_ID];
            if (m_options.ContainsKey(ACCESS_KEY))
                AWS_KEY.Text = m_options[ACCESS_KEY];
            if (m_options.ContainsKey(BUCKET_NAME))
                BucketName.Text = m_options[BUCKET_NAME];

            bool b;
            if (!m_options.ContainsKey(EUROBUCKET) || !bool.TryParse(m_options[EUROBUCKET], out b))
                b = false;
            
            UseEuroBuckets.Checked = b;

            if (!m_options.ContainsKey(HASTESTED) || !bool.TryParse(m_options[HASTESTED], out m_hasTested))
                m_hasTested = false;

        }

        private bool ValidateForm()
        {
            if (AWS_ID.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.S3UI.EmptyAWSIDError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (AWS_KEY.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.S3UI.EmptyAWSKeyError , Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (BucketName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.S3UI.EmptyBucketnameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (!BucketName.Text.ToLower().StartsWith(AWS_ID.Text.ToLower()))
            {
                DialogResult res = MessageBox.Show(this, Strings.S3UI.BucketnameNotPrefixedWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes)
                    BucketName.Text = AWS_ID.Text.ToLower() + "-" + BucketName.Text;
                else if (res == DialogResult.Cancel)
                {
                    return false;
                }
            }

            string bucketname;
            string prefix;

            if (BucketName.Text.Contains("/"))
            {
                bucketname = BucketName.Text.Substring(0, BucketName.Text.IndexOf("/"));
                prefix = BucketName.Text.Substring(BucketName.Text.IndexOf("/") + 1);
            }
            else
            {
                bucketname = BucketName.Text;
                prefix = null;
            }

            if (bucketname.ToLower() != bucketname)
            {
                string reason = UseEuroBuckets.Checked ?
                    Strings.S3UI.EuroBucketsRequireLowerCaseError :
                    Strings.S3UI.NewS3RequiresLowerCaseError;

                DialogResult res = MessageBox.Show(this, string.Format(Strings.S3UI.BucketnameCaseWarning, reason), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes)
                {
                    bucketname = bucketname.ToLower();
                    BucketName.Text = bucketname + "/" + prefix;
                }
                else if (res == DialogResult.Cancel || UseEuroBuckets.Checked)
                {
                    return false;
                }
            }

            return true;
        }

        private void SignUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Duplicati.GUI.UrlUtillity.OpenUrl(LOGIN_PAGE);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    Save();

                    Dictionary<string, string> options = new Dictionary<string, string>();
                    string destination = GetConfiguration(m_options, options);

                    S3 s3 = new S3(destination, options);
                    s3.List();

                    MessageBox.Show(this, Backend.CommonStrings.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backend.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

                }
                this.Cursor = c;
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
            if (guiOptions.ContainsKey(ACCESS_ID) && !string.IsNullOrEmpty(guiOptions[ACCESS_ID]))
                commandlineOptions["aws_access_key_id"] = guiOptions[ACCESS_ID];
            if (guiOptions.ContainsKey(ACCESS_KEY) && !string.IsNullOrEmpty(guiOptions[ACCESS_KEY]))
                commandlineOptions["aws_secret_access_key"] = guiOptions[ACCESS_KEY];

            bool useEuroBucket;
            bool useSubDomain;

            if (!guiOptions.ContainsKey(EUROBUCKET) || !bool.TryParse(guiOptions[EUROBUCKET], out useEuroBucket))
                useEuroBucket = false;

            if (!guiOptions.ContainsKey(SUBDOMAIN) || !bool.TryParse(guiOptions[SUBDOMAIN], out useSubDomain))
                useSubDomain = false;

            if (!guiOptions.ContainsKey(BUCKET_NAME))
                throw new Exception(string.Format(Backend.CommonStrings.ConfigurationIsMissingItemError, BUCKET_NAME));

            string bucketName = guiOptions[BUCKET_NAME];
            string host = guiOptions.ContainsKey(SERVER_URL) ? guiOptions[SERVER_URL] : "";
            string prefix = guiOptions.ContainsKey(PREFIX) ? guiOptions[PREFIX] : "";

            if (string.IsNullOrEmpty(host))
            {
                if (useEuroBucket || useSubDomain)
                    host = bucketName + "." + S3_PATH;
                else
                    host = S3_PATH;
            }

            if (useEuroBucket || useSubDomain)
                return "s3://" + host + "/" + (string.IsNullOrEmpty(prefix) ? "" : prefix);
            else
                return "s3://" + host + "/" + bucketName + (string.IsNullOrEmpty(prefix) ? "" : "/" + prefix);
        }

        public static string PageTitle
        {
            get { return Strings.S3UI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.S3UI.PageDescription; }
        }
    }
}
