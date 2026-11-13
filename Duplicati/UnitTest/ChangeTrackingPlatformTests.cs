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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.AutoUpdater;
using Duplicati.Library.ChangeTracking;
using Duplicati.Library.RestAPI.Database;
using Duplicati.Library.SQLiteHelper;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Creates hard links and reads short names for tests
/// </summary>
internal static class TestFileLinks
{
    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateHardLinkW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);

    [SupportedOSPlatform("windows")]
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetShortPathNameW")]
    private static extern uint GetShortPathName(string lpszLongPath, StringBuilder lpszShortPath, uint cchBuffer);

    /// <summary>
    /// Creates a hard link to an existing file
    /// </summary>
    /// <param name="existing">The existing file</param>
    /// <param name="link">The new link</param>
    public static async Task CreateHardLinkAsync(string existing, string link)
    {
        if (OperatingSystem.IsWindows())
        {
            if (!CreateHardLink(link, existing, IntPtr.Zero))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Could not link {link} to {existing}");
            return;
        }

        using var ln = System.Diagnostics.Process.Start("ln", [existing, link]);
        await ln!.WaitForExitAsync();
        if (ln.ExitCode != 0)
            throw new IOException($"ln exited with {ln.ExitCode} linking {link} to {existing}");
    }

    /// <summary>
    /// Gets the short 8.3 form of an existing path
    /// </summary>
    /// <param name="path">The path</param>
    /// <returns>The short form, which is the path itself when the volume has no short names</returns>
    [SupportedOSPlatform("windows")]
    public static string GetShortPath(string path)
    {
        var buffer = new StringBuilder(1024);
        var length = GetShortPathName(path, buffer, (uint)buffer.Capacity);
        if (length == 0 || length > buffer.Capacity)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), $"Could not get the short name of {path}");
        return buffer.ToString();
    }
}

/// <summary>
/// Covers the platform-specific parts of the file watcher, so that each CI platform runs
/// the code written for it, and the server rules that decide which backups are watched
/// </summary>
[NonParallelizable]
public class ChangeTrackingPlatformTests : BasicSetupHelper
{
    private string Folder => Path.Combine(BASEFOLDER, "platform");

    [SetUp]
    public void CreateFolder()
    {
        if (Directory.Exists(Folder))
            Directory.Delete(Folder, true);
        Directory.CreateDirectory(Folder);
    }

    [Test]
    [Category("ChangeTracking")]
    public void ShortNames_AreRecognized()
    {
        Assert.That(WindowsShortNames.LooksShort(@"C:\PROGRA~1\app.exe"), Is.True);
        Assert.That(WindowsShortNames.LooksShort(@"C:\Users\RUNNER~1"), Is.True);
        Assert.That(WindowsShortNames.LooksShort(@"C:\data\LONGFI~12.TXT"), Is.True);
        Assert.That(WindowsShortNames.LooksShort(@"C:\data\a~b.txt"), Is.False);
        Assert.That(WindowsShortNames.LooksShort(@"C:\data\file~1.text"), Is.False);
        Assert.That(WindowsShortNames.LooksShort(@"C:\data\ABCDEFG~1"), Is.False);
        Assert.That(WindowsShortNames.LooksShort(@"C:\data\plain.txt"), Is.False);
    }

    [Test]
    [Category("ChangeTracking")]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void ShortNames_ExpandToTheStoredName()
    {
        var folder = Path.Combine(Folder, "Long Folder Name For Test");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "Long File Name For Test.txt");
        File.WriteAllText(file, "x");

        var shortPath = TestFileLinks.GetShortPath(file);
        if (string.Equals(shortPath, file, StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("The volume does not create short names");

        Assert.That(WindowsShortNames.LooksShort(shortPath), Is.True, shortPath);
        var expanded = WindowsShortNames.TryExpand(shortPath);
        Assert.That(expanded, Is.Not.Null);
        Assert.That(WindowsShortNames.LooksShort(expanded!), Is.False, expanded);
        Assert.That(expanded, Does.EndWith(@"\Long Folder Name For Test\Long File Name For Test.txt"));
        Assert.That(File.Exists(expanded), Is.True);

        Assert.That(WindowsShortNames.TryExpand(Path.Combine(folder, "MISSIN~1.TXT")), Is.Null);
    }

    [Test]
    [Category("FileSystemWatcher")]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public async Task RealWatcher_OnAShortRootReportsLongNamesAsync()
    {
        var folder = Path.Combine(Folder, "Long Watched Folder Name");
        Directory.CreateDirectory(folder);
        var shortRoot = TestFileLinks.GetShortPath(folder);
        if (string.Equals(shortRoot, folder, StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("The volume does not create short names");

        var observed = new ConcurrentQueue<string>();
        var errors = new ConcurrentQueue<Exception>();
        using var source = new FileSystemWatcherEventSource(shortRoot);
        source.Changed += (path, kind) => observed.Enqueue(path);
        source.Error += ex => errors.Enqueue(ex);
        source.Start();

        var file = Path.Combine(folder, "Another Long File Name.txt");
        await File.WriteAllTextAsync(file, "x");

        var suffix = @"\Long Watched Folder Name\Another Long File Name.txt";
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && !observed.Any(x => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
            await Task.Delay(100);

        Assert.That(observed.Any(x => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)), Is.True, string.Join(", ", observed));
        Assert.That(observed.Where(WindowsShortNames.LooksShort), Is.Empty);
        Assert.That(errors, Is.Empty);
    }

    [Test]
    [Category("ChangeTracking")]
    public void RootIdentity_ChangesWhenTheFolderIsSwapped()
    {
        var root = Path.Combine(Folder, "root");
        var replacement = Path.Combine(Folder, "replacement");
        var moved = Path.Combine(Folder, "moved");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(replacement);

        var original = RootIdentity.Get(root);
        Assert.That(original, Is.Not.Null.And.Not.Empty);
        Assert.That(RootIdentity.Get(root), Is.EqualTo(original), "stable while the folder exists");
        Assert.That(RootIdentity.Get(replacement), Is.Not.EqualTo(original));

        // A tool that swaps a folder by renaming leaves another folder at the path
        Directory.Move(root, moved);
        Directory.Move(replacement, root);
        Assert.That(RootIdentity.Get(root), Is.Not.EqualTo(original));
        Assert.That(RootIdentity.Get(moved), Is.EqualTo(original));

        Assert.That(RootIdentity.Get(Path.Combine(Folder, "missing")), Is.Null);
    }

    [Test]
    [Category("ChangeTracking")]
    public void RootIdentity_WatchesTheFolderOfASingleFileRoot()
    {
        var file = Path.Combine(Folder, "single.txt");
        File.WriteAllText(file, "x");
        Assert.That(RootIdentity.GetWatchedFolder(file), Is.EqualTo(Folder));
        Assert.That(RootIdentity.GetWatchedFolder(Folder), Is.EqualTo(Folder));
    }

    [Test]
    [Category("ChangeTracking")]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void MountPoints_IncludeTheSystemDrive()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory)!;
        var mounts = WindowsVolumeMountPoints.GetMountPaths();

        Assert.That(mounts, Has.Some.EqualTo(systemRoot).IgnoreCase, string.Join(", ", mounts));
        Assert.That(mounts.All(x => x.EndsWith('\\')), Is.True, string.Join(", ", mounts));
    }

    [Test]
    [Category("ChangeTracking")]
    public void MountPoints_BelowARootExcludeTheRootItself()
    {
        var root = Path.GetPathRoot(Folder)!;
        var below = WatchedBackup.DiscoverMountPoints([root]).ToList();

        // Only Windows needs watchers for volumes mounted below a root
        if (!OperatingSystem.IsWindows())
        {
            Assert.That(below, Is.Empty);
            return;
        }

        Assert.That(below, Has.None.EqualTo(root).IgnoreCase);
        Assert.That(below.All(x => x.StartsWith(root, StringComparison.OrdinalIgnoreCase) && x.Length > root.Length), Is.True, string.Join(", ", below));
    }

    [Test]
    [Category("ChangeTracking")]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public async Task LinkCount_CountsHardLinksAsync()
    {
        var file = Path.Combine(Folder, "linked.txt");
        File.WriteAllText(file, "x");
        Assert.That(Library.Main.Operation.Backup.WindowsLinkCount.Get(file), Is.EqualTo(1));

        var link = Path.Combine(Folder, "other-link.txt");
        await TestFileLinks.CreateHardLinkAsync(file, link);
        Assert.That(Library.Main.Operation.Backup.WindowsLinkCount.Get(file), Is.EqualTo(2));
        Assert.That(Library.Main.Operation.Backup.WindowsLinkCount.Get(link), Is.EqualTo(2));

        // A file that another program holds open is still checked
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.That(Library.Main.Operation.Backup.WindowsLinkCount.Get(link), Is.EqualTo(2));

        Assert.Throws<IOException>(() => Library.Main.Operation.Backup.WindowsLinkCount.Get(Path.Combine(Folder, "missing.txt")));
    }

    [Test]
    [Category("ChangeTracking")]
    [Platform("Win")]
    [SupportedOSPlatform("windows")]
    public void LinkCount_ReadsLongPaths()
    {
        var folder = Folder;
        while (folder.Length < 300)
            folder = Path.Combine(folder, "a-folder-name-that-makes-the-path-long");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "deep.txt");
        File.WriteAllText(file, "x");

        Assert.That(file.Length, Is.GreaterThan(260));
        Assert.That(Library.Main.Operation.Backup.WindowsLinkCount.Get(file), Is.EqualTo(1));
    }

    [Test]
    [Category("ChangeTracking")]
    public void WatchedPath_IsTheFullPath()
    {
        var messy = Path.Combine(Folder, "a", "..", "b");
        Assert.That(Server.Runner.GetWatchedPath(messy), Is.EqualTo(Path.Combine(Folder, "b")));
    }

    [Test]
    [Category("ChangeTracking")]
    [Platform("Win")]
    public void WatchedPath_OfABareDriveIsTheDriveRoot()
    {
        // A bare drive means the current folder of the drive to Windows, but the root to a backup
        var drive = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        Assert.That(Server.Runner.GetWatchedPath(drive), Is.EqualTo(drive + "\\").IgnoreCase);
    }

    private static IBackup BackupWithSettings(params (string Name, string Value)[] settings)
        => new Server.Database.Backup
        {
            ID = "1",
            Name = "watched",
            Settings = settings.Select(x => (ISetting)new Setting { Name = x.Name, Value = x.Value, Filter = string.Empty }).ToArray(),
        };

    [Test]
    [Category("ChangeTracking")]
    public void FileWatcherFlag_IsOptInPerBackup()
    {
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings()), Is.False, "off unless enabled");
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings(("--" + Server.Runner.FILE_WATCHER_OPTION, "true"))), Is.True);
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings(("--" + Server.Runner.FILE_WATCHER_OPTION, ""))), Is.True, "a flag without a value is set");
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings((Server.Runner.FILE_WATCHER_OPTION, "true"))), Is.True);
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings(("--" + Server.Runner.FILE_WATCHER_OPTION, "false"))), Is.False);
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings((Server.Runner.FILE_WATCHER_OPTION, "true"), ("--" + Server.Runner.FILE_WATCHER_OPTION, "false"))), Is.False, "the override wins");
        Assert.That(Server.Runner.IsFileWatcherEnabled(BackupWithSettings(("--other", "true"))), Is.False);
    }

    [Test]
    [Category("ChangeTracking")]
    public void ChangeListSupport_IsDecidedByTheSharedRules()
    {
        static string? Reason(params (string Key, string Value)[] options)
            => new Library.Main.Options(options.ToDictionary(x => x.Key, x => (string?)x.Value)).GetChangeListUnsupportedReason();

        Assert.That(Reason(), Is.Null);
        Assert.That(Reason(("usn-policy", "off"), ("hardlink-policy", "all"), ("symlink-policy", "store")), Is.Null);
        Assert.That(Reason(("exclude-empty-folders", "true")), Is.Not.Null);
        Assert.That(Reason(("hardlink-policy", "first")), Is.Not.Null);
        Assert.That(Reason(("symlink-policy", "follow")), Is.Not.Null);
        Assert.That(Reason(("enable-ads-backup", "true")), Is.Not.Null);
        Assert.That(Reason(("usn-policy", "auto")), Is.Not.Null);
    }

    [Test]
    [Category("ChangeTracking")]
    public void SettingsHash_IsSalted()
    {
        var options = new Dictionary<string, string?> { ["passphrase"] = "secret" };
        var hash = Server.Runner.ComputeChangeTrackingHash([Folder], null, options);

        // The same input without the key would give the plain digest, which can be tested against guesses
        var plain = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes($"source:{Folder}\noption:passphrase=secret\n")));
        Assert.That(hash, Is.Not.EqualTo(plain));
        Assert.That(Server.Runner.ComputeChangeTrackingHash([Folder], null, options), Is.EqualTo(hash), "stable within the process");
    }

    /// <summary>
    /// Records which backups the listener stops
    /// </summary>
    private sealed class RecordingChangeTracking : IChangeTrackingService
    {
        public readonly ConcurrentQueue<string> Stopped = new();
        public bool Enabled => true;
        public Task<Handoff> PrepareHandoffAsync(string backupId, IReadOnlyList<string> sources, string settingsHash, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task CompleteHandoffAsync(Handoff handoff, bool success, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<ChangeTrackingStatus>> GetStatusAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public Task<bool> StopAsync(string backupId, CancellationToken cancellationToken)
        {
            Stopped.Enqueue(backupId);
            return Task.FromResult(true);
        }

        public async Task WaitForAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (Stopped.Count < count && DateTime.UtcNow < deadline)
                await Task.Delay(20);
        }
    }

    [Test]
    [Category("ChangeTracking")]
    public async Task Listener_StopsTheWatcherWhenABackupIsSavedOrDeletedAsync()
    {
        var databasePath = Path.Combine(Folder, DataFolderManager.SERVER_DATABASE_FILENAME);
        var dbConnection = await SQLiteLoader.LoadConnectionAsync(databasePath);
        DatabaseUpgrader.UpgradeDatabase(dbConnection, databasePath, typeof(DatabaseSchemaMarker));
        using var connection = new Connection(dbConnection, true, null, Folder, () => { });

        var changeTracking = new RecordingChangeTracking();
        ChangeTrackingBackupListener.Attach(connection, changeTracking);

        var backup = new Server.Database.Backup
        {
            Name = "watched",
            Description = string.Empty,
            Tags = [],
            TargetURL = "file:///target",
            Sources = [DATAFOLDER],
            Settings = [new Setting { Name = "--" + Server.Runner.FILE_WATCHER_OPTION, Value = "true", Filter = string.Empty }],
            Filters = [],
            Metadata = new Dictionary<string, string>(),
        };

        // Saving any way, as the web UI, an import or the remote controller does, goes through the connection
        connection.AddOrUpdateBackupAndSchedule(backup, null);
        var id = backup.ID;
        Assert.That(id, Is.Not.Null.And.Not.Empty);
        await changeTracking.WaitForAsync(1);
        Assert.That(changeTracking.Stopped, Is.EqualTo(new[] { id }));

        // Deleting by id is what the remote controller does
        connection.DeleteBackup(long.Parse(id));
        await changeTracking.WaitForAsync(2);
        Assert.That(changeTracking.Stopped, Is.EqualTo(new[] { id, id }));
    }
}
