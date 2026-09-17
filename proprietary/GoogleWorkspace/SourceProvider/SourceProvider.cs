// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using System.Collections.Concurrent;
using User = Google.Apis.Admin.Directory.directory_v1.Data.User;

namespace Duplicati.Proprietary.GoogleWorkspace;

public sealed partial class SourceProvider : ISourceProviderModule, IDisposable
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Log.LogTagFromType<SourceProvider>();

    private readonly APIHelper _apiHelper;
    private readonly string _mountPoint;
    private readonly OptionsHelper.GoogleWorkspaceOptions _options;
    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    /// <summary>
    /// Counter for tracking which licensed paths are being enumerated.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _enumerationCounter = new();

    /// <summary>
    /// The current number of users that has been enumerated.
    /// </summary>
    private int _userCount = 0;
    /// <summary>
    /// The current number of groups that has been enumerated.
    /// </summary>
    private int _groupCount = 0;
    /// <summary>
    /// The current number of shared drives that has been enumerated.
    /// </summary>
    private int _sharedDriveCount = 0;
    /// <summary>
    /// The current number of sites that has been enumerated.
    /// </summary>
    private int _siteCount = 0;

    /// <summary>
    /// Whether a license warning has been issued for users.
    /// </summary>
    private int _userLicenseWarningIssued = 0;
    /// <summary>
    /// Whether a license warning has been issued for groups.
    /// </summary>
    private int _groupLicenseWarningIssued = 0;
    /// <summary>
    /// Whether a license warning has been issued for shared drives.
    /// </summary>
    private int _sharedDriveLicenseWarningIssued = 0;
    /// <summary>
    /// Whether a license warning has been issued for sites.
    /// </summary>
    private int _siteLicenseWarningIssued = 0;

    /// <summary>
    /// Whether this provider is being used for a restore operation.
    /// </summary>
    internal bool UsedForRestoreOperation { get; private init; }

    /// <summary>
    /// Whether to avoid reading calendar ACLs.
    /// </summary>
    internal bool AvoidCalendarAcl { get; private init; }

    /// <summary>
    /// Indicates whether the metadata storage option has been set.
    /// </summary>
    private readonly bool _hasSetMetadataStorageOption;

    public APIHelper ApiHelper => _apiHelper;
    public OptionsHelper.GoogleWorkspaceOptions Options => _options;

    public SourceProvider()
    {
        _apiHelper = null!;
        _mountPoint = null!;
        _options = null!;
    }

    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
        : this(url, mountPoint, options, false)
    {
    }

    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options, bool usedForRestoreOperation)
    {
        _hasSetMetadataStorageOption = Library.Utility.Utility.ParseBoolOption(options, "store-metadata-content-in-database");

        _mountPoint = mountPoint;
        _options = OptionsHelper.ParseOptions(options);

        _apiHelper = new APIHelper(_options, usedForRestoreOperation);
        UsedForRestoreOperation = usedForRestoreOperation;
        AvoidCalendarAcl = Library.Utility.Utility.ParseBoolOption(options, OptionsHelper.GOOGLE_AVOID_CALENDAR_ACL_OPTION);
    }

    public string Key => OptionsHelper.ModuleKey;

    public string DisplayName => Strings.Common.DisplayName;

    public string Description => Strings.Common.Description;

    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SourceProviderSupportedCommands;

    public string MountedPath => _mountPoint;

    /// <inheritdoc />
    public bool NeedsStoredMetadata => true;

    public void Dispose()
    {
    }

    public IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync(CancellationToken cancellationToken)
    {
        return new RootSourceEntry(this).Enumerate(cancellationToken);
    }

    public async Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
    {
        if (_entryCache.TryGetValue(path, out var cachedEntry))
            return cachedEntry;

        ISourceProviderEntry resultEntry = new RootSourceEntry(this);
        var targetPath = path.TrimEnd(Path.DirectorySeparatorChar);

        var currentPath = targetPath;
        while (!string.IsNullOrEmpty(currentPath))
        {
            var parentPath = Path.GetDirectoryName(currentPath);
            if (parentPath != null && _entryCache.TryGetValue(parentPath, out var cachedParent))
            {
                resultEntry = cachedParent;
                break;
            }
            currentPath = parentPath;
        }

        var relativePath = string.IsNullOrEmpty(currentPath)
            ? targetPath
            : Path.GetRelativePath(currentPath, targetPath);

        var pathsegments = relativePath?.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        var segmentIndex = 0;
        foreach (var item in pathsegments)
        {
            var found = false;
            var isLastSegment = segmentIndex == pathsegments.Length - 1;
            await foreach (var entry in resultEntry.Enumerate(cancellationToken))
            {
                _entryCache.TryAdd(entry.Path, entry);

                var name = entry.Path.TrimEnd(Path.DirectorySeparatorChar)
                    .Split(Path.DirectorySeparatorChar)
                    .Last();
                if (name.Equals(item, StringComparison.OrdinalIgnoreCase))
                {
                    if (isLastSegment && entry.IsFolder != isFolder)
                        throw new UserInformationException($"Path segment '{item}' is not a {(isFolder ? "folder" : "file")}", "PathSegmentNotFolder");
                    else if (!isLastSegment && !entry.IsFolder)
                        throw new UserInformationException($"Path segment '{item}' is not a folder", "PathSegmentNotFolder");

                    resultEntry = entry;
                    found = true;
                    break;
                }
            }

            segmentIndex++;
            if (!found)
                return null;
        }

        return resultEntry;
    }

    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_hasSetMetadataStorageOption)
            throw new UserInformationException(Strings.MetadataStorageNotEnabled("store-metadata-content-in-database"), "DatabaseMetadataStorageNotEnabled");

        return Task.CompletedTask;
    }

    public Task TestAsync(CancellationToken cancellationToken)
    {
        _apiHelper.TestConnection();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Determines whether the given user consumes a Duplicati user seat. The intent is that a
    /// seat is consumed where Google charges the tenant for the account, mirroring the rule the
    /// Microsoft 365 provider applies to assigned licenses.
    /// </summary>
    /// <param name="user">The user to test, as returned by the Directory API.</param>
    /// <returns><c>true</c> if the user should count as a seat; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// The Directory API does not expose license assignments, so the decision is made from the
    /// account state alone: suspended accounts and archived accounts do not consume a seat, every
    /// other account does. Both flags are core properties of the <c>users.list</c> response, so no
    /// request is made per user and the cost is a single listing of the tenant. When a flag is
    /// missing from the object, the account is counted rather than given away.
    /// </remarks>
    internal static bool UserCountsAsSeat(User user)
        => ClassifyUser(user) == UserCategory.Active;

    /// <summary>
    /// The classification of a user account, used for item metadata and for reporting item
    /// counts. Only <see cref="Active"/> accounts consume a seat; see <see cref="UserCountsAsSeat"/>.
    /// </summary>
    internal enum UserCategory
    {
        /// <summary>A regular, usable account, which consumes a seat.</summary>
        Active,
        /// <summary>An account that has been suspended by an administrator or by Google.</summary>
        Suspended,
        /// <summary>An account that has been archived.</summary>
        Archived
    }

    /// <summary>
    /// Classifies a user account from the Directory object alone. An account that is both
    /// suspended and archived is reported as archived, so that each account lands in exactly
    /// one bucket.
    /// </summary>
    /// <param name="user">The user to classify.</param>
    /// <returns>The user category.</returns>
    internal static UserCategory ClassifyUser(User user)
    {
        if (user.Archived == true)
            return UserCategory.Archived;
        if (user.Suspended == true)
            return UserCategory.Suspended;
        return UserCategory.Active;
    }

    /// <summary>
    /// Determines whether the license is approved for the given entry.
    /// </summary>
    /// <param name="path">The path to verify.</param>
    /// <param name="type">The type of entry.</param>
    /// <param name="id">The ID of the entry.</param>
    /// <param name="increment">Whether to increment the count if the license is approved.</param>
    /// <param name="countsAsSeat">
    /// Whether this entry consumes a licensed seat. When <c>false</c>, the entry is always
    /// approved and never counted against the seat limit (used for suspended and archived users).
    /// </param>
    /// <returns><c>true</c> if the license is approved; otherwise, <c>false</c>.</returns>
    internal bool LicenseApprovedForEntry(string path, GoogleRootType type, string id, bool increment, bool countsAsSeat)
    {
        // We do not limit restores
        if (UsedForRestoreOperation)
            return true;

        // Entries that do not consume a seat (e.g. suspended or archived users) are always
        // approved and never counted against the seat limit.
        if (!countsAsSeat)
            return true;

        // Make a unique target path for the type and id, just for counting purposes, not matching actual paths
        var targetpath = $"{path.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{type}{Path.DirectorySeparatorChar}{id}";

        if (_enumerationCounter.ContainsKey(targetpath))
            return true;

        // The seat warning is written on whichever call first sees the limit. The listing-time
        // check does not increment and, once it returns false, the entry is never enumerated,
        // so waiting for the incrementing call would mean never warning at all.
        if (type == GoogleRootType.Users)
        {
            var approved = LicenseChecker.LicenseHelper.AvailableGoogleWorkspaceUserSeats;
            var current = _userCount;
            if (current >= approved)
            {
                if (Interlocked.Exchange(ref _userLicenseWarningIssued, 1) == 0)
                    Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
                return false;
            }
        }
        else if (type == GoogleRootType.Groups)
        {
            var approved = LicenseChecker.LicenseHelper.AvailableGoogleWorkspaceGroupSeats;
            var current = _groupCount;
            if (current >= approved)
            {
                if (Interlocked.Exchange(ref _groupLicenseWarningIssued, 1) == 0)
                    Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
                return false;
            }
        }
        else if (type == GoogleRootType.SharedDrives)
        {
            var approved = LicenseChecker.LicenseHelper.AvailableGoogleWorkspaceSharedDriveSeats;
            var current = _sharedDriveCount;
            if (current >= approved)
            {
                if (Interlocked.Exchange(ref _sharedDriveLicenseWarningIssued, 1) == 0)
                    Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
                return false;
            }
        }
        else if (type == GoogleRootType.Sites)
        {
            var approved = LicenseChecker.LicenseHelper.AvailableGoogleWorkspaceSiteSeats;
            var current = _siteCount;
            if (current >= approved)
            {
                if (Interlocked.Exchange(ref _siteLicenseWarningIssued, 1) == 0)
                    Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
                return false;
            }
        }
        else
        {
            // Unknown type, allow it
            return true;
        }

        if (increment && _enumerationCounter.TryAdd(targetpath, true))
        {
            if (type == GoogleRootType.Users)
                Interlocked.Increment(ref _userCount);
            else if (type == GoogleRootType.Groups)
                Interlocked.Increment(ref _groupCount);
            else if (type == GoogleRootType.SharedDrives)
                Interlocked.Increment(ref _sharedDriveCount);
            else if (type == GoogleRootType.Sites)
                Interlocked.Increment(ref _siteCount);
        }

        return true;
    }
}
