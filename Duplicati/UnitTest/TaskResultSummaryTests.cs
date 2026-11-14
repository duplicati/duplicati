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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Duplicati.Library.Interface;
using Duplicati.WebserverCore.Abstractions;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// Implements an interface from a table of values, for interfaces with more members than a test cares about.
/// A member without a value returns the default of its type.
/// </summary>
public class InterfaceStub : DispatchProxy
{
    /// <summary>
    /// The values of the members, by member name
    /// </summary>
    private Dictionary<string, object?> _values = new();

    /// <summary>
    /// Creates an implementation of an interface
    /// </summary>
    /// <typeparam name="T">The interface</typeparam>
    /// <param name="values">The values of the members, by member name</param>
    /// <returns>The implementation</returns>
    public static T Create<T>(Dictionary<string, object?> values) where T : class
    {
        var proxy = Create<T, InterfaceStub>();
        ((InterfaceStub)(object)proxy)._values = values;
        return proxy;
    }

    /// <inheritdoc/>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        var name = targetMethod!.Name.StartsWith("get_", StringComparison.Ordinal) ? targetMethod.Name.Substring(4) : targetMethod.Name;
        if (_values.TryGetValue(name, out var value))
            return value;

        var type = targetMethod.ReturnType;
        return type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
    }
}

/// <summary>
/// The summary of a finished task that is kept in the task result cache and returned by the MCP tools
/// </summary>
[TestFixture]
[Category("Mcp")]
public class TaskResultSummaryTests
{
    /// <summary>
    /// A time with a known offset to UTC in the time zone of the machine
    /// </summary>
    private static readonly DateTime VersionTimeUtc = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    /// <summary>
    /// Creates the results of a backup
    /// </summary>
    /// <param name="overrides">The members that differ from a complete, successful backup</param>
    /// <returns>The results</returns>
    private static IBackupResults BackupResults(Dictionary<string, object?>? overrides = null)
    {
        var values = new Dictionary<string, object?>
        {
            [nameof(IBasicResults.ParsedResult)] = ParsedResultType.Success,
            [nameof(IBasicResults.BeginTime)] = VersionTimeUtc,
            [nameof(IBasicResults.EndTime)] = VersionTimeUtc.AddMinutes(1),
            [nameof(IBasicResults.Warnings)] = Array.Empty<string>(),
            [nameof(IBasicResults.Errors)] = Array.Empty<string>(),
            [nameof(IBackupResults.ExaminedFiles)] = 10L,
            [nameof(IBackupResults.SizeOfExaminedFiles)] = 1000L,
            [nameof(IBackupResults.AddedFiles)] = 3L,
            [nameof(IBackupResults.ModifiedFiles)] = 2L,
            [nameof(IBackupResults.DeletedFiles)] = 1L,
            [nameof(IBackupResults.FilesWithError)] = 4L,
            [nameof(IBackendStatsticsReporter.BackendStatistics)] = BackendStatistics(VersionTimeUtc.ToLocalTime())
        };
        foreach (var o in overrides ?? [])
            values[o.Key] = o.Value;

        return InterfaceStub.Create<IBackupResults>(values);
    }

    /// <summary>
    /// Creates backend statistics with the time of the newest version
    /// </summary>
    /// <param name="lastBackupDate">The time of the newest version</param>
    /// <returns>The statistics</returns>
    private static IParsedBackendStatistics BackendStatistics(DateTime lastBackupDate)
        => InterfaceStub.Create<IParsedBackendStatistics>(new() { [nameof(IParsedBackendStatistics.LastBackupDate)] = lastBackupDate });

    [Test]
    public void NoResultsGiveNoSummary()
        => Assert.That(TaskResultSummary.FromResults(null), Is.Null);

    [Test]
    public void TimesAreReportedInUtc()
    {
        var begin = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Local);
        var summary = TaskResultSummary.FromResults(InterfaceStub.Create<IBasicResults>(new()
        {
            [nameof(IBasicResults.ParsedResult)] = ParsedResultType.Warning,
            [nameof(IBasicResults.BeginTime)] = begin,
            [nameof(IBasicResults.EndTime)] = begin.AddHours(1),
            [nameof(IBasicResults.Interrupted)] = true
        }))!;

        Assert.That(summary.ParsedResult, Is.EqualTo(ParsedResultType.Warning));
        Assert.That(summary.BeginTime, Is.EqualTo(begin.ToUniversalTime()));
        Assert.That(summary.BeginTime.Kind, Is.EqualTo(DateTimeKind.Utc));
        Assert.That(summary.EndTime, Is.EqualTo(begin.AddHours(1).ToUniversalTime()));
        Assert.That(summary.Interrupted, Is.True);
        Assert.That(summary.Backup, Is.Null, "Results that are not from a backup have no backup numbers");
        Assert.That(summary.Restore, Is.Null, "Results that are not from a restore have no restore numbers");
    }

    [Test]
    public void MissingMessageListsAreEmpty()
    {
        var summary = TaskResultSummary.FromResults(InterfaceStub.Create<IBasicResults>(new()))!;

        Assert.That(summary.WarningCount, Is.Zero);
        Assert.That(summary.Warnings, Is.Empty);
        Assert.That(summary.ErrorCount, Is.Zero);
        Assert.That(summary.Errors, Is.Empty);
    }

    [Test]
    public void MessagesAreCountedInFullAndKeptInPart()
    {
        var warnings = Enumerable.Range(0, TaskResultSummary.MaxMessages + 3).Select(x => $"warning {x}").ToArray();
        var longError = new string('e', TaskResultSummary.MaxMessageLength + 1);
        var exactError = new string('x', TaskResultSummary.MaxMessageLength);
        var summary = TaskResultSummary.FromResults(InterfaceStub.Create<IBasicResults>(new()
        {
            [nameof(IBasicResults.Warnings)] = warnings,
            [nameof(IBasicResults.Errors)] = new[] { longError, exactError }
        }))!;

        Assert.That(summary.WarningCount, Is.EqualTo(warnings.Length));
        Assert.That(summary.Warnings, Is.EqualTo(warnings.Take(TaskResultSummary.MaxMessages)));
        Assert.That(summary.ErrorCount, Is.EqualTo(2));
        Assert.That(summary.Errors[0], Is.EqualTo(new string('e', TaskResultSummary.MaxMessageLength) + "…"), "A message over the limit is cut and marked");
        Assert.That(summary.Errors[1], Is.EqualTo(exactError), "A message at the limit is kept as it is");
    }

    [Test]
    public void BackupNumbersAreCopied()
    {
        var summary = TaskResultSummary.FromResults(BackupResults())!;

        Assert.That(summary.Restore, Is.Null);
        Assert.That(summary.Backup, Is.EqualTo(new BackupResultSummary(10, 1000, 3, 2, 1, 4, false, VersionTimeUtc)));
        Assert.That(summary.Backup!.VersionTime!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
    }

    /// <summary>
    /// The newest version is recorded by the last step of a backup, so a backup that did not get
    /// that far has the version from before it started, which is not what it captured
    /// </summary>
    [TestCase(nameof(IBasicResults.Interrupted))]
    [TestCase(nameof(IBackupResults.PartialBackup))]
    [TestCase(nameof(IBackupResults.Dryrun))]
    public void BackupThatDidNotFinishHasNoVersionTime(string flag)
    {
        var summary = TaskResultSummary.FromResults(BackupResults(new() { [flag] = true }))!;

        Assert.That(summary.Backup, Is.Not.Null);
        Assert.That(summary.Backup!.VersionTime, Is.Null);
    }

    [Test]
    public void BackupWithoutVersionsHasNoVersionTime()
    {
        // The zero time is what a backup without versions reports, also after a conversion to local time
        foreach (var zero in new[] { new DateTime(0), new DateTime(0).ToLocalTime(), new DateTime(0, DateTimeKind.Utc) })
            Assert.That(TaskResultSummary.FromResults(BackupResults(new() { [nameof(IBackendStatsticsReporter.BackendStatistics)] = BackendStatistics(zero) }))!.Backup!.VersionTime, Is.Null);

        Assert.That(TaskResultSummary.FromResults(BackupResults(new() { [nameof(IBackendStatsticsReporter.BackendStatistics)] = null }))!.Backup!.VersionTime, Is.Null,
            "Results without backend statistics do not hold the time");
        Assert.That(TaskResultSummary.FromResults(BackupResults(new() { [nameof(IBackendStatsticsReporter.BackendStatistics)] = InterfaceStub.Create<IBackendStatstics>(new()) }))!.Backup!.VersionTime, Is.Null,
            "Statistics that were not parsed do not hold the time");
    }

    /// <summary>
    /// The time of the version is given to the reader as the restore point, and only for a backup that
    /// succeeded and a version that still exists, so it is not part of the summary as JSON
    /// </summary>
    [Test]
    public void VersionTimeIsNotWrittenAsJson()
    {
        var json = JsonSerializer.Serialize(TaskResultSummary.FromResults(BackupResults())!);

        Assert.That(json, Does.Contain(nameof(BackupResultSummary.AddedFiles)));
        Assert.That(json, Does.Not.Contain(nameof(BackupResultSummary.VersionTime)));
    }

    [Test]
    public void RestoreNumbersAreCopied()
    {
        var values = new Dictionary<string, object?>
        {
            [nameof(IRestoreResults.RestoredFiles)] = 5L,
            [nameof(IRestoreResults.SizeOfRestoredFiles)] = 500L,
            [nameof(IRestoreResults.RestoredFolders)] = 2L,
            [nameof(IRestoreResults.RestoredSymlinks)] = 1L,
            [nameof(IRestoreResults.UnmodifiedFiles)] = 7L,
            [nameof(IRestoreResults.RestorePath)] = "/restore/here"
        };
        var summary = TaskResultSummary.FromResults(InterfaceStub.Create<IRestoreResults>(values))!;

        Assert.That(summary.Backup, Is.Null);
        Assert.That(summary.Restore, Is.EqualTo(new RestoreResultSummary(5, 500, 2, 1, 7, "/restore/here")));

        // No restore path means the files went back to where they were backed up from
        values[nameof(IRestoreResults.RestorePath)] = "  ";
        Assert.That(TaskResultSummary.FromResults(InterfaceStub.Create<IRestoreResults>(values))!.Restore!.RestorePath, Is.Null);
    }
}
