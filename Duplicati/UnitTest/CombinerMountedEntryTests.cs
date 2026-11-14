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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.SourceProvider;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Looking up a single entry in a combined source finds the entries of a remote source
/// mounted with "@mountpoint|url"
/// </summary>
[TestFixture]
public class CombinerMountedEntryTests : BasicSetupHelper
{
    /// <summary>
    /// Creates a folder with a file whose size is the length of <paramref name="content"/>
    /// </summary>
    private string CreateFolder(string name, string content)
    {
        var dir = Path.Combine(BASEFOLDER, name);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.txt"), content);
        return dir;
    }

    /// <summary>
    /// A mountpoint that does not exist on disk, normalized like the source provider factory does
    /// </summary>
    private static string Mountpoint(string name)
        => Path.GetFullPath(Library.Common.IO.Util.AppendDirSeparator(
            Path.Combine(Path.GetPathRoot(Path.GetTempPath())!, "duplicati-virtual-combiner", name)));

    private static string FileUrl(string folder)
        => "file://" + folder.Replace('\\', '/');

    [Test]
    [Category("Targeted")]
    public async Task AnEntryOfAMountedSourceIsFoundAsync()
    {
        var music = CreateFolder("combiner-music", "music");
        var music2 = CreateFolder("combiner-music2", "longer music2");
        var options = new Dictionary<string, string>();

        // The second mountpoint extends the name of the first one
        using var combined = new Combiner([
            SourceProviderLoader.CreateSourceProvider(FileUrl(music), Mountpoint("music"), options),
            SourceProviderLoader.CreateSourceProvider(FileUrl(music2), Mountpoint("music2"), options),
        ]);

        var first = await combined.GetEntryAsync(Path.Combine(Mountpoint("music"), "a.txt"), false, CancellationToken.None);
        Assert.That(first, Is.Not.Null);
        Assert.That(first!.Path, Is.EqualTo(Path.Combine(Mountpoint("music"), "a.txt")));
        Assert.That(first.Size, Is.EqualTo("music".Length));

        var second = await combined.GetEntryAsync(Path.Combine(Mountpoint("music2"), "a.txt"), false, CancellationToken.None);
        Assert.That(second, Is.Not.Null);
        Assert.That(second!.Path, Is.EqualTo(Path.Combine(Mountpoint("music2"), "a.txt")));
        Assert.That(second.Size, Is.EqualTo("longer music2".Length));

        var missing = await combined.GetEntryAsync(Path.Combine(Mountpoint("music"), "missing.txt"), false, CancellationToken.None);
        Assert.That(missing, Is.Null);
    }

    [Test]
    [Category("Targeted")]
    public async Task AChangedFileOfARemoteSourceIsBackedUpAsync()
    {
        var remote = CreateFolder("combiner-remote", "first");
        Directory.CreateDirectory(DATAFOLDER);
        File.WriteAllText(Path.Combine(DATAFOLDER, "local.txt"), "local");

        var mountpoint = Mountpoint("remote");
        var sources = new[] { DATAFOLDER, "@" + mountpoint + "|" + FileUrl(remote) };

        using (var c = new Controller("file://" + TARGETFOLDER, TestOptions, null))
        {
            var res = await c.BackupAsync(sources);
            Assert.That(res.Errors, Is.Empty, string.Join(Environment.NewLine, res.Errors));
            Assert.That(res.AddedFiles, Is.EqualTo(2));
        }

        File.WriteAllText(Path.Combine(remote, "a.txt"), "second version");
        File.SetLastWriteTimeUtc(Path.Combine(remote, "a.txt"), DateTime.UtcNow.AddMinutes(1));

        var changedFiles = new Dictionary<string, string>(TestOptions)
        {
            ["changed-files"] = Path.Combine(mountpoint, "a.txt")
        };
        using (var c = new Controller("file://" + TARGETFOLDER, changedFiles, null))
        {
            var res = await c.BackupAsync(sources);
            Assert.That(res.Errors, Is.Empty, string.Join(Environment.NewLine, res.Errors));
            Assert.That(res.Warnings, Is.Empty, string.Join(Environment.NewLine, res.Warnings));
            Assert.That(res.ModifiedFiles, Is.EqualTo(1));
        }

        var restore = Path.Combine(BASEFOLDER, "combiner-restore");
        if (Directory.Exists(restore))
            Directory.Delete(restore, true);
        var restoreOptions = new Dictionary<string, string>(TestOptions) { ["restore-path"] = restore };
        using (var c = new Controller("file://" + TARGETFOLDER, restoreOptions, null))
        {
            var res = await c.RestoreAsync([Path.Combine(mountpoint, "a.txt")]);
            Assert.That(res.Errors, Is.Empty, string.Join(Environment.NewLine, res.Errors));
        }
        Assert.That(File.ReadAllText(Path.Combine(restore, "a.txt")), Is.EqualTo("second version"));
    }

    [Test]
    [Category("Targeted")]
    public async Task TestFiltersFollowsASymlinkToAMountedSourceAsync()
    {
        var remote = CreateFolder("combiner-symlink-remote", "remote");
        var mountpoint = Mountpoint("symlink-remote");
        Directory.CreateDirectory(DATAFOLDER);
        File.WriteAllText(Path.Combine(DATAFOLDER, "real.txt"), "local");
        try
        {
            // One link to a local file, one link into the mounted source
            File.CreateSymbolicLink(Path.Combine(DATAFOLDER, "link-local.txt"), Path.Combine(DATAFOLDER, "real.txt"));
            File.CreateSymbolicLink(Path.Combine(DATAFOLDER, "link-remote.txt"), Path.Combine(mountpoint, "a.txt"));
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            Assert.Ignore("Symbolic links cannot be created here: " + ex.Message);
        }

        var options = new Dictionary<string, string>(TestOptions) { ["symlink-policy"] = "follow" };
        // TestFilterAsync sends the messages of the file enumeration to the sink, so it needs one
        using var c = new Controller("file://" + TARGETFOLDER, options, new SilentSink());
        var res = await c.TestFilterAsync([DATAFOLDER, "@" + mountpoint + "|" + FileUrl(remote)]);

        Assert.That(res.Errors, Is.Empty, string.Join(Environment.NewLine, res.Errors));
        // real.txt, both links, and a.txt in the mounted source
        Assert.That(res.FileCount, Is.EqualTo(4));
    }

    private sealed class SilentSink : IMessageSink
    {
        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size) { }
        public void SetBackendProgress(IBackendProgress progress) { }
        public void SetOperationProgress(IOperationProgress progress) { }
        public void WriteMessage(Library.Logging.LogEntry entry) { }
    }
}
