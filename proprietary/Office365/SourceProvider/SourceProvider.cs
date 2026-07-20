// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

/// <summary>
/// The source provider for Office365.
/// </summary>
public sealed partial class SourceProvider : ISourceProviderModule, IDisposable
{
    /// <summary>
    /// The log tag for this class.
    /// </summary>
    private static readonly string LOGTAG = Library.Logging.Log.LogTagFromType<SourceProvider>();

    /// <summary>
    /// API helper for making requests to the Graph API.
    /// </summary>
    private readonly APIHelper _apiHelper;

    /// <summary>
    /// The mount point for this source provider instance.
    /// </summary>
    private readonly string _mountPoint;

    /// <summary>
    /// Indicates whether the metadata storage option has been set.
    /// </summary>
    private readonly bool _hasSetMetadataStorageOption;

    /// <summary>
    /// The timeout options for the backend
    /// </summary>
    private readonly TimeoutOptionsHelper.Timeouts _timeouts;

    /// <summary>
    /// Cache of already resolved source entries by their path.
    /// </summary>
    private readonly ConcurrentDictionary<string, ISourceProviderEntry> _entryCache = new();

    /// <summary>
    /// Counter for tracking which licensed paths are being enumerated.
    /// </summary>
    private readonly ConcurrentDictionary<string, bool> _enumerationCounter = new();

    /// <summary>
    /// Cache of the resolved seat classification for a given user (by id). This is resolved
    /// once per user and reused across the non-incrementing (gate) and incrementing (count)
    /// license checks, as well as for item metadata, so that the shared-mailbox lookup is
    /// performed at most once per user.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<UserSeatCategory>> _userSeatCache = new();

    /// <summary>
    /// The current number of users that has been enumerated.
    /// </summary>
    private int _userCount = 0;
    /// <summary>
    /// The current number of groups that has been enumerated.
    /// </summary>
    private int _groupCount = 0;
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
    /// Whether a license warning has been issued for sites.
    /// </summary>
    private int _siteLicenseWarningIssued = 0;

    /// <summary>
    /// The included root types.
    /// </summary>
    private readonly Office365MetaType[] _includedRootTypes;

    /// <summary>
    /// The included user types.
    /// </summary>
    private readonly Office365UserType[] _includedUserTypes;

    /// <summary>
    /// The included group types.
    /// </summary>
    private readonly Office365GroupType[] _includedGroupTypes;

    /// <summary>
    /// The included user classifications.
    /// </summary>
    private readonly SourceItems.Office365UserClassification _includedUserClassifications;

    /// <summary>
    /// The included group classifications.
    /// </summary>
    private readonly SourceItems.Office365GroupClassification _includedGroupClassifications;

    /// <summary>
    /// The included site classifications.
    /// </summary>
    private readonly SourceItems.Office365SiteClassification _includedSiteClassifications;

    /// <summary>
    /// Whether this provider is being used for a restore operation.
    /// </summary>
    internal bool UsedForRestoreOperation { get; set; } = false;


    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class.
    /// Only used for loading metadata properties.
    /// </summary>
    public SourceProvider()
    {
        _apiHelper = null!;
        _mountPoint = null!;
        _timeouts = null!;
        _includedRootTypes = null!;
        _includedUserTypes = null!;
        _includedGroupTypes = null!;
        _includedUserClassifications = OptionsHelper.ALL_USER_CLASSIFICATIONS;
        _includedGroupClassifications = OptionsHelper.ALL_GROUP_CLASSIFICATIONS;
        _includedSiteClassifications = OptionsHelper.ALL_SITE_CLASSIFICATIONS;
        _hasSetMetadataStorageOption = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SourceProvider"/> class.
    /// </summary>
    /// <param name="url">The source provider URL.</param>
    /// <param name="mountPoint">The mount point.</param>
    /// <param name="options">The source provider options.</param>
    public SourceProvider(string url, string mountPoint, Dictionary<string, string?> options)
    {
        _hasSetMetadataStorageOption = Library.Utility.Utility.ParseBoolOption(options, "store-metadata-content-in-database");

        _mountPoint = mountPoint;
        var parsedOptions = OptionsHelper.ParseAndValidateOptions(url, options);
        _timeouts = TimeoutOptionsHelper.Parse(options);
        _includedRootTypes = parsedOptions.IncludedRootTypes;
        _includedUserTypes = parsedOptions.IncludedUserTypes;
        _includedGroupTypes = parsedOptions.IncludedGroupTypes;
        _includedUserClassifications = parsedOptions.IncludedUserClassifications;
        _includedGroupClassifications = parsedOptions.IncludedGroupClassifications;
        _includedSiteClassifications = parsedOptions.IncludedSiteClassifications;
        _apiHelper = APIHelper.Create(
            tenantId: parsedOptions.TenantId,
            authOptions: parsedOptions.AuthOptions,
            graphBaseUrl: parsedOptions.GraphBaseUrl,
            timeouts: _timeouts,
            certificatePath: parsedOptions.CertificatePath,
            certificatePassword: parsedOptions.CertificatePassword,
            scope: parsedOptions.Scope
        );
    }

    /// <inheritdoc />
    public string Key => OptionsHelper.ModuleKey;

    /// <inheritdoc />
    public string DisplayName => Strings.ProviderDisplayName;

    /// <inheritdoc />
    public string Description => Strings.ProviderDescription;

    /// <inheritdoc />
    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands.Concat(TimeoutOptionsHelper.GetOptions()).ToList();

    /// <inheritdoc />
    public string MountedPath => _mountPoint;

    /// <inheritdoc />
    public void Dispose()
    {
        _apiHelper?.Dispose();
    }


    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (!_hasSetMetadataStorageOption)
            throw new UserInformationException(Strings.MetadataStorageNotEnabled("store-metadata-content-in-database"), "DatabaseMetadataStorageNotEnabled");

        return _apiHelper.AcquireAccessTokenAsync(true, cancellationToken);
    }

    /// <inheritdoc />
    public Task TestAsync(CancellationToken cancellationToken)
        => _apiHelper.AcquireAccessTokenAsync(false, cancellationToken);

    /// <inheritdoc />
    public async IAsyncEnumerable<ISourceProviderEntry> EnumerateAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new RootSourceEntry(this, _mountPoint);
    }

    /// <inheritdoc />
    public async Task<ISourceProviderEntry?> GetEntryAsync(string path, bool isFolder, CancellationToken cancellationToken)
    {
        if (_entryCache.TryGetValue(path, out var cachedEntry))
            return cachedEntry;

        ISourceProviderEntry resultEntry = new RootSourceEntry(this, _mountPoint);
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

    /// <summary>
    /// Determines whether the given user consumes a Duplicati user seat.
    /// Regular user mailboxes always count. Shared, room and equipment mailboxes only
    /// count when they have additional storage, i.e. an assigned Exchange Online license.
    /// </summary>
    /// <param name="user">The user to classify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the user should count as a seat; otherwise <c>false</c>.</returns>
    internal async Task<bool> UserCountsAsSeatAsync(GraphUser user, CancellationToken cancellationToken)
    {
        // Restores are unlimited; avoid any extra Graph calls for classification.
        if (UsedForRestoreOperation)
            return true;

        var category = await ClassifyUserAsync(user, cancellationToken).ConfigureAwait(false);
        return SeatCategoryCountsAsSeat(category);
    }

    /// <summary>
    /// Determines whether the given seat category consumes a licensed seat. Only shared,
    /// room and equipment mailboxes without additional storage do not consume a seat; every
    /// other category (including unlicensed and undetermined regular mailboxes) counts, so
    /// real user seats are never under-counted.
    /// </summary>
    private static bool SeatCategoryCountsAsSeat(UserSeatCategory category)
        => category != UserSeatCategory.SharedMailboxWithoutStorage;

    /// <summary>
    /// Returns the cached seat classification for a user if it has already been resolved,
    /// without triggering any Graph API call. Returns <c>null</c> when the classification has
    /// not been resolved yet (or has not completed successfully).
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The resolved seat category, or <c>null</c> if not yet available.</returns>
    internal UserSeatCategory? TryGetCachedUserSeatCategory(string userId)
        => _userSeatCache.TryGetValue(userId, out var task) && task.IsCompletedSuccessfully
            ? task.Result
            : null;

    /// <summary>
    /// Determines whether the given group consumes a Duplicati group seat.
    /// Only Microsoft 365 (Unified) groups have backable content and count as a seat.
    /// Security groups and distribution lists (mail-enabled security groups included) do
    /// not consume a group seat.
    /// </summary>
    /// <param name="group">The group to classify.</param>
    /// <returns><c>true</c> if the group should count as a seat; otherwise <c>false</c>.</returns>
    internal static bool GroupCountsAsSeat(GraphGroup group)
        => group.GroupTypes?.Any(t => t.Equals("Unified", StringComparison.OrdinalIgnoreCase)) == true;

    /// <summary>
    /// Determines whether the given mailbox <c>userPurpose</c> value denotes a mailbox that
    /// is not a regular user mailbox (shared, room, equipment or other).
    /// </summary>
    /// <param name="purpose">The mailbox purpose value.</param>
    /// <returns><c>true</c> if the mailbox is not a regular user mailbox; otherwise <c>false</c>.</returns>
    private static bool IsNonUserMailboxPurpose(string purpose)
        => purpose.Equals("shared", StringComparison.OrdinalIgnoreCase)
            || purpose.Equals("room", StringComparison.OrdinalIgnoreCase)
            || purpose.Equals("equipment", StringComparison.OrdinalIgnoreCase)
            || purpose.Equals("others", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The seat classification of a user, used for reporting item counts.
    /// </summary>
    internal enum UserSeatCategory
    {
        /// <summary>A regular user mailbox with one or more assigned licenses.</summary>
        Licensed,
        /// <summary>A regular user mailbox with no assigned license.</summary>
        Unlicensed,
        /// <summary>A shared/room/equipment mailbox with additional (licensed) storage.</summary>
        SharedMailboxWithStorage,
        /// <summary>A shared/room/equipment mailbox without additional storage.</summary>
        SharedMailboxWithoutStorage,
    }

    /// <summary>
    /// The classification of a SharePoint site, used for reporting item counts.
    /// Determined on a best-effort basis from the Microsoft Graph <c>site</c> object only.
    /// </summary>
    internal enum SiteCategory
    {
        /// <summary>A Microsoft 365 group-connected team site.</summary>
        Group,
        /// <summary>A classic (non-group) team site.</summary>
        Classic,
        /// <summary>A modern communication site.</summary>
        Communication,
        /// <summary>A personal (OneDrive for Business) site.</summary>
        Personal,
        /// <summary>Any other or undetermined site type.</summary>
        Other,
    }

    /// <summary>
    /// Maps a <see cref="UserSeatCategory"/> to its corresponding include-filter flag.
    /// </summary>
    private static SourceItems.Office365UserClassification ToClassificationFlag(UserSeatCategory category)
        => category switch
        {
            UserSeatCategory.Licensed => SourceItems.Office365UserClassification.Licensed,
            UserSeatCategory.Unlicensed => SourceItems.Office365UserClassification.Unlicensed,
            UserSeatCategory.SharedMailboxWithStorage => SourceItems.Office365UserClassification.SharedMailboxWithStorage,
            UserSeatCategory.SharedMailboxWithoutStorage => SourceItems.Office365UserClassification.SharedMailboxWithoutStorage,
            _ => SourceItems.Office365UserClassification.Licensed
        };

    /// <summary>
    /// Maps a <see cref="SiteCategory"/> to its corresponding include-filter flag.
    /// </summary>
    private static SourceItems.Office365SiteClassification ToClassificationFlag(SiteCategory category)
        => category switch
        {
            SiteCategory.Group => SourceItems.Office365SiteClassification.Group,
            SiteCategory.Classic => SourceItems.Office365SiteClassification.Classic,
            SiteCategory.Communication => SourceItems.Office365SiteClassification.Communication,
            SiteCategory.Personal => SourceItems.Office365SiteClassification.Personal,
            _ => SourceItems.Office365SiteClassification.Other
        };

    /// <summary>
    /// Determines whether a user should be included based on its classification and the
    /// configured user-classification include filter. Uses the cached classification, so no
    /// extra Graph API call is made beyond the one already performed for seat counting.
    /// </summary>
    /// <param name="user">The user to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the user should be included; otherwise <c>false</c>.</returns>
    internal async Task<bool> IsUserClassificationIncludedAsync(GraphUser user, CancellationToken cancellationToken)
    {
        var category = await ClassifyUserAsync(user, cancellationToken).ConfigureAwait(false);
        return _includedUserClassifications.HasFlag(ToClassificationFlag(category));
    }

    /// <summary>
    /// Determines whether a group should be included based on its classification and the
    /// configured group-classification include filter.
    /// </summary>
    /// <param name="group">The group to test.</param>
    /// <returns><c>true</c> if the group should be included; otherwise <c>false</c>.</returns>
    internal bool IsGroupClassificationIncluded(GraphGroup group)
    {
        var flag = GroupCountsAsSeat(group)
            ? SourceItems.Office365GroupClassification.Unified
            : SourceItems.Office365GroupClassification.NotUnified;
        return _includedGroupClassifications.HasFlag(flag);
    }

    /// <summary>
    /// Determines whether a site should be included based on its classification and the
    /// configured site-classification include filter.
    /// </summary>
    /// <param name="site">The site to test.</param>
    /// <returns><c>true</c> if the site should be included; otherwise <c>false</c>.</returns>
    internal bool IsSiteClassificationIncluded(GraphSite site)
        => _includedSiteClassifications.HasFlag(ToClassificationFlag(ClassifySite(site)));

    /// <summary>
    /// Classifies a user into one of the <see cref="UserSeatCategory"/> buckets.
    /// A licensed user (with assigned licenses) is <see cref="UserSeatCategory.Licensed"/>,
    /// unless it is a shared/room/equipment mailbox, in which case it is
    /// <see cref="UserSeatCategory.SharedMailboxWithStorage"/>. Unlicensed regular mailboxes
    /// are <see cref="UserSeatCategory.Unlicensed"/>, and unlicensed shared mailboxes are
    /// <see cref="UserSeatCategory.SharedMailboxWithoutStorage"/>.
    /// </summary>
    /// <param name="user">The user to classify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user seat category.</returns>
    /// <remarks>
    /// The result is resolved once per user and cached, so the underlying
    /// <c>mailboxSettings/userPurpose</c> lookup is performed at most once per user and is
    /// shared between seat counting, the count operation and item metadata.
    /// </remarks>
    internal Task<UserSeatCategory> ClassifyUserAsync(GraphUser user, CancellationToken cancellationToken)
        => _userSeatCache.GetOrAdd(user.Id, _ => ResolveUserSeatCategoryAsync(user, cancellationToken));

    /// <summary>
    /// Resolves the seat classification for a user. See <see cref="ClassifyUserAsync"/>.
    /// </summary>
    private async Task<UserSeatCategory> ResolveUserSeatCategoryAsync(GraphUser user, CancellationToken cancellationToken)
    {
        var hasStorage = user.AssignedLicenses is { Count: > 0 };

        string? purpose = null;
        try
        {
            purpose = await UserProfileApi.GetMailboxUserPurposeAsync(user.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // If the purpose cannot be determined, treat it as a regular user mailbox.
            Library.Logging.Log.WriteVerboseMessage(LOGTAG, "MailboxPurposeLookupFailed", ex, $"Failed to determine mailbox purpose for user '{user.Id}'; treating it as a regular user mailbox.");
        }

        var isShared = !string.IsNullOrWhiteSpace(purpose) && IsNonUserMailboxPurpose(purpose);

        if (isShared)
            return hasStorage
                ? UserSeatCategory.SharedMailboxWithStorage
                : UserSeatCategory.SharedMailboxWithoutStorage;

        return hasStorage
            ? UserSeatCategory.Licensed
            : UserSeatCategory.Unlicensed;
    }

    /// <summary>
    /// Classifies a SharePoint site into one of the <see cref="SiteCategory"/> buckets,
    /// on a best-effort basis using only the Microsoft Graph <c>site</c> object.
    /// Personal (OneDrive) sites are detected reliably. The Graph <c>site</c> object does
    /// not expose the SharePoint web template, so group, classic and communication sites
    /// cannot be reliably distinguished from Graph alone and are reported as
    /// <see cref="SiteCategory.Other"/>.
    /// </summary>
    /// <param name="site">The site to classify.</param>
    /// <returns>The site category.</returns>
    internal static SiteCategory ClassifySite(GraphSite site)
    {
        if (site.SiteCollection?.PersonalSite == true)
            return SiteCategory.Personal;

        // OneDrive personal sites are hosted on the "-my" SharePoint host.
        var host = site.SiteCollection?.Hostname;
        if (!string.IsNullOrWhiteSpace(host) && host.Contains("-my.", StringComparison.OrdinalIgnoreCase))
            return SiteCategory.Personal;

        if (!string.IsNullOrWhiteSpace(site.WebUrl)
            && (site.WebUrl.Contains("-my.", StringComparison.OrdinalIgnoreCase)
                || site.WebUrl.Contains("/personal/", StringComparison.OrdinalIgnoreCase)))
            return SiteCategory.Personal;

        // The Graph site object does not expose the web template, so group/classic/
        // communication cannot be distinguished reliably from Graph alone.
        return SiteCategory.Other;
    }

    /// <summary>
    /// Classifies a user using only the data already present on the directory object,
    /// without any additional Graph API call. This can only determine whether the user is
    /// licensed or unlicensed; it cannot distinguish shared/room/equipment mailboxes, which
    /// would require an extra <c>mailboxSettings/userPurpose</c> call.
    /// </summary>
    /// <param name="user">The user to classify.</param>
    /// <returns>The classification string suitable for item metadata.</returns>
    internal static string ClassifyUserFromDirectory(GraphUser user)
        => user.AssignedLicenses is { Count: > 0 } ? "Licensed" : "Unlicensed";

    /// <summary>
    /// Classifies a group using only the data already present on the directory object,
    /// without any additional Graph API call.
    /// </summary>
    /// <param name="group">The group to classify.</param>
    /// <returns>The classification string suitable for item metadata.</returns>
    internal static string ClassifyGroupFromDirectory(GraphGroup group)
        => GroupCountsAsSeat(group) ? "Unified" : "NotUnified";

    /// <summary>
    /// Determines whether the license is approved for the given entry.
    /// </summary>
    /// <param name="path">The path to verify.</param>
    /// <param name="type">The type of entry.</param>
    /// <param name="id">The ID of the entry.</param>
    /// <param name="increment">Whether to increment the counter if the license is approved.</param>
    /// <param name="countsAsSeat">
    /// Whether this entry consumes a licensed seat. When <c>false</c>, the entry is always
    /// approved and never counted against the seat limit (used for shared mailboxes without
    /// additional storage).
    /// </param>
    /// <returns><c>true</c> if the license is approved; otherwise, <c>false</c>.</returns>
    internal bool LicenseApprovedForEntry(string path, Office365MetaType type, string id, bool increment, bool countsAsSeat = true)
    {
        // We do not limit restores
        if (UsedForRestoreOperation)
            return true;

        // Entries that do not consume a seat (e.g. shared mailboxes without extra storage)
        // are always approved and never counted against the seat limit.
        if (!countsAsSeat)
            return true;

        // Make a unique target path for the type and id, just for counting purposes, not matching actual paths
        var targetpath = $"{path.TrimEnd(Path.DirectorySeparatorChar)}{Path.DirectorySeparatorChar}{type}{Path.DirectorySeparatorChar}{id}";

        if (_enumerationCounter.ContainsKey(targetpath))
            return true;

        if (type.HasFlag(Office365MetaType.Users))
        {
            var approved = LicenseChecker.LicenseHelper.AvailableOffice365UserSeats;
            var current = _userCount;
            if (current >= approved)
            {
                if (increment && Interlocked.Exchange(ref _userLicenseWarningIssued, 1) == 0)
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
                return false;
            }
        }
        else if (type.HasFlag(Office365MetaType.Groups))
        {
            var approved = LicenseChecker.LicenseHelper.AvailableOffice365GroupSeats;
            var current = _groupCount;
            if (current >= approved)
            {
                if (increment && Interlocked.Exchange(ref _groupLicenseWarningIssued, 1) == 0)
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
                return false;
            }
        }
        else if (type.HasFlag(Office365MetaType.Sites))
        {
            var approved = LicenseChecker.LicenseHelper.AvailableOffice365SiteSeats;
            var current = _siteCount;
            if (current >= approved)
            {
                if (increment && Interlocked.Exchange(ref _siteLicenseWarningIssued, 1) == 0)
                    Library.Logging.Log.WriteWarningMessage(LOGTAG, "LicenseWarning", null, Strings.LicenseWarning(type, approved));
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
            if (type.HasFlag(Office365MetaType.Users))
                Interlocked.Increment(ref _userCount);
            else if (type.HasFlag(Office365MetaType.Groups))
                Interlocked.Increment(ref _groupCount);
            else if (type.HasFlag(Office365MetaType.Sites))
                Interlocked.Increment(ref _siteCount);
        }

        return true;
    }


    /// <summary>
    /// Gets the included root types.
    /// </summary>
    internal IEnumerable<Office365MetaType> IncludedRootTypes =>
       _includedRootTypes;

    /// <summary>
    /// Gets the included user types.
    /// </summary>
    internal IEnumerable<Office365UserType> IncludedUserTypes =>
        _includedUserTypes;

    /// <summary>
    /// Gets the included group types.
    /// </summary>
    internal IEnumerable<Office365GroupType> IncludedGroupTypes =>
        _includedGroupTypes;
}
