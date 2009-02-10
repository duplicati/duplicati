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
using Duplicati.Datamodel.Backends;

namespace Duplicati.GUI.Service_controls
{
    public partial class S3Settings : UserControl
    {
        private S3 m_s3;
        private bool m_isUpdating = false;

        public S3Settings()
        {
            InitializeComponent();
        }

        public void Setup(S3 s3)
        {
            try
            {
                m_isUpdating = true;
                m_s3 = s3;

                AccessID.Text = m_s3.AccessID;
                AccessKey.Text = m_s3.AccessKey;
                BucketName.Text = m_s3.BucketName;
                EuropeanCheckbox.Checked = m_s3.UseEuroBucket;
                ServerUrl.Text = m_s3.ServerUrl;

            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void AccessID_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_s3 == null)
                return;

            m_s3.AccessID = AccessID.Text;
        }

        private void AccessKey_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_s3 == null)
                return;

            m_s3.AccessKey = AccessKey.Text;
        }

        private void BucketName_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_s3 == null)
                return;

            m_s3.BucketName = BucketName.Text;
        }

        private void EuropeanCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_s3 == null)
                return;

            m_s3.UseEuroBucket = EuropeanCheckbox.Checked;
        }

        private void ServerUrl_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating || m_s3 == null)
                return;

            if (ServerUrl.Text.Trim().Length == 0)
                m_s3.ServerUrl = null;
            else
                m_s3.ServerUrl = ServerUrl.Text;
        }

    }
}
