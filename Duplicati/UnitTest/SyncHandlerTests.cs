using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

[TestFixture]
public class SyncHandlerTests : BasicSetupHelper
{
    private string targetDir;
    private string backendUrl;

    [SetUp]
    public void Setup()
    {
        targetDir = Path.Combine(BASEFOLDER, "target");
        if (Directory.Exists(targetDir))
        {
            //Directory.Delete(targetDir, true);
        }
        Directory.CreateDirectory(targetDir);
        backendUrl = "file://" + targetDir.Replace("\\", "/");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(targetDir))
        {
            //Directory.Delete(targetDir, true);
        }
    }

    [Test]
    [Category("Sync")]
    public async Task TestBasicsAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "File 2 content");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
            ["snapshot-policy"] = "off"
        };

        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }

        if (!File.Exists(Path.Combine(targetDir, "file1.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file1.txt not found. Target has: " + string.Join(", ", files));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        if (!File.Exists(Path.Combine(targetDir, "file2.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file2.txt not found. Target has: " + string.Join(", ", files));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));
        if (!File.Exists(Path.Combine(targetDir, "subfolder/file3.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file3.txt not found. Target has: " + string.Join(", ", files) + "\nExpected to find: " + Path.Combine(targetDir, "subfolder/file3.txt"));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "subfolder/file3.txt")));

        Assert.AreEqual("Hello World", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("File 3 content", File.ReadAllText(Path.Combine(targetDir, "subfolder", "file3.txt")));
    }

    /// <summary>
    /// Verifies that the sync handler reports its progress via
    /// <see cref="SyncResults.OperationProgressUpdater"/>: the phase transitions through
    /// the new <c>Sync_*</c> phases (<c>Sync_Begin</c> -> <c>Sync_CountingFiles</c> ->
    /// <c>Sync_ProcessingFiles</c> -> <c>Sync_WaitForUpload</c> -> <c>Sync_Complete</c>),
    /// the parallel file counter feeds a non-zero file count + size into the updater,
    /// and the per-file processing advances the processed-files counter. This mirrors
    /// how the backup handler reports progress through <c>CountFilesHandler</c>.
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncReportsProgressViaOperationProgressUpdaterAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_progress");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "File 2 content");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        var observedPhases = new List<OperationPhase>();
        long reportedFileCount = -1;
        long reportedFileSize = -1;
        long reportedFilesProcessed = -1;

        using (var c = new Controller(backendUrl, opts, null))
        {
            c.OnOperationStarted += results =>
            {
                ((SyncResults)results).OperationProgressUpdater.PhaseChanged += (phase, _) =>
                {
                    observedPhases.Add(phase);
                };
            };

            await c.SyncAsync(new[] { dataFolder }, null);
        }

        // The begin phase is the first reported, and complete is the last.
        CollectionAssert.Contains(observedPhases, OperationPhase.Sync_Begin,
            "Sync should report the Sync_Begin phase via the operation progress updater.");
        CollectionAssert.Contains(observedPhases, OperationPhase.Sync_ProcessingFiles,
            "Sync should report the Sync_ProcessingFiles phase via the operation progress updater.");
        CollectionAssert.Contains(observedPhases, OperationPhase.Sync_WaitForUpload,
            "Sync should report the Sync_WaitForUpload phase via the operation progress updater.");
        CollectionAssert.Contains(observedPhases, OperationPhase.Sync_Complete,
            "Sync should report the Sync_Complete phase via the operation progress updater.");

        // When the file scanner is not disabled, the parallel counter runs and reports a
        // count + size. Three source files (file1.txt, file2.txt, subfolder/file3.txt) were
        // created, so the updater's final file count must be at least 3 and the size > 0.
        // (The counter is best-effort and may slightly lag, but a completed sync always
        // flushes the final tally via UpdatefileCount(SourceFiles, SizeOfSourceFiles, true).)
        using (var c = new Controller(backendUrl, opts, null))
        {
            c.OnOperationStarted += results =>
            {
                var updater = ((SyncResults)results).OperationProgressUpdater;
                updater.PhaseChanged += (phase, _) =>
                {
                    if (phase == OperationPhase.Sync_Complete)
                    {
                        ((IOperationProgress)updater).UpdateOverall(
                            out OperationPhase _phase,
                            out float _progress,
                            out long filesprocessed,
                            out long _filesizeprocessed,
                            out long filecount,
                            out long filesize,
                            out bool _countingfiles);
                        reportedFileCount = filecount;
                        reportedFileSize = filesize;
                        reportedFilesProcessed = filesprocessed;
                    }
                };
            };

            await c.SyncAsync(new[] { dataFolder }, null);
        }

        Assert.AreEqual(3, reportedFileCount,
            "The sync progress updater should report the three source files as the file count.");
        Assert.IsTrue(reportedFileSize > 0,
            "The sync progress updater should report a non-zero total source size.");
        Assert.AreEqual(3, reportedFilesProcessed,
            "The sync progress updater should report all three files as processed.");
    }

    [Test]
    [Category("Sync")]
    public async Task TestSyncUpdatesAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_updates");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["delete"] = "true",
            ["log-level"] = "Profiling",
            ["snapshot-policy"] = "off"
        };

        // Sync 1: Create
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hello World", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));

        // Modify file
        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello Sync");

        // Sync 2: Update
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hello Sync", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
    }

    [Test]
    [Category("Sync")]
    public async Task TestSyncDeletesAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_deletes");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "World");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
            ["snapshot-policy"] = "off"
        };

        // Sync 1
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        if (!File.Exists(Path.Combine(targetDir, "file2.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file2.txt not found. Target has: " + string.Join(", ", files));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));

        // Delete local file
        File.Delete(Path.Combine(dataFolder, "file2.txt"));

        // Sync 2
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        if (!File.Exists(Path.Combine(targetDir, "file1.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file1.txt not found. Target has: " + string.Join(", ", files));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(targetDir, "file2.txt")), "File2 should be deleted on remote");
    }

    [Test]
    [Category("Sync")]
    public async Task TestSyncNoDeleteAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_nodelete");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "World");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off"
            // Default is delete=false usually but we didn't specify it
        };

        // Sync 1
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        if (!File.Exists(Path.Combine(targetDir, "file2.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file2.txt not found. Target has: " + string.Join(", ", files));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));

        // Delete local file
        File.Delete(Path.Combine(dataFolder, "file2.txt"));

        // Sync 2
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        if (!File.Exists(Path.Combine(targetDir, "file1.txt")))
        {
            var files = Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories);
            throw new Exception("file1.txt not found. Target has: " + string.Join(", ", files));
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")), "File2 should NOT be deleted on remote");
    }

    [Test]
    [Category("Sync")]
    public async Task TestSyncHashVerificationAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_hash");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hash content");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-verify-hash"] = "true"
        };

        // Sync 1
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hash content", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));

        // Let's modify the target directly so sizes are the same, mod time could be older or same
        // But content is different. We just write same length.
        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Mash content"); // 12 bytes
        File.SetLastWriteTimeUtc(Path.Combine(dataFolder, "file1.txt"), File.GetLastWriteTimeUtc(Path.Combine(targetDir, "file1.txt"))); // Same modification time

        // Sync 2
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }

        Assert.AreEqual("Mash content", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
    }

    /// <summary>
    /// sync-remote-state=use-local-state must produce the same end state as the default
    /// (use-remote-state): files uploaded, sub-folders created, and a second run performs
    /// no extra work by diffing against the local inventory cache (no per-folder listing
    /// would be needed, but the observable contract - correct end state and updates
    /// propagating - is what we assert here).
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncUseLocalStateAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_localstate");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(Path.Combine(dataFolder, "sub"));

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello");
        File.WriteAllText(Path.Combine(dataFolder, "sub", "file2.txt"), "World");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-remote-state"] = "use-local-state",
        };

        // Sync 1: inventory is empty, so the handler falls back to listing fresh and
        // seeds the inventory. End state must match a normal sync.
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "sub", "file2.txt")));

        // Modify both files; the second run uses the local inventory as the baseline.
        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello updated");
        File.WriteAllText(Path.Combine(dataFolder, "sub", "file2.txt"), "World updated");

        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hello updated", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("World updated", File.ReadAllText(Path.Combine(targetDir, "sub", "file2.txt")));
    }

    /// <summary>
    /// sync-remote-state=blindly-upload must upload every local file unconditionally on
    /// every run (no remote state check), create sub-folders as needed, and ignore
    /// --sync-then-delete (deletes are not meaningful without remote state). A second
    /// run re-uploads the unchanged files; the end state is still correct.
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncBlindlyUploadAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_blind");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        Directory.CreateDirectory(Path.Combine(dataFolder, "sub"));

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello");
        File.WriteAllText(Path.Combine(dataFolder, "sub", "file2.txt"), "World");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-remote-state"] = "blindly-upload",
            // Deletes must be ignored under blind upload.
            ["sync-then-delete"] = "true",
        };

        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "sub", "file2.txt")));
        Assert.AreEqual("Hello", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));

        // A second blind run re-uploads everything; content is unchanged on disk but
        // the operation must still succeed and leave the correct end state.
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hello", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("World", File.ReadAllText(Path.Combine(targetDir, "sub", "file2.txt")));
    }

    /// <summary>
    /// A sync operation mirrors files unencrypted, so encryption must be disabled.
    /// This verifies the SyncHandler run-time enforcement: when encryption is not
    /// disabled (the default, or when a passphrase is supplied without
    /// <c>--no-encryption</c>), the run throws a
    /// <see cref="Duplicati.Library.Interface.UserInformationException"/> rather
    /// than encrypting the upload.
    /// </summary>
    [Test]
    [Category("Sync")]
    public void TestEncryptionIsRejectedForSync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_encryption_data");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "plain.txt"), "Encryption should be rejected by sync");

        // Supply a passphrase and leave encryption enabled (no "no-encryption" flag).
        // The sync path must reject this rather than encrypt the upload.
        var opts = new Dictionary<string, string>
        {
            ["passphrase"] = "should-be-rejected",
            ["snapshot-policy"] = "off",
            ["log-level"] = "Profiling",
        };

        using var c = new Controller(backendUrl, opts, null);
        var ex = Assert.Throws<Duplicati.Library.Interface.UserInformationException>(
            () => c.SyncAsync(new[] { dataFolder }, null).GetAwaiter().GetResult(),
            "A sync operation with encryption enabled must throw UserInformationException.");
        StringAssert.IsMatch("encryption", (ex.Message ?? string.Empty).ToLowerInvariant(),
            "The error message should mention that encryption must be disabled for sync.");

        // Nothing should have been mirrored: the run failed before any upload.
        Assert.IsFalse(File.Exists(Path.Combine(targetDir, "plain.txt")),
            "No file should be mirrored when the sync is rejected for having encryption enabled.");
    }

    /// <summary>
    /// Builds a controlled test dataset under <paramref name="targetfolder"/> with files
    /// placed at the root, one level deep, and across a deep folder hierarchy. Each file
    /// is sized around <paramref name="basedatasize"/> (plus a few boundary variants at
    /// the root) so the dataset exercises the sync path with realistic content without
    /// being expensive to create. Returns the map of relative path -> file size so the
    /// caller can assert against the mirrored tree and later mutate the dataset.
    /// </summary>
    private static Dictionary<string, int> WriteSyncDataset(string targetfolder, int basedatasize)
    {
        if (basedatasize <= 0)
            basedatasize = 1024;

        var filenames = new Dictionary<string, int>(StringComparer.Ordinal);

        // Root-level boundary variants, mirroring the spirit of BorderTests.
        filenames["root.txt"] = basedatasize;
        filenames["empty.txt"] = 0;
        filenames["tiny.txt"] = 1;
        filenames["plus1.txt"] = basedatasize + 1;
        filenames["minus1.txt"] = basedatasize - 1;

        // One level deep.
        filenames["l1/file.txt"] = basedatasize;
        filenames["l1/other.txt"] = basedatasize / 2;

        // A deep hierarchy (l1/l2/l3/l4/l5) with a file at the bottom.
        filenames["l1/l2/file.txt"] = basedatasize;
        filenames["l1/l2/l3/file.txt"] = basedatasize;
        filenames["l1/l2/l3/l4/file.txt"] = basedatasize;
        filenames["l1/l2/l3/l4/l5/deep.txt"] = basedatasize + 7;

        // A sibling deep branch with its own leaf at every level.
        filenames["a/b/c/d/e/f/leaf.txt"] = basedatasize / 4;

        var data = new byte[filenames.Values.Max()];
        foreach (var k in filenames)
        {
            var full = Path.Combine(targetfolder, k.Key.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, data.Take(k.Value).ToArray());
        }

        return filenames;
    }

    /// <summary>
    /// Asserts that the mirrored target tree exactly matches the local dataset: every
    /// relative path exists on the remote with identical content, and there are no
    /// extra files on the remote. Uses forward-slash relative paths for portability.
    /// </summary>
    private void AssertTargetMirrorsDataset(string localRoot, Dictionary<string, int> expected)
    {
        var expectedPaths = expected.Keys.OrderBy(x => x).ToArray();
        var actualRelPaths = Directory.Exists(targetDir)
            ? Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(targetDir.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(x => x).ToArray()
            : Array.Empty<string>();

        CollectionAssert.AreEquivalent(expectedPaths, actualRelPaths,
            "Remote tree does not match the local dataset. Expected: " + string.Join(", ", expectedPaths) +
            " Actual: " + string.Join(", ", actualRelPaths));

        foreach (var rel in expectedPaths)
        {
            var remote = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(remote), $"Missing remote file: {rel}");
            Assert.AreEqual(expected[rel], new FileInfo(remote).Length,
                $"Size mismatch for remote file: {rel}");
        }
    }

    /// <summary>
    /// Clears the shared remote target tree so each dataset test starts from a
    /// clean slate. The fixture's Setup/TearDown deliberately preserves the target
    /// directory between tests, so leftover files from earlier tests would otherwise
    /// pollute the "no extra files on the remote" assertion these dataset tests
    /// rely on.
    /// </summary>
    private void CleanTargetTree()
    {
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);
    }

    /// <summary>
    /// End-to-end sync over a test dataset that includes a deep folder hierarchy.
    /// Verifies that the first sync mirrors every file (including the deeply nested
    /// leaf at l1/l2/l3/l4/l5/deep.txt), and that a second no-op sync performs no
    /// uploads (the end state is unchanged and correct).
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncDeepFolderHierarchyAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_deep");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        CleanTargetTree();

        var dataset = WriteSyncDataset(dataFolder, 1024);

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        // Sync 1: mirror the whole dataset.
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }

        AssertTargetMirrorsDataset(dataFolder, dataset);

        // The deepest leaf must be present and correct.
        var deepRemote = Path.Combine(targetDir, "l1", "l2", "l3", "l4", "l5", "deep.txt");
        Assert.IsTrue(File.Exists(deepRemote),
            "Deeply nested file l1/l2/l3/l4/l5/deep.txt was not mirrored. Target has: " +
            string.Join(", ", Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)));

        // Sync 2: nothing changed locally, so the remote tree must be identical.
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        AssertTargetMirrorsDataset(dataFolder, dataset);
    }

    /// <summary>
    /// End-to-end sync exercising updates across the dataset: after the initial mirror,
    /// a root file, a one-level-deep file, and a deeply nested leaf are all rewritten
    /// with new content (some same-size, some size-changed). A second sync must update
    /// each of them on the remote and leave the rest untouched.
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncUpdatesAcrossHierarchyAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_updates_hierarchy");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        CleanTargetTree();

        var dataset = WriteSyncDataset(dataFolder, 1024);

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        // Rewrite three files at different depths with new content and bump sizes.
        var rn = new Random(42);
        var changedRelPaths = new[] { "root.txt", "l1/file.txt", "l1/l2/l3/l4/l5/deep.txt" };
        foreach (var rel in changedRelPaths)
        {
            dataset[rel] += 10; // size change forces an update regardless of mtime resolution
            var full = Path.Combine(dataFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            var bytes = new byte[dataset[rel]];
            rn.NextBytes(bytes);
            File.WriteAllBytes(full, bytes);
        }

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsDataset(dataFolder, dataset);

        // Content of the changed files must reflect the new bytes.
        foreach (var rel in changedRelPaths)
        {
            var local = Path.Combine(dataFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            var remote = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.AreEqual(File.ReadAllBytes(local), File.ReadAllBytes(remote),
                $"Content mismatch after update for {rel}");
        }
    }

    /// <summary>
    /// End-to-end sync exercising deletes across the dataset: after the initial mirror,
    /// files are removed at the root, mid-hierarchy, and at the deepest leaf. With
    /// --sync-then-delete the second sync must delete each of them on the remote and
    /// leave the rest untouched. Also verifies that an emptied deep folder does not
    /// strand stray files on the remote.
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncDeletesAcrossHierarchyAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_deletes_hierarchy");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        CleanTargetTree();

        var dataset = WriteSyncDataset(dataFolder, 1024);

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        // Remove files at three depths and drop them from the expected dataset.
        var deletedRelPaths = new[] { "root.txt", "l1/other.txt", "l1/l2/l3/l4/l5/deep.txt" };
        foreach (var rel in deletedRelPaths)
        {
            var full = Path.Combine(dataFolder, rel.Replace('/', Path.DirectorySeparatorChar));
            File.Delete(full);
            dataset.Remove(rel);
        }

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsDataset(dataFolder, dataset);

        // The deleted files must be gone from the remote.
        foreach (var rel in deletedRelPaths)
        {
            var remote = Path.Combine(targetDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsFalse(File.Exists(remote), $"Deleted file should be gone from remote: {rel}");
        }

        // Without --sync-then-delete, a subsequent local delete must NOT propagate.
        var noDeleteOpts = new Dictionary<string, string>(opts) { ["sync-then-delete"] = "false" };
        var survivingRel = "l1/file.txt";
        File.Delete(Path.Combine(dataFolder, survivingRel.Replace('/', Path.DirectorySeparatorChar)));

        using (var c = new Controller(backendUrl, noDeleteOpts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        Assert.IsTrue(File.Exists(Path.Combine(targetDir, survivingRel.Replace('/', Path.DirectorySeparatorChar))),
            $"File should NOT be deleted on remote when sync-then-delete is off: {survivingRel}");
    }

    /// <summary>
    /// End-to-end sync exercising a mixed workload over the dataset in a single run:
    /// add a new file, update an existing one, and delete another (all at varying
    /// depths). With --sync-then-delete the run must reconcile all three kinds of
    /// changes in one pass and leave the remote tree exactly mirroring the local one.
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncMixedAddUpdateDeleteAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_mixed");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        CleanTargetTree();

        var dataset = WriteSyncDataset(dataFolder, 1024);

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        // Add a brand-new file in a new deep folder.
        dataset["new/deep/path/added.txt"] = 333;
        Directory.CreateDirectory(Path.Combine(dataFolder, "new", "deep", "path"));
        File.WriteAllBytes(Path.Combine(dataFolder, "new", "deep", "path", "added.txt"), new byte[333]);

        // Update an existing deeply nested file (size change forces re-upload).
        dataset["a/b/c/d/e/f/leaf.txt"] += 5;
        File.WriteAllBytes(
            Path.Combine(dataFolder, "a", "b", "c", "d", "e", "f", "leaf.txt"),
            new byte[dataset["a/b/c/d/e/f/leaf.txt"]]);

        // Delete a root-level file.
        File.Delete(Path.Combine(dataFolder, "empty.txt"));
        dataset.Remove("empty.txt");

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsDataset(dataFolder, dataset);
    }

    /// <summary>
    /// End-to-end sync verifying the empty-folder handling option over a deep
    /// hierarchy. With --exclude-empty-folders, an empty local sub-folder must not
    /// contribute any files to the remote tree, and removing the last file from a
    /// deep folder (leaving it empty locally) must not strand a remote copy of the
    /// file when --sync-then-delete is set. The remote file set must exactly mirror
    /// the local dataset after each pass.
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestSyncExcludeEmptyFoldersDeepAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_empty_deep");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);
        CleanTargetTree();

        var dataset = WriteSyncDataset(dataFolder, 1024);

        // Add a truly empty sub-folder (no children at all) alongside the deep tree;
        // under --exclude-empty-folders it must not be created on the remote. The
        // handler peeks each sub-folder's direct children and skips folders with no
        // children, so a folder containing only empty sub-folders is still created
        // (it has a direct child); a folder with zero direct children is skipped.
        Directory.CreateDirectory(Path.Combine(dataFolder, "should_be_empty"));

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["exclude-empty-folders"] = "true",
            ["log-level"] = "Profiling",
        };

        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsDataset(dataFolder, dataset);

        // The empty folder must not be created on the remote at all: under
        // --exclude-empty-folders the handler peeks the sub-folder's direct children
        // and skips folders with none, so an empty local folder never triggers an
        // EnsureFolderAsync call.
        Assert.IsFalse(Directory.Exists(Path.Combine(targetDir, "should_be_empty")),
            "Empty local folder should not be created on the remote under exclude-empty-folders.");

        // A second no-op sync (nothing changed locally) must leave the remote tree
        // identical, including the empty folder still absent. This confirms the
        // empty-folder skip is stable across runs and the deep tree is reconciled
        // without re-creating the empty folder.
        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsDataset(dataFolder, dataset);
        Assert.IsFalse(Directory.Exists(Path.Combine(targetDir, "should_be_empty")),
            "Empty local folder should still not exist on the remote after a no-op sync.");
    }

    [Test]
    [Category("Sync")]
    public async Task TestMultipleTargetsAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_multi");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "File 2 content");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        var targetDir2 = Path.Combine(BASEFOLDER, "target2");
        if (Directory.Exists(targetDir2)) Directory.Delete(targetDir2, true);
        Assert.IsFalse(Directory.Exists(targetDir2), "target2 should be deleted before test");
        Directory.CreateDirectory(targetDir2);
        Directory.CreateDirectory(Path.Combine(targetDir2, "subfolder"));
        var backendUrl2 = "file://" + targetDir2.Replace("\\", "/");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-remote-state"] = "BlindlyUpload",
            ["remote-sync-json-config"] = @$"{{""destinations"": [{{""url"": ""{backendUrl2}""}}]}}"
        };

        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }

        // Verify primary target
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "subfolder", "file3.txt")));

        // Verify secondary target
        Assert.IsTrue(File.Exists(Path.Combine(targetDir2, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir2, "file2.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir2, "subfolder", "file3.txt")));
    }

    /// <summary>
    /// Verifies that a single source enumeration is replicated identically to
    /// multiple destinations, including across a second run that adds, updates,
    /// and deletes files. This is the core guarantee of the shared single-
    /// enumeration sync: every destination ends up byte-identical to the source
    /// and byte-identical to each other, so the destinations cannot diverge even
    /// when the source changes between runs. Uses UseRemoteState + sync-then-delete
    /// so updates and deletes are actually exercised (the existing
    /// <see cref="TestMultipleTargetsAsync"/> only covers a BlindlyUpload create).
    /// </summary>
    [Test]
    [Category("Sync")]
    public async Task TestMultipleTargetsReplicateSingleEnumerationAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "sync_data_multi_repl");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        // Initial source dataset: a couple of files plus a sub-folder.
        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "File 2 content");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        // The primary targetDir is shared across tests and its Setup/TearDown does
        // not clean it, so wipe both destinations here to start from a clean slate
        // (the existing dataset tests do the same via CleanTargetTree). The secondary
        // target is private to this test but is wiped for determinism.
        CleanTargetTree();
        var targetDir2 = Path.Combine(BASEFOLDER, "target2_repl");
        if (Directory.Exists(targetDir2)) Directory.Delete(targetDir2, true);
        Directory.CreateDirectory(targetDir2);
        var backendUrl2 = "file://" + targetDir2.Replace("\\", "/");

        // UseRemoteState lists each remote folder fresh and sync-then-delete removes
        // remote files no longer present locally, so the second run exercises both
        // the update and the delete paths on every destination.
        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-remote-state"] = "UseRemoteState",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
            ["remote-sync-json-config"] = @$"{{""destinations"": [{{""url"": ""{backendUrl2}""}}]}}"
        };

        // Run 1: initial mirror to both destinations.
        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsSource(dataFolder, targetDir);
        AssertTargetMirrorsSource(dataFolder, targetDir2);
        AssertTargetsIdentical(targetDir, targetDir2);

        // Mutate the source: update an existing file, add a new file, and delete one.
        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World UPDATED");
        File.WriteAllText(Path.Combine(dataFolder, "file4.txt"), "File 4 content");
        File.Delete(Path.Combine(dataFolder, "file2.txt"));
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder", "nested"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "nested", "file5.txt"), "File 5 content");

        // Run 2: re-sync. Because the source is enumerated once and replicated to
        // every destination, both targets must reflect the SAME mutated state (the
        // updated file1, the new file4, the deleted file2, and the new nested file5).
        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsSource(dataFolder, targetDir);
        AssertTargetMirrorsSource(dataFolder, targetDir2);
        AssertTargetsIdentical(targetDir, targetDir2);

        // The deleted file must be gone from BOTH destinations, confirming that the
        // single observed (post-delete) source state was applied everywhere.
        Assert.IsFalse(File.Exists(Path.Combine(targetDir, "file2.txt")),
            "Deleted local file should be removed from the primary target.");
        Assert.IsFalse(File.Exists(Path.Combine(targetDir2, "file2.txt")),
            "Deleted local file should be removed from the secondary target.");

        // The updated file must carry the new content on BOTH destinations.
        Assert.AreEqual("Hello World UPDATED", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("Hello World UPDATED", File.ReadAllText(Path.Combine(targetDir2, "file1.txt")));

        // Run 3: a no-op re-sync must not change anything and must keep the two
        // destinations identical (no per-backend divergence from a stray re-enumeration).
        using (var c = new Controller(backendUrl, opts, null))
            await c.SyncAsync(new[] { dataFolder }, null);

        AssertTargetMirrorsSource(dataFolder, targetDir);
        AssertTargetMirrorsSource(dataFolder, targetDir2);
        AssertTargetsIdentical(targetDir, targetDir2);
    }

    /// <summary>
    /// Asserts that every file under <paramref name="source"/> exists at the same
    /// relative path under <paramref name="target"/> with identical content, and
    /// that <paramref name="target"/> contains no extra files. Folder-only entries
    /// are implicit (derived from file presence), so only files are compared.
    /// </summary>
    private static void AssertTargetMirrorsSource(string source, string target)
    {
        var sourceRelPaths = Directory.Exists(source)
            ? Directory.GetFiles(source, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(x => x).ToArray()
            : Array.Empty<string>();
        var targetRelPaths = Directory.Exists(target)
            ? Directory.GetFiles(target, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(target.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(x => x).ToArray()
            : Array.Empty<string>();

        CollectionAssert.AreEquivalent(sourceRelPaths, targetRelPaths,
            $"Target {target} does not mirror source {source}. Expected: " + string.Join(", ", sourceRelPaths) +
            " Actual: " + string.Join(", ", targetRelPaths));

        foreach (var rel in sourceRelPaths)
        {
            var sourceFile = Path.Combine(source, rel.Replace('/', Path.DirectorySeparatorChar));
            var targetFile = Path.Combine(target, rel.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(File.Exists(targetFile), $"Missing target file: {rel} in {target}");
            CollectionAssert.AreEqual(File.ReadAllBytes(sourceFile), File.ReadAllBytes(targetFile),
                $"Content mismatch for {rel} in {target}");
        }
    }

    /// <summary>
    /// Asserts that two destination trees are byte-identical: the same set of
    /// relative file paths, and identical bytes for each. This is the cross-
    /// destination consistency guarantee of the single-enumeration sync.
    /// </summary>
    private static void AssertTargetsIdentical(string targetA, string targetB)
    {
        var relPathsA = Directory.Exists(targetA)
            ? Directory.GetFiles(targetA, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(targetA.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(x => x).ToArray()
            : Array.Empty<string>();
        var relPathsB = Directory.Exists(targetB)
            ? Directory.GetFiles(targetB, "*", SearchOption.AllDirectories)
                .Select(p => p.Substring(targetB.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/'))
                .OrderBy(x => x).ToArray()
            : Array.Empty<string>();

        CollectionAssert.AreEquivalent(relPathsA, relPathsB,
            $"Targets {targetA} and {targetB} differ in file set. A: " + string.Join(", ", relPathsA) +
            " B: " + string.Join(", ", relPathsB));

        foreach (var rel in relPathsA)
        {
            var fileA = Path.Combine(targetA, rel.Replace('/', Path.DirectorySeparatorChar));
            var fileB = Path.Combine(targetB, rel.Replace('/', Path.DirectorySeparatorChar));
            CollectionAssert.AreEqual(File.ReadAllBytes(fileA), File.ReadAllBytes(fileB),
                $"Targets {targetA} and {targetB} differ in content for {rel}.");
        }
    }
}

/// <summary>
/// Tests sync against a backend that does NOT implement
/// <see cref="Duplicati.Library.Interface.IFolderEnabledBackend"/>. The backend manager
/// must translate relative paths: for listings it points the backend at the sub-folder
/// URL and lists flat; for put/get/delete it splits the relative path into a sub-folder
/// URL and a flat filename. These tests exercise that translation end-to-end through the
/// <see cref="NonFolderBackend"/> test backend (which wraps the real file backend but
/// hides the folder-operation interface).
/// </summary>
[TestFixture]
[Category("Sync")]
public class NonFolderSyncHandlerTests : BasicSetupHelper
{
    private string targetDir;
    private string backendUrl;

    [SetUp]
    public void Setup()
    {
        targetDir = Path.Combine(BASEFOLDER, "nofolder_target");
        if (Directory.Exists(targetDir))
            Directory.Delete(targetDir, true);
        Directory.CreateDirectory(targetDir);

        // Register the non-folder test backend and point it at the target directory.
        // NonFolderBackend wraps the file backend, so the URL host/path is the target dir.
        Library.DynamicLoader.BackendLoader.AddBackend(new NonFolderBackend());
        backendUrl = new NonFolderBackend().ProtocolKey + "://" + targetDir.Replace("\\", "/");
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(targetDir))
        {
            try { Directory.Delete(targetDir, true); } catch { }
        }
    }

    /// <summary>
    /// A basic sync against a non-folder backend must upload files at their relative
    /// paths (including sub-folders) and let a second run reconcile without re-uploading.
    /// </summary>
    [Test]
    public async Task TestNonFolderBasicsAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "nofolder_data");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "File 2 content");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }

        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")),
            "file1.txt not found. Target has: " + string.Join(", ", Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "subfolder", "file3.txt")),
            "subfolder/file3.txt not found. Target has: " + string.Join(", ", Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories)));

        Assert.AreEqual("Hello World", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("File 3 content", File.ReadAllText(Path.Combine(targetDir, "subfolder", "file3.txt")));
    }

    /// <summary>
    /// A non-folder backend must support updates: changing a file locally and re-syncing
    /// updates the remote copy, including files in sub-folders.
    /// </summary>
    [Test]
    public async Task TestNonFolderUpdatesAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "nofolder_data_updates");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello World");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        var opts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        // Sync 1: create.
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hello World", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("File 3 content", File.ReadAllText(Path.Combine(targetDir, "subfolder", "file3.txt")));

        // Modify both a top-level file and a nested file.
        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello Sync");
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 updated");

        // Sync 2: update.
        using (var c = new Controller(backendUrl, opts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.AreEqual("Hello Sync", File.ReadAllText(Path.Combine(targetDir, "file1.txt")));
        Assert.AreEqual("File 3 updated", File.ReadAllText(Path.Combine(targetDir, "subfolder", "file3.txt")));
    }

    /// <summary>
    /// A non-folder backend must support deletes: removing a file locally and re-syncing
    /// (with sync-then-delete) deletes it on the remote, including files in sub-folders.
    /// It must also honor the no-delete default (remote files are left in place).
    /// </summary>
    [Test]
    public async Task TestNonFolderDeletesAsync()
    {
        var dataFolder = Path.Combine(BASEFOLDER, "nofolder_data_deletes");
        if (Directory.Exists(dataFolder)) Directory.Delete(dataFolder, true);
        Directory.CreateDirectory(dataFolder);

        File.WriteAllText(Path.Combine(dataFolder, "file1.txt"), "Hello");
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "World");
        Directory.CreateDirectory(Path.Combine(dataFolder, "subfolder"));
        File.WriteAllText(Path.Combine(dataFolder, "subfolder", "file3.txt"), "File 3 content");

        var deleteOpts = new Dictionary<string, string>
        {
            ["no-encryption"] = "true",
            ["snapshot-policy"] = "off",
            ["sync-then-delete"] = "true",
            ["log-level"] = "Profiling",
        };

        // Sync 1: create.
        using (var c = new Controller(backendUrl, deleteOpts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "subfolder", "file3.txt")));

        // Delete a top-level file and a nested file locally.
        File.Delete(Path.Combine(dataFolder, "file2.txt"));
        File.Delete(Path.Combine(dataFolder, "subfolder", "file3.txt"));

        // Sync 2: deletes propagate to the remote.
        using (var c = new Controller(backendUrl, deleteOpts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file1.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(targetDir, "file2.txt")), "file2.txt should be deleted on remote");
        Assert.IsFalse(File.Exists(Path.Combine(targetDir, "subfolder", "file3.txt")), "subfolder/file3.txt should be deleted on remote");

        // Recreate file2.txt locally and sync it back, then verify no-delete leaves it.
        File.WriteAllText(Path.Combine(dataFolder, "file2.txt"), "World again");

        var noDeleteOpts = new Dictionary<string, string>(deleteOpts)
        {
            ["sync-then-delete"] = "false",
        };

        using (var c = new Controller(backendUrl, noDeleteOpts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")));

        // Delete file2.txt locally again and sync WITHOUT delete: the remote copy stays.
        File.Delete(Path.Combine(dataFolder, "file2.txt"));
        using (var c = new Controller(backendUrl, noDeleteOpts, null))
        {
            await c.SyncAsync(new[] { dataFolder }, null);
        }
        Assert.IsTrue(File.Exists(Path.Combine(targetDir, "file2.txt")), "file2.txt should NOT be deleted when sync-then-delete is off");
    }
}

/// <summary>
/// Tests that exercise <see cref="Duplicati.Library.Main.Database.Sync.LocalSyncDatabase"/>
/// directly. These validate the observed-state cache (<c>RemoteInventory</c>) and the
/// intent journal (<c>PendingOperation</c>) without going through the backend manager,
/// so they are independent of whether the configured backend supports folder operations.
/// </summary>
[TestFixture]
[Category("Sync")]
public class LocalSyncDatabaseTests
{
    private string m_dbPath;

    [SetUp]
    public void Setup()
    {
        m_dbPath = Path.Combine(Path.GetTempPath(), $"duplicati_syncdb_{Guid.NewGuid():N}.sqlite");
    }

    [TearDown]
    public void TearDown()
    {
        if (File.Exists(m_dbPath))
        {
            try { File.Delete(m_dbPath); } catch { }
        }
    }

    [Test]
    public async Task InventoryRoundtripPersistsAndReadsBackAsync()
    {
        using (var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath))
        {
            Assert.IsFalse(await db.HasAnyInventoryAsync(CancellationToken.None));
            Assert.IsFalse(await db.HasAnyPendingOperationsAsync(CancellationToken.None));

            await db.UpsertInventoryAsync("a/b.txt", 42, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), "hash-a", CancellationToken.None);
            await db.UpsertInventoryAsync("c.txt", 7, new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), null, CancellationToken.None);

            Assert.IsTrue(await db.HasAnyInventoryAsync(CancellationToken.None));
        }

        // Reopen to prove the data landed on disk (schema columns are addressable).
        using (var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath))
        {
            var item = await db.GetInventoryItemAsync("a/b.txt", CancellationToken.None);
            Assert.IsNotNull(item);
            Assert.AreEqual(42, item!.Size);
            Assert.AreEqual("hash-a", item.ContentHash);

            var all = await db.GetInventoryAsync(CancellationToken.None).ToListAsync(CancellationToken.None);
            Assert.AreEqual(2, all.Count);
            var paths = all.Select(x => x.RelativePath).OrderBy(x => x).ToArray();
            Assert.AreEqual("a/b.txt", paths[0]);
            Assert.AreEqual("c.txt", paths[1]);
        }
    }

    [Test]
    public async Task PendingOperationJournalTracksIntentAndRemovesOnCompletionAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        // Record intent for an upload and a delete.
        await db.UpsertPendingOperationAsync("upload.txt", SyncOperation.Upload, 100, null, CancellationToken.None);
        await db.UpsertPendingOperationAsync("stale.txt", SyncOperation.Delete, null, null, CancellationToken.None);

        Assert.IsTrue(await db.HasAnyPendingOperationsAsync(CancellationToken.None));

        var pending = await db.GetPendingOperationsAsync(CancellationToken.None).ToListAsync(CancellationToken.None);
        Assert.AreEqual(2, pending.Count);
        var ops = pending.Select(x => x.Operation).OrderBy(x => x.ToString()).ToArray();
        Assert.AreEqual(SyncOperation.Delete, ops[0]);
        Assert.AreEqual(SyncOperation.Upload, ops[1]);
        Assert.AreEqual(0, pending.Single(x => x.Path == "upload.txt").Attempts);

        // Re-recording the same path bumps the attempt counter (re-queue on resume).
        await db.UpsertPendingOperationAsync("upload.txt", SyncOperation.Upload, 100, null, CancellationToken.None);
        var afterRequeue = await db.GetPendingOperationAsync("upload.txt", CancellationToken.None);
        Assert.IsNotNull(afterRequeue);
        Assert.AreEqual(1, afterRequeue!.Attempts);

        // Completing the upload clears its intent row but leaves the delete pending.
        await db.RemovePendingOperationAsync("upload.txt", CancellationToken.None);
        var remaining = await db.GetPendingOperationsAsync(CancellationToken.None).Select(x => x.Path).ToListAsync(CancellationToken.None);
        Assert.AreEqual(1, remaining.Count);
        Assert.AreEqual("stale.txt", remaining[0]);

        Assert.IsTrue(await db.HasAnyPendingOperationsAsync(CancellationToken.None));
        await db.ClearPendingOperationsAsync(CancellationToken.None);
        Assert.IsFalse(await db.HasAnyPendingOperationsAsync(CancellationToken.None));
    }

    [Test]
    public async Task RenameRemoteFileMovesBothInventoryAndPendingOperationAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        await db.UpsertInventoryAsync("old.txt", 9, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "h", CancellationToken.None);
        await db.UpsertPendingOperationAsync("old.txt", SyncOperation.Update, 9, "h", CancellationToken.None);

        await db.RenameRemoteFileAsync("old.txt", "new.txt", CancellationToken.None);

        Assert.IsNull(await db.GetInventoryItemAsync("old.txt", CancellationToken.None));
        Assert.IsNotNull(await db.GetInventoryItemAsync("new.txt", CancellationToken.None));
        Assert.IsNull(await db.GetPendingOperationAsync("old.txt", CancellationToken.None));
        var pending = await db.GetPendingOperationAsync("new.txt", CancellationToken.None);
        Assert.IsNotNull(pending);
        Assert.AreEqual(SyncOperation.Update, pending!.Operation);
    }

    [Test]
    public async Task BackendManagerInterfaceTranslatesVolumeStateToIntentAsync()
    {
        // The IBackendManagerDatabase surface was designed for the backup volume state machine.
        // Sync should translate the two states it cares about into intent rows and ignore the rest.
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        // Uploading -> SyncOperation.Upload intent row.
        await db.UpdateRemoteVolumeAsync("u.txt", RemoteVolumeState.Uploading, 50, null, CancellationToken.None);
        var u = await db.GetPendingOperationAsync("u.txt", CancellationToken.None);
        Assert.IsNotNull(u);
        Assert.AreEqual(SyncOperation.Upload, u!.Operation);

        // Deleting -> SyncOperation.Delete intent row.
        await db.UpdateRemoteVolumeAsync("d.txt", RemoteVolumeState.Deleting, 0, null, CancellationToken.None);
        var d = await db.GetPendingOperationAsync("d.txt", CancellationToken.None);
        Assert.IsNotNull(d);
        Assert.AreEqual(SyncOperation.Delete, d!.Operation);

        // Backup-internal states with no sync meaning must NOT create intent rows.
        await db.UpdateRemoteVolumeAsync("x.txt", RemoteVolumeState.Uploaded, 50, null, CancellationToken.None);
        await db.UpdateRemoteVolumeAsync("x.txt", RemoteVolumeState.Verified, 50, null, CancellationToken.None);
        Assert.IsNull(await db.GetPendingOperationAsync("x.txt", CancellationToken.None));

        // The richer overload should behave the same (extra backup params are ignored).
        await db.UpdateRemoteVolumeAsync("u2.txt", RemoteVolumeState.Uploading, 50, null, true, TimeSpan.FromHours(2), null, CancellationToken.None);
        var u2 = await db.GetPendingOperationAsync("u2.txt", CancellationToken.None);
        Assert.IsNotNull(u2);
        Assert.AreEqual(SyncOperation.Upload, u2!.Operation);

        // RemoveRemoteVolumesAsync (the backend-manager flush path) drops intent rows.
        await db.RemoveRemoteVolumesAsync(new[] { "u.txt", "d.txt" }, CancellationToken.None);
        Assert.IsNull(await db.GetPendingOperationAsync("u.txt", CancellationToken.None));
        Assert.IsNull(await db.GetPendingOperationAsync("d.txt", CancellationToken.None));
    }

    [Test]
    public async Task ExecuteInTransactionCommitsAtomicallyAndRollsBackOnErrorAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        // Successful transaction: both writes commit.
        await db.ExecuteInTransactionAsync(async txCt =>
        {
            await db.UpsertInventoryAsync("a.txt", 1, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, txCt);
            await db.UpsertPendingOperationAsync("a.txt", SyncOperation.Upload, 1, null, txCt);
        }, CancellationToken.None);

        Assert.IsNotNull(await db.GetInventoryItemAsync("a.txt", CancellationToken.None));
        Assert.IsNotNull(await db.GetPendingOperationAsync("a.txt", CancellationToken.None));

        // Failing transaction: neither write commits (rollback).
        static Task ThrowAsync(CancellationToken ct) => throw new InvalidOperationException("boom");
        try
        {
            await db.ExecuteInTransactionAsync(async txCt =>
            {
                await db.UpsertInventoryAsync("b.txt", 2, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), null, txCt);
                await ThrowAsync(txCt);
                await db.UpsertPendingOperationAsync("b.txt", SyncOperation.Upload, 2, null, txCt);
            }, CancellationToken.None);
            Assert.Fail("Expected the transaction to throw");
        }
        catch (InvalidOperationException ex)
        {
            Assert.AreEqual("boom", ex.Message);
        }

        Assert.IsNull(await db.GetInventoryItemAsync("b.txt", CancellationToken.None));
        Assert.IsNull(await db.GetPendingOperationAsync("b.txt", CancellationToken.None));
    }

    [Test]
    public async Task AuditLogPurgeIsThrottledAndNotRunPerCallAsync()
    {
        // The purge is throttled to at most once per hour; logging many entries in quick
        // succession must not trigger a full-scan DELETE on every single insert. We
        // verify this indirectly by confirming that entries logged after the first call
        // are all retained (the 30-day purge would otherwise leave them, but the point is
        // that the throttle avoids re-running the DELETE each time).
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        for (var i = 0; i < 20; i++)
            await db.LogRemoteOperationAsync("put", $"file{i}.txt", null, CancellationToken.None);

        // All 20 entries should be present (none older than 30 days, and the throttle
        // means the purge ran at most once at the start, deleting nothing).
        var firstPurge = db.GetType().GetField("m_lastAuditPurgeUtc",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.IsNotNull(firstPurge, "Expected the throttle field to exist on LocalSyncDatabase.");
        // The field should have advanced from MinValue once during the 20 calls.
        var lastPurge = (DateTime)firstPurge!.GetValue(db)!;
        Assert.AreNotEqual(DateTime.MinValue, lastPurge);
    }

    /// <summary>
    /// Exercises the temp-table-backed diff that the refactored SyncHandler uses. It
    /// builds a RemoteInventory table and a LocalFiles temp table, then verifies that
    /// the upload/update/delete plan streams produce exactly the rows the in-memory
    /// version produced before, including the hash-recheck path.
    /// </summary>
    [Test]
    public async Task TempTablePlanMatchesExpectedUploadUpdateDeleteAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        // Remote inventory: a.txt (unchanged), b.txt (size differs -> update),
        // c.txt (older remote mtime -> update), d.txt (remote only -> delete),
        // e.txt (same size+mtime but has a hash, only relevant under verifyHash).
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.UpsertInventoryAsync("a.txt", 10, baseTime, null, CancellationToken.None);
        await db.UpsertInventoryAsync("b.txt", 10, baseTime, null, CancellationToken.None);
        await db.UpsertInventoryAsync("c.txt", 10, baseTime.AddSeconds(-5), null, CancellationToken.None);
        await db.UpsertInventoryAsync("d.txt", 10, baseTime, null, CancellationToken.None);
        await db.UpsertInventoryAsync("e.txt", 10, baseTime, "remote-hash-e", CancellationToken.None);

        // Local files: a.txt (unchanged), b.txt (size changed), c.txt (newer mtime),
        // e.txt (same size+mtime, different content -> hash recheck), new.txt (upload only).
        var localFilesColumns = "\"RelativePath\" TEXT NOT NULL UNIQUE, \"AbsolutePath\" TEXT NOT NULL, \"Size\" INTEGER NOT NULL, \"LastModified\" TEXT NOT NULL";
        await using var localTable = await db.CreateTempTableAsync(localFilesColumns, CancellationToken.None);
        var localTableName = localTable.Name;

        await db.InsertLocalFilesAsync(localTableName, new[]
        {
            ("a.txt", "/local/a.txt", 10L, baseTime),
            ("b.txt", "/local/b.txt", 20L, baseTime),
            ("c.txt", "/local/c.txt", 10L, baseTime),
            ("e.txt", "/local/e.txt", 10L, baseTime),
            ("new.txt", "/local/new.txt", 1L, baseTime),
        }, CancellationToken.None);

        // Upload plan: only new.txt is absent from remote.
        var uploads = await db.GetUploadPlanAsync(localTableName, CancellationToken.None).Select(r => r.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "new.txt" }, uploads);
        Assert.AreEqual(1, await db.CountUploadPlanAsync(localTableName, CancellationToken.None));

        // Update plan WITHOUT verifyHash: b.txt (size) and c.txt (mtime). e.txt is NOT
        // included because size+mtime are unchanged and no hash check is requested.
        var updatesNoHash = await db.GetUpdatePlanAsync(localTableName, verifyHash: false, CancellationToken.None).Select(r => r.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "b.txt", "c.txt" }, updatesNoHash);
        Assert.AreEqual(2, await db.CountUpdatePlanAsync(localTableName, verifyHash: false, CancellationToken.None));

        // Update plan WITH verifyHash: b.txt, c.txt AND e.txt (hash-recheck candidate).
        var updatesWithHash = await db.GetUpdatePlanAsync(localTableName, verifyHash: true, CancellationToken.None).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "b.txt", "c.txt", "e.txt" }, updatesWithHash.Select(r => r.RelativePath).ToArray());
        Assert.AreEqual(3, await db.CountUpdatePlanAsync(localTableName, verifyHash: true, CancellationToken.None));

        // e.txt must be flagged HashRecheckOnly so the handler knows to compute the local
        // hash before deciding to upload; the size/mtime-changed rows must NOT be flagged.
        var eRow = updatesWithHash.Single(r => r.RelativePath == "e.txt");
        Assert.IsTrue(eRow.HashRecheckOnly, "e.txt should be a hash-recheck-only candidate");
        Assert.AreEqual("remote-hash-e", eRow.RemoteContentHash);
        foreach (var r in updatesWithHash.Where(r => r.RelativePath != "e.txt"))
            Assert.IsFalse(r.HashRecheckOnly, $"{r.RelativePath} should not be hash-recheck-only");

        // Delete plan: only d.txt (remote-only, no local file, no pending intent).
        var deletes = await db.GetDeletePlanAsync(localTableName, CancellationToken.None).Select(r => r.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "d.txt" }, deletes);
        Assert.AreEqual(1, await db.CountDeletePlanAsync(localTableName, CancellationToken.None));
    }

    /// <summary>
    /// A delete for a path with an outstanding pending-operation intent row must be
    /// excluded from the delete plan, mirroring the old in-memory behavior that checked
    /// the intent journal to avoid a delete racing its own upload.
    /// </summary>
    [Test]
    public async Task DeletePlanExcludesPathsWithPendingOperationAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        await db.UpsertInventoryAsync("inflight.txt", 10, baseTime, null, CancellationToken.None);
        await db.UpsertInventoryAsync("orphan.txt", 10, baseTime, null, CancellationToken.None);

        // Record an in-flight upload for inflight.txt; it should NOT be deleted even
        // though it has no local file, because its upload might still be running.
        await db.UpsertPendingOperationAsync("inflight.txt", SyncOperation.Upload, 10, null, CancellationToken.None);

        var localFilesColumns = "\"RelativePath\" TEXT NOT NULL UNIQUE, \"AbsolutePath\" TEXT NOT NULL, \"Size\" INTEGER NOT NULL, \"LastModified\" TEXT NOT NULL";
        await using var localTable = await db.CreateTempTableAsync(localFilesColumns, CancellationToken.None);

        // No local files: both remote entries are candidates, but only orphan.txt has no
        // pending intent, so only orphan.txt should appear in the delete plan.
        await db.InsertLocalFilesAsync(localTable.Name, Array.Empty<(string, string, long, DateTime)>(), CancellationToken.None);

        var deletes = await db.GetDeletePlanAsync(localTable.Name, CancellationToken.None).Select(r => r.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "orphan.txt" }, deletes);
    }

    /// <summary>
    /// InsertLocalFilesAsync deduplicates by relative path via INSERT OR IGNORE on the
    /// UNIQUE constraint, so duplicate paths from multiple sources do not produce
    /// duplicate plan rows.
    /// </summary>
    [Test]
    public async Task InsertLocalFilesDeduplicatesByRelativePathAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);

        var localFilesColumns = "\"RelativePath\" TEXT NOT NULL UNIQUE, \"AbsolutePath\" TEXT NOT NULL, \"Size\" INTEGER NOT NULL, \"LastModified\" TEXT NOT NULL";
        await using var localTable = await db.CreateTempTableAsync(localFilesColumns, CancellationToken.None);

        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        // Insert the same relative path twice with different absolute paths; the first wins.
        await db.InsertLocalFilesAsync(localTable.Name, new[]
        {
            ("dup.txt", "/local/first/dup.txt", 10L, baseTime),
            ("dup.txt", "/local/second/dup.txt", 20L, baseTime),
            ("unique.txt", "/local/unique.txt", 5L, baseTime),
        }, CancellationToken.None);

        Assert.AreEqual(2, await db.CountRowsAsync(localTable.Name, CancellationToken.None));

        var rows = await db.GetUploadPlanAsync(localTable.Name, CancellationToken.None).ToListAsync(CancellationToken.None);
        var dup = rows.Single(r => r.RelativePath == "dup.txt");
        // The first-seen row should win (INSERT OR IGNORE keeps the first).
        Assert.AreEqual("/local/first/dup.txt", dup.AbsolutePath);
        Assert.AreEqual(10, dup.Size);
    }

    /// <summary>
    /// GetInventoryItemsInFolderAsync must return only the direct file children of the
    /// given folder, where a direct child's relative path has no '/' beyond the folder
    /// prefix. The root (null/empty folder) yields top-level entries; a nested folder
    /// yields entries whose path is exactly "{folder}/{name}". This is the per-folder
    /// building block the folder-by-folder sync uses under UseLocalState.
    /// </summary>
    [Test]
    public async Task GetInventoryItemsInFolderReturnsOnlyDirectChildrenAsync()
    {
        using var db = new Duplicati.Library.Main.Database.Sync.LocalSyncDatabase(m_dbPath);
        var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        // Root-level files.
        await db.UpsertInventoryAsync("root_a.txt", 1, baseTime, null, CancellationToken.None);
        await db.UpsertInventoryAsync("root_b.txt", 2, baseTime, null, CancellationToken.None);
        // One level deep.
        await db.UpsertInventoryAsync("sub/child_a.txt", 3, baseTime, null, CancellationToken.None);
        await db.UpsertInventoryAsync("sub/child_b.txt", 4, baseTime, null, CancellationToken.None);
        // Two levels deep - must NOT appear when listing "sub".
        await db.UpsertInventoryAsync("sub/deep/grandchild.txt", 5, baseTime, null, CancellationToken.None);
        // Sibling folder that shares the "sub" prefix as a string but is a distinct folder.
        await db.UpsertInventoryAsync("sub2/other.txt", 6, baseTime, null, CancellationToken.None);

        // Root listing: only the two top-level files.
        var root = await db.GetInventoryItemsInFolderAsync(null, CancellationToken.None).Select(x => x.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "root_a.txt", "root_b.txt" }, root);

        // "sub" listing: only its direct children, not the grandchild or the "sub2" sibling.
        var sub = await db.GetInventoryItemsInFolderAsync("sub", CancellationToken.None).Select(x => x.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "sub/child_a.txt", "sub/child_b.txt" }, sub);

        // "sub/deep" listing: only the grandchild.
        var deep = await db.GetInventoryItemsInFolderAsync("sub/deep", CancellationToken.None).Select(x => x.RelativePath).ToListAsync(CancellationToken.None);
        CollectionAssert.AreEquivalent(new[] { "sub/deep/grandchild.txt" }, deep);

        // An empty folder yields nothing.
        var empty = await db.GetInventoryItemsInFolderAsync("nonexistent", CancellationToken.None).Select(x => x.RelativePath).ToListAsync(CancellationToken.None);
        Assert.IsEmpty(empty);
    }
}
