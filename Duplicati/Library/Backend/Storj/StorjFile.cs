using Duplicati.Library.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Backend.Tardigrade
{
    public class StorjFile : IFileEntry
    {
        public static readonly string STORJ_LAST_ACCESS = "DUPLICATI:LAST-ACCESS";
        public static readonly string STORJ_LAST_MODIFICATION = "DUPLICATI:LAST-MODIFICATION";
        public bool IsFolder { get; set; }

        public DateTime LastAccess { get; set; }

        public DateTime LastModification { get; set; }

        public string Name { get; set; }

        public long Size { get; set; }

        public StorjFile()
        {

        }

        public StorjFile(uplink.NET.Models.Object tardigradeObject)
        {
            IsFolder = tardigradeObject.IsPrefix;
            var lastAccess = tardigradeObject.CustomMetaData.Entries.Where(e => e.Key == STORJ_LAST_ACCESS).FirstOrDefault();
            if (lastAccess != null && !string.IsNullOrEmpty(lastAccess.Value))
            {
                LastAccess = DateTime.Parse(lastAccess.Value);
            }
            else
            {
                LastAccess = DateTime.MinValue;
            }

            var lastMod = tardigradeObject.CustomMetaData.Entries.Where(e => e.Key == STORJ_LAST_MODIFICATION).FirstOrDefault();
            if (lastMod != null && !string.IsNullOrEmpty(lastMod.Value))
            {
                LastModification = DateTime.Parse(lastMod.Value);
            }
            else
            {
                LastModification = DateTime.MinValue;
            }

            Name = tardigradeObject.Key;
            Size = tardigradeObject.SystemMetaData.ContentLength;
        }
    }
}
