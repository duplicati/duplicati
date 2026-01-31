using System;
using System.IO;

namespace Duplicati.Proprietary.DiskImage.Filesystem;

public interface IFileMetadata
{
    string Path { get; }
    long Size { get; }
    DateTime CreatedUtc { get; }
    DateTime LastModifiedUtc { get; }
    FileAttributes Attributes { get; }
    bool IsFolder { get; }
}