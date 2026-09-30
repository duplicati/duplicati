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
using Duplicati.Library.Main;
using Duplicati.Server;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Tests the metadata the runner records for a sync run, which follows the same
/// rule as a backup run: only a run that was not interrupted is recorded
/// </summary>
[TestFixture]
public class RunnerSyncMetadataTests
{
    /// <summary>The keys a completed sync run records</summary>
    private static readonly string[] RecordedKeys =
    [
        "LastSyncStarted", "LastSyncFinished", "LastSyncDuration",
        "SourceFilesSize", "SourceFilesCount", "SourceSizeString"
    ];

    private static readonly DateTime Begin = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

    private static Backup CreateBackup(Dictionary<string, string> metadata)
        => new Backup
        {
            Name = "Sync job",
            Description = string.Empty,
            Tags = Array.Empty<string>(),
            TargetURL = "file:///mock_sync_target",
            Sources = Array.Empty<string>(),
            Settings = Array.Empty<ISetting>(),
            Filters = Array.Empty<IFilter>(),
            Metadata = metadata,
            OperationType = OperationType.Sync,
        };

    private static SyncResults CreateResults(DateTime begin, bool interrupted)
        => new SyncResults
        {
            BeginTime = begin,
            EndTime = begin.AddMinutes(30),
            SourceFiles = 12,
            SizeOfSourceFiles = 4096,
            Interrupted = interrupted
        };

    [Test]
    [Category("Sync")]
    public void ACompletedSyncRunIsRecorded()
    {
        var backup = CreateBackup(new Dictionary<string, string>());

        Runner.UpdateMetadataLastSync(backup, CreateResults(Begin, false));

        Assert.AreEqual(Library.Utility.Utility.SerializeDateTime(Begin), backup.Metadata["LastSyncStarted"]);
        Assert.AreEqual(Library.Utility.Utility.SerializeDateTime(Begin.AddMinutes(30)), backup.Metadata["LastSyncFinished"]);
        Assert.AreEqual(TimeSpan.FromMinutes(30).ToString(), backup.Metadata["LastSyncDuration"]);
        Assert.AreEqual("12", backup.Metadata["SourceFilesCount"]);
        Assert.AreEqual("4096", backup.Metadata["SourceFilesSize"]);
        Assert.AreEqual(Library.Utility.Utility.FormatSizeString(4096), backup.Metadata["SourceSizeString"]);

        // A sync run is not a backup run
        foreach (var key in new[] { "LastBackupStarted", "LastBackupFinished", "LastBackupDuration" })
            Assert.IsFalse(backup.Metadata.ContainsKey(key), $"The key {key} should not be written for a sync run");
    }

    [Test]
    [Category("Sync")]
    public void AnInterruptedFirstSyncRunRecordsNothing()
    {
        var backup = CreateBackup(new Dictionary<string, string>());

        Runner.UpdateMetadataLastSync(backup, CreateResults(Begin, true));

        Assert.IsEmpty(backup.Metadata, "An interrupted run is not a completed run");
    }

    [Test]
    [Category("Sync")]
    public void AnInterruptedSyncRunKeepsTheLastCompletedRun()
    {
        var backup = CreateBackup(new Dictionary<string, string>());
        Runner.UpdateMetadataLastSync(backup, CreateResults(Begin, false));
        var completed = new Dictionary<string, string>(backup.Metadata);

        var interrupted = CreateResults(Begin.AddDays(1), true);
        interrupted.SourceFiles = 3;
        interrupted.SizeOfSourceFiles = 100;
        Runner.UpdateMetadataLastSync(backup, interrupted);

        foreach (var key in RecordedKeys)
            Assert.AreEqual(completed[key], backup.Metadata[key], $"The key {key} was changed by an interrupted run");
        Assert.AreEqual(completed.Count, backup.Metadata.Count);
    }
}
