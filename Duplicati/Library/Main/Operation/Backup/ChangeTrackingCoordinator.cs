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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.DynamicLoader;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Library.Main.Database.Local;
using Duplicati.Library.Main.Operation.Common;
using Duplicati.Library.SourceProvider;
using Duplicati.Library.Utility;
using Duplicati.Library.Utility.Options;

namespace Duplicati.Library.Main.Operation.Backup;

/// <summary>
/// Connects the source providers that track their own changes to the backup:
/// loads the state stored with the previous fileset, answers the carry-forward
/// predicate by asking the provider that owns the path, and stores the exported
/// state with the new fileset.
/// </summary>
internal sealed class ChangeTrackingCoordinator
{
    /// <summary>
    /// The log tag for this class
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<ChangeTrackingCoordinator>();

    /// <summary>
    /// The number of newest filesets whose state is kept. Only the newest fileset's state is
    /// read, but the state of a few older ones is kept so a backup still continues
    /// incrementally if the user deletes the newest version: it then redoes the deleted
    /// backup from the state of the version before it.
    /// </summary>
    internal const int STATE_FILESETS_TO_KEEP = 3;

    /// <summary>
    /// A provider that takes part in change tracking
    /// </summary>
    /// <param name="Provider">The provider</param>
    /// <param name="MountedPath">The path the provider is mounted at, used to find the owner of a path</param>
    private sealed record TrackedProvider(IChangeTrackingSourceProvider Provider, string MountedPath)
    {
        /// <summary>
        /// Whether state from the previous fileset was handed to the provider
        /// </summary>
        public bool HasLoadedState => LoadedState.Count > 0;

        /// <summary>
        /// The state handed to the provider
        /// </summary>
        public IReadOnlyDictionary<string, string> LoadedState { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// The providers, ordered by the length of their mount paths so the most specific owner is found first
    /// </summary>
    private readonly List<TrackedProvider> _providers;

    /// <summary>
    /// The hash of the configuration that decides which paths are walked
    /// </summary>
    private readonly string _configHash;

    /// <summary>
    /// Creates a new coordinator
    /// </summary>
    /// <param name="providers">The providers that track changes</param>
    /// <param name="configHash">The hash of the current configuration</param>
    private ChangeTrackingCoordinator(List<TrackedProvider> providers, string configHash)
    {
        _providers = providers;
        _configHash = configHash;
    }

    /// <summary>
    /// The hash of the configuration that decides which paths are walked
    /// </summary>
    public string ConfigHash => _configHash;

    /// <summary>
    /// Whether any provider was handed state from the previous fileset, which means
    /// it may have enumerated only a subset of its entries
    /// </summary>
    public bool HasLoadedState => _providers.Any(x => x.HasLoadedState);

    /// <summary>
    /// Finds the providers that track changes in the source, if any
    /// </summary>
    /// <param name="source">The source provider, possibly a combination of providers</param>
    /// <param name="filter">The filter in use</param>
    /// <param name="sources">The source strings the backup was started with</param>
    /// <returns>The coordinator, or <c>null</c> if no provider tracks changes</returns>
    public static ChangeTrackingCoordinator? Create(ISourceProvider source, IFilter? filter, IEnumerable<string> sources)
    {
        var candidates = FileEnumerationProcess.GetProviders(source)
            .OfType<IChangeTrackingSourceProvider>()
            .ToList();

        // A provider's paths are attributed to it by its mount path, and the carry-forward
        // depends on that attribution. A provider without one is never handed state, so it
        // lists everything on every run and its paths count as examined
        foreach (var p in candidates.Where(x => string.IsNullOrWhiteSpace(x.MountedPath)))
            Log.WriteWarningMessage(LOGTAG, "ChangeTrackingDisabledWithoutMountPath", null, "The source provider with scope \"{0}\" tracks changes but has no mount path, so change tracking is disabled for it and it lists everything on every run", p.ChangeTrackingScope);

        var providers = candidates
            .Where(x => !string.IsNullOrWhiteSpace(x.MountedPath))
            .Select(x => new TrackedProvider(x, x.MountedPath))
            .OrderByDescending(x => x.MountedPath.Length)
            .ToList();

        if (providers.Count == 0)
            return null;

        var duplicateScopes = providers
            .GroupBy(x => x.Provider.ChangeTrackingScope, StringComparer.Ordinal)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();
        if (duplicateScopes.Count > 0)
            throw new UserInformationException($"Multiple sources share the change tracking scope \"{duplicateScopes[0]}\"", "DuplicateChangeTrackingScope");

        return new ChangeTrackingCoordinator(providers, ComputeConfigHash(filter, sources));
    }

    /// <summary>
    /// Computes the hash of the configuration that decides which paths are walked: the filter
    /// and the sources. The credentials are left out of the source urls so a rotated secret does
    /// not force a full walk, while any other change to a url does.
    /// </summary>
    /// <param name="filter">The filter in use</param>
    /// <param name="sources">The source strings the backup was started with</param>
    /// <returns>The hash as a hex string</returns>
    internal static string ComputeConfigHash(IFilter? filter, IEnumerable<string> sources)
    {
        var sanitizedSources = sources
            .Select(x => SourceProviderFactory.ParseRemoteSource(x) is { } remote
                ? $"@{remote.Mountpoint}|{StripSecrets(remote.Url)}"
                : x)
            .OrderBy(x => x, StringComparer.Ordinal);

        return Library.Utility.Utility.ByteArrayAsHexString(MD5HashHelper.GetHash(
            new[] { filter?.ToString() ?? string.Empty }.Concat(sanitizedSources)));
    }

    /// <summary>
    /// Removes the credentials from a source url and keeps every other option. The user info is
    /// dropped, as are the query values of the options the provider declares as passwords and
    /// of the generic authentication options, and the remaining query values are sorted so that
    /// their order does not matter. A url that cannot be parsed is reduced to its displayable
    /// form, which carries no credentials either.
    /// </summary>
    /// <param name="url">The source url</param>
    /// <returns>The url without credentials</returns>
    internal static string StripSecrets(string url)
    {
        RelaxedUri uri;
        try
        {
            uri = new RelaxedUri(url);
        }
        catch
        {
            // An invalid url is reported when the provider is loaded
            return Library.Utility.Utility.GetUrlWithoutCredentials(url);
        }

        var secrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { AuthOptionsHelper.AuthPasswordOption, AuthIdOptionsHelper.AuthIdOption };
        try
        {
            foreach (var command in SourceProviderLoader.GetSupportedCommands(url) ?? [])
            {
                if (command.Type != CommandLineArgument.ArgumentType.Password)
                    continue;

                secrets.Add(command.Name);
                foreach (var alias in command.Aliases ?? [])
                    secrets.Add(alias);
            }
        }
        catch
        {
            // An unsupported url is reported when the provider is loaded
        }

        var query = uri.QueryParameters;
        var kept = query.AllKeys
            .Where(key => !string.IsNullOrEmpty(key) && !secrets.Contains(key))
            .OrderBy(key => key, StringComparer.Ordinal)
            .Select(key => $"{Uri.EscapeDataString(key!)}={Uri.EscapeDataString(query[key] ?? string.Empty)}")
            .ToList();

        return new RelaxedUri(uri.Scheme, uri.Host, uri.Path, kept.Count == 0 ? null : string.Join("&", kept), null, null, uri.Port, uri.PathIsEncoded).ToString();
    }

    /// <summary>
    /// Loads the state stored with the previous fileset and hands it to the providers.
    /// A provider whose stored state was recorded under another configuration, or that
    /// has no stored state, is handed an empty set and walks everything.
    /// </summary>
    /// <param name="database">The database to read from</param>
    /// <param name="lastFilesetId">The previous fileset, or a negative value if there is none</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task LoadStateAsync(LocalBackupDatabase database, long lastFilesetId, CancellationToken cancellationToken)
    {
        foreach (var tracked in _providers)
        {
            var scope = tracked.Provider.ChangeTrackingScope;
            IReadOnlyDictionary<string, string> rows = new Dictionary<string, string>();

            if (lastFilesetId >= 0)
            {
                var stored = await database.GetSourceProviderStateAsync(lastFilesetId, scope, cancellationToken).ConfigureAwait(false);
                if (stored == null)
                    Log.WriteVerboseMessage(LOGTAG, "NoChangeTrackingState", "No change tracking state is stored for \"{0}\", walking everything", scope);
                else if (!string.Equals(stored.ConfigHash, _configHash, StringComparison.Ordinal))
                    Log.WriteInformationMessage(LOGTAG, "ChangeTrackingStateDiscarded", "Discarding the change tracking state stored for \"{0}\" because the sources or filters have changed since it was recorded, walking everything", scope);
                else
                    rows = stored.Rows;
            }

            tracked.LoadedState = rows;
            tracked.Provider.LoadChangeTrackingState(rows);
        }
    }

    /// <summary>
    /// The carry-forward predicate: <c>true</c> if the path was examined in this run.
    /// A path that no tracked provider owns was produced by a provider that enumerates
    /// everything, so it counts as examined.
    /// </summary>
    /// <param name="path">The path from the previous fileset</param>
    /// <returns><c>true</c> if the path was examined in this run</returns>
    public bool WasPathEnumerated(string path)
    {
        foreach (var tracked in _providers)
            if (path.StartsWith(tracked.MountedPath, Library.Utility.Utility.ClientFilenameStringComparison))
            {
                // The provider lists its root entry on every run
                if (path.Length == tracked.MountedPath.Length)
                    return true;

                return tracked.Provider.WasPathEnumerated(path);
            }

        return true;
    }

    /// <summary>
    /// Whether a folder was enumerated incrementally by a provider that was handed state,
    /// so that it lists only what changed and a folder that lists nothing may simply be unchanged
    /// </summary>
    /// <param name="folderPath">The folder path to check</param>
    /// <returns><c>true</c> if the folder was enumerated incrementally</returns>
    public bool IsIncrementallyEnumeratedFolder(string folderPath)
    {
        foreach (var tracked in _providers)
            if (folderPath.StartsWith(tracked.MountedPath, Library.Utility.Utility.ClientFilenameStringComparison))
                return tracked.HasLoadedState && tracked.Provider.IsIncrementallyEnumeratedFolder(folderPath);

        return false;
    }

    /// <summary>
    /// Stores the state the providers export with the current fileset, so each fileset
    /// carries the tokens its enumeration ended with. If the fileset is later dropped
    /// because nothing needed uploading, the rows move to the previous fileset, whose
    /// content is then the same. A partial backup stores nothing: the enumeration was cut
    /// short, so the fileset lacks what the stop left out, including everything below the
    /// entries a provider never reached, which it cannot tell apart from entries it listed
    /// in full and so does not report as unexamined. Neither the exported state nor the
    /// loaded one describes such a fileset, so the next backup is handed no state and walks
    /// everything; a dropped partial fileset leaves the previous fileset's state in place.
    /// A provider that exports nothing stores nothing either, and is walked in full next time.
    /// </summary>
    /// <param name="database">The database to write to</param>
    /// <param name="partial">Whether the backup was cut short</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>An awaitable task</returns>
    public async Task StoreStateAsync(BackupDatabase database, bool partial, CancellationToken cancellationToken)
    {
        if (partial)
            Log.WriteInformationMessage(LOGTAG, "ChangeTrackingStateNotStored", "The backup was cut short, so no change tracking state is stored and the next backup lists everything");
        else
            foreach (var tracked in _providers)
            {
                var scope = tracked.Provider.ChangeTrackingScope;
                var rows = tracked.Provider.ExportChangeTrackingState();
                if (rows.Count == 0)
                {
                    Log.WriteVerboseMessage(LOGTAG, "NoChangeTrackingStateToStore", "No change tracking state was exported for \"{0}\", so none is stored and the next backup walks it in full", scope);
                    continue;
                }

                Log.WriteVerboseMessage(LOGTAG, "StoringChangeTrackingState", "Storing {0} change tracking state rows for \"{1}\"", rows.Count, scope);
                await database.CreateSourceProviderStateAsync(scope, _configHash, rows, cancellationToken).ConfigureAwait(false);
            }

        var removed = await database.PruneSourceProviderStateAsync(STATE_FILESETS_TO_KEEP, cancellationToken).ConfigureAwait(false);
        if (removed > 0)
            Log.WriteVerboseMessage(LOGTAG, "PrunedChangeTrackingState", "Removed {0} change tracking state rows stored with older filesets", removed);
    }
}
