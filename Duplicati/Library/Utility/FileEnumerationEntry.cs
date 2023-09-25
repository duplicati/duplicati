using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Duplicati.Library.Utility
{
    /// <summary>
    /// File enumeration entry, including whether path was due to a direct
    /// source filter match.
    /// </summary>
    public class FileEnumerationEntry
    {
        public FileEnumerationEntry(string path, bool isSourceFilterMatch)
        {
            Path = path;
            IsSourceFilterMatch = isSourceFilterMatch;
        }

        /// <summary>
        /// File enumeration path.
        /// </summary>
        public string Path { get; private set; }
        /// <summary>
        /// True if path was included due to a direct source filter match.
        /// </summary>
        public bool IsSourceFilterMatch { get; private set; }
    }
}
