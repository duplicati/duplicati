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

namespace Duplicati.GUI.Wizard_pages
{
    public partial class SelectBackend : WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public SelectBackend()
            : base("Select a place to store the backups", "On this page you can select the type of device or service that store the backups. You may need information from the service provider when you continue.")
        {
            InitializeComponent();
            base.PageEnter += new PageChangeHandler(SelectBackend_PageEnter);
            base.PageLeave += new PageChangeHandler(SelectBackend_PageLeave);
            base.PageDisplay += new PageChangeHandler(SelectBackend_PageDisplay);
        }

        void SelectBackend_PageDisplay(object sender, PageChangedArgs args)
        {
            Item_CheckChanged(null, null);
        }

        void SelectBackend_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            if (!(File.Checked || FTP.Checked || SSH.Checked || WebDAV.Checked || S3.Checked))
            {
                MessageBox.Show(this, "You must enter the storage method before you can continue.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (WebDAV.Checked)
            {
                MessageBox.Show(this, "WebDAV is not implemented yet.", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                args.Cancel = true;
                return;
            }

            if (File.Checked)
            {
                args.NextPage = new Backends.File.FileOptions();
                m_wrapper.Backend = WizardSettingsWrapper.BackendType.File;
            }
            else if (FTP.Checked)
            {
                args.NextPage = new Backends.FTP.FTPOptions();
                m_wrapper.Backend = WizardSettingsWrapper.BackendType.FTP;
            }
            else if (SSH.Checked)
            {
                args.NextPage = new Backends.SSH.SSHOptions();
                m_wrapper.Backend = WizardSettingsWrapper.BackendType.SSH;
            }
            else if (WebDAV.Checked)
            {
                args.NextPage = new Backends.WebDAV.WebDAVOptions();
                m_wrapper.Backend = WizardSettingsWrapper.BackendType.WebDav;
            }
            else if (S3.Checked)
            {
                args.NextPage = new Backends.S3.S3Options();
                m_wrapper.Backend = WizardSettingsWrapper.BackendType.S3;
            }
            else
            {
                m_wrapper.Backend = WizardSettingsWrapper.BackendType.Unknown;
                args.NextPage = null;
                args.Cancel = true;
                return;
            }
        }


        void SelectBackend_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            if (!m_valuesAutoLoaded)
            {
                switch (m_wrapper.Backend)
                {
                    case WizardSettingsWrapper.BackendType.File:
                        File.Checked = true;
                        break;
                    case WizardSettingsWrapper.BackendType.FTP:
                        FTP.Checked = true;
                        break;
                    case WizardSettingsWrapper.BackendType.SSH:
                        SSH.Checked = true;
                        break;
                    case WizardSettingsWrapper.BackendType.WebDav:
                        WebDAV.Checked = true;
                        break;
                    case WizardSettingsWrapper.BackendType.S3:
                        S3.Checked = true;
                        break;
                }
            }

            Item_CheckChanged(null, null);


        }

        private void Item_CheckChanged(object sender, EventArgs e)
        {
            m_owner.NextButton.Enabled = File.Checked || FTP.Checked || SSH.Checked || WebDAV.Checked || S3.Checked;
        }
    }
}
