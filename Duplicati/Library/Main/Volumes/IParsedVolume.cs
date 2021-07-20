using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Library.Main.Volumes
{
    public interface IParsedVolume
    {
        RemoteVolumeType FileType { get; }
        string Prefix { get; }
        string Guid { get; }
        DateTime Time { get; }
        string CompressionModule { get; }
        string EncryptionModule { get; }
        string ParityModule { get; }
        Library.Interface.IFileEntry File { get; }
    }
}
