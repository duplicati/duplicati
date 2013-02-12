using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public struct RemoteVolumeEntry
    {
        public readonly string Name;
        public readonly string Hash;
        public readonly long Size;
        public readonly RemoteVolumeType Type;
        public readonly RemoteVolumeState State;

        public RemoteVolumeEntry(string name, string hash, long size, RemoteVolumeType type, RemoteVolumeState state)
        {
            this.Name = name;
            this.Size = size;
            this.Type = type;
            this.State = state;
            this.Hash = hash;
        }
    }
}
