using System;
using Duplicati.Library.Common.IO;
using TeleSharp.TL;

namespace Duplicati.Library.Backend
{
    public class ChannelFileInfo : IEquatable<ChannelFileInfo>
    {
        public int MessageId { get; set; }
        public long MediaDocAccessHash { get; set; }
        public long DocumentId { get; set; }
        public int Version { get; set; }
        public long Size { get; set; }
        public string Name { get; set; }
        public DateTime Date { get; set; }
        public long AccessHash { get; set; }

        public ChannelFileInfo()
        { }

        public ChannelFileInfo(int messageId, long mediaDocAccessHash, long documentId, int version, long size, string name, DateTime date)
        {
            MessageId = messageId;
            MediaDocAccessHash = mediaDocAccessHash;
            DocumentId = documentId;
            Version = version;
            Size = size;
            Name = name;
            Date = date;
        }

        public FileEntry ToFileEntry()
        {
            return new FileEntry(Name, Size)
            {
                LastModification = Date,
                LastAccess = Date,
                IsFolder = false
            };
        }

        public TLInputDocumentFileLocation ToFileLocation()
        {
            return new TLInputDocumentFileLocation
            {
                Id = DocumentId,
                Version = Version,
                AccessHash = MediaDocAccessHash
            };
        }

        public bool Equals(ChannelFileInfo other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return MessageId == other.MessageId;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != this.GetType())
            {
                return false;
            }

            return Equals((ChannelFileInfo)obj);
        }

        public override int GetHashCode()
        {
            return MessageId;
        }

        public static bool operator ==(ChannelFileInfo left, ChannelFileInfo right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(ChannelFileInfo left, ChannelFileInfo right)
        {
            return !Equals(left, right);
        }
    }
}