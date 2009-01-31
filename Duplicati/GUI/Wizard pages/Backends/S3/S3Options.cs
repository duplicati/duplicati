#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
        private Duplicati.Datamodel.Backends.S3 m_s3;
        private bool m_hasTested;
       
        public S3Options()
            : base("Backup storage options", "On this page you can select where to store the backup data.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(S3Options_PageEnter);
            base.PageLeave += new PageChangeHandler(S3Options_PageLeave);
        }

        void S3Options_PageLeave(object sender, PageChangedArgs args)
        {
            SaveSettings();

            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!ValidateForm())
            {
                args.Cancel = true;
                return;
            }

            if (!m_hasTested)
                switch (MessageBox.Show(this, "Do you want to test the connection?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question))
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


            SaveSettings();

            args.NextPage = new Add_backup.AdvancedOptions();
        }

        void S3Options_PageEnter(object sender, PageChangedArgs args)
        {
            m_s3 = new Duplicati.Datamodel.Backends.S3(((Schedule)m_settings["Schedule"]).Tasks[0]);

            if (!m_valuesAutoLoaded)
            {
                AWS_ID.Text = m_s3.AccessID;
                AWS_KEY.Text = m_s3.AccessKey;
                BucketName.Text = m_s3.BucketName + "/" + m_s3.Prefix;
                UseEuroBuckets.Checked = m_s3.UseEuroBucket;
            }

            if (!m_settings.ContainsKey("S3:HasTested"))
                m_hasTested = false;
            else
                m_hasTested = (bool)m_settings["S3:HasTested"];
            
        }

        private void SaveSettings()
        {
            m_s3.AccessID = AWS_ID.Text;
            m_s3.AccessKey = AWS_KEY.Text;
            m_s3.ServerUrl = null;
            m_s3.UseEuroBucket = UseEuroBuckets.Checked;
            m_s3.SetService();
            m_settings["S3:Path"] = BucketName.Text;
            m_settings["S3:HasTested"] = m_hasTested;
        }

        private bool ValidateForm()
        {
            if (AWS_ID.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter your AWS Access ID.\nYou may click the link to the right\nto open the AWS login page, and retrieve it.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (AWS_KEY.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter your AWS Access ID.\nYou may click the link to the right\nto open the AWS login page, and retrieve it.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (BucketName.Text.Trim().Length <= 0)
            {
                MessageBox.Show(this, "You must enter a name for the bucket.\nYou must use a unique name for each backup.\nYou may enter any name you like.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            }

            if (!BucketName.Text.ToLower().StartsWith(AWS_ID.Text.ToLower()))
            {
                DialogResult res = MessageBox.Show(this, "The bucket name does not start with your user ID.\nTo avoid using a bucket owned by another user,\nit is recommended that you put your user ID in front of the bucket name.\nDo you want to insert the user ID in front of the bucket name?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
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
                    "The european buckets require that the bucket name is in lower case." :
                    "The new amazon s3 API requires that bucket names are all lower case.";

                DialogResult res = MessageBox.Show(this, "The bucket name is not in all lower case.\n" + reason + "\nDo you want to convert the bucket name to lower case?", Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Warning);
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

            m_s3.BucketName = bucketname;
            m_s3.Prefix = prefix;
            m_s3.UseSubdomainStrategy = bucketname.ToLower() == bucketname;

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
                SaveSettings();

                try
                {
                    string target = m_s3.GetDestinationPath();
                    Dictionary<string, string> options = new Dictionary<string, string>();
                    m_s3.GetOptions(options);

                    string[] files = Duplicati.Library.Main.Interface.List(target, options);

                    MessageBox.Show(this, "Connection succeeded!", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    m_hasTested = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "Connection Failed: " + ex.Message, Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);

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
    }
}
