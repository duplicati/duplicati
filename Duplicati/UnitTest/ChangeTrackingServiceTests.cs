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
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.ChangeTracking;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The change tracking service records what a watcher reports and hands each
/// backup run the paths observed since the previous run
/// </summary>
[NonParallelizable]
public class ChangeTrackingServiceTests : BasicSetupHelper
{
    private sealed class FakeEventSource : IFileSystemEventSource
    {
        public readonly string Root;
        public readonly bool Fail;
        public bool Started;
        public bool Disposed;

        public FakeEventSource(string root, bool fail)
        {
            Root = root;
            Fail = fail;
        }

        public event Action<string, ChangeKind>? Changed;
        public event Action<Exception>? Error;

        public void Start()
        {
            if (Fail)
                throw new DirectoryNotFoundException($"cannot watch {Root}");
            Started = true;
        }

        public void Raise(string path, ChangeKind kind) => Changed?.Invoke(path, kind);
        public void RaiseError(Exception ex) => Error?.Invoke(ex);
        public void Dispose() => Disposed = true;
    }

    private sealed class FakeFactory
    {
        public readonly List<FakeEventSource> Sources = new();
        public readonly HashSet<string> FailRoots = new(StringComparer.Ordinal);

        public IFileSystemEventSource Create(string root)
        {
            var source = new FakeEventSource(root, FailRoots.Contains(root));
            Sources.Add(source);
            return source;
        }

        public FakeEventSource Active => Sources.Last(x => x.Started && !x.Disposed);
        public void Raise(string path, ChangeKind kind) => Active.Raise(path, kind);
    }

    private const string BACKUP = "1";
    private const string HASH = "hash-a";
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(50);

    // For the tests that act while a drain waits for the grace delay. Timers on the macOS
    // runner fire tens of milliseconds late, which is more than a quarter of Grace
    private static readonly TimeSpan LongGrace = TimeSpan.FromSeconds(1);

    private string Root => Path.Combine(DATAFOLDER, "watched");
    private string DbPath => Path.Combine(BASEFOLDER, "change-tracking.sqlite");

    private ChangeTrackingSettings Settings(bool enabled, TimeSpan? grace = null)
        => new(enabled, DbPath, BASEFOLDER, TimeSpan.FromMilliseconds(100), 3, 10, grace ?? Grace);

    private Task<ChangeTrackingService> CreateServiceAsync(FakeFactory factory, TimeSpan? grace = null)
        => ChangeTrackingService.CreateAsync(Settings(true, grace), factory.Create, CancellationToken.None);

    [TearDown]
    public void DeleteChangeLists()
    {
        // A test that fails before it completes its handoff leaves the list behind, and a later
        // test that checks that no list is written would then fail as well
        if (Directory.Exists(BASEFOLDER))
            foreach (var file in Directory.EnumerateFiles(BASEFOLDER, "duplicati-changes-*.txt"))
                File.Delete(file);
    }

    private static Task<Dictionary<string, char>> ReadListAsync(Library.Interface.ChangeListHandoff? handoff)
        => ReadListAsync(handoff?.ListPath);

    private static async Task<Dictionary<string, char>> ReadListAsync(string? path)
    {
        Assert.That(path, Is.Not.Null, "expected a change list");
        var lines = await File.ReadAllLinesAsync(path!);
        return lines.Where(x => x.Length > 0).ToDictionary(x => x.Substring(1), x => x[0], StringComparer.Ordinal);
    }

    private string P(string name) => Path.Combine(Root, name);

    [Test]
    [Category("ChangeTracking")]
    public void Buffer_MergesKindsAndSignalsHighWater()
    {
        var buffer = new ChangeBuffer(2);
        Assert.That(buffer.Add("a", ChangeKind.Created, 10), Is.False);
        Assert.That(buffer.Add("a", ChangeKind.Changed, 5), Is.False);
        Assert.That(buffer.Add("b", ChangeKind.Deleted, 7), Is.True);
        Assert.That(buffer.Count, Is.EqualTo(2));

        var drained = buffer.Drain();
        Assert.That(buffer.Count, Is.EqualTo(0));
        Assert.That(drained["a"], Is.EqualTo(new BufferedChange(ChangeKind.Created | ChangeKind.Changed, 10)));
        Assert.That(drained["b"], Is.EqualTo(new BufferedChange(ChangeKind.Deleted, 7)));

        buffer.Add("a", ChangeKind.Deleted, 20);
        buffer.Merge(drained);
        Assert.That(buffer.Drain()["a"], Is.EqualTo(new BufferedChange(ChangeKind.Created | ChangeKind.Changed | ChangeKind.Deleted, 20)));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Database_CreateDeletesExistingFilesAsync()
    {
        await File.WriteAllTextAsync(DbPath, "not a database");
        await File.WriteAllTextAsync(DbPath + "-wal", "stale");
        await File.WriteAllTextAsync(DbPath + "-shm", "stale");

        await using var db = await ChangeTrackingDatabase.CreateAsync(DbPath, CancellationToken.None);
        Assert.That(File.Exists(DbPath + "-shm") && new FileInfo(DbPath + "-shm").Length == 5, Is.False);
        var id = await db.StartSessionAsync(BACKUP, HASH, CancellationToken.None);
        Assert.That(id, Is.GreaterThan(0));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Database_WritesMergeAndRotateClosesAsync()
    {
        await using var db = await ChangeTrackingDatabase.CreateAsync(DbPath, CancellationToken.None);
        var first = await db.StartSessionAsync(BACKUP, HASH, CancellationToken.None);

        await db.WriteChangesAsync(first, new Dictionary<string, BufferedChange> { ["a"] = new(ChangeKind.Created, 10) }, CancellationToken.None);
        var rows = await db.WriteChangesAsync(first, new Dictionary<string, BufferedChange> { ["a"] = new(ChangeKind.Changed, 5), ["b"] = new(ChangeKind.Deleted, 7) }, CancellationToken.None);
        Assert.That(rows, Is.EqualTo(2));

        var changes = await db.ReadChangesAsync(first, CancellationToken.None).ToDictionaryAsync(x => x.Path, x => x);
        Assert.That(changes["a"].Kind, Is.EqualTo(ChangeKind.Created | ChangeKind.Changed));
        Assert.That(changes["a"].ObservedUtcTicks, Is.EqualTo(10));

        var (closed, created, _) = await db.RotateAsync(BACKUP, "hash-b", CancellationToken.None);
        Assert.That(closed, Is.EqualTo(first));
        Assert.That(created, Is.GreaterThan(first));
        Assert.That((await db.GetSessionAsync(first, CancellationToken.None))!.State, Is.EqualTo(SessionState.Handoff));
        Assert.That((await db.GetSessionAsync(created, CancellationToken.None))!.SettingsHash, Is.EqualTo("hash-b"));

        // Only the newer observation is copied, keeping the source
        Assert.That(await db.CopyObservedSinceAsync(first, created, 8, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await db.CountChangesAsync(created, CancellationToken.None), Is.EqualTo(1));
        Assert.That(await db.CountChangesAsync(first, CancellationToken.None), Is.EqualTo(2));

        await db.MergeSessionIntoAsync(first, created, CancellationToken.None);
        Assert.That(await db.CountChangesAsync(created, CancellationToken.None), Is.EqualTo(2));
        Assert.That(await db.GetSessionAsync(first, CancellationToken.None), Is.Null);

        await db.DeleteSessionsForBackupAsync(BACKUP, CancellationToken.None);
        Assert.That(await db.GetSessionAsync(created, CancellationToken.None), Is.Null);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_FirstRunScansAll_SecondRunGetsListAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        // First run: watchers start, no session yet
        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(first.Usable, Is.False);
        Assert.That(factory.Sources.Single().Started, Is.True);

        // Observed before the first snapshot: kept, as it may or may not be in the snapshot
        factory.Raise(P("early"), ChangeKind.Created);
        var firstHandoff = await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(firstHandoff, Is.Not.Null);
        Assert.That(firstHandoff!.ListPath, Is.Null);
        Assert.That(firstHandoff.Token, Is.Not.Empty);
        Assert.That(first.Drained, Is.False);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("file.txt"), ChangeKind.Changed);
        factory.Raise(P("gone"), ChangeKind.Deleted);
        factory.Raise(P("dir"), ChangeKind.Created | ChangeKind.IsDirectoryHint);
        factory.Raise(P("parent"), ChangeKind.Changed | ChangeKind.IsDirectoryHint);

        // Second run: the list holds what was observed, with the right markers
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.True, second.FullScanReason);
        await Task.Delay(Grace + Grace);
        var tRef = DateTime.UtcNow;
        var secondHandoff = await second.DrainChangeListAsync(tRef, CancellationToken.None);
        var listPath = secondHandoff?.ListPath;
        Assert.That(secondHandoff!.BaseTokens, Is.EquivalentTo(new[] { firstHandoff.Token }));
        var list = await ReadListAsync(listPath);
        Assert.That(list, Is.EquivalentTo(new Dictionary<string, char>
        {
            [P("early")] = 'E',
            [P("file.txt")] = 'E',
            [P("gone")] = 'R',
            [P("dir")] = 'R',
            [P("parent")] = 'E',
        }));
        Assert.That(second.Drained, Is.True);
        Assert.That(second.EntryCount, Is.EqualTo(5));

        // Observed during the run: belongs to the next run
        factory.Raise(P("during.txt"), ChangeKind.Changed);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
        Assert.That(File.Exists(listPath), Is.False, "the list file is deleted with the handoff");

        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.True, third.FullScanReason);
        await Task.Delay(Grace + Grace);
        list = await ReadListAsync(await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("during.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_StraddleRowsAppearInBothRunsAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        // Well before the snapshot: only this run. Around the snapshot: this run and the next
        factory.Raise(P("old.txt"), ChangeKind.Changed);
        await Task.Delay(Grace * 6);
        var tRef = DateTime.UtcNow;
        factory.Raise(P("straddle.txt"), ChangeKind.Changed);

        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var list = await ReadListAsync(await second.DrainChangeListAsync(tRef, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("old.txt"), P("straddle.txt") }));
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        list = await ReadListAsync(await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("straddle.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_LateEventsAfterRotationAreInTheListAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory, LongGrace);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("before.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);

        // Raise an event while the drain waits for the grace delay
        var drain = second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await Task.Delay(LongGrace / 4);
        factory.Raise(P("late.txt"), ChangeKind.Created);

        var list = await ReadListAsync(await drain);
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("before.txt"), P("late.txt") }));
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_FailedRunKeepsPathsForNextRunAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await Task.Delay(Grace + Grace);
        Assert.That(await ReadListAsync(await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None)), Has.Count.EqualTo(1));
        factory.Raise(P("b.txt"), ChangeKind.Changed);
        await service.CompleteHandoffAsync(second, false, CancellationToken.None);

        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.True, third.FullScanReason);
        await Task.Delay(Grace + Grace);
        var list = await ReadListAsync(await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("a.txt"), P("b.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_CompleteWithoutDrainLeavesSessionRecordingAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);

        // The run failed before the snapshot, so it never asked for the list
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await service.CompleteHandoffAsync(second, false, CancellationToken.None);

        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.True, third.FullScanReason);
        var list = await ReadListAsync(await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("a.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_WatcherErrorForcesFullScan_ThenRecoversAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        factory.Active.RaiseError(new InternalBufferOverflowException("too many"));
        // Outside the window around the next snapshot, which is carried over to the run after it
        await Task.Delay(Grace * 6);

        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That(second.FullScanReason, Does.Contain("InternalBufferOverflowException"));
        Assert.That((await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        // The rotation opened a clean session
        factory.Raise(P("b.txt"), ChangeKind.Changed);
        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.True, third.FullScanReason);
        var list = await ReadListAsync(await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("b.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_TooManyChangesForcesFullScanAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        for (var i = 0; i < 12; i++)
            factory.Raise(P($"f{i}"), ChangeKind.Changed);

        // The flush loop runs every 100 ms and the limit is 10
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && (await service.GetStatusAsync(CancellationToken.None)).Single().OverflowReason == null)
            await Task.Delay(50);

        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That(second.FullScanReason, Does.Contain("TooManyChanges"));
        Assert.That((await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_SettingsChangeForcesFullScanAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], "hash-b", CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That((await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        // The next run records under the new hash
        var third = await service.PrepareHandoffAsync(BACKUP, [Root], "hash-b", CancellationToken.None);
        Assert.That(third.Usable, Is.True, third.FullScanReason);
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_SourceChangeRestartsWatchersAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        var other = Path.Combine(DATAFOLDER, "other");
        var second = await service.PrepareHandoffAsync(BACKUP, [Root, other], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That(factory.Sources[0].Disposed, Is.True);
        Assert.That(factory.Sources.Where(x => x.Started && !x.Disposed).Select(x => x.Root), Is.EquivalentTo(new[] { Root, other }));
        await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_ReconfigureStopsWatchingAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        await service.StopAsync(BACKUP, CancellationToken.None);
        Assert.That(factory.Sources.Single().Disposed, Is.True);
        Assert.That(await service.GetStatusAsync(CancellationToken.None), Is.Empty);

        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That(factory.Sources.Count, Is.EqualTo(2));
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_UnwatchableRootIsNeverUsableAsync()
    {
        var factory = new FakeFactory();
        var other = Path.Combine(DATAFOLDER, "other");
        factory.FailRoots.Add(other);
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root, other], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        var status = (await service.GetStatusAsync(CancellationToken.None)).Single();
        Assert.That(status.Healthy, Is.False);

        // Every run restarts the watchers and scans everything until the root can be watched
        var second = await service.PrepareHandoffAsync(BACKUP, [Root, other], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That(second.FullScanReason, Does.Contain("CannotWatch"));
        Assert.That((await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        factory.FailRoots.Clear();
        var third = await service.PrepareHandoffAsync(BACKUP, [Root, other], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.False);
        await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);

        var fourth = await service.PrepareHandoffAsync(BACKUP, [Root, other], HASH, CancellationToken.None);
        Assert.That(fourth.Usable, Is.True, fourth.FullScanReason);
        await service.CompleteHandoffAsync(fourth, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_DisabledStartsNoWatchersAsync()
    {
        var factory = new FakeFactory();
        await using var service = await ChangeTrackingService.CreateAsync(Settings(false), factory.Create, CancellationToken.None);
        Assert.That(service.Enabled, Is.False);

        var handoff = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(handoff.Usable, Is.False);
        Assert.That(await handoff.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None), Is.Null);
        Assert.That(factory.Sources, Is.Empty);
        await service.CompleteHandoffAsync(handoff, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_UnrepresentablePathForcesFullScanAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        factory.Raise(P("line\nbreak.txt"), ChangeKind.Changed);
        // Outside the window around the snapshot, which would carry it to the next run
        await Task.Delay(Grace * 6);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False, "the path cannot be listed, so the recording is incomplete");
        var h2 = await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(h2?.ListPath, Is.Null);
        Assert.That(h2?.Token, Is.Not.Null, "the backup scans everything and stores the new token");
        Assert.That(second.Drained, Is.False);
        Assert.That(Directory.EnumerateFiles(BASEFOLDER, "duplicati-changes-*.txt"), Is.Empty);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        // The path is not carried on, so the next run gets a list again
        factory.Raise(P("b.txt"), ChangeKind.Changed);
        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var h3 = await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(h3!.BaseTokens, Is.EquivalentTo(new[] { h2!.Token }));
        Assert.That((await ReadListAsync(h3)).Keys, Is.EquivalentTo(new[] { P("b.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_FailedRunListAcceptsBothBasesAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var t1 = (await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))!.Token;
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var h2 = await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(h2!.BaseTokens, Is.EquivalentTo(new[] { t1 }));
        await service.CompleteHandoffAsync(second, false, CancellationToken.None);

        // The failed run may or may not have stored its token, and the list covers both cases
        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var h3 = await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(h3!.BaseTokens, Is.EquivalentTo(new[] { t1, h2.Token }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);

        // After a successful run only its own token is a valid base
        var fourth = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var h4 = await fourth.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(h4!.BaseTokens, Is.EquivalentTo(new[] { h3.Token }));
        await service.CompleteHandoffAsync(fourth, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_FullScanDrainKeepsChangesAroundTheSnapshotAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        // The settings changed, so this run scans everything, but a change made around its
        // snapshot may not be in it, so the next run must still see it
        var tRef = DateTime.UtcNow;
        factory.Raise(P("straddle.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], "hash-b", CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        var h2 = await second.DrainChangeListAsync(tRef, CancellationToken.None);
        Assert.That(h2!.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        var third = await service.PrepareHandoffAsync(BACKUP, [Root], "hash-b", CancellationToken.None);
        var h3 = await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        Assert.That(h3!.BaseTokens, Is.EquivalentTo(new[] { h2.Token }));
        Assert.That((await ReadListAsync(h3)).Keys, Is.EquivalentTo(new[] { P("straddle.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_WatcherFailureRestartsWatchersAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        // Not lost events, but an error that may have stopped the watcher
        factory.Active.RaiseError(new IOException("the network share went away"));
        Assert.That((await service.GetStatusAsync(CancellationToken.None)).Single().Healthy, Is.False);

        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.False);
        Assert.That(factory.Sources[0].Disposed, Is.True);
        Assert.That(factory.Sources.Count(x => x.Started && !x.Disposed), Is.EqualTo(1));
        Assert.That((await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.True, third.FullScanReason);
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_CompleteWaitsForALateDrainAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);

        // The backup gave up on the drain and the run ended before the drain did
        var drain = second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(second, false, CancellationToken.None);
        var h2 = await drain;
        Assert.That(h2?.ListPath, Is.Not.Null);
        Assert.That(File.Exists(h2!.ListPath), Is.False, "the list file is deleted by the completion");

        // The failed run's paths are kept
        var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var list = await ReadListAsync(await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None));
        Assert.That(list.Keys, Is.EquivalentTo(new[] { P("a.txt") }));
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);

        // A drain after the completion does nothing
        Assert.That(await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None), Is.Null);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_OverflowDuringGraceDelayDiscardsTheListAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory, LongGrace);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(second.Usable, Is.True, second.FullScanReason);

        // The overflow is reported after the rotation, but the lost events may be older
        var drain = second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await Task.Delay(LongGrace / 4);
        factory.Active.RaiseError(new InternalBufferOverflowException("burst"));
        var h2 = await drain;
        Assert.That(h2!.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_OverflowAfterTheSnapshotTaintsTheNextRunAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        // Events are lost while the snapshot is being created, before the rotation
        var tRef = DateTime.UtcNow;
        factory.Active.RaiseError(new InternalBufferOverflowException("burst"));
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That((await second.DrainChangeListAsync(tRef, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);

        // The next snapshot is taken well after the loss; a timer may fire early, so the
        // grace delay of the drain above does not put the loss outside this run's window
        await Task.Delay(Grace + Grace);

        // The lost events may be missing from that snapshot too, so the next run cannot use a list either
        var third =await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(third.Usable, Is.False);
        Assert.That((await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None))?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(third, true, CancellationToken.None);

        // Then it recovers
        var fourth = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(fourth.Usable, Is.True, fourth.FullScanReason);
        await service.CompleteHandoffAsync(fourth, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_WithoutDatabaseIsDisabledAsync()
    {
        // The parent of the database path is a file, so the database cannot be created
        var blocker = Path.Combine(BASEFOLDER, "not-a-folder");
        await File.WriteAllTextAsync(blocker, "");
        var settings = Settings(true) with { DatabasePath = Path.Combine(blocker, "change-tracking.sqlite") };
        var factory = new FakeFactory();
        await using var service = await ChangeTrackingService.CreateAsync(settings, factory.Create, CancellationToken.None);

        Assert.That(service.Enabled, Is.False);
        var handoff = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        Assert.That(handoff.Usable, Is.False);
        Assert.That(await handoff.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None), Is.Null);
        await service.CompleteHandoffAsync(handoff, true, CancellationToken.None);
        await service.StopAsync(BACKUP, CancellationToken.None);
        Assert.That(factory.Sources, Is.Empty);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_ReconfigureDuringDrainHandsOverNoListAsync()
    {
        var factory = new FakeFactory();
        await using var service = await CreateServiceAsync(factory, LongGrace);

        var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await service.CompleteHandoffAsync(first, true, CancellationToken.None);

        factory.Raise(P("a.txt"), ChangeKind.Changed);
        var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
        var drain = second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
        await Task.Delay(LongGrace / 4);
        await service.StopAsync(BACKUP, CancellationToken.None);

        // The sessions are gone, so whatever the drain does, it must not hand over a list
        Library.Interface.ChangeListHandoff? h2 = null;
        try
        {
            h2 = await drain;
        }
        catch (Exception)
        {
        }
        Assert.That(h2?.ListPath, Is.Null);
        await service.CompleteHandoffAsync(second, true, CancellationToken.None);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_ReplacedRootForcesFullScanAndRestartAsync()
    {
        var old = Root.TrimEnd(Path.DirectorySeparatorChar) + ".old";
        if (Directory.Exists(old))
            Directory.Delete(old, true);
        Directory.CreateDirectory(Root);
        try
        {
            var factory = new FakeFactory();
            await using var service = await CreateServiceAsync(factory);

            var first = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
            await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
            await service.CompleteHandoffAsync(first, true, CancellationToken.None);

            factory.Raise(P("a.txt"), ChangeKind.Changed);
            var second = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
            Assert.That(second.Usable, Is.True, second.FullScanReason);

            // Swapped by renaming after the run started; the watcher follows the old folder
            Directory.Move(Root, old);
            Directory.CreateDirectory(Root);

            var h2 = await second.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
            Assert.That(h2?.ListPath, Is.Null);
            await service.CompleteHandoffAsync(second, true, CancellationToken.None);

            var third = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
            Assert.That(third.Usable, Is.False);
            Assert.That(factory.Sources[0].Disposed, Is.True, "the watcher is restarted on the new folder");
            await third.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
            await service.CompleteHandoffAsync(third, true, CancellationToken.None);

            var fourth = await service.PrepareHandoffAsync(BACKUP, [Root], HASH, CancellationToken.None);
            Assert.That(fourth.Usable, Is.True, fourth.FullScanReason);
            await service.CompleteHandoffAsync(fourth, true, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(old))
                Directory.Delete(old, true);
        }
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Service_FileRootReplacedByFolderRestartsWatcherAsync()
    {
        Directory.CreateDirectory(DATAFOLDER);
        var fileRoot = Path.Combine(DATAFOLDER, "single.txt");
        if (Directory.Exists(fileRoot))
            Directory.Delete(fileRoot, true);
        await File.WriteAllTextAsync(fileRoot, "S");
        try
        {
            var factory = new FakeFactory();
            await using var service = await CreateServiceAsync(factory);

            var first = await service.PrepareHandoffAsync(BACKUP, [fileRoot], HASH, CancellationToken.None);
            await first.DrainChangeListAsync(DateTime.UtcNow, CancellationToken.None);
            await service.CompleteHandoffAsync(first, true, CancellationToken.None);

            // The file becomes a folder, which a watcher on its parent does not look into
            File.Delete(fileRoot);
            Directory.CreateDirectory(fileRoot);

            var second = await service.PrepareHandoffAsync(BACKUP, [fileRoot], HASH, CancellationToken.None);
            Assert.That(second.Usable, Is.False);
            Assert.That(factory.Sources[0].Disposed, Is.True, "the watcher is restarted");
            await service.CompleteHandoffAsync(second, true, CancellationToken.None);
        }
        finally
        {
            if (Directory.Exists(fileRoot))
                Directory.Delete(fileRoot, true);
            if (File.Exists(fileRoot))
                File.Delete(fileRoot);
        }
    }

    [Test]
    [Category("ChangeTracking")]
    public void Writer_MapsKindsToMarkers()
    {
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.Deleted), Is.True);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.RenamedFrom), Is.True);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.Created | ChangeKind.IsDirectoryHint), Is.True);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.RenamedTo | ChangeKind.IsDirectoryHint), Is.True);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.Created), Is.False);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.RenamedTo), Is.False);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.Changed | ChangeKind.IsDirectoryHint), Is.False);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.Changed), Is.False);
        Assert.That(ChangeListWriter.RequiresRecursion(ChangeKind.Deleted | ChangeKind.Created), Is.True);
        Assert.That(ChangeListWriter.IsRepresentable("plain"), Is.True);
        Assert.That(ChangeListWriter.IsRepresentable("a\rb"), Is.False);
        Assert.That(ChangeListWriter.IsRepresentable("a\uD800.txt"), Is.False);
        Assert.That(ChangeListWriter.IsRepresentable("a\uDC00.txt"), Is.False);
        Assert.That(ChangeListWriter.IsRepresentable("a\uD83C\uDF89.txt"), Is.True);
    }

    [Test]
    [Category("FileSystemWatcher")]
    public async Task RealWatcher_SingleFileRootIgnoresTheTemporaryFileOfAnAtomicSaveAsync()
    {
        Directory.CreateDirectory(Root);
        var target = P("document.txt");
        await File.WriteAllTextAsync(target, "1");

        var observed = new List<string>();
        using var source = new FileSystemWatcherEventSource(target);
        source.Changed += (path, kind) => { lock (observed) observed.Add(path); };
        source.Start();

        var temp = P(".document.txt.tmp");
        await File.WriteAllTextAsync(temp, "2");
        File.Move(temp, target, true);

        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            lock (observed)
                if (observed.Any(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase)))
                    break;
            await Task.Delay(100);
        }
        await Task.Delay(500);

        lock (observed)
        {
            Assert.That(observed, Is.Not.Empty, "the save is reported");
            Assert.That(observed.All(x => string.Equals(x, target, StringComparison.OrdinalIgnoreCase)), Is.True, string.Join(", ", observed));
        }
    }

    [Test]
    [Category("FileSystemWatcher")]
    public async Task RealWatcher_ReportsCreateModifyRenameDeleteAsync()
    {
        Directory.CreateDirectory(Root);
        var observed = new List<(string Path, ChangeKind Kind)>();
        var errors = new List<Exception>();
        using var source = new FileSystemWatcherEventSource(Root);
        source.Changed += (path, kind) => { lock (observed) observed.Add((path, kind)); };
        source.Error += ex => { lock (errors) errors.Add(ex); };
        source.Start();

        var file = P("file.txt");
        var renamed = P("renamed.txt");
        var folder = P("folder");

        bool Seen(string path, ChangeKind kind)
        {
            lock (observed)
                return observed.Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase) && (x.Kind & kind) != 0);
        }

        // The operating system coalesces events on the same path, so each step is
        // waited for before the next one, as a backup would see them minutes apart
        async Task WaitForAsync(string description, Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline && !condition())
                await Task.Delay(100);
            Assert.That(condition(), Is.True, description);
        }

        await File.WriteAllTextAsync(file, "1");
        await WaitForAsync("created", () => Seen(file, ChangeKind.Created));

        await File.WriteAllTextAsync(file, "12");
        await WaitForAsync("changed", () => Seen(file, ChangeKind.Changed | ChangeKind.Created));

        File.Move(file, renamed);
        await WaitForAsync("renamed from", () => Seen(file, ChangeKind.RenamedFrom | ChangeKind.Deleted));
        await WaitForAsync("renamed to", () => Seen(renamed, ChangeKind.RenamedTo | ChangeKind.Created));

        Directory.CreateDirectory(folder);
        await WaitForAsync("folder created", () => Seen(folder, ChangeKind.Created) && Seen(folder, ChangeKind.IsDirectoryHint));

        File.Delete(renamed);
        await WaitForAsync("deleted", () => Seen(renamed, ChangeKind.Deleted));

        Assert.That(errors, Is.Empty);
    }
}
