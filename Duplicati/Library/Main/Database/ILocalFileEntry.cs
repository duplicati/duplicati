using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Database
{
    public interface ILocalFileEntry
    {
        string Path { get; }
        long Length { get; }
        string Hash { get; }
        string Metahash { get; }
    }
}
