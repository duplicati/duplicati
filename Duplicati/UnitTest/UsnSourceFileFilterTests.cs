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

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Snapshots.USN;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// With the USN journal, the files that changed since the last backup are checked against the
/// filters together with the folders above them, up to a source. A source that is a single file
/// was never reached that way, so the check went on up to the drive root, which has the hidden
/// and system attributes, and the file was left out when those are excluded (issue #5175).
/// Reading the journal needs an elevated process, so the filtering is called directly, with
/// the cache seeded with the sources as the journal service does.
/// </summary>
public class UsnSourceFileFilterTests
{
    /// <summary>
    /// A source entry, made as a proxy so it does not name the members of the interface
    /// </summary>
    public class Entry : DispatchProxy
    {
        public string EntryPath = "";
        public bool Folder;
        public FileAttributes EntryAttributes;

        public static ISourceProviderEntry Create(string path, bool folder, FileAttributes attributes)
        {
            var proxy = Create<ISourceProviderEntry, Entry>();
            var e = (Entry)(object)proxy;
            e.EntryPath = path;
            e.Folder = folder;
            e.EntryAttributes = attributes;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "get_Path" => EntryPath,
                "get_IsFolder" => Folder,
                "get_Attributes" => EntryAttributes,
                _ => targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null
            };
    }

    /// <summary>
    /// A snapshot that knows a fixed set of entries
    /// </summary>
    public class Snapshot : DispatchProxy
    {
        public Dictionary<string, ISourceProviderEntry> Entries = new(StringComparer.OrdinalIgnoreCase);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            => targetMethod!.Name switch
            {
                "GetFilesystemEntry" => Entries.GetValueOrDefault((string)args![0]!),
                _ => targetMethod.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null
            };
    }

    /// <summary>
    /// Runs the filtering of changed files that the journal service does
    /// </summary>
    /// <param name="sources">The sources of the backup</param>
    /// <param name="changedFiles">The files the journal reports as changed</param>
    /// <param name="entries">The entries on disk, with their attributes</param>
    /// <returns>The files that are kept.</returns>
    private static async Task<List<string>> FilterAsync(string[] sources, string[] changedFiles, params ISourceProviderEntry[] entries)
    {
        var snapshot = DispatchProxy.Create<ISnapshotService, Snapshot>();
        foreach (var e in entries)
            ((Snapshot)(object)snapshot).Entries[e.Path] = e;

        // As the journal service seeds it: the sources are included
        var cache = new Dictionary<string, bool>();
        foreach (var source in sources)
            cache[source] = true;

        // Exclude hidden and system entries, as the attribute filter does
        Func<ISourceProviderEntry, ValueTask<bool>> filter =
            e => ValueTask.FromResult((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0);

        var method = typeof(UsnJournalService).GetMethod("FilterExcludedFiles", BindingFlags.NonPublic | BindingFlags.Static)!;
        var result = (IAsyncEnumerable<ISourceProviderEntry>)method.Invoke(null, [changedFiles, snapshot, filter, cache, CancellationToken.None])!;

        var kept = new List<string>();
        await foreach (var e in result)
            kept.Add(e.Path);
        return kept;
    }

    private static readonly ISourceProviderEntry[] Disk =
    [
        Entry.Create(@"C:\", true, FileAttributes.Directory | FileAttributes.Hidden | FileAttributes.System),
        Entry.Create(@"C:\tmp\", true, FileAttributes.Directory),
        Entry.Create(@"C:\tmp\usntest\", true, FileAttributes.Directory),
        Entry.Create(@"C:\tmp\usntest\A.txt", false, FileAttributes.Normal),
        Entry.Create(@"C:\tmp\usntest\hidden\", true, FileAttributes.Directory | FileAttributes.Hidden),
        Entry.Create(@"C:\tmp\usntest\hidden\B.txt", false, FileAttributes.Normal),
    ];

    [SetUp]
    public void WindowsPathsOnly()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("The USN journal is Windows only, and the paths here are Windows paths");
    }

    [Test]
    public async Task AChangedFileThatIsASourceIsKept()
    {
        var kept = await FilterAsync([@"C:\tmp\usntest\A.txt"], [@"C:\tmp\usntest\A.txt"], Disk);
        Assert.That(kept, Is.EqualTo(new[] { @"C:\tmp\usntest\A.txt" }), "the drive root's attributes should not exclude a source file");
    }

    [Test]
    public async Task AChangedFileInAFolderSourceIsKept()
    {
        var kept = await FilterAsync([@"C:\tmp\usntest\"], [@"C:\tmp\usntest\A.txt"], Disk);
        Assert.That(kept, Is.EqualTo(new[] { @"C:\tmp\usntest\A.txt" }));
    }

    [Test]
    public async Task AChangedFileBelowAnExcludedFolderIsLeftOut()
    {
        var kept = await FilterAsync([@"C:\tmp\usntest\"], [@"C:\tmp\usntest\hidden\B.txt"], Disk);
        Assert.That(kept, Is.Empty, "a file in a hidden folder below the source is still excluded");
    }
}
