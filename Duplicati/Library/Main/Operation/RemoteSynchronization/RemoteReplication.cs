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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Main.Backend;
using Duplicati.Library.Main.Database.Sync;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.Main.Operation.Sync;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.Operation.RemoteSynchronization;

/// <summary>
/// A destination that a Duplicati destination is mirrored to.
/// </summary>
/// <param name="Url">The url of the destination</param>
/// <param name="Options">The options of the destination, laid over the options of the source</param>
/// <param name="SyncDatabasePath">The path of the sync database of the destination, or <c>null</c> to use the one registered for the url</param>
/// <param name="DryRun">Whether nothing is written to or deleted from the destination</param>
/// <param name="Force">Whether every volume is uploaded again without looking at what the destination holds</param>
public sealed record RemoteReplicationTarget(string Url, Dictionary<string, string?> Options, string? SyncDatabasePath, bool DryRun, bool Force)
{
    /// <summary>
    /// The destination url with any credentials removed, safe to include in log messages.
    /// </summary>
    public string DisplayDestination => Library.Utility.Utility.GetUrlWithoutCredentials(Url);

    /// <summary>
    /// Describes the destination without the url credentials or the options, which carry
    /// backend credentials. The generated record ToString would print the url as it is.
    /// </summary>
    public override string ToString()
        => $"Url: {DisplayDestination}, DryRun: {DryRun}, Force: {Force}";
}

/// <summary>
/// What a replication run did to one destination.
/// </summary>
/// <param name="Destination">The destination, without credentials</param>
/// <param name="Copied">The number of volumes copied to the destination</param>
/// <param name="CopiedSize">The size of the volumes copied</param>
/// <param name="Deleted">The number of volumes deleted from the destination</param>
/// <param name="UploadFailures">The number of volumes that could not be copied</param>
/// <param name="DeleteFailures">The number of volumes that could not be deleted</param>
/// <param name="Failed">Whether the destination failed as a whole</param>
internal sealed record RemoteReplicationSummary(string Destination, long Copied, long CopiedSize, long Deleted, long UploadFailures, long DeleteFailures, bool Failed)
{
    /// <summary>
    /// Whether the destination now mirrors the source
    /// </summary>
    public bool Succeeded => !Failed && UploadFailures == 0 && DeleteFailures == 0;
}

/// <summary>
/// Mirrors a Duplicati destination to other destinations. A Duplicati destination
/// is flat: every volume is a direct child of the destination folder, so mirroring
/// it is a sync of one folder, without recursion, of the files whose names are
/// volume names. This is the one entry point for that, used by the remote
/// synchronization after a backup and by the sync tool.
/// </summary>
internal static class RemoteReplication
{
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(RemoteReplication));

    /// <summary>
    /// Builds the filter that selects the volumes of a backup: the first entry includes
    /// the names that are volume names, the second excludes everything else. Folders
    /// never match the first, so nothing below the destination folder is touched.
    /// </summary>
    /// <param name="prefix">The prefix of the backup, or <c>null</c> to select the volumes of any backup</param>
    /// <returns>The filter</returns>
    public static IFilter CreateVolumeFilter(string? prefix)
    {
        // The pattern of VolumeBase.ParsedVolume, anchored to the last path segment.
        // A parity file is named after its volume with one more extension. The inline
        // option makes the match independent of the case rules of the host file
        // system, which the filter otherwise follows.
        var prefixPattern = prefix == null ? @"[^\-/\\]+" : Regex.Escape(prefix);
        var pattern = @"(?i)^(.*[/\\])?" + prefixPattern + @"\-(([ib][0-9a-f]+)|(\d{8}T\d{6}Z))\.((dblock)|(dindex)|(dlist))\.[^./\\]+(\.[\w\-]+){0,2}$";

        return JoinedFilterExpression.Join(
            new FilterExpression("[" + pattern + "]", true),
            new FilterExpression("*", false));
    }

    /// <summary>
    /// Mirrors the volumes of the source to every target: volumes that are missing
    /// or changed are copied, and volumes the source no longer has are deleted. The
    /// source is listed once, and each target once.
    /// </summary>
    /// <param name="sourceUrl">The url of the destination to mirror</param>
    /// <param name="sourceOptions">The options of the source</param>
    /// <param name="targetBaseOptions">The options every target inherits, before its own options; the ones that only describe the job of the source are left out</param>
    /// <param name="targets">The targets</param>
    /// <param name="backendWriter">The writer that receives the backend events of the source</param>
    /// <param name="targetWriters">The writers that receive the backend events of each target, or <c>null</c> to use <paramref name="backendWriter"/> for all</param>
    /// <param name="taskReader">The task reader that pauses and stops the transfers</param>
    /// <param name="progress">The progress updater that receives file counts and per-file progress</param>
    /// <param name="onTargetStarted">Invoked with the index of the target and the number of targets when the work moves to another target</param>
    /// <param name="anyPrefix">Whether the volumes of every backup in the source are mirrored, instead of those with the prefix of the options</param>
    /// <param name="token">The cancellation token</param>
    /// <returns>What was done to each target, in the order of <paramref name="targets"/></returns>
    public static async Task<IReadOnlyList<RemoteReplicationSummary>> RunAsync(
        string sourceUrl, Dictionary<string, string?> sourceOptions,
        IReadOnlyDictionary<string, string?> targetBaseOptions,
        IReadOnlyList<RemoteReplicationTarget> targets,
        IBackendWriter backendWriter, IReadOnlyList<IBackendWriter>? targetWriters, ITaskReader taskReader,
        IOperationProgressUpdater progress, Action<int, int>? onTargetStarted,
        bool anyPrefix,
        CancellationToken token)
    {
        if (targetWriters != null && targetWriters.Count != targets.Count)
            throw new ArgumentException("There must be one writer for each target", nameof(targetWriters));

        var summaries = new RemoteReplicationSummary?[targets.Count];
        var syncTargets = new List<SyncTarget>();
        var syncTargetIndex = new List<int>();
        var caseInsensitive = Library.Utility.Utility.ParseBoolOption(sourceOptions, "case-insensitive-remote");

        // The source is only ever read from. Creating a missing source folder would
        // turn "the source is gone" into "the source is empty", and an empty source
        // deletes every volume in the targets.
        var sourceManagerOptions = new Dictionary<string, string?>(sourceOptions) { ["disable-autocreate-folder"] = "true" };
        var keepModificationTime = new Options(sourceOptions).SyncKeepModificationTime
            || Library.Utility.Utility.ParseBoolOption(targetBaseOptions, "sync-keep-modification-time");
        var filter = CreateVolumeFilter(anyPrefix ? null : new Options(sourceOptions).Prefix);

        // A dedicated manager, not the one of the operation this may be a step of:
        // its lifetime is this run.
        using var sourceManager = new BackendManager(sourceUrl, new Options(sourceManagerOptions), backendWriter, taskReader, collectDatabaseMessages: false);
        try
        {
            for (var i = 0; i < targets.Count; i++)
            {
                var target = targets[i];
                BackendManager? manager = null;
                try
                {
                    var rawOptions = RemoteSyncConfig.BuildTargetOptions(targetBaseOptions, target with { Options = [] }, key => key.StartsWith("sync-", StringComparison.OrdinalIgnoreCase));
                    // The volumes are copied as they are: they are already encrypted, or not, by the backup
                    rawOptions["no-encryption"] = "true";
                    foreach (var (key, value) in target.Options)
                        rawOptions[key] = value;

                    var targetOptions = new Options(rawOptions);
                    caseInsensitive |= targetOptions.CaseInsensitiveRemote;

                    manager = new BackendManager(target.Url, targetOptions, targetWriters?[i] ?? backendWriter, taskReader, collectDatabaseMessages: false);
                    var database = new LocalSyncDatabase(RemoteSyncConfig.LocateSyncDatabase(target, targetOptions, sourceOptions.GetValueOrDefault("dbpath")));
                    syncTargets.Add(new SyncTarget(
                        manager,
                        database,
                        target.DisplayDestination,
                        target.Force ? SyncRemoteState.BlindlyUpload : SyncRemoteState.UseRemoteState,
                        target.DryRun || targetOptions.Dryrun));
                    syncTargetIndex.Add(i);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    manager?.Dispose();
                    Logging.Log.WriteErrorMessage(LOGTAG, "ReplicationTargetSetupFailed", ex, "Failed to prepare the destination {0}: {1}", target.DisplayDestination, ex.Message);
                    summaries[i] = new RemoteReplicationSummary(target.DisplayDestination, 0, 0, 0, 0, 0, true);
                }
            }

            if (syncTargets.Count > 0)
            {
                // The run has its own options: what selects the source files of the
                // backup (attribute filters, ignore files, empty folders) has no say in
                // which volumes are mirrored. The two listings are matched against each
                // other, so one comparer answers for both sides and is applied if either
                // side asks for it, as the backup does.
                var handlerOptions = new Options(new Dictionary<string, string?>
                {
                    ["no-encryption"] = "true",
                    [Options.SYNC_THEN_DELETE_OPTION] = "true",
                    ["case-insensitive-remote"] = caseInsensitive ? "true" : "false",
                    ["sync-keep-modification-time"] = keepModificationTime ? "true" : "false",
                });

                using var sourceProvider = new BackendManagerSourceProvider(sourceManager, caseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                var result = await new SyncHandler(handlerOptions, progress).RunAsync(
                    sourceProvider,
                    [BackendManagerSourceProvider.ROOT_PATH],
                    syncTargets,
                    filter,
                    maxDepth: 0,
                    reportPhases: false,
                    onTargetStarted: onTargetStarted == null ? null : (index, _) => onTargetStarted(syncTargetIndex[index], targets.Count),
                    token).ConfigureAwait(false);

                // A run that was stopped did not mirror anything it can vouch for
                token.ThrowIfCancellationRequested();

                for (var i = 0; i < result.Targets.Count; i++)
                {
                    var ps = result.Targets[i];
                    summaries[syncTargetIndex[i]] = new RemoteReplicationSummary(ps.DisplayName, ps.Upload + ps.Update, ps.SizeOfUploadedFiles, ps.Delete, ps.UploadFailures, ps.DeleteFailures, ps.Failed);
                }
            }
        }
        finally
        {
            foreach (var target in syncTargets)
            {
                try
                {
                    target.BackendManager.Dispose();
                    target.Database.Dispose();
                }
                catch (Exception ex)
                {
                    Logging.Log.WriteWarningMessage(LOGTAG, "BackendDisposeFailed", ex, "Failed to release the destination {0}: {1}", target.DisplayName, ex.Message);
                }
            }
        }

        return summaries.Select(x => x!).ToList();
    }
}
