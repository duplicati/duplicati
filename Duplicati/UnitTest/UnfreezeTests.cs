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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The unfreeze command asks the destination to bring back the volumes a restore reads, when they
/// are in archive storage (#6228).
/// </summary>
[NonParallelizable]
public class UnfreezeTests : BasicSetupHelper
{
    /// <summary>
    /// A local folder that pretends some of its files are in archive storage
    /// </summary>
    public sealed class ArchiveFolderBackend : Library.Backend.File, IArchiveRestoreEnabledBackend
    {
        public static readonly ConcurrentDictionary<string, ArchiveRestoreState> States = new();
        public static readonly ConcurrentQueue<string> Requests = new();

        public ArchiveFolderBackend()
        {
        }

        public ArchiveFolderBackend(string url, Dictionary<string, string?> options)
            : base("file://" + url.Substring(url.IndexOf("://", StringComparison.Ordinal) + 3), options)
        {
        }

        public override string ProtocolKey => "archivefolder";

        public Task<ArchiveRestoreState> GetArchiveRestoreStateAsync(string remotename, CancellationToken cancellationToken)
            => Task.FromResult(States.GetValueOrDefault(remotename, ArchiveRestoreState.Available));

        public Task RequestArchiveRestoreAsync(string remotename, CancellationToken cancellationToken)
        {
            Requests.Enqueue(remotename);
            States[remotename] = ArchiveRestoreState.RestoreInProgress;
            return Task.CompletedTask;
        }
    }

    private Dictionary<string, string> Options => new(TestOptions)
    {
        ["no-encryption"] = "true",
        ["dblock-size"] = "100kb",
        ["blocksize"] = "10kb",
    };

    private string ArchiveTarget => "archivefolder://" + TARGETFOLDER;

    private string FirstFolder => Path.Combine(DATAFOLDER, "first") + Path.DirectorySeparatorChar + "*";

    [SetUp]
    public void RegisterBackend()
    {
        Library.DynamicLoader.BackendLoader.AddBackend(new ArchiveFolderBackend());
        ArchiveFolderBackend.States.Clear();
        ArchiveFolderBackend.Requests.Clear();
    }

    private void CreateSourceData(string folder, int seed)
    {
        var random = new Random(seed);
        Directory.CreateDirectory(Path.Combine(DATAFOLDER, folder));
        for (var i = 0; i < 3; i++)
        {
            var data = new byte[200 * 1024];
            random.NextBytes(data);
            File.WriteAllBytes(Path.Combine(DATAFOLDER, folder, $"file{i}.bin"), data);
        }
    }

    /// <summary>
    /// Two backups, so the second folder is only in the volumes of the second one
    /// </summary>
    private async Task<List<string>> BackupAndArchiveEverythingAsync()
    {
        CreateSourceData("first", 1);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));
        CreateSourceData("second", 2);
        using (var c = new Controller("file://" + TARGETFOLDER, Options, null))
            TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER]));

        // A lifecycle rule has moved every file to archive storage
        foreach (var name in Directory.EnumerateFiles(TARGETFOLDER).Select(x => Path.GetFileName(x)!))
            ArchiveFolderBackend.States[name] = ArchiveRestoreState.Archived;

        using var list = new Controller("file://" + TARGETFOLDER, Options, null);
        return (await list.ListRestoreVolumesAsync([FirstFolder])).Volumes.Select(x => x.Name).ToList();
    }

    [Test]
    [Category("Targeted")]
    public async Task OnlyTheVolumesTheRestoreReadsAreAskedForAsync()
    {
        var needed = await BackupAndArchiveEverythingAsync();
        Assert.That(needed, Is.Not.Empty);
        Assert.That(needed.Count, Is.LessThan(Directory.EnumerateFiles(TARGETFOLDER, "*.dblock.*").Count()), "The first folder should not need every volume");

        using (var c = new Controller(ArchiveTarget, Options, null))
        {
            var res = await c.UnfreezeAsync([FirstFolder]);
            Assert.That(ArchiveFolderBackend.Requests, Is.EquivalentTo(needed), "Exactly the volumes the restore reads are asked for");
            Assert.That(res.Volumes.Select(x => x.Name), Is.EquivalentTo(needed));
            Assert.That(res.Volumes.All(x => x.Requested && x.State == ArchiveRestoreState.RestoreInProgress), Is.True);
            Assert.That(res.Warnings.Any(x => x.Contains("VolumesNotAvailable")), Is.True, "The volumes cannot be read yet, which is a warning");
        }

        // Asking again while the volumes are brought back makes no new requests
        using (var c = new Controller(ArchiveTarget, Options, null))
        {
            var res = await c.UnfreezeAsync([FirstFolder]);
            Assert.That(ArchiveFolderBackend.Requests.Count, Is.EqualTo(needed.Count));
            Assert.That(res.Volumes.All(x => !x.Requested && x.State == ArchiveRestoreState.RestoreInProgress), Is.True);
        }

        // Once they are back, the restore can run
        foreach (var name in needed)
            ArchiveFolderBackend.States[name] = ArchiveRestoreState.Available;
        using (var c = new Controller(ArchiveTarget, Options, null))
        {
            var res = await c.UnfreezeAsync([FirstFolder]);
            Assert.That(res.Volumes.All(x => x.State == ArchiveRestoreState.Available), Is.True);
            Assert.That(res.Warnings, Is.Empty);
        }
    }

    [Test]
    [Category("Targeted")]
    public async Task ADryRunAsksForNothingAsync()
    {
        var needed = await BackupAndArchiveEverythingAsync();

        using var c = new Controller(ArchiveTarget, new Dictionary<string, string>(Options) { ["dry-run"] = "true" }, null);
        var res = await c.UnfreezeAsync([FirstFolder]);
        Assert.That(ArchiveFolderBackend.Requests, Is.Empty);
        Assert.That(res.Volumes.Select(x => x.Name), Is.EquivalentTo(needed));
        Assert.That(res.Volumes.All(x => !x.Requested && x.State == ArchiveRestoreState.Archived), Is.True);
    }

    [Test]
    [Category("Targeted")]
    public async Task ADestinationWithoutArchiveStorageIsReportedAsync()
    {
        await BackupAndArchiveEverythingAsync();

        using var c = new Controller("file://" + TARGETFOLDER, Options, null);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => c.UnfreezeAsync([FirstFolder]));
        Assert.That(ex!.HelpID, Is.EqualTo("ArchiveRestoreNotSupported"));
    }
}
