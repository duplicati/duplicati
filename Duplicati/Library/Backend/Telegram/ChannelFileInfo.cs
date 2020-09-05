using System;
using Duplicati.Library.Common.IO;

namespace Duplicati.Library.Backend
{
    public class ChannelFileInfo
    {
        public int MessageId { get; set; }
        public int Version { get; set; }
        public long Size { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }

        public ChannelFileInfo()
        { }

        public ChannelFileInfo(int messageId, int version, long size, string name, DateTime date)
        {
            MessageId = messageId;
            Version = version;
            Size = size;
            Name = name;
            Date = date;
        }

        public FileEntry GetFileEntry()
        {
            return new FileEntry(Name, Size)
            {
                LastModification = Date,
                LastAccess = Date,
                IsFolder = false
            };
        }
    }
}