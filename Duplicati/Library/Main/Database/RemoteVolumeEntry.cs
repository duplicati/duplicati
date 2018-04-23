using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    public struct RemoteVolumeEntry : IRemoteVolume
    {
        public long ID { get; private set; }
        public string Name { get; private set; }
        public string Hash { get; private set; }
        public long Size { get; private set; }
        public RemoteVolumeType Type { get; private set; }
        public RemoteVolumeState State { get; private set; }
        public DateTime DeleteGracePeriod { get; private set; }

        public static readonly RemoteVolumeEntry Empty = new RemoteVolumeEntry(-1, null, null, -1, (RemoteVolumeType)(-1), (RemoteVolumeState)(-1), default(DateTime));

        public RemoteVolumeEntry(long id, string name, string hash, long size, RemoteVolumeType type, RemoteVolumeState state, DateTime deleteGracePeriod)
        {
            ID = id;
            Name = name;
            Size = size;
            Type = type;
            State = state;
            Hash = hash;
            DeleteGracePeriod = deleteGracePeriod;
        }
    }
}
