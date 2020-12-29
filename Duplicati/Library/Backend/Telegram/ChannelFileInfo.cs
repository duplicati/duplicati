using System;
using Duplicati.Library.Common.IO;
using TeleSharp.TL;

namespace Duplicati.Library.Backend
{
    public class ChannelFileInfo : IEquatable<ChannelFileInfo>
    {
        public int MessageId { get; }
        public long MediaDocAccessHash { get; }
        public long DocumentId { get; }
        public int Version { get; }
        public long Size { get; }
        public string Name { get; }
        public DateTime Date { get; }

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
            return ReferenceEquals(this, obj) || obj is ChannelFileInfo other && Equals(other);
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