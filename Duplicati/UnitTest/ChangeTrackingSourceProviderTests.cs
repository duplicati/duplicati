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
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main;
using Duplicati.Library.SQLiteHelper;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// A source provider that enumerates only what changed and relies on the backup to copy
/// the rest from the previous fileset, as a provider using a Graph delta query does.
/// </summary>
[NonParallelizable]
public class ChangeTrackingSourceProviderTests : BasicSetupHelper
{
    /// <summary>
    /// What the fake provider does in a run, and what it saw
    /// </summary>
    private sealed class Scenario
    {
        public static Scenario Current = new();

        /// <summary>The entries below the mount point</summary>
        public Func<string, FakeEntry[]> Children = _ => [];
        /// <summary>The carry-forward predicate</summary>
        public Func<string, bool> Enumerated = _ => true;
        /// <summary>Whether a folder listed only its changes, so an empty listing does not make it empty</summary>
        public Func<string, bool> Incremental = _ => true;
        /// <summary>The state to export</summary>
        public Dictionary<string, string> Export = new();
        /// <summary>The modification time of every entry</summary>
        public DateTime Timestamp = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        /// <summary>Called when the root entry is enumerated</summary>
        public Action? OnEnumerate;
        /// <summary>A pause between the root entry's children, so a stop can arrive while enumerating</summary>
        public TimeSpan EnumerateDelay = TimeSpan.Zero;

        /// <summary>The state the provider was handed</summary>
        public IReadOnlyDictionary<string, string>? Loaded;
        /// <summary>How many times state was handed to the provider</summary>
        public int LoadCalls;
        /// <summary>How many times the provider was asked to export state</summary>
        public int ExportCalls;
        /// <summary>How many times the carry-forward predicate was asked</summary>
        public int EnumeratedCalls;
        /// <summary>The observer the backup registered for how the folders are walked, if any</summary>
        public Action<string, FolderWalkMode>? FolderWalkObserver;
    }

    /// <summary>
    /// Keeps the progress object the backup reports to, so the totals can be read while it runs
    /// </summary>
    private sealed class ProgressSink : IMessageSink
    {
        public IOperationProgress? Progress { get; private set; }

        public void BackendEvent(BackendActionType action, BackendEventType type, string path, long size) { }
        public void SetBackendProgress(IBackendProgress progress) { }
        public void SetOperationProgress(IOperationProgress progress) => Progress = progress;
        public void WriteMessage(LogEntry entry) { }
    }

    private sealed class FakeEntry : ISourceProviderEntry
    {
        private readonly byte[]? _content;
        private readonly List<FakeEntry> _children;

        public FakeEntry(string path, byte[]? content, bool root, params FakeEntry[] children)
        {
            Path = path;
            _content = content;
            IsRootEntry = root;
            _children = children.ToList();
        }

        public bool IsFolder => _content == null;
        public bool IsMetaEntry => false;
        public bool IsRootEntry { get; }
        public DateTime CreatedUtc => Scenario.Current.Timestamp;
        public DateTime LastModificationUtc => Scenario.Current.Timestamp;
        public string Path { get; }
        public long Size => _content?.Length ?? 0;
        public bool IsSymlink => false;
        public string? SymlinkTarget => null;
        public FileAttributes Attributes => IsFolder ? FileAttributes.Directory : FileAttributes.Normal;
        public bool IsBlockDevice => false;
        public bool IsCharacterDevice => false;
        public bool IsAlternateStream => false;
        public string? HardlinkTargetId => null;

        public Task<Stream> OpenRead(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new MemoryStream(_content ?? []));

        public Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
            => Task.FromResult(new Dictionary<string, string?>());

        public Task<bool> FileExists(string filename, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            if (IsRootEntry)
                Scenario.Current.OnEnumerate?.Invoke();
            foreach (var c in _children)
            {
                yield return c;
                if (IsRootEntry && Scenario.Current.EnumerateDelay > TimeSpan.Zero)
                    await Task.Delay(Scenario.Current.EnumerateDelay, cancellationToken);
            }
        }
    }

    private sealed class TrackingProvider : ISourceProviderModule, IChangeTrackingSourceProvider
    {
        private readonly string _mountPoint;

        public TrackingProvider() { _mountPoint = string.Empty; }

        public TrackingProvider(string url, string mountPoint, Dictionary<string, string?> options) { _mountPoint = mountPoint; }

        /// <summary>
        /// Creates a provider instance mounted at a given path, bypassing the loader
        /// </summary>
        /// <param name="mountPoint">The mount path, possibly empty</param>
        public TrackingProvider(string mountPoint) { _mountPoint = mountPoint; }

        public string Key => "test-change-tracking";
        public string DisplayName => "Test change tracking provider";
        public string Description => "Test provider that enumerates only changes";
        /// <summary>
        /// The option the test provider declares as a password, so the configuration hash can leave it out
        /// </summary>
        public const string SecretOption = "test-secret";

        /// <summary>
        /// An option of the test provider that is not a secret, so a change to it must change the configuration hash
        /// </summary>
        public const string PlainOption = "test-option";

        public IList<ICommandLineArgument> SupportedCommands =>
        [
            new CommandLineArgument(SecretOption, CommandLineArgument.ArgumentType.Password, "The secret", "The secret of the test provider"),
            new CommandLineArgument(PlainOption, CommandLineArgument.ArgumentType.String, "An option", "An option of the test provider"),
        ];
        public string MountedPath => _mountPoint;
        public bool NeedsStoredMetadata => false;

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task TestAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield return new FakeEntry(_mountPoint, null, true, Scenario.Current.Children(_mountPoint));
        }

        public Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
            => Task.FromResult<ISourceProviderEntry?>(null);

        public void Dispose() { }

        public string ChangeTrackingScope => _mountPoint;

        public void LoadChangeTrackingState(IReadOnlyDictionary<string, string> state)
        {
            Scenario.Current.Loaded = state;
            Scenario.Current.LoadCalls++;
        }

        public IReadOnlyDictionary<string, string> ExportChangeTrackingState()
        {
            Scenario.Current.ExportCalls++;
            return Scenario.Current.Export;
        }

        public bool WasPathEnumerated(string path)
        {
            Scenario.Current.EnumeratedCalls++;
            return Scenario.Current.Enumerated(path);
        }

        public bool IsIncrementallyEnumeratedFolder(string folderPath)
            => Scenario.Current.Incremental(folderPath);

        public void SetFolderWalkObserver(Action<string, FolderWalkMode> observer)
            => Scenario.Current.FolderWalkObserver = observer;
    }

    private string Mount => Util.AppendDirSeparator(Path.GetFullPath(Path.Combine(BASEFOLDER, "tracked")));
    private string Source => "@" + Mount.TrimEnd(Path.DirectorySeparatorChar) + "|test-change-tracking://source";

    private static FakeEntry File(string mount, string relative, string content)
        => new(Path.Combine(mount, relative), Encoding.UTF8.GetBytes(content), false);

    private static FakeEntry Folder(string mount, string relative, params FakeEntry[] children)
        => new(Util.AppendDirSeparator(Path.Combine(mount, relative)), null, false, children);

    private string P(string relative) => Path.Combine(Mount, relative);
    private string D(string relative) => Util.AppendDirSeparator(Path.Combine(Mount, relative));

    private Dictionary<string, string> Prepare()
    {
        Library.DynamicLoader.SourceProviderLoader.AddSourceProvider(new TrackingProvider());
        Scenario.Current = new Scenario();
        return new Dictionary<string, string>(TestOptions)
        {
            ["no-encryption"] = "true",
            ["disable-file-scanner"] = "true",
        };
    }

    private static FakeEntry[] InitialTree(string m)
        => [File(m, "a.txt", "A"), Folder(m, "sub", File(m, Path.Combine("sub", "b.txt"), "B"), File(m, Path.Combine("sub", "c.txt"), "C"))];

    private async Task RunBackupAsync(Dictionary<string, string> options, Library.Utility.IFilter? filter = null)
    {
        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var r = await c.BackupAsync([Source], filter);
        TestUtils.AssertResults(r);
    }

    /// <summary>
    /// Runs a backup and returns the file count and size the progress showed when the given
    /// path was enumerated. The total is adjusted in the background as the provider reports
    /// its folders, so the read waits until the totals satisfy the expectation, or gives up
    /// after a while and returns what it saw last.
    /// </summary>
    /// <param name="options">The backup options</param>
    /// <param name="atPath">The enumerated path to read the totals at</param>
    /// <param name="expected">Whether the totals are the ones waited for</param>
    /// <returns>The totals</returns>
    private async Task<(long Count, long Size)> RunBackupAndReadTotalsAsync(Dictionary<string, string> options, string atPath, Func<(long Count, long Size), bool> expected)
    {
        var sink = new ProgressSink();
        (long Count, long Size)? totals = null;
        using (var c = new Controller("file://" + TARGETFOLDER, options, sink))
        {
            c.OnOperationStarted = r =>
            {
#if DEBUG
                ((ITaskControlProvider)r).TaskControl.TestMethodCallback = path =>
                {
                    if (totals != null || sink.Progress == null || !string.Equals(path, atPath, StringComparison.Ordinal))
                        return;

                    var deadline = DateTime.UtcNow.AddSeconds(10);
                    while (true)
                    {
                        sink.Progress.UpdateOverall(out _, out _, out _, out _, out var count, out var size, out _);
                        totals = (count, size);
                        if (expected(totals.Value) || DateTime.UtcNow > deadline)
                            break;
                        Thread.Sleep(20);
                    }
                };
#endif
            };
            TestUtils.AssertResults(await c.BackupAsync([Source]));
        }

#if !DEBUG
        Assert.Ignore("Reading the progress while the files are processed needs a Debug build");
#endif
        Assert.That(totals, Is.Not.Null, "the progress was not read while the files were processed");
        return totals!.Value;
    }

    private async Task<HashSet<string>> ListPathsAsync(Dictionary<string, string> options)
    {
        using var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 0 }), null);
        var r = await c.ListAsync("*");
        TestUtils.AssertResults(r);
        return r.Files.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>> RestoreAsync(Dictionary<string, string> options)
    {
        if (Directory.Exists(RESTOREFOLDER))
            Directory.Delete(RESTOREFOLDER, true);
        Directory.CreateDirectory(RESTOREFOLDER);

        using var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 0, restore_path = RESTOREFOLDER }), null);
        TestUtils.AssertResults(await c.RestoreAsync(["*"]));

        return Directory.EnumerateFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories)
            .ToDictionary(x => Path.GetFileName(x), x => System.IO.File.ReadAllText(x), StringComparer.Ordinal);
    }

    private static async Task<List<(long FilesetId, string Scope, string Key, string Value)>> ReadStateRowsAsync(string dbfile)
    {
        using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT ""FilesetID"", ""Scope"", ""Key"", ""Value"" FROM ""SourceProviderState"" ORDER BY ""FilesetID"", ""Key""";
        var rows = new List<(long, string, string, string)>();
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            rows.Add((rd.GetInt64(0), rd.GetString(1), rd.GetString(2), rd.GetString(3)));
        return rows;
    }

    private static async Task<List<long>> ReadFilesetIdsAsync(string dbfile)
    {
        using var db = await SQLiteLoader.LoadConnectionAsync(dbfile);
        using var cmd = db.CreateCommand();
        cmd.CommandText = @"SELECT ""ID"" FROM ""Fileset"" ORDER BY ""Timestamp""";
        var ids = new List<long>();
        using var rd = await cmd.ExecuteReaderAsync();
        while (await rd.ReadAsync())
            ids.Add(rd.GetInt64(0));
        return ids;
    }

    private async Task FirstRunAsync(Dictionary<string, string> options)
    {
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v1" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.LoadCalls, Is.EqualTo(1), "The provider should be handed state once per run");
        Assert.That(Scenario.Current.Loaded, Is.Empty, "The first run has no stored state");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task SecondRunCarriesUnchangedFilesForwardAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        // Only b.txt changed; the provider yields its folder and the file
        Scenario.Current = new Scenario
        {
            Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Children = m => [Folder(m, "sub", File(m, Path.Combine("sub", "b.txt"), "B2"))],
            Enumerated = p => p == D("sub") || p == P(Path.Combine("sub", "b.txt")),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v1" }), "The second run should see the state of the first");

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), D("sub"), P(Path.Combine("sub", "b.txt")), P(Path.Combine("sub", "c.txt")) }));

        var restored = await RestoreAsync(options);
        Assert.That(restored["a.txt"], Is.EqualTo("A"), "An unchanged file keeps its content");
        Assert.That(restored["b.txt"], Is.EqualTo("B2"), "A changed file has its new content");
        Assert.That(restored["c.txt"], Is.EqualTo("C"), "An unchanged file in a yielded folder keeps its content");

        var filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        var rows = await ReadStateRowsAsync(options["dbpath"]);
        Assert.That(filesets.Count, Is.EqualTo(2));
        Assert.That(rows.Where(x => x.FilesetId == filesets[1]).Select(x => (x.Key, x.Value)), Is.EqualTo(new[] { ("k", "v2") }), "The new fileset stores the exported state");
        Assert.That(rows.Where(x => x.FilesetId == filesets[0]).Select(x => (x.Key, x.Value)), Is.EqualTo(new[] { ("k", "v1") }), "The previous fileset keeps the state its own run ended with");
        Assert.That(rows.Select(x => x.Scope).Distinct(), Is.EqualTo(new[] { Mount }));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task TombstonedPathIsAbsentAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        Scenario.Current = new Scenario
        {
            Children = m => [File(m, "d.txt", "D")],
            Enumerated = p => p == P("a.txt") || p == P("d.txt"),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Does.Not.Contain(P("a.txt")), "A path the provider examined but did not yield is gone");
        Assert.That(paths, Is.SupersetOf(new[] { P("d.txt"), D("sub"), P(Path.Combine("sub", "b.txt")), P(Path.Combine("sub", "c.txt")) }));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task RemovedPrefixDropsEverythingBelowItAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        Scenario.Current = new Scenario
        {
            Children = m => [File(m, "d.txt", "D")],
            Enumerated = p => p.StartsWith(D("sub"), StringComparison.Ordinal) || p == P("d.txt"),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Does.Not.Contain(D("sub")));
        Assert.That(paths, Does.Not.Contain(P(Path.Combine("sub", "b.txt"))));
        Assert.That(paths, Does.Not.Contain(P(Path.Combine("sub", "c.txt"))));
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), P("d.txt") }));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task FullyWalkedSubtreeDropsTheMissingFileAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        Scenario.Current = new Scenario
        {
            Children = m => [Folder(m, "sub", File(m, Path.Combine("sub", "b.txt"), "B"))],
            Enumerated = p => p.StartsWith(D("sub"), StringComparison.Ordinal),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Does.Not.Contain(P(Path.Combine("sub", "c.txt"))), "A file missing from a fully walked folder is gone");
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), D("sub"), P(Path.Combine("sub", "b.txt")) }));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task YieldedFolderKeepsItsUnlistedChildrenAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        Scenario.Current = new Scenario
        {
            Children = m => [Folder(m, "sub")],
            Enumerated = p => p == D("sub"),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), D("sub"), P(Path.Combine("sub", "b.txt")), P(Path.Combine("sub", "c.txt")) }));

        var restored = await RestoreAsync(options);
        Assert.That(restored["b.txt"], Is.EqualTo("B"));
        Assert.That(restored["c.txt"], Is.EqualTo("C"));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task YieldedFolderIsNotExcludedAsEmptyAsync()
    {
        var options = Prepare();
        options["exclude-empty-folders"] = "true";
        await FirstRunAsync(options);

        // The folder lists nothing because nothing in it changed; it must not be taken for empty
        Scenario.Current = new Scenario
        {
            Children = m => [Folder(m, "sub"), Folder(m, "empty")],
            Enumerated = p => p == D("sub") || p == D("empty"),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), D("sub"), P(Path.Combine("sub", "b.txt")), P(Path.Combine("sub", "c.txt")) }));
        Assert.That(paths, Does.Contain(D("empty")), "An incrementally enumerated folder cannot be told apart from an empty one and is kept");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task FullyListedEmptyFolderIsStillExcludedAsync()
    {
        var options = Prepare();
        options["exclude-empty-folders"] = "true";
        await FirstRunAsync(options);

        // Both folders list nothing, but only "sub" was walked incrementally; "empty" was
        // listed in full, so it is really empty and is excluded like in a run without state
        Scenario.Current = new Scenario
        {
            Children = m => [Folder(m, "sub"), Folder(m, "empty")],
            Enumerated = p => p == D("sub") || p == D("empty"),
            Incremental = p => p == D("sub"),
            Export = { ["k"] = "v2" }
        };
        await RunBackupAsync(options);

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), D("sub"), P(Path.Combine("sub", "b.txt")), P(Path.Combine("sub", "c.txt")) }));
        Assert.That(paths, Does.Not.Contain(D("empty")), "A folder the provider listed in full is excluded when it is empty");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task PartialBackupStoresNoStateAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var stopped = false;
            Scenario.Current = new Scenario
            {
                Children = m => [File(m, "d.txt", "D")],
                Enumerated = _ => false,
                Export = { ["k"] = "v2" },
                OnEnumerate = () =>
                {
                    if (!stopped)
                    {
                        stopped = true;
                        c.StopAsync().GetAwaiter().GetResult();
                    }
                }
            };

            var r = await c.BackupAsync([Source]);
            Assert.That(r.PartialBackup, Is.True, "Stopping during enumeration makes a partial backup");
        }

        // A stopped run cannot tell what it did not reach from what it listed in full, so it stores no
        // state and does not ask the provider for any. Nothing was uploaded before the stop, so the new
        // fileset is dropped again, and the previous fileset keeps the state that describes its content
        Assert.That(Scenario.Current.ExportCalls, Is.EqualTo(0), "A partial backup does not export state");
        var filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        var rows = await ReadStateRowsAsync(options["dbpath"]);
        Assert.That(filesets.Count, Is.EqualTo(1));
        Assert.That(rows.Select(x => (x.FilesetId, x.Key, x.Value)), Is.EqualTo(new[] { (filesets[0], "k", "v1") }), "The previous fileset keeps its state");

        Scenario.Current = new Scenario { Children = m => [File(m, "d.txt", "D")], Enumerated = _ => false, Export = { ["k"] = "v3" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v1" }), "The run after a dropped partial backup continues from the kept fileset");

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Is.SupersetOf(new[] { P("a.txt"), D("sub"), P(Path.Combine("sub", "b.txt")), P(Path.Combine("sub", "c.txt")), P("d.txt") }));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task PartialBackupWithUploadsStoresNoStateAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        // The stop is requested while the files are listed. The provider claims to have examined
        // everything, as one that lists most content in full does, so the paths it did not reach
        // count as deleted and the fileset is kept whether or not a file was uploaded before the stop
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
        {
            var stopped = false;
            Scenario.Current = new Scenario
            {
                Children = m => [File(m, "d.txt", "D"), File(m, "e.txt", "E")],
                Enumerated = _ => true,
                Export = { ["k"] = "v2" },
                EnumerateDelay = TimeSpan.FromSeconds(2),
            };
            Scenario.Current.OnEnumerate = () =>
            {
                if (!stopped)
                {
                    stopped = true;
                    Task.Run(async () =>
                    {
                        await Task.Delay(500);
                        await c.StopAsync();
                    });
                }
            };

            var r = await c.BackupAsync([Source]);
            Assert.That(r.PartialBackup, Is.True, "The stop arrived while the backup was still enumerating");
        }

        // The partial fileset carries no state: the tokens it started from describe the previous
        // fileset, not the entries the stop left out of this one, and the provider may not have
        // reached everything its exported state would claim
        Assert.That(Scenario.Current.ExportCalls, Is.EqualTo(0), "A partial backup does not export state");
        var filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        var rows = await ReadStateRowsAsync(options["dbpath"]);
        Assert.That(filesets.Count, Is.EqualTo(2), "The partial fileset is kept, as it lacks the paths the stop left out");
        Assert.That(rows.Select(x => (x.FilesetId, x.Key, x.Value)), Is.EqualTo(new[] { (filesets[0], "k", "v1") }), "Only the complete fileset carries state");

        // The run after it is handed nothing, walks everything, and carries nothing forward
        Scenario.Current = new Scenario { Children = m => [File(m, "f.txt", "F")], Enumerated = _ => false, Export = { ["k"] = "v3" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded, Is.Empty, "The run after a partial backup starts over");
        Assert.That(Scenario.Current.EnumeratedCalls, Is.EqualTo(0), "Without state nothing is copied from the partial fileset");

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Does.Contain(P("f.txt")));
        Assert.That(paths, Does.Not.Contain(P("a.txt")), "The provider listed everything it has, so what it did not list is gone");
        Assert.That(paths, Does.Not.Contain(P("d.txt")));

        rows = await ReadStateRowsAsync(options["dbpath"]);
        filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        Assert.That(rows.Where(x => x.FilesetId == filesets[^1]).Select(x => (x.Key, x.Value)), Is.EqualTo(new[] { ("k", "v3") }), "The complete run stores its state again");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task FilterChangeDiscardsTheStateAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        // A run that changes nothing leaves no new fileset, so each run changes a file
        var filter = new Library.Utility.FilterExpression("*.tmp", false);
        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A2")], Export = { ["k"] = "v2" } };
        await RunBackupAsync(options, filter);
        Assert.That(Scenario.Current.LoadCalls, Is.EqualTo(1));
        Assert.That(Scenario.Current.Loaded, Is.Empty, "State recorded under another filter is discarded");

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A3")], Export = { ["k"] = "v3" } };
        await RunBackupAsync(options, filter);
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v2" }), "State recorded under the same filter is loaded");

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 4, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A4")], Export = { ["k"] = "v4" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded, Is.Empty, "Going back to the original filter discards the state again");
    }

    [Test]
    [Category("ChangeTracking")]
    public void ConfigHashLeavesOutCredentialsAndKeepsTheOtherOptions()
    {
        Prepare();
        string Hash(params string[] sources) => Library.Main.Operation.Backup.ChangeTrackingCoordinator.ComputeConfigHash(null, sources);
        var mount = "@" + Mount.TrimEnd(Path.DirectorySeparatorChar) + "|";

        var reference = Hash(mount + "test-change-tracking://source?opt=1&test-secret=s1&auth-password=p1&authid=a1");
        Assert.That(Hash(mount + "test-change-tracking://source?opt=1&test-secret=s2&auth-password=p2&authid=a2"), Is.EqualTo(reference), "Rotated secrets do not change the hash");
        Assert.That(Hash(mount + "test-change-tracking://source?opt=1"), Is.EqualTo(reference), "Secrets are left out entirely");
        Assert.That(Hash(mount + "test-change-tracking://user:pw@source?opt=1"), Is.EqualTo(reference), "The user info is left out");
        Assert.That(Hash(mount + "test-change-tracking://source?test-secret=s1&opt=1"), Is.EqualTo(reference), "The order of the options does not matter");

        Assert.That(Hash(mount + "test-change-tracking://source?opt=2"), Is.Not.EqualTo(reference), "A changed option changes the hash");
        Assert.That(Hash(mount + "test-change-tracking://source?opt=1&other=x"), Is.Not.EqualTo(reference), "An added option changes the hash");
        Assert.That(Hash(mount + "test-change-tracking://source"), Is.Not.EqualTo(reference), "A removed option changes the hash");
        Assert.That(Hash(mount + "test-change-tracking://other?opt=1"), Is.Not.EqualTo(reference), "A changed host changes the hash");
        Assert.That(Hash("@" + Path.Combine(Mount, "elsewhere").TrimEnd(Path.DirectorySeparatorChar) + "|test-change-tracking://source?opt=1"), Is.Not.EqualTo(reference), "A changed mount point changes the hash");
        Assert.That(Hash(mount + "test-change-tracking://source?opt=1", Path.Combine(BASEFOLDER, "local")), Is.Not.EqualTo(reference), "An added source changes the hash");

        Assert.That(Hash(mount + "test-change-tracking://source?opt=1", Path.Combine(BASEFOLDER, "local")), Is.EqualTo(Hash(Path.Combine(BASEFOLDER, "local"), mount + "test-change-tracking://source?opt=1")), "The order of the sources does not matter");
        Assert.That(Library.Main.Operation.Backup.ChangeTrackingCoordinator.StripSecrets("test-change-tracking://user:pw@source?opt=1&test-secret=s&auth-password=p"), Does.Not.Contain("pw").And.Not.Contain("=s").And.Not.Contain("=p"), "The hashed form carries no secret");

        // The hash stored for a scope covers the filter and the scope's own source only
        string ScopeHash(params string[] sources) => Library.Main.Operation.Backup.ChangeTrackingCoordinator.ComputeConfigHash(null, Mount, sources);
        Assert.That(ScopeHash(mount + "test-change-tracking://source?opt=1", Path.Combine(BASEFOLDER, "local")), Is.EqualTo(reference), "A source mounted elsewhere is left out of the scope's hash");
        Assert.That(ScopeHash("@" + Mount + "|test-change-tracking://source?opt=1"), Is.EqualTo(reference), "The mount point is normalized, so a trailing separator does not change the hash");
        Assert.That(ScopeHash(mount + "test-change-tracking://source?opt=2", Path.Combine(BASEFOLDER, "local")), Is.Not.EqualTo(reference), "A changed option of the scope's own source changes the hash");
        Assert.That(ScopeHash(Path.Combine(BASEFOLDER, "local")), Is.EqualTo(Hash(Path.Combine(BASEFOLDER, "local"))), "Without a source at the mount point every source is hashed");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task AddingAnUnrelatedSourceKeepsTheStateAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        var local = Path.Combine(BASEFOLDER, "local");
        Directory.CreateDirectory(local);
        System.IO.File.WriteAllText(Path.Combine(local, "l.txt"), "L");

        // Only a.txt changed; the provider yields it alone and claims nothing else was examined
        Scenario.Current = new Scenario
        {
            Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            Children = m => [File(m, "a.txt", "A2")],
            Enumerated = p => p == P("a.txt"),
            Export = { ["k"] = "v2" }
        };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([Source, local]));
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v1" }), "Adding a source mounted elsewhere keeps the state");

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Does.Contain(Path.Combine(local, "l.txt")), "The added source is backed up");
        Assert.That(paths, Does.Contain(P(Path.Combine("sub", "b.txt"))), "The unchanged entries are carried forward");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task RotatedSecretKeepsTheStateButAChangedOptionDiscardsItAsync()
    {
        var options = Prepare();
        var source = Source + "?test-option=1&test-secret=s1";
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v1" } };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([source]));

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A2")], Export = { ["k"] = "v2" } };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([Source + "?test-option=1&test-secret=s2"]));
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v1" }), "A rotated secret keeps the state");

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A3")], Export = { ["k"] = "v3" } };
        using (var c = new Controller("file://" + TARGETFOLDER, options, null))
            TestUtils.AssertResults(await c.BackupAsync([Source + "?test-option=2&test-secret=s2"]));
        Assert.That(Scenario.Current.Loaded, Is.Empty, "A changed option discards the state");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task DeletingAFilesetRemovesItsStateAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A2")], Export = { ["k"] = "v2" } };
        await RunBackupAsync(options);

        var filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        Assert.That(filesets.Count, Is.EqualTo(2));
        Assert.That((await ReadStateRowsAsync(options["dbpath"])).Select(x => x.FilesetId).Distinct(), Is.EquivalentTo(filesets));

        using (var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 1 }), null))
            TestUtils.AssertResults(await c.DeleteAsync());

        var rows = await ReadStateRowsAsync(options["dbpath"]);
        Assert.That(rows.Select(x => x.FilesetId).Distinct(), Is.EqualTo(new[] { filesets[1] }), "The deleted fileset's state is gone, the other fileset's state remains");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task RunWithoutChangesMovesItsStateToTheKeptFilesetAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        // Nothing changed, so the new fileset is dropped, but the tokens the run ended with
        // describe the remaining fileset just as well
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v2", ["extra"] = "e2" } };
        await RunBackupAsync(options);

        var filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        Assert.That(filesets.Count, Is.EqualTo(1), "A run without changes leaves no new fileset");
        var rows = await ReadStateRowsAsync(options["dbpath"]);
        Assert.That(rows.Select(x => (x.FilesetId, x.Key, x.Value)), Is.EquivalentTo(new[] { (filesets[0], "extra", "e2"), (filesets[0], "k", "v2") }), "The dropped fileset's state replaces the kept fileset's state");

        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v3" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v2", ["extra"] = "e2" }), "The next run continues from the moved state");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task DeletingTheNewestVersionRedoesTheBackupFromItsStartAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A2")], Export = { ["k"] = "v2" } };
        await RunBackupAsync(options);

        using (var c = new Controller("file://" + TARGETFOLDER, options.Expand(new { version = 0 }), null))
            TestUtils.AssertResults(await c.DeleteAsync());

        Scenario.Current = new Scenario { Timestamp = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", "A3")], Export = { ["k"] = "v3" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v1" }), "After deleting the newest version the backup resumes from the version before it");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task StateOfOlderFilesetsIsPrunedAsync()
    {
        var options = Prepare();
        await FirstRunAsync(options);

        for (var run = 2; run <= 5; run++)
        {
            var content = "A" + run;
            Scenario.Current = new Scenario { Timestamp = new DateTime(2024, run, 1, 0, 0, 0, DateTimeKind.Utc), Children = m => [File(m, "a.txt", content)], Export = { ["k"] = "v" + run } };
            await RunBackupAsync(options);
            Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v" + (run - 1) }));
        }

        var filesets = await ReadFilesetIdsAsync(options["dbpath"]);
        Assert.That(filesets.Count, Is.EqualTo(5));

        var rows = await ReadStateRowsAsync(options["dbpath"]);
        Assert.That(rows.Select(x => x.FilesetId).Distinct(), Is.EquivalentTo(filesets.Skip(2)), "Only the newest filesets keep their state");
        Assert.That(rows.Single(x => x.FilesetId == filesets[4]).Value, Is.EqualTo("v5"));
        Assert.That(rows.Single(x => x.FilesetId == filesets[3]).Value, Is.EqualTo("v4"));
        Assert.That(rows.Single(x => x.FilesetId == filesets[2]).Value, Is.EqualTo("v3"));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task LargeStateValueRoundTripsAsync()
    {
        var options = Prepare();
        var big = new string('x', 3 * 1024 * 1024);
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["big"] = big, ["k"] = "v1" } };
        await RunBackupAsync(options);

        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v2" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded!["big"], Is.EqualTo(big));
        Assert.That(Scenario.Current.Loaded!["k"], Is.EqualTo("v1"));
    }

    [Test]
    [Category("ChangeTracking")]
    public void ProviderWithoutMountPathIsNotTracked()
    {
        Scenario.Current = new Scenario { Enumerated = _ => false };
        var mounted = new TrackingProvider(Mount);
        var unmounted = new TrackingProvider(string.Empty);

        var messages = new List<Library.Logging.LogEntry>();
        using (Library.Logging.Log.StartIsolatingScope(true))
        using (Library.Logging.Log.StartScope(messages.Add))
        {
            var both = Library.Main.Operation.Backup.ChangeTrackingCoordinator.Create(new Library.SourceProvider.Combiner([mounted, unmounted]), null, [Source]);
            Assert.That(both, Is.Not.Null);
            Assert.That(both!.WasPathEnumerated(P("a.txt")), Is.False, "A path under the mounted provider is decided by that provider");
            Assert.That(both.WasPathEnumerated(Path.Combine(BASEFOLDER, "elsewhere", "a.txt")), Is.True, "A path no tracked provider owns counts as examined");

            Assert.That(Library.Main.Operation.Backup.ChangeTrackingCoordinator.Create(unmounted, null, [Source]), Is.Null, "A provider without a mount path alone leaves nothing to coordinate");
        }

        Assert.That(messages.Count(x => x.Id == "ChangeTrackingDisabledWithoutMountPath" && x.Level == Library.Logging.LogMessageType.Warning), Is.EqualTo(2));
        Assert.That(Scenario.Current.LoadCalls, Is.EqualTo(0), "Creating the coordinator hands out no state");
    }

    [Test]
    [Category("ChangeTracking")]
    public void ChangedFilesIsRefusedWithATrackedProvider()
    {
        var options = Prepare();
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v1" } };
        options["changed-files"] = P("a.txt");

        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        var ex = Assert.ThrowsAsync<UserInformationException>(() => c.BackupAsync([Source]));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangedFilesWithChangeTrackingSource"));
        Assert.That(Scenario.Current.LoadCalls, Is.EqualTo(0), "A refused run hands the provider no state");
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task FileCounterDoesNotEnumerateATrackedProviderAsync()
    {
        var options = Prepare();
        options["disable-file-scanner"] = "false";

        var enumerations = 0;
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v1" }, OnEnumerate = () => Interlocked.Increment(ref enumerations) };
        await RunBackupAsync(options);
        Assert.That(enumerations, Is.EqualTo(1), "Only the backup enumerates a provider that tracks changes; a parallel file count would race it for the exported state");

        enumerations = 0;
        Scenario.Current = new Scenario { Children = InitialTree, Export = { ["k"] = "v2" }, OnEnumerate = () => Interlocked.Increment(ref enumerations) };
        await RunBackupAsync(options);
        Assert.That(enumerations, Is.EqualTo(1));
        Assert.That(Scenario.Current.Loaded, Is.EqualTo(new Dictionary<string, string> { ["k"] = "v1" }));
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task ProviderWithoutStateStoresNoRowsAsync()
    {
        var options = Prepare();
        Scenario.Current = new Scenario { Children = InitialTree };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.LoadCalls, Is.EqualTo(1));
        Assert.That(await ReadStateRowsAsync(options["dbpath"]), Is.Empty, "A provider that exports nothing and loaded nothing stores no rows");

        Scenario.Current = new Scenario { Children = InitialTree };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.Loaded, Is.Empty);
        Assert.That(await ReadStateRowsAsync(options["dbpath"]), Is.Empty);
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task ProgressTotalLeavesOutTheFoldersCarriedForwardAsync()
    {
        var options = Prepare();

        // a.txt is 1 byte, "sub" holds 30 bytes in two files and "other" 100 bytes in one
        FakeEntry[] Tree(string m) =>
        [
            File(m, "a.txt", "A"),
            Folder(m, "sub", File(m, Path.Combine("sub", "b.txt"), new string('b', 10)), File(m, Path.Combine("sub", "c.txt"), new string('c', 20))),
            Folder(m, "other", File(m, Path.Combine("other", "d.txt"), new string('d', 100))),
        ];

        Scenario.Current = new Scenario { Children = Tree, Export = { ["k"] = "v1" } };
        await RunBackupAsync(options);
        Assert.That(Scenario.Current.FolderWalkObserver, Is.Null, "A provider that was handed no state walks everything, so no observer is registered");

        // The second run lists only a.txt. It reports "other" and "sub" as walked incrementally,
        // a folder below "sub" that is covered by it, and then lists "other" in full after all
        Scenario.Current = new Scenario
        {
            Children = m => [File(m, "a.txt", "A")],
            Enumerated = path => !path.StartsWith(D("sub"), StringComparison.Ordinal),
            Incremental = path => path.StartsWith(D("sub"), StringComparison.Ordinal),
            Export = { ["k"] = "v2" },
        };
        Scenario.Current.OnEnumerate = () =>
        {
            var observer = Scenario.Current.FolderWalkObserver;
            Assert.That(observer, Is.Not.Null, "The backup registers an observer with a provider that was handed state");
            observer!(D("other"), FolderWalkMode.Incremental);
            observer(D("sub"), FolderWalkMode.Incremental);
            observer(D(Path.Combine("sub", "nested")), FolderWalkMode.Incremental);
            observer(D("other"), FolderWalkMode.Full);
        };

        var totals = await RunBackupAndReadTotalsAsync(options, P("a.txt"), t => t == (2, 101));
        Assert.That(totals, Is.EqualTo((2L, 101L)), "The previous backup holds 4 files of 131 bytes; the 2 files of 30 bytes below \"sub\" are carried forward, and \"other\" is listed in full after all");

        var paths = await ListPathsAsync(options);
        Assert.That(paths, Does.Contain(P("a.txt")));
        Assert.That(paths, Does.Contain(P(Path.Combine("sub", "b.txt"))), "The folder walked incrementally is carried forward");
        Assert.That(paths, Does.Not.Contain(P(Path.Combine("other", "d.txt"))), "The folder listed in full is not carried forward");
    }
}
