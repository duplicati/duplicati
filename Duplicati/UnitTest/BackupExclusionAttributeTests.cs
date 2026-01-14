// Copyright (C) 2025, The Duplicati Team
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.Operation.Backup;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

public class BackupExclusionAttributeTests : BasicSetupHelper
{
    private sealed class TestEntry : ISourceProviderEntry
    {
        public bool IsFolder { get; set; }
        public bool IsMetaEntry { get; set; }
        public bool IsRootEntry { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime LastModificationUtc { get; set; }
        public string Path { get; set; } = string.Empty;
        public long Size { get; set; }
        public bool IsSymlink { get; set; }
        public string? SymlinkTarget { get; set; }
        public FileAttributes Attributes { get; set; } = FileAttributes.Normal;
        public bool IsBlockDevice { get; set; }
        public bool IsCharacterDevice { get; set; }
        public bool IsAlternateStream { get; set; }
        public string? HardlinkTargetId { get; set; }
        public Dictionary<string, string?> MinorMetadata { get; set; } = new();

        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(MinorMetadata);

        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private static bool InvokeHasBackupExclusionAttribute(ISourceProviderEntry entry)
    {
        var method = typeof(FileEnumerationProcess).GetMethod(
            "HasBackupExclusionAttribute",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "HasBackupExclusionAttribute not found via reflection");
        return (bool)method!.Invoke(null, [entry])!;
    }

    [Test]
    public void HasBackupExclusionAttribute_ReturnsFalse_WhenMetadataIsEmpty()
    {
        var entry = new TestEntry
        {
            Path = "/test/file.txt",
            MinorMetadata = new Dictionary<string, string?>()
        };

        var result = InvokeHasBackupExclusionAttribute(entry);

        Assert.That(result, Is.False);
    }

    [Test]
    public void HasBackupExclusionAttribute_DetectsKnownExclusionAttributes()
    {
        var entry = new TestEntry
        {
            Path = "/test/file.txt",
            MinorMetadata = new Dictionary<string, string?>()
        };

        // macOS attribute
        entry.MinorMetadata["unix-ext:com.apple.metadata:com_apple_backup_excludeItem"] = "ignored";
        Assert.That(InvokeHasBackupExclusionAttribute(entry), Is.True, "macOS exclusion attribute was not detected");

        // Linux system attribute
        entry.MinorMetadata.Clear();
        entry.MinorMetadata["unix-ext:duplicati.exclude"] = "ignored";
        Assert.That(InvokeHasBackupExclusionAttribute(entry), Is.True, "Linux exclusion attribute was not detected");

        // Linux user attribute
        entry.MinorMetadata.Clear();
        entry.MinorMetadata["unix-ext:user.duplicati.exclude"] = "ignored";
        Assert.That(InvokeHasBackupExclusionAttribute(entry), Is.True, "Linux user exclusion attribute was not detected");
    }
}