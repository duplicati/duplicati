#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class GeneratedFilenameOptions : System.Windows.Forms.Wizard.WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        public GeneratedFilenameOptions()
            : base(Strings.GeneratedFilenameOptions.PageTitle, Strings.GeneratedFilenameOptions.PageDescription)
        {
            InitializeComponent();

            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(GeneratedFilenameOptions_PageEnter);
            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(GeneratedFilenameOptions_PageLeave);
        }

        void GeneratedFilenameOptions_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            m_wrapper.FileTimeSeperator = FileTimeSeperator.Text;
            m_wrapper.FilePrefix = FilePrefixEnabled.Checked ? "" : FilePrefix.Text;
            m_wrapper.ShortFilenames = UseShortFilenames.Checked;

            WizardSettingsWrapper wrapper = new WizardSettingsWrapper(m_settings);

            if (wrapper.PrimayAction == WizardSettingsWrapper.MainAction.RestoreSetup)
                args.NextPage = new RestoreSetup.FinishedRestoreSetup();
            else if (wrapper.PrimayAction == WizardSettingsWrapper.MainAction.Restore)
            {
                Datamodel.Schedule s = wrapper.DataConnection.GetObjectById<Datamodel.Schedule>(wrapper.ScheduleID);
                wrapper.UpdateSchedule(s);
                args.NextPage = new Restore.SelectBackup();
            }
            else if ((bool)m_settings["Advanced:Overrides"])
                args.NextPage = new Wizard_pages.Add_backup.SettingOverrides();
            else
                args.NextPage = new Wizard_pages.Add_backup.FinishedAdd();

        }

        void GeneratedFilenameOptions_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            if (!m_valuesAutoLoaded)
            {
                FileTimeSeperator.Text = m_wrapper.FileTimeSeperator;
                if (FileTimeSeperator.Text == "")
                    FileTimeSeperator.Text = ":";
                FilePrefixEnabled.Checked = !string.IsNullOrEmpty(m_wrapper.FilePrefix);
                FilePrefix.Text = m_wrapper.FilePrefix;
                UseShortFilenames.Checked = m_wrapper.ShortFilenames;
            }
        }

        private void FilePrefixEnabled_CheckedChanged(object sender, EventArgs e)
        {
            FilePrefix.Enabled = FilePrefixEnabled.Checked;
        }
    }
}

