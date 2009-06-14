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

namespace Duplicati.GUI.Wizard_pages.Backends.S3
{
    public partial class S3Options : WizardControl
    {
        private const string LOGIN_PAGE = "http://aws.amazon.com/s3";
        private S3Settings m_wrapper;
        private bool m_hasTested;
       
        public S3Options()
            : base(Strings.S3Options.PageTitle, Strings.S3Options.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(S3Options_PageEnter);
            base.PageLeave += new PageChangeHandler(S3Options_PageLeave);
        }

        void S3Options_PageLeave(object sender, PageChangedArgs args)
        {
            m_settings["S3:HasTested"] = m_hasTested;

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!ValidateForm())
            {
                args.Cancel = true;
                return;
            }

            if (!m_hasTested)
                switch (MessageBox.Show(this, Backends.Strings.Common.ConfirmTestConnectionQuestion, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
                {
                    case DialogResult.Yes:
                        TestConnection_Click(null, null);
                        if (!m_hasTested)
                        {
                            args.Cancel = true;
                            return;
                        }
                        break;
                    case DialogResult.No:
                        break;
                    default: //Cancel
                        args.Cancel = true;
                        return;
                }


            m_settings["S3:HasTested"] = m_hasTested;

            m_wrapper.Username = AWS_ID.Text;
            m_wrapper.Password = AWS_KEY.Text;
            m_wrapper.Path = BucketName.Text;
            m_wrapper.UseEuroServer = UseEuroBuckets.Checked;
            string bucketname = m_wrapper.Path;
            if (bucketname.IndexOf("/") > 0)
                bucketname = bucketname.Substring(0, bucketname.IndexOf("/"));
            m_wrapper.UseSubDomains = bucketname.ToLower() == bucketname;

            if (new WizardSettingsWrapper(m_settings).PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new Add_backup.GeneratedFilenameOptions();
            else
                args.NextPage = new Add_backup.AdvancedOptions();
        }

        void S3Options_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings).S3Settings;

            if (!m_valuesAutoLoaded)
            {
                AWS_ID.Text = m_wrapper.Username;
                AWS_KEY.Text = m_wrapper.Password;
                BucketName.Text = m_wrapper.Path;
                UseEuroBuckets.Checked = m_wrapper.UseEuroServer;
            }

            if (m_settings.ContainsKey("S3:HasTested"))
                m_hasTested = (bool)m_settings["S3:HasTested"];
            
        }

        private bool ValidateForm()
        {
            if (AWS_ID.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.S3Options.EmptyAWSIDError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (AWS_KEY.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.S3Options.EmptyAWSKeyError , Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (BucketName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, Strings.S3Options.EmptyBucketnameError, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (!BucketName.Text.ToLower().StartsWith(AWS_ID.Text.ToLower()))
            {
                DialogResult res = MessageBox.Show(this, Strings.S3Options.BucketnameNotPrefixedWarning, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
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
                    Strings.S3Options.EuroBucketsRequireLowerCaseError :
                    Strings.S3Options.NewS3RequiresLowerCaseError;

                DialogResult res = MessageBox.Show(this, string.Format(Strings.S3Options.BucketnameCaseWarning, reason), Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
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
            UrlUtillity.OpenUrl(LOGIN_PAGE);
        }

        private void TestConnection_Click(object sender, EventArgs e)
        {
            if (ValidateForm())
            {
                Cursor c = this.Cursor;
                try
                {
                    this.Cursor = Cursors.WaitCursor;
                    System.Data.LightDatamodel.IDataFetcherCached con = new System.Data.LightDatamodel.DataFetcherNested(Program.DataConnection);
                    Datamodel.Backends.S3 s3 = new Duplicati.Datamodel.Backends.S3(con.Add<Task>());

                    s3.BucketName = BucketName.Text;
                    s3.Prefix = "";

                    if (s3.BucketName.IndexOf("/") > 0)
                    {
                        s3.Prefix = s3.BucketName.Substring(s3.BucketName.IndexOf("/") + 1);
                        s3.BucketName = s3.BucketName.Substring(0, s3.BucketName.IndexOf("/"));
                    }

                    s3.AccessID = AWS_ID.Text;
                    s3.AccessKey = AWS_KEY.Text;
                    s3.ServerUrl = "";
                    s3.UseEuroBucket = UseEuroBuckets.Checked;
                    s3.UseSubdomainStrategy = s3.UseEuroBucket || s3.BucketName == s3.BucketName.ToLower();

                    string target = s3.GetDestinationPath();
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    s3.GetOptions(options);

                    string[] files = Duplicati.Library.Main.Interface.List(target, options);

                    MessageBox.Show(this, Backends.Strings.Common.ConnectionSuccess, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Backends.Strings.Common.ConnectionFailure, ex.Message), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

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
    }
}
