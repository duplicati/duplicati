using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.Operation.Sync;

/// <summary>
/// The layout of a snapshot-style sync destination (<see cref="Options.SyncSnapshotFolders"/>):
/// one folder per run below the destination root, named after the UTC start time of the
/// run, and a marker file in the root that names the latest completed snapshot. Shared by
/// the sync handler, which writes the layout, and the sync restore handler, which reads it.
/// </summary>
internal static class SyncSnapshotLayout
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Logging.Log.LogTagFromType(typeof(SyncSnapshotLayout));

    /// <summary>
    /// The format of a snapshot folder name: the UTC start time of the run. It sorts
    /// lexically in time order and contains no character that is invalid in a path on
    /// any supported platform.
    /// </summary>
    public const string FolderNameFormat = "yyyy-MM-dd'T'HHmmss'Z'";

    /// <summary>
    /// The name of the marker file in the destination root. It holds the name of the
    /// latest completed snapshot folder, so a browser or a later run can find it.
    /// </summary>
    public const string MarkerFileName = "duplicati-latest-snapshot.txt";

    /// <summary>
    /// Formats the snapshot folder name for a point in time.
    /// </summary>
    /// <param name="utc">The time, in UTC</param>
    /// <returns>The folder name</returns>
    public static string FormatFolderName(DateTime utc)
        => utc.ToUniversalTime().ToString(FolderNameFormat, CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a snapshot folder name.
    /// </summary>
    /// <param name="name">The folder name, with or without a trailing separator</param>
    /// <param name="utc">The time the snapshot was started, in UTC</param>
    /// <returns><c>true</c> if the name is a snapshot folder name</returns>
    public static bool TryParseFolderName(string? name, out DateTime utc)
    {
        utc = default;
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return DateTime.TryParseExact(
            name.TrimEnd('/', '\\'),
            FolderNameFormat,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out utc);
    }

    /// <summary>
    /// Joins a snapshot folder name and a relative path below it.
    /// </summary>
    /// <param name="snapshot">The snapshot folder name</param>
    /// <param name="relativePath">The relative path below the snapshot, or empty for the snapshot folder itself</param>
    /// <returns>The relative path from the destination root</returns>
    public static string Combine(string snapshot, string relativePath)
        => string.IsNullOrEmpty(relativePath) ? snapshot : snapshot + "/" + relativePath;

    /// <summary>
    /// What the destination root holds: the snapshot folders, oldest first, and the
    /// snapshot the marker file names, if the marker exists and names one of them.
    /// </summary>
    /// <param name="Snapshots">The snapshot folder names, sorted oldest first</param>
    /// <param name="MarkedLatest">The latest completed snapshot according to the marker file, or <c>null</c></param>
    public sealed record RootState(IReadOnlyList<string> Snapshots, string? MarkedLatest)
    {
        /// <summary>
        /// The latest completed snapshot: the one the marker names, or, without a usable
        /// marker, the newest snapshot folder. <c>null</c> when there are no snapshots.
        /// </summary>
        public string? Latest => MarkedLatest ?? (Snapshots.Count > 0 ? Snapshots[^1] : null);

        /// <summary>
        /// The snapshots that a run completed: every snapshot up to and including
        /// <see cref="Latest"/>. Newer folders belong to runs that were interrupted
        /// before they finished.
        /// </summary>
        public IEnumerable<string> CompletedSnapshots
            => Latest == null ? [] : Snapshots.Where(x => string.CompareOrdinal(x, Latest) <= 0);
    }

    /// <summary>
    /// Lists the destination root and reads the marker file.
    /// </summary>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The state of the destination root</returns>
    public static async Task<RootState> ReadRootStateAsync(IBackendManager backendManager, CancellationToken cancellationToken)
    {
        var snapshots = new List<string>();
        var hasMarker = false;
        foreach (var entry in await backendManager.ListAsync(null, cancellationToken).ConfigureAwait(false))
        {
            var name = entry.Name?.TrimEnd('/', '\\');
            if (entry.IsFolder && TryParseFolderName(name, out _))
                snapshots.Add(name!);
            else if (!entry.IsFolder && string.Equals(name, MarkerFileName, StringComparison.Ordinal))
                hasMarker = true;
        }
        snapshots.Sort(StringComparer.Ordinal);

        string? marked = null;
        if (hasMarker)
        {
            try
            {
                using var tf = await backendManager.GetFileUnencryptedWithPathAsync(MarkerFileName, cancellationToken).ConfigureAwait(false);
                var content = (await File.ReadAllTextAsync(tf, Encoding.UTF8, cancellationToken).ConfigureAwait(false)).Trim();
                if (TryParseFolderName(content, out _) && snapshots.Contains(content, StringComparer.Ordinal))
                    marked = content;
                else
                    Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotMarkerInvalid", null, "The snapshot marker file {0} names \"{1}\", which is not a snapshot folder on the destination; using the newest snapshot folder instead.", MarkerFileName, content);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logging.Log.WriteWarningMessage(LOGTAG, "SnapshotMarkerReadFailed", ex, "Failed to read the snapshot marker file {0}; using the newest snapshot folder instead.", MarkerFileName);
            }
        }

        return new RootState(snapshots, marked);
    }

    /// <summary>
    /// Writes the marker file so it names the given snapshot.
    /// </summary>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="snapshot">The snapshot folder name</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public static async Task WriteMarkerAsync(IBackendManager backendManager, string snapshot, CancellationToken cancellationToken)
    {
        using var tf = new TempFile();
        await File.WriteAllTextAsync(tf, snapshot, new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
        await backendManager.PutFileUnencryptedWithPathAsync(MarkerFileName, tf, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes a snapshot folder with everything in it, see <see cref="SyncRemoteFolders.DeleteTreeAsync"/>.
    /// </summary>
    /// <param name="backendManager">The backend manager of the destination</param>
    /// <param name="snapshot">The snapshot folder name</param>
    /// <param name="cancellationToken">The cancellation token</param>
    public static Task DeleteSnapshotAsync(IBackendManager backendManager, string snapshot, CancellationToken cancellationToken)
    {
        if (!TryParseFolderName(snapshot, out _))
            throw new ArgumentException($"Not a snapshot folder name: {snapshot}", nameof(snapshot));

        return SyncRemoteFolders.DeleteTreeAsync(backendManager, snapshot, cancellationToken);
    }
}
