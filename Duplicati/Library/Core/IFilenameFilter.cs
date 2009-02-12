using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Core
{
    /// <summary>
    /// A common interface for filtering filenames
    /// </summary>
    public interface IFilenameFilter
    {
        bool Include { get; }
        bool Match(string filename);
    }
}
