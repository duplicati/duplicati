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
        string Metablockhash { get; }
        long Metasize { get; }
        string Blockhash { get; }
        long Blocksize { get; }
        IEnumerable<string> BlocklistHashes { get; }
        IEnumerable<string> MetaBlocklistHashes { get; }
    }
}
