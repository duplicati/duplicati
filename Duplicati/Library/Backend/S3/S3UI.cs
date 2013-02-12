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
    public partial class S3UI : UserControl
    {
        private const string ACCESS_ID = "AccessID";
        private const string ACCESS_KEY = "AccessKey";
        private const string BUCKET_NAME = "Bucketname";
        private const string EUROBUCKET = "UseEuroBucket";
        private const string RRS = "UseRRS";
        private const string SERVER_HOSTNAME = "ServerHostname";
        private const string BUCKET_LOCATION = "BucketRegion";
        private const string PREFIX = "Prefix";
        private const string HASTESTED = "UI: HasTested";
        private const string HASCREATEDBUCKET = "UI: Has created bucket";
        private const string HASSUGGESTEDPREFIX = "UI: Has suggested prefix";
        private const string HASSUGGESTEDLOWERCASE = "UI: Has suggested lowercase";
        private const string HASWARNEDINVALIDBUCKETNAME = "UI: Has warned invalid bucket name";
        private const string INITIALPASSWORD = "UI: Temp password";

        private const string SIGNUP_PAGE_AWS = "http://aws.amazon.com/s3";
        private const string SIGNUP_PAGE_HOSTEUROPE = "http://www.hosteurope.de/produkt/Cloud-Storage";
        private const string SIGNUP_PAGE_DUNKEL = "http://dunkel.de/s3/";

        private static string GetSignupLink(string servername)
        {
            if (!string.IsNullOrEmpty(servername))
            {
                if (servername.Equals("cs.hosteurope.de")) return SIGNUP_PAGE_HOSTEUROPE;
                if (servername.Equals("dcs.dunkel.de")) return SIGNUP_PAGE_DUNKEL;
            }

            return SIGNUP_PAGE_AWS;
        }

        private IDictionary<string, string> m_options;
        private IDictionary<string, string> m_applicationSettings;

        private bool m_hasTested;
        private bool m_hasCreatedbucket = false;
        private bool m_hasSuggestedPrefix = false;
        private bool m_hasSuggestedLowerCase = false;
        private bool m_hasWarnedInvalidBucketname = false;

        private const string DUPLICATI_ACTION_MARKER = "*duplicati-action*";
        private string m_uiAction = null;

        public S3UI(IDictionary<string, string> applicationSettings, IDictionary<string, string> options)
            : this()
        {
            m_options = options;
            m_applicationSettings = applicationSettings;
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

            Dictionary<string, string> tmp = new Dictionary<string, string>();
            foreach (Utility.ComboBoxItemPair<string> x in AWS_ID.Items)
                tmp[x.ToString()] = x.Value;

            tmp[AWS_ID.Text] = AWS_KEY.Text;
            S3CommonOptions.EncodeAccounts(tmp, m_applicationSettings);

            m_options.Remove(INITIALPASSWORD);

            return true;
        }

        private void Save()
        {
            string initialPwd;
            bool hasInitial = m_options.TryGetValue(INITIALPASSWORD, out initialPwd);

            m_options.Clear();
            m_options[HASTESTED] = m_hasTested.ToString();
            m_options[HASCREATEDBUCKET] = m_hasCreatedbucket.ToString();
            m_options[HASSUGGESTEDPREFIX] = m_hasSuggestedPrefix.ToString();
            m_options[HASSUGGESTEDLOWERCASE] = m_hasSuggestedLowerCase.ToString();
            m_options[HASWARNEDINVALIDBUCKETNAME] = m_hasWarnedInvalidBucketname.ToString();

            m_options[ACCESS_ID] = AWS_ID.Text;
            m_options[ACCESS_KEY] = AWS_KEY.Text;
            m_options[BUCKET_NAME] = BucketName.Text;
            m_options[RRS] = UseRRS.Checked.ToString();
            
            if (Servernames.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                m_options[SERVER_HOSTNAME] = Servernames.Text;
            else
                m_options[SERVER_HOSTNAME] = (Servernames.SelectedItem as Utility.ComboBoxItemPair<string>).Value;
            
            if (Bucketregions.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                m_options[BUCKET_LOCATION] = Bucketregions.Text;
            else
                m_options[BUCKET_LOCATION] = (Bucketregions.SelectedItem as Utility.ComboBoxItemPair<string>).Value;

            string bucketname = BucketName.Text;
            int ix = bucketname.IndexOf("/");
            if (ix > 0)
            {
                m_options[PREFIX] = bucketname.Substring(ix + 1);
                m_options[BUCKET_NAME] = bucketname.Substring(0, ix);
            }
            else
            {
                m_options[PREFIX] = "";
                m_options[BUCKET_NAME] = bucketname;
            }

            bucketname = m_options[BUCKET_NAME];

            if (hasInitial)
                m_options[INITIALPASSWORD] = initialPwd;

            if (!string.IsNullOrEmpty(m_uiAction))
                m_options.Add(DUPLICATI_ACTION_MARKER, m_uiAction);
        }

        void S3UI_Load(object sender, EventArgs args)
        {
            AWS_ID.Items.Clear();
            foreach (KeyValuePair<string, string> x in S3CommonOptions.ExtractAccounts(m_applicationSettings))
                AWS_ID.Items.Add(new Utility.ComboBoxItemPair<string>(x.Key, x.Value));

            Bucketregions.Items.Clear();
            Servernames.Items.Clear();

            foreach (KeyValuePair<string, string> s in S3.KNOWN_S3_LOCATIONS)
                Bucketregions.Items.Add(new Utility.ComboBoxItemPair<string>(string.Format("{0} ({1})", s.Key, string.IsNullOrEmpty(s.Value) ? "-none-" : s.Value), s.Value));

            foreach (KeyValuePair<string, string> s in S3.KNOWN_S3_PROVIDERS)
                Servernames.Items.Add(new Utility.ComboBoxItemPair<string>(string.Format("{0} ({1})", s.Key, s.Value), s.Value));

            if (m_options.ContainsKey(ACCESS_ID))
                AWS_ID.Text = m_options[ACCESS_ID];
            if (m_options.ContainsKey(ACCESS_KEY))
                AWS_KEY.Text = m_options[ACCESS_KEY];
            if (m_options.ContainsKey(BUCKET_NAME))
                BucketName.Text = m_options[BUCKET_NAME];
            if (m_options.ContainsKey(PREFIX) && !string.IsNullOrEmpty(m_options[PREFIX]))
                BucketName.Text += "/" + m_options[PREFIX];

            if (!m_options.ContainsKey(INITIALPASSWORD))
                m_options[INITIALPASSWORD] = m_options.ContainsKey(ACCESS_KEY) ? m_options[ACCESS_KEY] : "";
            AWS_KEY.AskToEnterNewPassword = !string.IsNullOrEmpty(m_options[INITIALPASSWORD]);
            AWS_KEY.InitialPassword = m_options[INITIALPASSWORD];

            bool b = false;

            if (m_options.ContainsKey(EUROBUCKET))
            {
                if (!bool.TryParse(m_options[EUROBUCKET], out b))
                    b = false;
            }

            string region;

            if (m_options.ContainsKey(BUCKET_LOCATION))
                region = m_options[BUCKET_LOCATION];
            else if (m_options.ContainsKey(EUROBUCKET))
                region = b ? S3.S3_EU_REGION_NAME : null;
            else
                region = S3CommonOptions.ExtractDefaultBucketRegion(m_applicationSettings);

            string server;

            if (m_options.ContainsKey(SERVER_HOSTNAME))
                server = m_options[SERVER_HOSTNAME];
            else
                server = S3CommonOptions.ExtractDefaultServername(m_applicationSettings);

            if (string.IsNullOrEmpty(server))
                server = S3.DEFAULT_S3_HOST;

            Servernames.Text = server;
            Bucketregions.Text = region;

            if (m_options.ContainsKey(RRS))
            {
                if (!bool.TryParse(m_options[RRS], out b))
                    b = false;
            }
            else
            {
                b = S3CommonOptions.ExtractDefaultRRS(m_applicationSettings);
            }

            UseRRS.Checked = b;

            if (!m_options.ContainsKey(HASTESTED) || !bool.TryParse(m_options[HASTESTED], out m_hasTested))
                m_hasTested = false;

            if (!m_options.ContainsKey(HASCREATEDBUCKET) || !bool.TryParse(m_options[HASCREATEDBUCKET], out m_hasCreatedbucket))
                m_hasCreatedbucket = false;

            if (!m_options.ContainsKey(HASSUGGESTEDPREFIX) || !bool.TryParse(m_options[HASSUGGESTEDPREFIX], out m_hasSuggestedPrefix))
                m_hasSuggestedPrefix = false;

            if (!m_options.ContainsKey(HASSUGGESTEDLOWERCASE) || !bool.TryParse(m_options[HASSUGGESTEDLOWERCASE], out m_hasSuggestedLowerCase))
                m_hasSuggestedLowerCase = false;

            if (!m_options.ContainsKey(HASWARNEDINVALIDBUCKETNAME) || !bool.TryParse(m_options[HASWARNEDINVALIDBUCKETNAME], out m_hasWarnedInvalidBucketname))
                m_hasWarnedInvalidBucketname = false;

            m_options.TryGetValue(DUPLICATI_ACTION_MARKER, out m_uiAction);
        }

        /// <summary>
        /// Mono has a problem with non-existing buckets
        /// </summary>
        /// <returns>True if the bucket is created, false otherwise</returns>
        private bool EnsureBucketForMono()
        {
            try
            {
                if (m_hasCreatedbucket)
                    return true;

                //The problem should be fixed in any version after 2.4.2.3
                if (Utility.Utility.IsMono && Utility.Utility.MonoVersion <= new Version(2, 4, 2, 3))
                {
                    switch (MessageBox.Show(this, Strings.S3UI.MonoRequiresExistingBucket, Application.ProductName, MessageBoxButtons.YesNoCancel))
                    {
                        case DialogResult.Yes: //Create it
                            CreateBucket.PerformClick();
                            if (!m_hasCreatedbucket)
                                return false;
                            break;
                        case DialogResult.No: //Ignore
                            break;
                        default:
                            return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, string.Format(Interface.CommonStrings.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            return true;
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
                MessageBox.Show(this, Library.Interface.CommonStrings.EmptyServernameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servernames.Focus(); }
                catch { }

                return false;
            }

            if (!Library.Utility.Utility.IsValidHostname(servername))
            {
                MessageBox.Show(this, string.Format(Library.Interface.CommonStrings.InvalidServernameError, servername), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { Servernames.Focus(); }
                catch { }

                return false;
            }

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

            if (!m_hasSuggestedPrefix && !BucketName.Text.ToLower().StartsWith(AWS_ID.Text.ToLower()))
            {
                DialogResult res = MessageBox.Show(this, Strings.S3UI.BucketnameNotPrefixedWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes)
                    BucketName.Text = AWS_ID.Text.ToLower() + "-" + BucketName.Text;
                else if (res == DialogResult.No)
                    m_hasSuggestedPrefix = true;
                else if (res == DialogResult.Cancel)
                    return false;
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

            string region;
            if (Bucketregions.SelectedItem as Utility.ComboBoxItemPair<string> == null)
                region = Bucketregions.Text;
            else
                region = (Bucketregions.SelectedItem as Utility.ComboBoxItemPair<string>).Value;

            //TODO: Figure out if the AWSSDK even supports old bucket names
            bool isNonDefaultBucket = !(string.IsNullOrEmpty(region) || region.Equals("us-west-1"));

            if (!m_hasSuggestedLowerCase && bucketname.ToLower() != bucketname)
            {
                string reason = isNonDefaultBucket ?
                    Strings.S3UI.NonUSBucketsRequireLowerCaseError :
                    Strings.S3UI.NewS3RequiresLowerCaseError;

                DialogResult res = MessageBox.Show(this, string.Format(Strings.S3UI.BucketnameCaseWarning, reason), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
                if (res == DialogResult.Yes)
                {
                    bucketname = bucketname.ToLower();
                    BucketName.Text = bucketname + "/" + prefix;
                }
                else if (res == DialogResult.Cancel || isNonDefaultBucket)
                {
                    return false;
                }
                else if (res == DialogResult.No)
                    m_hasSuggestedLowerCase = true;
            }

            if (!S3.IsValidHostname(bucketname))
            {
                if (isNonDefaultBucket)
                {
                    MessageBox.Show(this, string.Format(Strings.S3UI.HostnameInvalidWithNonUSBucketOptionError, bucketname), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return false;
                }
                else
                {
                    if (!m_hasWarnedInvalidBucketname && MessageBox.Show(this, string.Format(Strings.S3UI.HostnameInvalidWarning, bucketname), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning) != DialogResult.Yes)
                        return false;
                }

                m_hasWarnedInvalidBucketname = true;
            }

            if (checkForBucket)
                return EnsureBucketForMono();
            else
                return true;
        }

        private void SignUpLink_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Duplicati.Library.Utility.UrlUtillity.OpenUrl(GetSignupLink(Servernames.Text));
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
                    using(S3 s3 = new S3(destination, options))
                        foreach (Interface.IFileEntry n in s3.List())
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
                    m_hasCreatedbucket = true; //If the test succeeds, the bucket exists
                }
                catch (Interface.FolderMissingException)
                {
                    switch (MessageBox.Show(this, Strings.S3UI.CreateMissingBucket, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                    {
                        case DialogResult.Yes:
                            CreateBucket.PerformClick();
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
            m_hasCreatedbucket = false;
            m_hasSuggestedPrefix = false;
            m_hasSuggestedLowerCase = false;
            m_hasWarnedInvalidBucketname = false;
        }

        private void UseEuroBuckets_CheckedChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_hasWarnedInvalidBucketname = false;
        }

        private void AWS_ID_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (AWS_ID.SelectedItem as Utility.ComboBoxItemPair<string> != null)
            {
                AWS_KEY.AskToEnterNewPassword = false;
                AWS_KEY.IsPasswordVisible = false;
                AWS_KEY.Text = (AWS_ID.SelectedItem as Utility.ComboBoxItemPair<string>).Value;
                AWS_KEY.AskToEnterNewPassword = true;
            }
        }

        public static string GetConfiguration(IDictionary<string, string> guiOptions, IDictionary<string, string> commandlineOptions)
        {
            if (guiOptions.ContainsKey(ACCESS_ID) && !string.IsNullOrEmpty(guiOptions[ACCESS_ID]))
                commandlineOptions["aws_access_key_id"] = guiOptions[ACCESS_ID];
            if (guiOptions.ContainsKey(ACCESS_KEY) && !string.IsNullOrEmpty(guiOptions[ACCESS_KEY]))
                commandlineOptions["aws_secret_access_key"] = guiOptions[ACCESS_KEY];

            bool useEuroBucket;
            bool useRRS;

            if (!guiOptions.ContainsKey(EUROBUCKET) || !bool.TryParse(guiOptions[EUROBUCKET], out useEuroBucket))
                useEuroBucket = false;

            if (!guiOptions.ContainsKey(RRS) || !bool.TryParse(guiOptions[RRS], out useRRS))
                useRRS = false;

            if (!guiOptions.ContainsKey(BUCKET_NAME))
                throw new Exception(string.Format(Interface.CommonStrings.ConfigurationIsMissingItemError, BUCKET_NAME));

            string bucketName = guiOptions[BUCKET_NAME];
            string host = guiOptions.ContainsKey(SERVER_HOSTNAME) ? guiOptions[SERVER_HOSTNAME] : "";
            string prefix = guiOptions.ContainsKey(PREFIX) ? guiOptions[PREFIX] : "";
            string region = guiOptions.ContainsKey(BUCKET_LOCATION) ? guiOptions[BUCKET_LOCATION] : (useEuroBucket ? S3.S3_EU_REGION_NAME : null);

            if (useRRS)
                commandlineOptions[S3.RRS_OPTION] = "";

            if (!string.IsNullOrEmpty(host))
                commandlineOptions[S3.SERVER_NAME] = host;
            if (!string.IsNullOrEmpty(region))
                commandlineOptions[S3.LOCATION_OPTION] = region;

            return "s3://" + bucketName + "/" + (string.IsNullOrEmpty(prefix) ? "" : prefix);
        }

        public static string PageTitle
        {
            get { return Strings.S3UI.PageTitle; }
        }

        public static string PageDescription
        {
            get { return Strings.S3UI.PageDescription; }
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

                    S3 s3 = new S3(destination, options);
                    s3.CreateFolder();

                    MessageBox.Show(this, Interface.CommonStrings.FolderCreated, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                    m_hasCreatedbucket = true;
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

        private void Bucketregions_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Bucketregions.SelectedItem as Utility.ComboBoxItemPair<string> != null)
                BeginInvoke(new SetComboTextDelegate(SetComboText), Bucketregions, (Bucketregions.SelectedItem as Utility.ComboBoxItemPair<string>).Value);
        }

        private void Servernames_TextChanged(object sender, EventArgs e)
        {
            m_hasTested = false;
            m_hasCreatedbucket = false;
            m_hasSuggestedPrefix = false;
            m_hasSuggestedLowerCase = false;
            m_hasWarnedInvalidBucketname = false;
        }

    }
}
