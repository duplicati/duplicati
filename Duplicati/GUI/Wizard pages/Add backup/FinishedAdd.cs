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

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class FinishedAdd : WizardControl
    {
        WizardSettingsWrapper m_wrapper;

        public FinishedAdd()
            : base("Ready to add backup", "You have now entered all the required data, and can now create the backup.")
        {
            InitializeComponent();

            base.PageEnter += new PageChangeHandler(FinishedAdd_PageEnter);
            base.PageLeave += new PageChangeHandler(FinishedAdd_PageLeave);
        }

        void FinishedAdd_PageLeave(object sender, PageChangedArgs args)
        {
            if (args.Direction == PageChangedDirection.Back)
                return;

            m_wrapper.RunImmediately = RunNow.Checked;
        }

        void FinishedAdd_PageEnter(object sender, PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);

            List<KeyValuePair<string, string>> strings = new List<KeyValuePair<string, string>>();
            if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
                strings.Add(new KeyValuePair<string, string>("Action", "Add new backup"));
            else
                strings.Add(new KeyValuePair<string, string>("Action", "Modify backup"));

            strings.Add(new KeyValuePair<string, string>("Source folder", m_wrapper.SourcePath));
            strings.Add(new KeyValuePair<string, string>("When", m_wrapper.BackupTimeOffset.ToString()));
            if (!string.IsNullOrEmpty(m_wrapper.RepeatInterval))
                strings.Add(new KeyValuePair<string, string>("Repeat", m_wrapper.RepeatInterval));
            if (!string.IsNullOrEmpty(m_wrapper.FullBackupInterval))
                strings.Add(new KeyValuePair<string, string>("Full backup each", m_wrapper.FullBackupInterval));
            if (m_wrapper.MaxFullBackups > 0)
                strings.Add(new KeyValuePair<string, string>("Keep full backups", m_wrapper.MaxFullBackups.ToString()));

            strings.Add(new KeyValuePair<string, string>(null, null));
            strings.Add(new KeyValuePair<string, string>("Destination", m_wrapper.Backend.ToString()));

            switch(m_wrapper.Backend)
            {
                case WizardSettingsWrapper.BackendType.File:
                    FileSettings file = new FileSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>("Destination path", file.Path));
                    break;
                case WizardSettingsWrapper.BackendType.FTP:
                    FTPSettings ftp = new FTPSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>("Destination path", ftp.Server + "/" + ftp.Path ));
                    break;
                case WizardSettingsWrapper.BackendType.SSH:
                    SSHSettings ssh = new SSHSettings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>("Destination path", ssh.Server + "/" + ssh.Path));
                    break;
                case WizardSettingsWrapper.BackendType.S3:
                    S3Settings s3 = new S3Settings(m_wrapper);
                    strings.Add(new KeyValuePair<string, string>("Destination path", s3.Path));
                    break;
            }
            
            int maxlen = 0;
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key != null)
                    maxlen = Math.Max(maxlen, i.Key.Length);

            System.Text.StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> i in strings)
                if (i.Key == null)
                    sb.Append("\r\n");
                else
                    sb.Append(i.Key + ": " + new String(' ', maxlen - i.Key.Length) + i.Value + "\r\n");

            Summary.Text = sb.ToString();

            args.TreatAsLast = true;
        }

        public override string HelpText
        {
            get
            {
                if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
                    return base.HelpText;
                else
                    return "The backup modifications are now ready to be saved.";
            }
        }

        public override string Title
        {
            get
            {
                if (m_wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Add)
                    return base.Title;
                else
                    return "Ready to save modifications";
            }
        }
    }
}
