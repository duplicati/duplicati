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
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.IPC;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The backup examines only the paths in a change list, supplied by option or by a
/// provider, and copies everything else from the previous fileset
/// </summary>
[NonParallelizable]
public class ChangedFilesListTests : BasicSetupHelper
{
    private sealed class FakeProvider : IChangeListProvider
    {
        private readonly Func<DateTime, ChangeListHandoff?> _drain;

        public FakeProvider(Func<DateTime, ChangeListHandoff?> drain)
        {
            _drain = drain;
        }

        public int Calls;
        public DateTime? ReferenceTimeUtc;

        public Task<ChangeListHandoff?> DrainChangeListAsync(DateTime referenceTimeUtc, CancellationToken cancellationToken)
        {
            Calls++;
            ReferenceTimeUtc = referenceTimeUtc;
            return Task.FromResult(_drain(referenceTimeUtc));
        }
    }

    /// <summary>A provider that hands over no list and starts a recording with the given token</summary>
    private static FakeProvider FullScan(string token)
        => new(_ => new ChangeListHandoff(null, token, []));

    /// <summary>A provider that hands over a list relative to the given token</summary>
    private FakeProvider Incremental(string baseToken, string token, params string[] lines)
        => new(_ => new ChangeListHandoff(WriteList(lines), token, [baseToken]));

    private static void AssertNoErrors(IBasicResults results)
        => Assert.That(results.Errors, Is.Empty, string.Join("; ", results.Errors));

    private string Sub => Path.Combine(DATAFOLDER, "sub");
    private string ListFile => Path.Combine(BASEFOLDER, "changes.txt");

    private Dictionary<string, string> Options()
        => new(TestOptions) { ["no-encryption"] = "true" };

    private Dictionary<string, string> OptionsWithList()
        => new(Options()) { ["changed-files-list"] = ListFile };

    private void CreateTree()
    {
        Directory.CreateDirectory(Sub);
        File.WriteAllText(Path.Combine(DATAFOLDER, "a.txt"), "A");
        File.WriteAllText(Path.Combine(DATAFOLDER, "b.txt"), "B");
        File.WriteAllText(Path.Combine(Sub, "c.txt"), "C");
        File.WriteAllText(Path.Combine(Sub, "d.txt"), "D");
    }

    private static void Modify(string path, string content)
    {
        var previous = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, content);
        File.SetLastWriteTimeUtc(path, previous.AddSeconds(2));
    }

    private string WriteList(params string[] lines)
    {
        File.WriteAllText(ListFile, string.Join("\n", lines) + "\n");
        return ListFile;
    }

    private async Task<IBackupResults> BackupAsync(Dictionary<string, string> options, IChangeListProvider? provider, Library.Utility.IFilter? filter)
    {
        using var c = new Controller("file://" + TARGETFOLDER, options, null);
        if (provider != null)
            await c.SetChangeListProviderAsync(provider);
        return await c.BackupAsync([DATAFOLDER], filter);
    }

    private async Task<HashSet<string>> ListPathsAsync()
    {
        using var c = new Controller("file://" + TARGETFOLDER, Options().Expand(new { version = 0 }), null);
        var r = await c.ListAsync("*");
        TestUtils.AssertResults(r);
        return r.Files.Select(x => x.Path).ToHashSet(StringComparer.Ordinal);
    }

    private async Task<Dictionary<string, string>> RestoreAsync()
    {
        if (Directory.Exists(RESTOREFOLDER))
            Directory.Delete(RESTOREFOLDER, true);
        Directory.CreateDirectory(RESTOREFOLDER);

        using var c = new Controller("file://" + TARGETFOLDER, Options().Expand(new { version = 0, restore_path = RESTOREFOLDER }), null);
        TestUtils.AssertResults(await c.RestoreAsync(["*"]));

        return Directory.EnumerateFiles(RESTOREFOLDER, "*", SearchOption.AllDirectories)
            .ToDictionary(x => Path.GetRelativePath(RESTOREFOLDER, x), x => File.ReadAllText(x), StringComparer.Ordinal);
    }

    [Test]
    [Category("ChangeList")]
    public async Task ModifiedFile_IsReread_OthersCarriedForwardAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Modify(Path.Combine(DATAFOLDER, "b.txt"), "B2");
        WriteList("E" + Path.Combine(DATAFOLDER, "b.txt"));
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "b.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "d.txt")));
        Assert.That(paths, Does.Contain(Util.AppendDirSeparator(Sub)));

        var restored = await RestoreAsync();
        Assert.That(restored["b.txt"], Is.EqualTo("B2"));
        Assert.That(restored["a.txt"], Is.EqualTo("A"));
        Assert.That(restored[Path.Combine("sub", "c.txt")], Is.EqualTo("C"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task DeletedFile_DropsOutAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        File.Delete(Path.Combine(DATAFOLDER, "b.txt"));
        WriteList("E" + Path.Combine(DATAFOLDER, "b.txt"));
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(Path.Combine(DATAFOLDER, "b.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task DeletedFolder_SubtreeDropsOutAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Directory.Delete(Sub, true);
        WriteList("R" + Sub);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(Util.AppendDirSeparator(Sub)));
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "d.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "b.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task RenamedFolder_MovesContentAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        var sub2 = Path.Combine(DATAFOLDER, "sub2");
        Directory.Move(Sub, sub2);
        WriteList("R" + Sub, "R" + sub2);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(Util.AppendDirSeparator(Sub)));
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Contain(Util.AppendDirSeparator(sub2)));
        Assert.That(paths, Does.Contain(Path.Combine(sub2, "c.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(sub2, "d.txt")));

        var restored = await RestoreAsync();
        Assert.That(restored[Path.Combine("sub2", "c.txt")], Is.EqualTo("C"));
        Assert.That(restored.ContainsKey(Path.Combine("sub", "c.txt")), Is.False);
    }

    [Test]
    [Category("ChangeList")]
    public async Task NewFolder_IsRecursedAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        var created = Path.Combine(DATAFOLDER, "new");
        Directory.CreateDirectory(Path.Combine(created, "deeper"));
        File.WriteAllText(Path.Combine(created, "x.txt"), "X");
        File.WriteAllText(Path.Combine(created, "deeper", "y.txt"), "Y");
        WriteList("R" + created);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Path.Combine(created, "x.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(created, "deeper", "y.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task ExaminedFolder_IsNotRecursedAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        // The file is gone on disk, but the folder is only examined, so it is carried forward
        File.Delete(Path.Combine(Sub, "c.txt"));
        WriteList("E" + Sub);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Util.AppendDirSeparator(Sub)));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "d.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task ChangedFileUnderExcludedFolder_StaysExcludedAsync()
    {
        CreateTree();
        var filter = new Library.Utility.FilterExpression(Util.AppendDirSeparator(Sub), false);
        TestUtils.AssertResults(await BackupAsync(Options(), null, filter));
        Assert.That(await ListPathsAsync(), Does.Not.Contain(Path.Combine(Sub, "c.txt")));

        Modify(Path.Combine(Sub, "c.txt"), "C2");
        WriteList("E" + Path.Combine(Sub, "c.txt"));
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, filter));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task PathOutsideSources_IsIgnoredAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        var outside = Path.Combine(BASEFOLDER, "outside.txt");
        File.WriteAllText(outside, "O");
        WriteList("E" + outside);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(outside));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task BadLine_FailsRunAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        WriteList("X" + Path.Combine(DATAFOLDER, "a.txt"));
        Assert.CatchAsync<Exception>(async () =>
        {
            var r = await BackupAsync(OptionsWithList(), null, null);
            if (r.Errors.Any())
                throw new Exception(string.Join("; ", r.Errors));
        });
    }

    [Test]
    [Category("ChangeList")]
    public async Task MissingListFile_FailsRunAsync()
    {
        CreateTree();
        var options = new Dictionary<string, string>(Options()) { ["changed-files-list"] = Path.Combine(BASEFOLDER, "does-not-exist.txt") };
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await BackupAsync(options, null, null));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangeListFileMissing"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task WithUsnPolicy_FailsRunAsync()
    {
        CreateTree();
        WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
        var options = new Dictionary<string, string>(OptionsWithList()) { ["usn-policy"] = "on" };
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await BackupAsync(options, null, null));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangeListWithUsn"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task WithInlineList_FailsRunAsync()
    {
        CreateTree();
        WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
        var options = new Dictionary<string, string>(OptionsWithList()) { ["changed-files"] = Path.Combine(DATAFOLDER, "a.txt") };
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await BackupAsync(options, null, null));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangedFilesListWithChangedFiles"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_IsCalledOnce_AndListIsUsedAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        // The folder is only examined, so a file deleted below it is carried forward,
        // which shows the list and not a full scan decided the fileset
        File.Delete(Path.Combine(Sub, "c.txt"));
        var started = DateTime.UtcNow;
        var provider = Incremental("T1", "T2", "E" + Sub);
        TestUtils.AssertResults(await BackupAsync(Options(), provider, null));

        Assert.That(provider.Calls, Is.EqualTo(1));
        Assert.That(provider.ReferenceTimeUtc, Is.Not.Null);
        Assert.That(provider.ReferenceTimeUtc!.Value, Is.GreaterThanOrEqualTo(started.AddSeconds(-1)));
        Assert.That(provider.ReferenceTimeUtc!.Value, Is.LessThanOrEqualTo(DateTime.UtcNow));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_IsCalledBeforeEnumerationAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        // A file created by the drain is only in the fileset if the drain ran before the scan
        var late = Path.Combine(DATAFOLDER, "late.txt");
        var provider = new FakeProvider(_ =>
        {
            File.WriteAllText(late, "L");
            return null;
        });
        TestUtils.AssertResults(await BackupAsync(Options(), provider, null));

        Assert.That(provider.Calls, Is.EqualTo(1));
        Assert.That(await ListPathsAsync(), Does.Contain(late));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_ReturnsNull_ScansAllAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Modify(Path.Combine(DATAFOLDER, "b.txt"), "B2");
        var provider = new FakeProvider(_ => null);
        TestUtils.AssertResults(await BackupAsync(Options(), provider, null));

        Assert.That(provider.Calls, Is.EqualTo(1));
        Assert.That((await RestoreAsync())["b.txt"], Is.EqualTo("B2"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_Throws_WarnsAndScansAllAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Modify(Path.Combine(DATAFOLDER, "b.txt"), "B2");
        var provider = new FakeProvider(_ => throw new InvalidOperationException("boom"));
        var results = await BackupAsync(Options(), provider, null);

        Assert.That(results.Errors.Count(), Is.EqualTo(0));
        Assert.That(results.Warnings.Any(x => x.Contains("ChangeListDrainFailed")), Is.True, string.Join("; ", results.Warnings));
        Assert.That((await RestoreAsync())["b.txt"], Is.EqualTo("B2"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_WithOptionList_FailsBeforeCallAsync()
    {
        CreateTree();
        WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
        var provider = Incremental("T1", "T2", "E" + Path.Combine(DATAFOLDER, "a.txt"));
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await BackupAsync(OptionsWithList(), provider, null));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangeListOptionWithProvider"));
        Assert.That(provider.Calls, Is.EqualTo(0));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_ListAgainstOtherFileset_ScansAllAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        // A list recorded against another fileset is not trusted, so the deleted file,
        // which the list does not name, is still noticed
        File.Delete(Path.Combine(Sub, "c.txt"));
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("OTHER", "T2", "E" + Sub), null));
        Assert.That(await ListPathsAsync(), Does.Not.Contain(Path.Combine(Sub, "c.txt")));

        // The full scan stored T2, so a list against it is used again
        File.Delete(Path.Combine(Sub, "d.txt"));
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T2", "T3", "E" + Sub), null));
        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(Sub, "d.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_WithoutStoredToken_ScansAllAsync()
    {
        CreateTree();
        // The first backup had no provider, so no token is stored with it
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        File.Delete(Path.Combine(Sub, "c.txt"));
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T1", "T2", "E" + Sub), null));
        Assert.That(await ListPathsAsync(), Does.Not.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task FolderAndChildListed_AreProcessedOnceAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        var created = Path.Combine(DATAFOLDER, "new");
        Directory.CreateDirectory(created);
        File.WriteAllText(Path.Combine(created, "x.txt"), "X");
        WriteList("E" + Path.Combine(created, "x.txt"), "R" + created, "E" + Path.Combine(created, "x.txt"), "E" + Path.Combine(DATAFOLDER, "a.txt"), "E" + Path.Combine(DATAFOLDER, "a.txt"));
        var results = await BackupAsync(OptionsWithList(), null, null);
        TestUtils.AssertResults(results);

        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(created, "x.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task DeletedFolder_DropsChildrenWithSupplementaryCharactersAsync()
    {
        CreateTree();
        var party = Path.Combine(Sub, "\U0001F389party.txt");
        File.WriteAllText(party, "P");
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));
        Assert.That(await ListPathsAsync(), Does.Contain(party));

        Directory.Delete(Sub, true);
        WriteList("R" + Sub);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(party));
        Assert.That(paths.Where(x => x.StartsWith(Sub, StringComparison.Ordinal)), Is.Empty);
    }

    [Test]
    [Category("ChangeList")]
    public async Task SourceRootMatchingExclude_IsNotFilteredAsync()
    {
        CreateTree();
        // A full scan never filters the source root itself
        var filter = new Library.Utility.FilterExpression(Util.AppendDirSeparator(DATAFOLDER), false);
        TestUtils.AssertResults(await BackupAsync(Options(), null, filter));
        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));

        Modify(Path.Combine(DATAFOLDER, "a.txt"), "A2");
        WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, filter));

        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
        Assert.That((await RestoreAsync())["a.txt"], Is.EqualTo("A2"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task IgnoreMarkerRemoved_AddsFolderContentAsync()
    {
        CreateTree();
        var marker = Path.Combine(Sub, "CACHEDIR.TAG");
        File.WriteAllText(marker, "");
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));
        Assert.That(await ListPathsAsync(), Does.Not.Contain(Path.Combine(Sub, "c.txt")));

        File.Delete(marker);
        WriteList("R" + marker);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Contain(Path.Combine(Sub, "d.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task IgnoreMarkerAdded_DropsFolderContentAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));
        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(Sub, "c.txt")));

        var marker = Path.Combine(Sub, "CACHEDIR.TAG");
        File.WriteAllText(marker, "");
        WriteList("E" + marker);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Not.Contain(Util.AppendDirSeparator(Sub)));
        Assert.That(paths, Does.Contain(Path.Combine(DATAFOLDER, "a.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task CaseOnlyRename_KeepsTheSpellingOnDiskAsync()
    {
        if (Library.Utility.Utility.IsFSCaseSensitive)
        {
            Assert.Ignore("The file system is case-sensitive");
            return;
        }

        CreateTree();
        var oldName = Path.Combine(DATAFOLDER, "Report.txt");
        var newName = Path.Combine(DATAFOLDER, "report.txt");
        File.WriteAllText(oldName, "R");
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        File.Move(oldName, Path.Combine(DATAFOLDER, "tmp.txt"));
        File.Move(Path.Combine(DATAFOLDER, "tmp.txt"), newName);
        WriteList("R" + oldName, "E" + newName);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(newName));
        Assert.That(paths, Does.Not.Contain(oldName));
    }

    [Test]
    [Category("ChangeList")]
    public async Task FailedFile_IsRetriedByTheNextListAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses POSIX permissions to make a file unreadable");
            return;
        }

        CreateTree();
        var locked = Path.Combine(DATAFOLDER, "locked.txt");
        File.WriteAllText(locked, "L");
        File.SetUnixFileMode(locked, UnixFileMode.None);
        try
        {
            var results = await BackupAsync(Options(), FullScan("T1"), null);
            AssertNoErrors(results);
            Assert.That(results.Warnings, Is.Not.Empty, "the unreadable file should cause a warning");
            Assert.That(await ListPathsAsync(), Does.Not.Contain(locked));
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        // The list does not name the file, but the previous run failed on it
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T1", "T2", "E" + Path.Combine(DATAFOLDER, "a.txt")), null));
        Assert.That(await ListPathsAsync(), Does.Contain(locked));
        Assert.That((await RestoreAsync())["locked.txt"], Is.EqualTo("L"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task FolderReplacedByFile_DropsOldChildrenAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Directory.Delete(Sub, true);
        File.WriteAllText(Sub, "now a file");
        // A watcher also reports the deleted children; they are gone, not hidden, so no warning
        WriteList("R" + Sub, "R" + Path.Combine(Sub, "c.txt"), "R" + Path.Combine(Sub, "d.txt"));
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Sub));
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
        Assert.That(paths, Does.Not.Contain(Util.AppendDirSeparator(Sub)));
    }

    [Test]
    [Category("ChangeList")]
    public async Task NonAsciiCaseOnlyRename_KeepsOneSpellingAsync()
    {
        if (Library.Utility.Utility.IsFSCaseSensitive)
        {
            Assert.Ignore("The file system is case-sensitive");
            return;
        }

        CreateTree();
        var upper = Path.Combine(DATAFOLDER, "\u00C4rger");
        var lower = Path.Combine(DATAFOLDER, "\u00E4rger");
        Directory.CreateDirectory(upper);
        File.WriteAllText(Path.Combine(upper, "x.txt"), "X");
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Directory.Move(upper, Path.Combine(DATAFOLDER, "tmp"));
        Directory.Move(Path.Combine(DATAFOLDER, "tmp"), lower);
        WriteList("R" + upper, "R" + lower);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths.Count(x => x.EndsWith("x.txt", StringComparison.Ordinal)), Is.EqualTo(1), string.Join(", ", paths));
        Assert.That(paths, Does.Contain(Path.Combine(lower, "x.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_ListWithPathOutsideSources_ScansAllAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        // A path outside the sources shows the list was recorded under another spelling of them
        File.Delete(Path.Combine(Sub, "c.txt"));
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T1", "T2", "E" + Sub, "E" + Path.Combine(BASEFOLDER, "elsewhere.txt")), null));
        Assert.That(await ListPathsAsync(), Does.Not.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task Provider_ListedHardlink_ScansAllAsync()
    {
        CreateTree();
        var other = Path.Combine(Sub, "other-link.txt");
        await TestFileLinks.CreateHardLinkAsync(Path.Combine(DATAFOLDER, "a.txt"), other);
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        // Written through one link; the other is not listed but has changed too
        File.AppendAllText(Path.Combine(DATAFOLDER, "a.txt"), "-changed");
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T1", "T2", "E" + Path.Combine(DATAFOLDER, "a.txt")), null));

        var restored = await RestoreAsync();
        Assert.That(restored[Path.Combine("sub", "other-link.txt")], Is.EqualTo("A-changed"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task WithExcludeEmptyFolders_FailsRunAsync()
    {
        CreateTree();
        WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
        var options = new Dictionary<string, string>(OptionsWithList()) { ["exclude-empty-folders"] = "true" };
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await BackupAsync(options, null, null));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangeListNotSupported"));
    }

    [Test]
    [Category("ChangeList")]
    public void SettingsHash_IgnoresPerRunOptions()
    {
        var options = new Dictionary<string, string?> { ["passphrase"] = "x", ["control-files"] = "/tmp/a/task-setup.json", ["next-scheduled-run"] = "1" };
        var again = new Dictionary<string, string?> { ["passphrase"] = "x", ["control-files"] = "/tmp/b/task-setup.json", ["next-scheduled-run"] = "2" };
        var changed = new Dictionary<string, string?> { ["passphrase"] = "x", ["control-files"] = "/tmp/b/task-setup.json", ["symlink-policy"] = "store" };

        var hash = Server.Runner.ComputeChangeTrackingHash([DATAFOLDER], null, options);
        Assert.That(Server.Runner.ComputeChangeTrackingHash([DATAFOLDER], null, again), Is.EqualTo(hash));
        Assert.That(Server.Runner.ComputeChangeTrackingHash([DATAFOLDER], null, changed), Is.Not.EqualTo(hash));
    }

    [Test]
    [Category("ChangeList")]
    public async Task ChangeInsidePhotosLibrary_ScansAllAsync()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Ignore("Photos libraries are only expanded on macOS");
            return;
        }

        CreateTree();
        var library = Path.Combine(DATAFOLDER, "Test.photoslibrary");
        Directory.CreateDirectory(library);
        File.WriteAllText(Path.Combine(library, "database.db"), "D");
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        // The list names a path inside the library, so it is not used, and the deleted
        // file below an examined folder is noticed by the full scan
        File.Delete(Path.Combine(Sub, "c.txt"));
        File.WriteAllText(Path.Combine(library, "database.db"), "D2");
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T1", "T2", "E" + Sub, "E" + Path.Combine(library, "database.db")), null));
        Assert.That(await ListPathsAsync(), Does.Not.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task WithFollowedSymlinks_FailsRunAsync()
    {
        CreateTree();
        WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
        var options = new Dictionary<string, string>(OptionsWithList()) { ["symlink-policy"] = "follow" };
        var ex = Assert.ThrowsAsync<UserInformationException>(async () => await BackupAsync(options, null, null));
        Assert.That(ex!.HelpID, Is.EqualTo("ChangeListNotSupported"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task FileInUnreadableFolder_IsRetriedAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses POSIX permissions to make a folder unreadable");
            return;
        }

        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        // The file changes, then its folder becomes unreadable, so it cannot be told from a deleted file
        Modify(Path.Combine(Sub, "c.txt"), "C2");
        File.SetUnixFileMode(Sub, UnixFileMode.None);
        try
        {
            var results = await BackupAsync(Options(), Incremental("T1", "T2", "E" + Path.Combine(Sub, "c.txt")), null);
            AssertNoErrors(results);
            Assert.That(results.Warnings.Any(x => x.Contains("ChangeListPathNotAccessible")), Is.True, string.Join("; ", results.Warnings));
        }
        finally
        {
            File.SetUnixFileMode(Sub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        // Readable again, and only the folder is reported; the file is retried
        TestUtils.AssertResults(await BackupAsync(Options(), Incremental("T2", "T3", "E" + Sub), null));
        Assert.That((await RestoreAsync())[Path.Combine("sub", "c.txt")], Is.EqualTo("C2"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task MissingSourceRoot_DropsItsContentAsync()
    {
        CreateTree();
        var second = Path.Combine(BASEFOLDER, "second-source");
        Directory.CreateDirectory(second);
        File.WriteAllText(Path.Combine(second, "s.txt"), "S");
        try
        {
            using (var c = new Controller("file://" + TARGETFOLDER, Options(), null))
                TestUtils.AssertResults(await c.BackupAsync([DATAFOLDER, second]));

            // The second source disappears; a full scan would leave it out
            Directory.Delete(second, true);
            WriteList("E" + Path.Combine(DATAFOLDER, "a.txt"));
            using (var c = new Controller("file://" + TARGETFOLDER, OptionsWithList(), null))
            {
                var results = await c.BackupAsync([DATAFOLDER, second]);
                AssertNoErrors(results);
            }

            var paths = await ListPathsAsync();
            Assert.That(paths, Does.Not.Contain(Path.Combine(second, "s.txt")));
            Assert.That(paths, Does.Contain(Path.Combine(Sub, "c.txt")));
        }
        finally
        {
            if (Directory.Exists(second))
                Directory.Delete(second, true);
        }
    }

    [Test]
    [Category("ChangeList")]
    public async Task FolderReplacedByFileListedOnlyAsChanged_DropsOldChildrenAsync()
    {
        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        Directory.Delete(Sub, true);
        File.WriteAllText(Sub, "now a file");
        WriteList("E" + Sub);
        TestUtils.AssertResults(await BackupAsync(OptionsWithList(), null, null));

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Contain(Sub));
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task NestedSourceBelowExcludedFolder_UsesTheInnerRootAsync()
    {
        CreateTree();
        var inner = Path.Combine(Sub, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "x.txt"), "X");
        var filter = new Library.Utility.FilterExpression(Util.AppendDirSeparator(Sub), false);
        string[] sources = [DATAFOLDER, Util.AppendDirSeparator(inner)];

        using (var c = new Controller("file://" + TARGETFOLDER, Options(), null))
            TestUtils.AssertResults(await c.BackupAsync(sources, filter));
        if (!(await ListPathsAsync()).Contains(Path.Combine(inner, "x.txt")))
        {
            Assert.Ignore("The nested source is not kept below an excluded folder in this configuration");
            return;
        }

        Modify(Path.Combine(inner, "x.txt"), "X2");
        WriteList("E" + Path.Combine(inner, "x.txt"));
        using (var c = new Controller("file://" + TARGETFOLDER, OptionsWithList(), null))
            TestUtils.AssertResults(await c.BackupAsync(sources, filter));

        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(inner, "x.txt")));
        Assert.That((await RestoreAsync()).Single(x => x.Key.EndsWith("x.txt", StringComparison.Ordinal)).Value, Is.EqualTo("X2"));
    }

    [Test]
    [Category("ChangeList")]
    public async Task ChangeInExcludedFolderHoldingNestedSource_KeepsTheNestedSourceAsync()
    {
        CreateTree();
        var inner = Path.Combine(Sub, "inner");
        Directory.CreateDirectory(inner);
        File.WriteAllText(Path.Combine(inner, "x.txt"), "X");
        var filter = new Library.Utility.FilterExpression(Util.AppendDirSeparator(Sub), false);
        string[] sources = [DATAFOLDER, Util.AppendDirSeparator(inner)];

        using (var c = new Controller("file://" + TARGETFOLDER, Options(), null))
            TestUtils.AssertResults(await c.BackupAsync(sources, filter));
        if (!(await ListPathsAsync()).Contains(Path.Combine(inner, "x.txt")))
        {
            Assert.Ignore("The nested source is not kept below an excluded folder in this configuration");
            return;
        }

        // A change directly in the excluded folder, which also holds the nested source
        Modify(Path.Combine(Sub, "c.txt"), "C2");
        WriteList("E" + Path.Combine(Sub, "c.txt"));
        using (var c = new Controller("file://" + TARGETFOLDER, OptionsWithList(), null))
            TestUtils.AssertResults(await c.BackupAsync(sources, filter));

        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(inner, "x.txt")));
    }

    [Test]
    [Category("ChangeList")]
    public async Task FolderBecomingUnreadable_DropsItsChildrenAsAFullScanDoesAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Ignore("Uses POSIX permissions to make a folder unreadable");
            return;
        }

        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), null, null));

        File.SetUnixFileMode(Sub, UnixFileMode.None);
        try
        {
            WriteList("E" + Sub);
            var results = await BackupAsync(OptionsWithList(), null, null);
            AssertNoErrors(results);
            Assert.That(results.Warnings, Is.Not.Empty, "the unreadable folder is reported");
        }
        finally
        {
            File.SetUnixFileMode(Sub, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var paths = await ListPathsAsync();
        Assert.That(paths, Does.Not.Contain(Path.Combine(Sub, "c.txt")));
    }

    [Test]
    [Category("IPC")]
    public async Task Provider_RoundTripsThroughOutOfProcessControllerAsync()
    {
        var exePath = typeof(ControllerRpcProxy)
            .GetMethod("GetControllerExecutablePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?.Invoke(null, null) as string;

        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            Assert.Ignore($"Controller executable not found at '{exePath}', skipping IPC test.");
            return;
        }

        CreateTree();
        TestUtils.AssertResults(await BackupAsync(Options(), FullScan("T1"), null));

        File.Delete(Path.Combine(Sub, "c.txt"));
        var provider = Incremental("T1", "T2", "E" + Sub);

        var options = new Dictionary<string, string?>(Options()!);
        IController controller = await ControllerRpcProxy.CreateProxyAsync("file://" + TARGETFOLDER, options, null!);
        try
        {
            await controller.SetChangeListProviderAsync(provider);
            var results = await controller.BackupAsync([DATAFOLDER]);
            Assert.That(results.Errors.Count(), Is.EqualTo(0), string.Join("; ", results.Errors));
        }
        finally
        {
            (controller as IDisposable)?.Dispose();
        }

        Assert.That(provider.Calls, Is.EqualTo(1));
        Assert.That(await ListPathsAsync(), Does.Contain(Path.Combine(Sub, "c.txt")));
    }
}
