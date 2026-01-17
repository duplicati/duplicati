// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using System.Text.Json;
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using Duplicati.WebserverCore.Abstractions;
using Duplicati.WebserverCore.Dto;
using Duplicati.Library.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for the FolderStatusService and related DTOs
/// </summary>
[TestFixture]
public class FolderStatusServiceTests
{
    #region Mock Classes

    /// <summary>
    /// Mock implementation of IQueueRunnerService for testing
    /// </summary>
    private sealed class MockQueueRunnerService : IQueueRunnerService
    {
        public IQueuedTask? CurrentTask { get; set; }
        public List<IQueuedTask> QueuedTasks { get; set; } = new();

        public IQueuedTask? GetCurrentTask() => CurrentTask;
        public List<IQueuedTask> GetCurrentTasks() => QueuedTasks;
        public bool GetIsActive() => CurrentTask != null;
        public CachedTaskResult? GetCachedTaskResults(long taskID) => null;
        public long AddTask(IQueuedTask task) => 0;
        public long AddTask(IQueuedTask task, bool skipQueue) => 0;
        public void Terminate(bool wait) { }
        public void Resume() { }
        public void Pause() { }
        public IList<Tuple<long, string?>> GetQueueWithIds() => new List<Tuple<long, string?>>();
        public IBasicResults? RunImmediately(IQueuedTask task) => null;
    }

    /// <summary>
    /// Mock implementation of IQueuedTask for testing
    /// </summary>
    private sealed class MockQueuedTask : IQueuedTask
    {
        public long TaskID { get; set; }
        public string? BackupID { get; set; }
        public DuplicatiOperation Operation { get; set; }
        public Func<System.Threading.Tasks.Task>? OnStarting { get; set; }
        public Func<Exception?, System.Threading.Tasks.Task>? OnFinished { get; set; }
        public DateTime? TaskStarted { get; set; }
        public DateTime? TaskFinished { get; set; }

        public void UpdateThrottleSpeeds(string? uploadSpeed, string? downloadSpeed) { }
        public void Stop() { }
        public void Abort() { }
        public void Pause(bool alsoTransfers) { }
        public void Resume() { }
    }

    /// <summary>
    /// Mock implementation of IBackup for testing
    /// </summary>
    private sealed class MockBackup : IBackup
    {
        public string ID { get; set; } = string.Empty;
        public string ExternalID { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string TargetURL { get; set; } = string.Empty;
        public string DBPath => string.Empty;
        public string[] Sources { get; set; } = Array.Empty<string>();
        public ISetting[] Settings { get; set; } = Array.Empty<ISetting>();
        public IFilter[] Filters { get; set; } = Array.Empty<IFilter>();
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
        public bool IsTemporary => false;

        public void RemoveSensitiveInformation() { }
        public void MaskSensitiveInformation() { }
    }

    /// <summary>
    /// Testable version of FolderStatusService that allows injecting mock backups
    /// </summary>
    private sealed class TestableFolderStatusService : IFolderStatusService
    {
        private readonly IBackup[] _backups;
        private readonly IQueueRunnerService _queueRunnerService;

        public TestableFolderStatusService(IBackup[] backups, IQueueRunnerService queueRunnerService)
        {
            _backups = backups;
            _queueRunnerService = queueRunnerService;
        }

        public IEnumerable<FolderStatusDto> GetAllFolderStatuses()
        {
            var results = new List<FolderStatusDto>();
            var activeBackupIds = GetActiveBackupIds();

            foreach (var backup in _backups)
            {
                if (backup.Sources == null)
                    continue;

                var status = DetermineBackupStatus(backup, activeBackupIds);
                var lastBackupTime = GetLastBackupTime(backup);

                foreach (var source in backup.Sources)
                {
                    if (string.IsNullOrEmpty(source))
                        continue;

                    results.Add(new FolderStatusDto
                    {
                        Path = source,
                        Status = status,
                        BackupName = backup.Name,
                        LastBackupTime = lastBackupTime,
                        BackupId = backup.ID
                    });
                }
            }

            return results;
        }

        public FolderStatusDto GetFolderStatus(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new FolderStatusDto
                {
                    Path = path,
                    Status = FolderBackupStatusValues.NotInBackup
                };
            }

            var normalizedPath = NormalizePath(path);
            var activeBackupIds = GetActiveBackupIds();

            foreach (var backup in _backups)
            {
                if (backup.Sources == null)
                    continue;

                foreach (var source in backup.Sources)
                {
                    if (string.IsNullOrEmpty(source))
                        continue;

                    var normalizedSource = NormalizePath(source);

                    if (string.Equals(normalizedPath, normalizedSource, StringComparison.OrdinalIgnoreCase) ||
                        normalizedPath.StartsWith(normalizedSource + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        var status = DetermineBackupStatus(backup, activeBackupIds);
                        var lastBackupTime = GetLastBackupTime(backup);

                        return new FolderStatusDto
                        {
                            Path = path,
                            Status = status,
                            BackupName = backup.Name,
                            LastBackupTime = lastBackupTime,
                            BackupId = backup.ID
                        };
                    }
                }
            }

            return new FolderStatusDto
            {
                Path = path,
                Status = FolderBackupStatusValues.NotInBackup
            };
        }

        private HashSet<string> GetActiveBackupIds()
        {
            var activeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var currentTask = _queueRunnerService.GetCurrentTask();
            if (currentTask?.BackupID != null &&
                currentTask.Operation == DuplicatiOperation.Backup)
            {
                activeIds.Add(currentTask.BackupID);
            }

            var queuedTasks = _queueRunnerService.GetCurrentTasks();
            foreach (var task in queuedTasks)
            {
                if (task.BackupID != null &&
                    task.Operation == DuplicatiOperation.Backup)
                {
                    activeIds.Add(task.BackupID);
                }
            }

            return activeIds;
        }

        private string DetermineBackupStatus(IBackup backup, HashSet<string> activeBackupIds)
        {
            if (backup.ID != null && activeBackupIds.Contains(backup.ID))
            {
                return FolderBackupStatusValues.InProgress;
            }

            if (backup.Metadata == null ||
                !backup.Metadata.TryGetValue("LastBackupDate", out var lastDateStr) ||
                string.IsNullOrEmpty(lastDateStr))
            {
                return FolderBackupStatusValues.Never;
            }

            if (backup.Metadata.TryGetValue("LastErrorDate", out var errorDate) &&
                !string.IsNullOrEmpty(errorDate))
            {
                if (DateTime.TryParse(lastDateStr, out var lastDt) &&
                    DateTime.TryParse(errorDate, out var errorDt) &&
                    Math.Abs((lastDt - errorDt).TotalMinutes) < 1)
                {
                    return FolderBackupStatusValues.Failed;
                }
            }

            if (backup.Metadata.TryGetValue("LastWarningDate", out var warningDate) &&
                !string.IsNullOrEmpty(warningDate))
            {
                if (DateTime.TryParse(lastDateStr, out var lastDt) &&
                    DateTime.TryParse(warningDate, out var warningDt) &&
                    Math.Abs((lastDt - warningDt).TotalMinutes) < 1)
                {
                    return FolderBackupStatusValues.Warning;
                }
            }

            return FolderBackupStatusValues.BackedUp;
        }

        private DateTime? GetLastBackupTime(IBackup backup)
        {
            if (backup.Metadata != null &&
                backup.Metadata.TryGetValue("LastBackupDate", out var dateStr) &&
                DateTime.TryParse(dateStr, out var date))
            {
                return date;
            }

            return null;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            try
            {
                return System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }
        }
    }

    #endregion

    #region FolderStatusDto Tests

    [Test]
    [Category("FolderStatus")]
    public void FolderStatusDto_RecordEquality_WorksCorrectly()
    {
        var dto1 = new FolderStatusDto
        {
            Path = "/test/path",
            Status = FolderBackupStatusValues.BackedUp,
            BackupName = "Test Backup",
            BackupId = "1"
        };

        var dto2 = new FolderStatusDto
        {
            Path = "/test/path",
            Status = FolderBackupStatusValues.BackedUp,
            BackupName = "Test Backup",
            BackupId = "1"
        };

        Assert.AreEqual(dto1, dto2);
    }

    [Test]
    [Category("FolderStatus")]
    public void FolderStatusDto_RecordInequality_WorksCorrectly()
    {
        var dto1 = new FolderStatusDto
        {
            Path = "/test/path",
            Status = FolderBackupStatusValues.BackedUp,
            BackupName = "Test Backup",
            BackupId = "1"
        };

        var dto2 = new FolderStatusDto
        {
            Path = "/test/path",
            Status = FolderBackupStatusValues.Failed, // Different status
            BackupName = "Test Backup",
            BackupId = "1"
        };

        Assert.AreNotEqual(dto1, dto2);
    }

    [Test]
    [Category("FolderStatus")]
    public void FolderBackupStatusValues_ContainsExpectedValues()
    {
        Assert.AreEqual("notinbackup", FolderBackupStatusValues.NotInBackup);
        Assert.AreEqual("backedup", FolderBackupStatusValues.BackedUp);
        Assert.AreEqual("warning", FolderBackupStatusValues.Warning);
        Assert.AreEqual("failed", FolderBackupStatusValues.Failed);
        Assert.AreEqual("inprogress", FolderBackupStatusValues.InProgress);
        Assert.AreEqual("never", FolderBackupStatusValues.Never);
    }

    [Test]
    [Category("FolderStatus")]
    public void FolderStatusDto_JsonSerialization_RoundTrips()
    {
        var dto = new FolderStatusDto
        {
            Path = "/test/path",
            Status = FolderBackupStatusValues.BackedUp,
            BackupName = "Test Backup",
            BackupId = "1",
            LastBackupTime = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc)
        };

        var json = JsonSerializer.Serialize(dto);
        var deserialized = JsonSerializer.Deserialize<FolderStatusDto>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual(dto.Path, deserialized!.Path);
        Assert.AreEqual(dto.Status, deserialized.Status);
        Assert.AreEqual(dto.BackupName, deserialized.BackupName);
        Assert.AreEqual(dto.BackupId, deserialized.BackupId);
        Assert.AreEqual(dto.LastBackupTime, deserialized.LastBackupTime);
    }

    [Test]
    [Category("FolderStatus")]
    public void FolderStatusDto_JsonDeserialization_HandlesNullOptionalFields()
    {
        var json = """{"Path":"/test/path","Status":"backedup"}""";
        var deserialized = JsonSerializer.Deserialize<FolderStatusDto>(json);

        Assert.IsNotNull(deserialized);
        Assert.AreEqual("/test/path", deserialized!.Path);
        Assert.AreEqual("backedup", deserialized.Status);
        Assert.IsNull(deserialized.BackupName);
        Assert.IsNull(deserialized.BackupId);
        Assert.IsNull(deserialized.LastBackupTime);
    }

    #endregion

    #region GetFolderStatus Tests

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_EmptyPath_ReturnsNotInBackup()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var service = new TestableFolderStatusService(Array.Empty<IBackup>(), mockQueueRunner);

        var result = service.GetFolderStatus(string.Empty);

        Assert.AreEqual(FolderBackupStatusValues.NotInBackup, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_NullPath_ReturnsNotInBackup()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var service = new TestableFolderStatusService(Array.Empty<IBackup>(), mockQueueRunner);

        var result = service.GetFolderStatus(null!);

        Assert.AreEqual(FolderBackupStatusValues.NotInBackup, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_PathNotInBackup_ReturnsNotInBackup()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { "/backup/source" }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus("/some/other/path");

        Assert.AreEqual(FolderBackupStatusValues.NotInBackup, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_PathMatchesSource_ReturnsCorrectStatus()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var lastBackupDate = DateTime.Now.ToString("o");
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", lastBackupDate }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.BackedUp, result.Status);
        Assert.AreEqual("Test Backup", result.BackupName);
        Assert.AreEqual("1", result.BackupId);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_SubdirectoryOfSource_ReturnsCorrectStatus()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var lastBackupDate = DateTime.Now.ToString("o");
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", lastBackupDate }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var subdir = System.IO.Path.Combine(tempPath, "subdir");
        var result = service.GetFolderStatus(subdir);

        Assert.AreEqual(FolderBackupStatusValues.BackedUp, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_BackupNeverRun_ReturnsNever()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>() // No LastBackupDate
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.Never, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_BackupInProgress_ReturnsInProgress()
    {
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var mockQueueRunner = new MockQueueRunnerService
        {
            CurrentTask = new MockQueuedTask
            {
                BackupID = "1",
                Operation = DuplicatiOperation.Backup
            }
        };
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", DateTime.Now.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.InProgress, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_BackupFailed_ReturnsFailed()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backupTime = DateTime.Now;
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", backupTime.ToString("o") },
                    { "LastErrorDate", backupTime.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.Failed, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_BackupWithWarning_ReturnsWarning()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backupTime = DateTime.Now;
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", backupTime.ToString("o") },
                    { "LastWarningDate", backupTime.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.Warning, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_OldError_ReturnsBackedUp()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backupTime = DateTime.Now;
        var oldErrorTime = backupTime.AddHours(-1);
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", backupTime.ToString("o") },
                    { "LastErrorDate", oldErrorTime.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.BackedUp, result.Status);
    }

    #endregion

    #region GetAllFolderStatuses Tests

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_NoBackups_ReturnsEmpty()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var service = new TestableFolderStatusService(Array.Empty<IBackup>(), mockQueueRunner);

        var result = service.GetAllFolderStatuses();

        Assert.IsEmpty(result);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_SingleBackupWithSources_ReturnsAllSources()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var lastBackupDate = DateTime.Now.ToString("o");
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { "/path/one", "/path/two", "/path/three" },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", lastBackupDate }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.All(r => r.BackupName == "Test Backup"));
        Assert.IsTrue(result.All(r => r.Status == FolderBackupStatusValues.BackedUp));
    }

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_MultipleBackups_ReturnsAllSources()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var lastBackupDate = DateTime.Now.ToString("o");
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Backup 1",
                Sources = new[] { "/path/one" },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", lastBackupDate }
                }
            },
            new MockBackup
            {
                ID = "2",
                Name = "Backup 2",
                Sources = new[] { "/path/two" },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", lastBackupDate }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(r => r.BackupName == "Backup 1" && r.Path == "/path/one"));
        Assert.IsTrue(result.Any(r => r.BackupName == "Backup 2" && r.Path == "/path/two"));
    }

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_SkipsNullAndEmptySources()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var lastBackupDate = DateTime.Now.ToString("o");
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { "/path/one", null!, "", "/path/two" },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", lastBackupDate }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(2, result.Count);
        Assert.IsTrue(result.Any(r => r.Path == "/path/one"));
        Assert.IsTrue(result.Any(r => r.Path == "/path/two"));
    }

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_BackupWithNullSources_SkipsBackup()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = null!
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetAllFolderStatuses().ToList();

        Assert.IsEmpty(result);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_QueuedBackup_ReturnsInProgress()
    {
        var mockQueueRunner = new MockQueueRunnerService
        {
            QueuedTasks = new List<IQueuedTask>
            {
                new MockQueuedTask
                {
                    BackupID = "1",
                    Operation = DuplicatiOperation.Backup
                }
            }
        };
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { "/path/one" },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", DateTime.Now.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(FolderBackupStatusValues.InProgress, result[0].Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetAllFolderStatuses_NonBackupOperation_NotConsideredInProgress()
    {
        var mockQueueRunner = new MockQueueRunnerService
        {
            CurrentTask = new MockQueuedTask
            {
                BackupID = "1",
                Operation = DuplicatiOperation.Restore // Not a backup operation
            }
        };
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { "/path/one" },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", DateTime.Now.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetAllFolderStatuses().ToList();

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(FolderBackupStatusValues.BackedUp, result[0].Status);
    }

    #endregion

    #region LastBackupTime Tests

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_ValidLastBackupDate_ReturnsLastBackupTime()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var expectedTime = new DateTime(2025, 1, 15, 10, 30, 0);
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", expectedTime.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.IsNotNull(result.LastBackupTime);
        Assert.AreEqual(expectedTime, result.LastBackupTime!.Value);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_InvalidLastBackupDate_ReturnsNever()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", "not-a-valid-date" }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        // Status should be "never" because the date couldn't be parsed
        Assert.AreEqual(FolderBackupStatusValues.Never, result.Status);
    }

    #endregion

    #region Edge Cases

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_NullMetadata_ReturnsNever()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = null!
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        Assert.AreEqual(FolderBackupStatusValues.Never, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_CaseInsensitivePath_FindsBackup()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath.ToLowerInvariant() },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", DateTime.Now.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(tempPath.ToUpperInvariant());

        Assert.AreEqual(FolderBackupStatusValues.BackedUp, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_ErrorAndWarningAtSameTime_ErrorTakesPrecedence()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backupTime = DateTime.Now;
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", backupTime.ToString("o") },
                    { "LastErrorDate", backupTime.ToString("o") },
                    { "LastWarningDate", backupTime.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        // Error should take precedence over warning
        Assert.AreEqual(FolderBackupStatusValues.Failed, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_EmptySourcesArray_ReturnsNotInBackup()
    {
        var mockQueueRunner = new MockQueueRunnerService();
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = Array.Empty<string>(),
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", DateTime.Now.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus("/any/path");

        Assert.AreEqual(FolderBackupStatusValues.NotInBackup, result.Status);
    }

    [Test]
    [Category("FolderStatus")]
    public void GetFolderStatus_NoMatchingBackupId_BackupNotConsideredInProgress()
    {
        var tempPath = System.IO.Path.GetTempPath().TrimEnd(System.IO.Path.DirectorySeparatorChar);
        var mockQueueRunner = new MockQueueRunnerService
        {
            CurrentTask = new MockQueuedTask
            {
                BackupID = "different-id",
                Operation = DuplicatiOperation.Backup
            }
        };
        var backups = new[]
        {
            new MockBackup
            {
                ID = "1",
                Name = "Test Backup",
                Sources = new[] { tempPath },
                Metadata = new Dictionary<string, string>
                {
                    { "LastBackupDate", DateTime.Now.ToString("o") }
                }
            }
        };
        var service = new TestableFolderStatusService(backups, mockQueueRunner);

        var result = service.GetFolderStatus(System.IO.Path.GetTempPath());

        // Should be BackedUp, not InProgress, because the running backup has a different ID
        Assert.AreEqual(FolderBackupStatusValues.BackedUp, result.Status);
    }

    #endregion
}
