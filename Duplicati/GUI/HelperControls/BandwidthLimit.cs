using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.GUI.HelperControls
{
    public partial class BandwidthLimit : UserControl
    {
        public BandwidthLimit()
        {
            InitializeComponent();
        }

        public event EventHandler DownloadLimitChanged;
        public event EventHandler DownloadLimitInBytesChanged;
        public event EventHandler UploadLimitChanged;
        public event EventHandler UploadLimitInBytesChanged;

        [DefaultValue("")]
        public string DownloadLimit
        {
            get
            {
                if (!DownloadLimitEnabled)
                    return null;
                else
                    return DownloadLimitPicker.CurrentSize;
            }
            set
            {
                if (string.IsNullOrEmpty(value) || Library.Core.Sizeparser.ParseSize(value) == 0)
                {
                    DownloadLimitEnabled = false;
                }
                else
                {
                    DownloadLimitPicker.CurrentSize = value;
                    DownloadLimitEnabled = true;
                }

                if (DownloadLimitChanged != null)
                    DownloadLimitChanged(this, null);
                if (DownloadLimitInBytesChanged != null)
                    DownloadLimitInBytesChanged(this, null);
            }
        }

        [DefaultValue(0)]
        public long DownloadLimitInBytes
        {
            get
            {
                if (DownloadLimitEnabled)
                    return DownloadLimitPicker.CurrentSizeInBytes;
                else
                    return 0;
            }
            set
            {
                if (value <= 0)
                {
                    DownloadLimitEnabled = false;
                }
                else
                {
                    DownloadLimitPicker.CurrentSizeInBytes = value;
                    DownloadLimitEnabled = true;
                }

                if (DownloadLimitChanged != null)
                    DownloadLimitChanged(this, null);
                if (DownloadLimitInBytesChanged != null)
                    DownloadLimitInBytesChanged(this, null);
            }
        }

        [DefaultValue(false)]
        public bool DownloadLimitEnabled
        {
            get { return DownloadLimitCheckbox.Checked; }
            set { DownloadLimitCheckbox.Checked = value; }
        }

        [DefaultValue("")]
        public string UploadLimit
        {
            get
            {
                if (!UploadLimitEnabled)
                    return null;
                else
                    return UploadLimitPicker.CurrentSize;
            }
            set
            {
                if (string.IsNullOrEmpty(value) || Library.Core.Sizeparser.ParseSize(value) == 0)
                {
                    UploadLimitEnabled = false;
                }
                else
                {
                    UploadLimitPicker.CurrentSize = value;
                    UploadLimitEnabled = true;
                }
                if (UploadLimitChanged != null)
                    UploadLimitChanged(this, null);
                if (UploadLimitInBytesChanged != null)
                    UploadLimitInBytesChanged(this, null);
            }
        }

        [DefaultValue(0)]
        public long UploadLimitInBytes
        {
            get
            {
                if (UploadLimitEnabled)
                    return UploadLimitPicker.CurrentSizeInBytes;
                else
                    return 0;
            }
            set
            {
                if (value <= 0)
                {
                    UploadLimitEnabled = false;
                }
                else
                {
                    UploadLimitPicker.CurrentSizeInBytes = value;
                    UploadLimitEnabled = true;
                }

                if (UploadLimitChanged != null)
                    UploadLimitChanged(this, null);
                if (UploadLimitInBytesChanged != null)
                    UploadLimitInBytesChanged(this, null);
            }
        }

        [DefaultValue(false)]
        public bool UploadLimitEnabled
        {
            get { return UploadLimitCheckbox.Checked; }
            set { UploadLimitCheckbox.Checked = value; }
        }

        private void UploadLimitCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            UploadLimitPicker.Enabled = UploadLimitEnabled;

            if (UploadLimitChanged != null)
                UploadLimitChanged(this, null);
            if (UploadLimitInBytesChanged != null)
                UploadLimitInBytesChanged(this, null);
        }

        private void DownloadLimitCheckbox_CheckedChanged(object sender, EventArgs e)
        {
            DownloadLimitPicker.Enabled = DownloadLimitEnabled;

            if (DownloadLimitChanged != null)
                DownloadLimitChanged(this, null);
            if (DownloadLimitInBytesChanged != null)
                DownloadLimitInBytesChanged(this, null);
        }

        private void UploadLimitPicker_CurrentSizeChanged(object sender, EventArgs e)
        {
            if (UploadLimitChanged != null)
                UploadLimitChanged(this, null);
            if (UploadLimitInBytesChanged != null)
                UploadLimitInBytesChanged(this, null);
        }

        private void DownloadLimitPicker_CurrentSizeChanged(object sender, EventArgs e)
        {
            if (DownloadLimitChanged != null)
                DownloadLimitChanged(this, null);
            if (DownloadLimitInBytesChanged != null)
                DownloadLimitInBytesChanged(this, null);
        }
    }
}
