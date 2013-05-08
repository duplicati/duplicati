using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    public class RemoteVolume : IRemoteVolume
    {
        public RemoteVolume(Library.Interface.IFileEntry file, string hash = null)
        {
            this.Name = file.Name;
            this.Size = file.Size;
            this.Hash = hash;
            this.File = file;
        }

        public RemoteVolume(string name, string hash, long size)
        {
            this.Name = name;
            this.Hash = hash;
            this.Size = size;
            this.File = null;
        }

        public string Name { get; private set; }
        public string Hash { get; private set; }
        public long Size { get; private set; }
        public Library.Interface.IFileEntry File { get; private set; }
    }
}
