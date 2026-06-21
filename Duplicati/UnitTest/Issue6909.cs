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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Operation.Backup;
using Duplicati.Library.Utility;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Reproduction test for https://github.com/duplicati/duplicati/issues/6909
/// When a folder is excluded by filter, the backend should not attempt to read
/// extended attributes (which can fail with permission denied) and should not
/// emit a warning.
/// </summary>
public class Issue6909
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
        public FileAttributes Attributes { get; set; } = FileAttributes.Directory;
        public bool IsBlockDevice { get; set; }
        public bool IsCharacterDevice { get; set; }
        public bool IsAlternateStream { get; set; }
        public string? HardlinkTargetId { get; set; }

        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => throw new Exception($"Unable to access the file \"{Path}\" with method llistxattr, error: EACCES (13)");

        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public IAsyncEnumerable<ISourceProviderEntry> Enumerate(CancellationToken cancellationToken)
            => throw new NotImplementedException();
    }

    private sealed class LogSink : ILogDestination
    {
        public List<LogEntry> Entries { get; } = [];

        public void WriteMessage(LogEntry entry)
        {
            Entries.Add(entry);
        }
    }

    private static async Task<bool> InvokeSourceFileEntryFilterAsync(
        ISourceProviderEntry entry,
        IFilter filter)
    {
        var method = typeof(FileEnumerationProcess).GetMethod(
            "SourceFileEntryFilterAsync",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.That(method, Is.Not.Null, "SourceFileEntryFilterAsync not found via reflection");

        var hardlinkmap = new Dictionary<string, string>();
        var mixinqueue = new Queue<ISourceProviderEntry>();

        var result = method!.Invoke(null, new object?[]
        {
            entry,
            new HashSet<string>(),
            Options.HardlinkStrategy.All,
            Options.SymlinkStrategy.Follow,
            hardlinkmap,
            FileAttributes.Normal,
            filter,
            null,
            mixinqueue,
            false,
            CancellationToken.None
        });

        return await (ValueTask<bool>)result!;
    }

    [Test]
    public async Task FilteredExcludedFolder_DoesNotWarnOnMetadataAccessAsync()
    {
        var entry = new TestEntry
        {
            Path = "/docs/User/#Recycle/",
            IsFolder = true,
            Attributes = FileAttributes.Directory
        };

        // Exclude filter for #Recycle folder (matching the user's setup)
        var excludeFilter = new FilterExpression("*#Recycle*", false);

        var logSink = new LogSink();
        using var isolatingScope = Log.StartIsolatingScope(true);
        using var log = Log.StartScope(logSink);

        var result = await InvokeSourceFileEntryFilterAsync(entry, excludeFilter);

        Assert.That(result, Is.False, "Entry should be excluded by filter");

        // This assertion replicates the bug: currently a warning is emitted
        // because extended attributes are checked before filter matching.
        // When the bug is fixed, this assertion should be changed to
        // Assert.That(metadataWarnings, Is.Empty, ...)
        var metadataWarnings = new List<LogEntry>();
        foreach (var e in logSink.Entries)
        {
            if (e.Message.Contains("Failed to process extended attributes", StringComparison.OrdinalIgnoreCase)
                || e.Message.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase)
                || e.Id == "PathProcessingErrorMetadata")
            {
                metadataWarnings.Add(e);
            }
        }

        Assert.That(metadataWarnings, Is.Empty, "A warning should not be emitted for a filtered-out path when metadata access fails");
    }
}
