using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.ForestHash.Database
{
    public interface IRemoteVolume
    {
        string Name { get; }
        string Hash { get; }
        long Size { get; }
    }
}
