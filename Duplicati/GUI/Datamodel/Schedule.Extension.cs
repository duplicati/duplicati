using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Datamodel
{
    public partial class Schedule
    {
        public bool ExistsInDb
        {
            get { return this.ID > 0; }
        }

        public void GetOptions(Dictionary<string, string> options)
        {
            if (!string.IsNullOrEmpty(this.MaxUploadsize))
                options["totalsize"] = this.MaxUploadsize;
            if (!string.IsNullOrEmpty(this.VolumeSize))
                options["volsize"] = this.VolumeSize;
            if (!string.IsNullOrEmpty(this.DownloadBandwidth))
                options["max-download-pr-second"] = this.DownloadBandwidth;
            if (!string.IsNullOrEmpty(this.UploadBandwidth))
                options["max-upload-pr-second"] = this.DownloadBandwidth;
        }
    }
}
