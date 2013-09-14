using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Volumes
{
    public interface IFileEntry
    {
        FilelistEntryType Type { get; }
        string TypeString { get; }
        string Path { get; }
        string Hash { get; }
        long Size { get; }
        DateTime Time { get; }
        string Metahash { get; }
        long Metasize { get; }
        IEnumerable<string> BlocklistHashes { get; }
    }
}
