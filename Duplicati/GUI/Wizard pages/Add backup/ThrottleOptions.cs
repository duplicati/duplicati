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
using System.Data;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using Duplicati.Datamodel;

namespace Duplicati.GUI.Wizard_pages.Add_backup
{
    public partial class ThrottleOptions : System.Windows.Forms.Wizard.WizardControl
    {
        private WizardSettingsWrapper m_wrapper;

        /// <summary>
        /// Textual representation of priorities
        /// </summary>
        private readonly string[] THREAD_PRIORITIES = new string[] {
            GUI.Strings.Common.ThreadPriorityHighest,
            GUI.Strings.Common.ThreadPriorityAboveNormal,
            GUI.Strings.Common.ThreadPriorityNormal,
            GUI.Strings.Common.ThreadPriorityBelowNormal,
            GUI.Strings.Common.ThreadPriortyLowest
        };

        /// <summary>
        /// Mapping the priority to the string index
        /// </summary>
        private System.Threading.ThreadPriority[] PRIORITY_LOOKUP = new System.Threading.ThreadPriority[] {
            System.Threading.ThreadPriority.Highest,
            System.Threading.ThreadPriority.AboveNormal,
            System.Threading.ThreadPriority.Normal,
            System.Threading.ThreadPriority.BelowNormal,
            System.Threading.ThreadPriority.Lowest,
        };

        public ThrottleOptions()
            : base(Strings.ThrottleOptions.PageTitle, Strings.ThrottleOptions.PageDescription)
        {
            InitializeComponent();

            m_autoFillValues = false;

            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageLeave);
            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageEnter);
            base.PageDisplay += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageDisplay);
        }

        void ThrottleOptions_PageDisplay(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            //Reload due to annoying bug with the numerics
            if (m_valuesAutoLoaded)
                base.LoadDialogSettings();
        }

        void ThrottleOptions_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_wrapper = new WizardSettingsWrapper(m_settings);
            if (!m_valuesAutoLoaded)
            {
                Bandwidth.UploadLimit = m_wrapper.UploadSpeedLimit;
                Bandwidth.DownloadLimit = m_wrapper.DownloadSpeedLimit;
                
                if (string.IsNullOrEmpty(m_wrapper.BackupSizeLimit) || Library.Utility.Sizeparser.ParseSize(m_wrapper.BackupSizeLimit) == 0)
                    BackupLimitEnabled.Checked = false;
                else
                {
                    BackupLimitEnabled.Checked = true;
                    BackupLimit.CurrentSize = m_wrapper.BackupSizeLimit;
                }

                VolumeSize.CurrentSize = m_wrapper.VolumeSize;

                AsyncEnabled.Checked = m_wrapper.AsyncTransfer;
                ThreadPriorityPicker.SelectedPriority = string.IsNullOrEmpty(m_wrapper.ThreadPriority) ? null : (System.Threading.ThreadPriority?)Library.Utility.Utility.ParsePriority(m_wrapper.ThreadPriority);
            }
        }

        void ThrottleOptions_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            m_wrapper.UploadSpeedLimit =
                Bandwidth.UploadLimitEnabled ?
                Bandwidth.UploadLimit :
                "";

            m_wrapper.DownloadSpeedLimit =
                Bandwidth.DownloadLimitEnabled ?
                Bandwidth.DownloadLimit :
                "";

            m_wrapper.BackupSizeLimit =
               BackupLimitEnabled.Checked ?
               BackupLimit.CurrentSize :
               "";

            m_wrapper.VolumeSize = VolumeSize.CurrentSize;
            m_wrapper.AsyncTransfer = AsyncEnabled.Checked;
            m_wrapper.ThreadPriority =
                ThreadPriorityPicker.SelectedPriority == null ?
                "" :
                ThreadPriorityPicker.SelectedPriority.Value.ToString();

            //Don't set args.NextPage, it runs on a list
        }

        private void BackupLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            BackupLimit.Enabled = BackupLimitEnabled.Checked;
        }
    }
}

