// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.SourceProvider.DiskImage.General;

namespace Duplicati.Library.SourceProvider.DiskImage.SourceItems;

public class PhysicalDriveSourceEntry(PhysicalDriveInfo driveInfo) : ISourceProviderEntry
{
    public bool IsFolder => true;

    public bool IsMetaEntry => true;

    public bool IsRootEntry => false;

    public DateTime CreatedUtc => new DateTime(0);

    public DateTime LastModificationUtc => new DateTime(0);

    public string Path => Util.AppendDirSeparator(driveInfo.Path);

    public long Size => (long)driveInfo.Size;

    public bool IsSymlink => false;

    public string? SymlinkTarget => null;

    public FileAttributes Attributes => FileAttributes.None;

    public bool IsSpecialFile => true;

    public bool IsCharacterDevice => false;

    public bool IsAlternateStream => false;

    public string? HardlinkTargetId => null;

    public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>
        {
            { "diskimage:Type", "physicaldrive" },
            { "diskimage:Number", driveInfo.Number },
            { "diskimage:FriendlyName", driveInfo.DisplayName },
            { "diskimage:Size", driveInfo.Size.ToString() },
            { "diskimage:DevicePath", driveInfo.Path },
            { "diskimage:Name", $"{driveInfo.DisplayName} ({Library.Utility.Utility.FormatSizeString(driveInfo.Size)})" },
        });

    public Task<Stream> OpenRead(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
