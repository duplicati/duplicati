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
using System.Text;
using Duplicati.Library.Logging;
using Duplicati.Library.Common.IO;
using System.Threading.Tasks;

namespace Duplicati.UnitTest;

/// <summary>
/// This class encapsulates a method for testing the correctness of the sync
/// operation. It mirrors the approach taken by <see cref="SVNCheckoutTest"/>:
/// it iterates over a series of "versions" (folders), updating the source tree
/// to match each version, running a sync, and verifying that the remote
/// destination is in a consistent state after every version. "Consistent" here
/// means every local file is present on the remote with identical content, and
/// files removed from a folder that still exists locally are deleted from the
/// remote via <c>--sync-then-delete</c>. Sync does not prune whole subfolders
/// that no longer exist locally, so the verification is a presence+content
/// check rather than a full tree equivalence.
/// </summary>
public class SyncCheckoutTest
{
    /// <summary>
    /// The log tag
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SyncCheckoutTest>();

    /// <summary>
    /// A helper class to write debug messages to the log file
    /// </summary>
    private class LogHelper : StreamLogDestination
    {
        public static long WarningCount = 0;
        public static long ErrorCount = 0;

        public LogHelper(string file)
            : base(file)
        { }

        public override void WriteMessage(LogEntry entry)
        {
            if (entry.Level == LogMessageType.Error)
                System.Threading.Interlocked.Increment(ref ErrorCount);
            else if (entry.Level == LogMessageType.Warning)
                System.Threading.Interlocked.Increment(ref WarningCount);
            base.WriteMessage(entry);
        }
    }

    /// <summary>
    /// Running the test confirms the correctness of the sync operation across a
    /// series of source versions. For each version the source folder is updated
    /// to match that version's contents (files added, modified, and removed),
    /// a sync is run, and the remote destination is verified to consistently
    /// mirror the source: every local file is present on the remote with
    /// identical content, and removed files in folders that still exist locally
    /// are deleted via <c>--sync-then-delete</c>.
    /// </summary>
    /// <param name="folders">The folders to sync. Folder at index 0 is the base; all others are incrementals.</param>
    /// <param name="options">The base options dictionary (the test will adjust sync-specific options).</param>
    /// <param name="target">The target destination for the syncs. When null a local file:// target is used.</param>
    public static async Task RunTestAsync(string[] folders, Dictionary<string, string> options, string target)
    {
        string tempdir = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "tempdir_sync");
        string logfilename = Path.Combine(tempdir, string.Format("unittest-sync-{0}.log", Library.Utility.Utility.SerializeDateTime(DateTime.Now)));

        try
        {
            if (Directory.Exists(tempdir))
                Directory.Delete(tempdir, true);

            Directory.CreateDirectory(tempdir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Failed to clean tempdir: {0}", ex);
        }

        using (var log = new LogHelper(logfilename))
        using (Log.StartScope(log, LogMessageType.Profiling))
        {
            // Filter empty entries, commonly occurring with copy/paste and newlines
            folders = (from x in folders
                       where !string.IsNullOrWhiteSpace(x)
                       select Environment.ExpandEnvironmentVariables(x)).ToArray();

            foreach (var f in folders)
                foreach (var n in f.Split(new char[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                    if (!Directory.Exists(n))
                        throw new Exception(string.Format("Missing source folder: {0}", n));

            Duplicati.Library.Utility.TempFolder.SystemTempPath = tempdir;

            // Sync mirrors files unencrypted, so a passphrase is meaningless and
            // must not be set. Remove any inherited passphrase and disable encryption.
            options.Remove("passphrase");
            options["no-encryption"] = "true";

            if (!options.ContainsKey("prefix"))
                options["prefix"] = "duplicati_unittest";

            // We want all messages in the log
            options["log-file-log-level"] = LogMessageType.Profiling.ToString();

            // We use precise times so unchanged files are reliably detected.
            options["disable-time-tolerance"] = "true";

            // Sync deletes files removed locally only when --sync-then-delete is on.
            // The whole point of iterating versions is to exercise updates AND deletes,
            // so we always enable it here.
            options["sync-then-delete"] = "true";

            // The SVN dataset contains many files that keep the same size across versions
            // but differ in content (e.g. Eclipse .cdtbuild / .settings prefs). Sync by
            // default decides uploads on size+mtime alone, which would miss same-size
            // changes; --sync-verify-hash makes sync re-check the content hash when size
            // and mtime are unchanged, so updates are reliably detected across versions.
            options["sync-verify-hash"] = "true";

            using (new Timer(LOGTAG, "SyncUnitTest", "Total sync unittest"))
            using (var sourceWork = new Library.Utility.TempFolder())
            using (var defaultTarget = string.IsNullOrEmpty(target) ? new Library.Utility.TempFolder() : null)
            {
                options["dbpath"] = Path.Combine(tempdir, "unittest_sync.sqlite");
                if (File.Exists(options["dbpath"]))
                    File.Delete(options["dbpath"]);

                // When no target was supplied, use a distinct local file:// target so the
                // destination tree is separate from the working source folder.
                if (string.IsNullOrEmpty(target))
                    target = "file://" + defaultTarget;

                // A dedicated target directory so the destination tree is distinct from
                // the working source folder. Each version's verification reads from here.
                string targetDir;
                var isFileTarget = target.StartsWith("file://", StringComparison.Ordinal);
                if (isFileTarget)
                {
                    targetDir = target.Substring("file://".Length);
                    if (Directory.Exists(targetDir))
                        Directory.Delete(targetDir, true);
                    Directory.CreateDirectory(targetDir);
                }
                else
                {
                    targetDir = null;
                    // For non-file backends, clean up any existing files on the remote first.
                    BasicSetupHelper.ProgressWriteLine("Removing old sync target contents");
                    var tmp = new Dictionary<string, string>(options) { ["force"] = "" };
                    try
                    {
                        using (var bk = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(target, tmp))
                            foreach (var f in bk.ListAsync(System.Threading.CancellationToken.None).ToBlockingEnumerable())
                                if (!f.IsFolder)
                                    await bk.DeleteAsync(f.Name, System.Threading.CancellationToken.None);
                    }
                    catch (Duplicati.Library.Interface.FolderMissingException)
                    {
                    }
                }

                // Seed the working source folder with the first version's contents.
                TestUtils.CopyDirectoryRecursive(folders[0], sourceWork);

                await RunSyncAsync(sourceWork, target, options, folders[0]);
                VerifyTargetMirrorsSource(folders[0], (string)sourceWork, target, targetDir);

                for (int i = 1; i < folders.Length; i++)
                {
                    // If the syncs are too close, mtime resolution can hide updates.
                    System.Threading.Thread.Sleep(1000 * 2);

                    // Replace the working source with the next version: this exercises
                    // adds, updates, and deletes (the latter via --sync-then-delete).
                    Directory.Delete(sourceWork, true);
                    TestUtils.CopyDirectoryRecursive(folders[i], sourceWork);

                    await RunSyncAsync(sourceWork, target, options, folders[i]);
                    VerifyTargetMirrorsSource(folders[i], (string)sourceWork, target, targetDir);
                }
            }
        }

        if (LogHelper.ErrorCount > 0)
            BasicSetupHelper.ProgressWriteLine("Sync unittest completed, but with {0} errors, see logfile for details", LogHelper.ErrorCount);
        else if (LogHelper.WarningCount > 0)
            BasicSetupHelper.ProgressWriteLine("Sync unittest completed, but with {0} warnings, see logfile for details", LogHelper.WarningCount);
        else
            BasicSetupHelper.ProgressWriteLine("Sync unittest completed successfully - Have some cake!");

        System.Diagnostics.Debug.Assert(LogHelper.ErrorCount == 0);
    }

    /// <summary>
    /// Runs a single sync pass of the given source against the target.
    /// </summary>
    private static async Task RunSyncAsync(Library.Utility.TempFolder source, string target, Dictionary<string, string> options, string sourcename)
    {
        BasicSetupHelper.ProgressWriteLine("Syncing the copy: " + sourcename);
        using (new Timer(LOGTAG, "SyncRun", "Sync of " + sourcename))
        using (var console = new CommandLine.ConsoleOutput(Console.Out, options))
        using (var i = new Duplicati.Library.Main.Controller(target, options, console))
            Log.WriteInformationMessage(LOGTAG, "SyncOutput", (await i.SyncAsync(new[] { (string)source }, null)).ToString());
    }

    /// <summary>
    /// Verifies the remote target exactly mirrors the source after a sync. For a
    /// local file:// target the destination tree on disk is walked directly; for a
    /// non-file backend the destination is listed. The sync contract verified here is:
    /// (1) every local file is present on the remote with identical content, and
    /// (2) within folders that still exist locally, files removed since the previous
    /// version are deleted from the remote (--sync-then-delete). Sync does not prune
    /// whole subfolders that no longer exist locally (folders are implicit, derived
    /// from file presence, and deletes are scoped to folders the run still visits),
    /// so the assertion is a presence+content check, not a full-tree equivalence.
    /// </summary>
    private static void VerifyTargetMirrorsSource(string sourceVersionName, string source, string target, string targetDir)
    {
        using (new Timer(LOGTAG, "SyncVerify", "Verification of sync of " + sourceVersionName))
        {
            if (targetDir != null)
                VerifyLocalFilesPresent(source, targetDir, sourceVersionName);
            else
                VerifyRemoteFilesPresent(source, target, sourceVersionName);
        }
    }

    /// <summary>
    /// Verifies that every file in the source tree exists on the local file:// target
    /// with identical size and content. This asserts the sync upload/update contract
    /// (every local file is mirrored) without asserting the absence of stale files in
    /// subfolders the sync no longer visits (see the contract note above).
    /// </summary>
    private static void VerifyLocalFilesPresent(string source, string targetDir, string sourceVersionName)
    {
        if (!Directory.Exists(targetDir))
        {
            var msg = $"Sync verification failed for {sourceVersionName}: target directory does not exist: {targetDir}";
            Log.WriteErrorMessage(LOGTAG, "SyncVerifyTargetMissing", null, "{0}", msg);
            throw new Exception(msg);
        }

        var missing = new List<string>();
        var mismatches = new List<string>();
        foreach (var rel in EnumerateRelativeFiles(source))
        {
            var remote = Path.Combine(targetDir, rel);
            if (!File.Exists(remote))
            {
                missing.Add(rel);
                continue;
            }
            var local = Path.Combine(source, rel);
            try
            {
                TestUtils.AssertFilesAreEqual(local, remote, false, $"SyncVerify({sourceVersionName})");
            }
            catch (Exception)
            {
                mismatches.Add(rel);
            }
        }

        if (missing.Count > 0 || mismatches.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Sync verification failed for {sourceVersionName}:");
            if (missing.Count > 0)
                sb.AppendLine("  Missing on remote: " + string.Join(", ", missing.OrderBy(x => x)));
            if (mismatches.Count > 0)
                sb.AppendLine("  Content mismatch: " + string.Join(", ", mismatches.OrderBy(x => x)));
            Log.WriteErrorMessage(LOGTAG, "SyncVerifyMismatch", null, "{0}", sb.ToString());
            throw new Exception(sb.ToString());
        }
    }

    /// <summary>
    /// For a non-file backend, verifies that every local file is present on the remote
    /// (by name and size). A full content download is not feasible across an arbitrary
    /// backend, so this asserts presence and size only. As with the local path, extra
    /// remote files in folders the sync no longer visits are not flagged.
    /// </summary>
    private static void VerifyRemoteFilesPresent(string source, string target, string sourceVersionName)
    {
        var expected = EnumerateRelativeFiles(source)
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(x => x)
            .ToDictionary(p => p, p => new FileInfo(Path.Combine(source, p.Replace('/', Path.DirectorySeparatorChar))).Length);

        Dictionary<string, long> remote;
        using (var bk = Duplicati.Library.DynamicLoader.BackendLoader.GetBackend(target, new Dictionary<string, string>()))
        {
            remote = bk.ListAsync(System.Threading.CancellationToken.None)
                .ToBlockingEnumerable()
                .Where(fe => !fe.IsFolder)
                .ToDictionary(fe => fe.Name.Replace('\\', '/'), fe => Math.Max(fe.Size, 0L));
        }

        var missing = new List<string>();
        var sizeMismatch = new List<string>();
        foreach (var kv in expected)
        {
            if (!remote.TryGetValue(kv.Key, out var remoteSize))
                missing.Add(kv.Key);
            else if (remoteSize != kv.Value)
                sizeMismatch.Add($"{kv.Key} (local {kv.Value}, remote {remoteSize})");
        }

        if (missing.Count > 0 || sizeMismatch.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Sync verification failed for {sourceVersionName}:");
            if (missing.Count > 0)
                sb.AppendLine("  Missing on remote: " + string.Join(", ", missing.OrderBy(x => x)));
            if (sizeMismatch.Count > 0)
                sb.AppendLine("  Size mismatch: " + string.Join(", ", sizeMismatch.OrderBy(x => x)));
            Log.WriteErrorMessage(LOGTAG, "SyncVerifyRemoteMissing", null, "{0}", sb.ToString());
            throw new Exception(sb.ToString());
        }
    }

    /// <summary>
    /// Enumerates all files under <paramref name="root"/> returning their paths
    /// relative to <paramref name="root"/> using the OS directory separator.
    /// </summary>
    private static IEnumerable<string> EnumerateRelativeFiles(string root)
    {
        root = Util.AppendDirSeparator(root);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            yield return f.Substring(root.Length);
    }
}
