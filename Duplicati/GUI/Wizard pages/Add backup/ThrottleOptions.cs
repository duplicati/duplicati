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
        private Schedule m_schedule;

        public ThrottleOptions()
            : base("Select how to limit the backup", "On this page you may select limits that prevent the backup procedure from using too many resources")
        {
            InitializeComponent();

            base.PageLeave += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageLeave);
            base.PageEnter += new System.Windows.Forms.Wizard.PageChangeHandler(ThrottleOptions_PageEnter);
        }

        private void LoadItem(string name, int defaultSuffix, string value)
        {
            if (this.Controls.ContainsKey(name + "LimitEnabled"))
            {
                ((CheckBox)this.Controls[name + "LimitEnabled"]).Checked = !string.IsNullOrEmpty(value) && value != "0";
                if (!((CheckBox)this.Controls[name + "LimitEnabled"]).Checked)
                    return;
            }

            ComboBox combo = (ComboBox)this.Controls[name + "LimitSuffix"];
            NumericUpDown number = (NumericUpDown)this.Controls[name + "LimitNumber"];

            long size = 0;

            size = Duplicati.Library.Core.Sizeparser.ParseSize(value);
            number.Value = 0;

            if (size != 0)
            {
                for (int i = 0; i < combo.Items.Count; i++)
                    if (size < Math.Pow(2, 10 * (i + 1)))
                    {
                        combo.SelectedIndex = i;
                        number.Value = (int)(size / (long)Math.Pow(2, 10 * i));
                        break;
                    }

                if (number.Value == 0)
                    size = 0;
            }

            if (size == 0)
            {
                combo.SelectedIndex = defaultSuffix;
                number.Value = 0;
            }
        }

        void ThrottleOptions_PageEnter(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            m_schedule = (Schedule)m_settings["Schedule"];
            if (!m_valuesAutoLoaded)
            {
                LoadItem("Upload", 1, m_schedule.UploadBandwidth);
                LoadItem("Download", 1, m_schedule.DownloadBandwidth);
                LoadItem("Backup", 2, m_schedule.MaxUploadsize);
                LoadItem("VolumeSize", 2, m_schedule.VolumeSize);
            }
        }

        void ThrottleOptions_PageLeave(object sender, System.Windows.Forms.Wizard.PageChangedArgs args)
        {
            if (args.Direction == System.Windows.Forms.Wizard.PageChangedDirection.Back)
                return;

            if (UploadLimitEnabled.Checked)
                m_schedule.UploadBandwidth = UploadLimitNumber.Value.ToString() + UploadLimitSuffix.Text.Substring(0, UploadLimitSuffix.Text.Length - 2);
            else
                m_schedule.UploadBandwidth = "";

            if (DownloadLimitEnabled.Checked)
                m_schedule.DownloadBandwidth = DownloadLimitNumber.Value.ToString() + DownloadLimitSuffix.Text.Substring(0, DownloadLimitSuffix.Text.Length - 2);
            else
                m_schedule.DownloadBandwidth = "";

            if (BackupLimitEnabled.Checked)
                m_schedule.MaxUploadsize = BackupLimitNumber.Value.ToString() + BackupLimitSuffix.Text;
            else
                m_schedule.MaxUploadsize = "";

            m_schedule.VolumeSize = VolumeSizeLimitNumber.Value.ToString() + VolumeSizeLimitSuffix.Text;

            if ((bool)m_settings["Advanced:Filters"])
                args.NextPage = new Wizard_pages.Add_backup.FilterEditor();
            else
                args.NextPage = new FinishedAdd();
        }


        private void UploadLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            UploadLimitNumber.Enabled = UploadLimitSuffix.Enabled = UploadLimitEnabled.Checked;
        }

        private void DownloadLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            DownloadLimitNumber.Enabled = DownloadLimitSuffix.Enabled = DownloadLimitEnabled.Checked;
        }

        private void BackupLimitEnabled_CheckedChanged(object sender, EventArgs e)
        {
            BackupLimitNumber.Enabled = BackupLimitSuffix.Enabled = BackupLimitEnabled.Checked;
        }
    }
}

