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
    /// The machine id recorded in the backup description, honoring the global <c>machine-id</c> override.
    /// </summary>
    private readonly string _machineId;

    /// <summary>
    /// The machine id to record in the backup description, honoring the global <c>machine-id</c> override.
    /// </summary>
    internal string MachineId => _machineId;

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
    /// Cache of the resolved classification for a given user (by id). This is resolved once
    /// per user and reused between the include filter and item metadata, so that the
    /// shared-mailbox lookup is performed at most once per user. Seat counting does not use
    /// this, as it is decided from the directory object alone.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<UserCategory>> _userCategoryCache = new();

    /// <summary>
    /// Cache of the resolved classification for a given site (by id). Resolved once per site
    /// and reused across the non-incrementing (gate) and incrementing (count) license checks
    /// as well as for item metadata.
    /// </summary>
    private readonly ConcurrentDictionary<string, Task<SiteCategory>> _siteCategoryCache = new();

    /// <summary>
    /// The lazily built index of the sites that host the OneDrive of an unlicensed user.
    /// Built at most once per provider instance, and only if a personal site is encountered.
    /// </summary>
    private Task<UnlicensedUserSiteIndex>? _unlicensedUserSiteIndex;

    /// <summary>
    /// Guards the creation of <see cref="_unlicensedUserSiteIndex"/>.
    /// </summary>
    private readonly object _unlicensedUserSiteIndexLock = new();

    /// <summary>
    /// Whether a warning has been issued about a failed unlicensed-user lookup.
    /// </summary>
    private int _unlicensedUserLookupWarningIssued = 0;

    /// <summary>
    /// Whether a warning has been issued about a user whose licensing state could not be
    /// determined. See <see cref="UserCountsAsSeat"/>.
    /// </summary>
    private int _userLicenseStateWarningIssued = 0;

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
    /// Whether this provider instance is enumerating items for display rather than for backup.
    /// Only then does the item metadata carry the classification, which the user interface uses
    /// to pick an icon; resolving it can cost a Graph request per item, and a backup never reads
    /// it back.
    /// </summary>
    internal bool EnumerationMode { get; }

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
        _machineId = null!;
        _includedRootTypes = null!;
        _includedUserTypes = null!;
        _includedGroupTypes = null!;
        _includedUserClassifications = OptionsHelper.ALL_USER_CLASSIFICATIONS;
        _includedGroupClassifications = OptionsHelper.ALL_GROUP_CLASSIFICATIONS;
        _includedSiteClassifications = OptionsHelper.ALL_SITE_CLASSIFICATIONS;
        EnumerationMode = false;
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
        EnumerationMode = parsedOptions.EnumerationMode;
        _machineId = parsedOptions.MachineId;
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
    public bool NeedsStoredMetadata => true;

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

    /// <summary>
    /// Gets the application permissions granted to the app registration, as listed in
    /// the roles claim of the acquired access token.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The names of the granted application permissions.</returns>
    internal async Task<IReadOnlySet<string>> GetGrantedApplicationPermissionsAsync(CancellationToken cancellationToken)
    {
        var token = await _apiHelper.AcquireAccessTokenAsync(false, cancellationToken).ConfigureAwait(false);
        return GraphPermissions.ExtractGrantedRoles(token);
    }

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
    /// Determines whether the account has one or more assigned Microsoft 365 licenses, or
    /// <c>null</c> when that cannot be determined from the given directory object.
    /// </summary>
    /// <param name="user">The user to test.</param>
    /// <returns>
    /// <c>true</c> if the account has at least one assigned license, <c>false</c> if it has
    /// none, and <c>null</c> if the licensing state is not known.
    /// </returns>
    private static bool? HasAssignedLicense(GraphUser user)
        => user.AssignedLicenses == null
            ? null
            : user.AssignedLicenses.Count > 0;

    /// <summary>
    /// Determines whether the given user consumes a Duplicati user seat, which is the case
    /// exactly when the account has one or more assigned Microsoft 365 licenses. An account
    /// without a license cannot be used and is not paid for in the tenant, so it does not
    /// consume a seat here either.
    /// </summary>
    /// <param name="user">The user to test.</param>
    /// <returns><c>true</c> if the user should count as a seat; otherwise <c>false</c>.</returns>
    internal bool UserCountsAsSeat(GraphUser user)
    {
        var licensed = HasAssignedLicense(user);
        if (licensed != null)
            return licensed.Value;

        // The licensing state is only unknown if the user object was obtained without the
        // assignedLicenses property, which is a defect in the caller rather than a property of
        // the tenant. Count the seat rather than give it away, and report it once so that the
        // cause is visible instead of silently deciding that no user consumes a seat.
        if (Interlocked.Exchange(ref _userLicenseStateWarningIssued, 1) == 0)
            Library.Logging.Log.WriteWarningMessage(LOGTAG, "UserLicenseStateUnavailable", null, Strings.UserLicenseStateUnavailable(user.Id));

        return true;
    }

    /// <summary>
    /// Returns the cached classification for a user if it has already been resolved,
    /// without triggering any Graph API call. Returns <c>null</c> when the classification has
    /// not been resolved yet (or has not completed successfully).
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The resolved user category, or <c>null</c> if not yet available.</returns>
    internal UserCategory? TryGetCachedUserCategory(string userId)
        => _userCategoryCache.TryGetValue(userId, out var task) && task.IsCompletedSuccessfully
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
    /// The classification of a user, used for the include filter, for item metadata and for
    /// reporting item counts. It does not decide whether the user consumes a seat; see
    /// <see cref="UserCountsAsSeat"/> for that.
    /// </summary>
    internal enum UserCategory
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
    /// Determined on a best-effort basis from the Microsoft Graph <c>site</c> object, plus a
    /// lookup of the tenant's unlicensed users to separate out
    /// <see cref="PersonalUnlicensedUser"/>.
    /// </summary>
    internal enum SiteCategory
    {
        /// <summary>A Microsoft 365 group-connected team site.</summary>
        Group,
        /// <summary>
        /// A personal (OneDrive for Business) site whose owner holds a Microsoft 365 license,
        /// or could not be determined.
        /// </summary>
        PersonalLicensedUser,
        /// <summary>Any other or undetermined site type.</summary>
        Other,
        /// <summary>
        /// A personal (OneDrive for Business) site owned by a user account without an assigned
        /// Microsoft 365 license.
        /// </summary>
        PersonalUnlicensedUser,
    }

    /// <summary>
    /// Maps a <see cref="UserCategory"/> to its corresponding include-filter flag.
    /// </summary>
    private static SourceItems.Office365UserClassification ToClassificationFlag(UserCategory category)
        => category switch
        {
            UserCategory.Licensed => SourceItems.Office365UserClassification.Licensed,
            UserCategory.Unlicensed => SourceItems.Office365UserClassification.Unlicensed,
            UserCategory.SharedMailboxWithStorage => SourceItems.Office365UserClassification.SharedMailboxWithStorage,
            UserCategory.SharedMailboxWithoutStorage => SourceItems.Office365UserClassification.SharedMailboxWithoutStorage,
            _ => SourceItems.Office365UserClassification.Licensed
        };

    /// <summary>
    /// Maps a <see cref="SiteCategory"/> to its corresponding include-filter flag.
    /// </summary>
    /// <remarks>
    /// <see cref="SiteCategory.PersonalLicensedUser"/> maps to the licensed-owner flag rather
    /// than to the <see cref="SourceItems.Office365SiteClassification.Personal"/> alias, because
    /// the alias covers both owner states and would make a filter that excludes only one of them
    /// match anyway. A site whose owner could not be determined is therefore filtered as if the
    /// owner were licensed, which is the same assumption the seat counting makes.
    /// </remarks>
    private static SourceItems.Office365SiteClassification ToClassificationFlag(SiteCategory category)
        => category switch
        {
            SiteCategory.Group => Office365SiteClassification.Group,
            SiteCategory.PersonalLicensedUser => Office365SiteClassification.PersonalLicensedUser,
            SiteCategory.PersonalUnlicensedUser => Office365SiteClassification.PersonalUnlicensedUser,
            _ => Office365SiteClassification.Other
        };

    /// <summary>
    /// Whether the user-classification include filter covers every classification. When it
    /// does, the filter can never exclude a user, so the classification does not need to be
    /// resolved in order to apply it.
    /// </summary>
    private bool AllUserClassificationsIncluded
        => _includedUserClassifications.HasFlag(OptionsHelper.ALL_USER_CLASSIFICATIONS);

    /// <summary>
    /// Whether the site-classification include filter covers every classification. When it
    /// does, the filter can never exclude a site, so the classification does not need to be
    /// resolved in order to apply it.
    /// </summary>
    private bool AllSiteClassificationsIncluded
        => _includedSiteClassifications.HasFlag(OptionsHelper.ALL_SITE_CLASSIFICATIONS);

    /// <summary>
    /// Determines whether a user should be included based on its classification and the
    /// configured user-classification include filter.
    /// </summary>
    /// <param name="user">The user to test.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the user should be included; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Classifying a user costs one Graph request (the <c>mailboxSettings/userPurpose</c>
    /// lookup), so when the include filter covers every classification the test is a
    /// tautology and is answered without classifying at all. That is the default
    /// configuration, and it is what keeps a tenant-wide user listing from issuing one
    /// request per user.
    /// </remarks>
    internal async Task<bool> IsUserClassificationIncludedAsync(GraphUser user, CancellationToken cancellationToken)
    {
        if (AllUserClassificationsIncluded)
            return true;

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
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the site should be included; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Separating a personal site owned by an unlicensed user from one owned by a licensed user
    /// requires the unlicensed-user index, so when the include filter covers every
    /// classification the test is a tautology and is answered without classifying at all.
    /// That is the default configuration.
    /// </remarks>
    internal async Task<bool> IsSiteClassificationIncludedAsync(GraphSite site, CancellationToken cancellationToken)
    {
        if (AllSiteClassificationsIncluded)
            return true;

        var category = await ClassifySiteAsync(site, cancellationToken).ConfigureAwait(false);
        return _includedSiteClassifications.HasFlag(ToClassificationFlag(category));
    }

    /// <summary>
    /// Determines whether the given site consumes a Duplicati site seat. Personal (OneDrive)
    /// sites owned by a user account without an assigned Microsoft 365 license do not, which is
    /// the same rule <see cref="UserCountsAsSeat"/> applies to the account itself: a seat is
    /// consumed where Microsoft charges the tenant for the account. A site that is not personal
    /// has no single owning account to attribute it to and therefore always counts.
    /// </summary>
    /// <param name="site">The site to classify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the site should count as a seat; otherwise <c>false</c>.</returns>
    internal async Task<bool> SiteCountsAsSeatAsync(GraphSite site, CancellationToken cancellationToken)
    {
        // Restores are unlimited; avoid any extra Graph calls for classification.
        if (UsedForRestoreOperation)
            return true;

        var category = await ClassifySiteAsync(site, cancellationToken).ConfigureAwait(false);
        return SiteCategoryCountsAsSeat(category);
    }

    /// <summary>
    /// Determines whether the given site category consumes a licensed seat. Only personal
    /// sites that could be positively attributed to an unlicensed user account are exempt; every
    /// other category (including personal sites whose owner could not be determined) counts,
    /// so site seats are never under-counted.
    /// </summary>
    private static bool SiteCategoryCountsAsSeat(SiteCategory category)
        => category != SiteCategory.PersonalUnlicensedUser;

    /// <summary>
    /// Returns the cached classification for a site if it has already been resolved, without
    /// triggering any Graph API call. Returns <c>null</c> when the classification has not been
    /// resolved yet (or has not completed successfully).
    /// </summary>
    /// <param name="siteId">The site id.</param>
    /// <returns>The resolved site category, or <c>null</c> if not yet available.</returns>
    internal SiteCategory? TryGetCachedSiteCategory(string siteId)
        => _siteCategoryCache.TryGetValue(siteId, out var task) && task.IsCompletedSuccessfully
            ? task.Result
            : null;

    /// <summary>
    /// Classifies a user into one of the <see cref="UserCategory"/> buckets.
    /// A licensed user (with assigned licenses) is <see cref="UserCategory.Licensed"/>,
    /// unless it is a shared/room/equipment mailbox, in which case it is
    /// <see cref="UserCategory.SharedMailboxWithStorage"/>. Unlicensed regular mailboxes
    /// are <see cref="UserCategory.Unlicensed"/>, and unlicensed shared mailboxes are
    /// <see cref="UserCategory.SharedMailboxWithoutStorage"/>.
    /// </summary>
    /// <param name="user">The user to classify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The user category.</returns>
    /// <remarks>
    /// This costs one <c>mailboxSettings/userPurpose</c> Graph request per user, so it is only
    /// resolved where the exact bucket is actually needed: a narrowed include filter, the
    /// item metadata the user interface displays, and the count operation. Seat counting does
    /// not use it. The result is resolved once per user and cached, so the lookup is performed
    /// at most once per user and is shared between those callers.
    /// </remarks>
    internal Task<UserCategory> ClassifyUserAsync(GraphUser user, CancellationToken cancellationToken)
    {
        var task = _userCategoryCache.GetOrAdd(user.Id, _ => ResolveUserCategoryAsync(user, cancellationToken));

        // The cached task captured the cancellation token of whichever caller asked first, so a
        // cancelled lookup would otherwise stay cached and keep failing for the lifetime of the
        // provider. Evict it so the next caller retries with its own token.
        if (task.IsCanceled || task.IsFaulted)
            _userCategoryCache.TryRemove(new KeyValuePair<string, Task<UserCategory>>(user.Id, task));

        return task;
    }

    /// <summary>
    /// Resolves the classification for a user. See <see cref="ClassifyUserAsync"/>.
    /// </summary>
    private async Task<UserCategory> ResolveUserCategoryAsync(GraphUser user, CancellationToken cancellationToken)
    {
        // An unknown licensing state is treated as licensed, so that the bucket agrees with the
        // seat count, which counts the user in that case. See <see cref="HasAssignedLicense"/>.
        var hasLicense = HasAssignedLicense(user) ?? true;

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
            return hasLicense
                ? UserCategory.SharedMailboxWithStorage
                : UserCategory.SharedMailboxWithoutStorage;

        return hasLicense
            ? UserCategory.Licensed
            : UserCategory.Unlicensed;
    }

    /// <summary>
    /// Classifies a SharePoint site into one of the <see cref="SiteCategory"/> buckets,
    /// on a best-effort basis using only the Microsoft Graph <c>site</c> object.
    /// Personal (OneDrive) sites are detected reliably. The Graph <c>site</c> object does
    /// not expose the SharePoint web template, so group-connected team sites cannot be
    /// reliably distinguished from other sites from Graph alone and are reported as
    /// <see cref="SiteCategory.Other"/>.
    /// </summary>
    /// <param name="site">The site to classify.</param>
    /// <returns>The site category.</returns>
    /// <remarks>
    /// The site object says nothing about the licensing state of the owner of a personal site,
    /// so every personal site is reported as <see cref="SiteCategory.PersonalLicensedUser"/>
    /// here. <see cref="ResolveSiteCategoryAsync"/> refines that once the owner is known, and
    /// leaving it as the licensed bucket until then means an unresolved owner counts as a seat
    /// rather than being given away.
    /// </remarks>
    internal static SiteCategory ClassifySite(GraphSite site)
    {
        if (site.IsPersonalSite == true)
            return SiteCategory.PersonalLicensedUser;

        // OneDrive personal sites are hosted on the "-my" SharePoint host. Note that the
        // siteCollection facet is only populated on the root site of a site collection, so
        // this is a fallback for the root sites only.
        var host = site.SiteCollection?.Hostname;
        if (!string.IsNullOrWhiteSpace(host) && host.Contains("-my.", StringComparison.OrdinalIgnoreCase))
            return SiteCategory.PersonalLicensedUser;

        if (!string.IsNullOrWhiteSpace(site.WebUrl)
            && (site.WebUrl.Contains("-my.", StringComparison.OrdinalIgnoreCase)
                || site.WebUrl.Contains("/personal/", StringComparison.OrdinalIgnoreCase)))
            return SiteCategory.PersonalLicensedUser;

        // The Graph site object does not expose the web template, so group-connected
        // team sites cannot be distinguished reliably from Graph alone.
        return SiteCategory.Other;
    }

    /// <summary>
    /// Classifies a SharePoint site, additionally separating personal (OneDrive) sites owned
    /// by an unlicensed user account into <see cref="SiteCategory.PersonalUnlicensedUser"/>.
    /// </summary>
    /// <param name="site">The site to classify.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The site category.</returns>
    /// <remarks>
    /// The result is resolved once per site and cached, so the unlicensed-user index is
    /// consulted at most once per site and is shared between seat counting, the count
    /// operation and item metadata.
    /// </remarks>
    internal Task<SiteCategory> ClassifySiteAsync(GraphSite site, CancellationToken cancellationToken)
    {
        // Restores are unlimited, so the owner cannot affect seat counting and must not be paid
        // for. It is still resolved when the include filter needs it, because otherwise the
        // same configuration would select a different set of sites on restore than on backup.
        if (UsedForRestoreOperation && AllSiteClassificationsIncluded)
            return Task.FromResult(ClassifySite(site));

        // A site without an id cannot be cached, as the key would be shared with every other
        // id-less site. The unlicensed-user index is cached independently of this, so resolving
        // it directly costs no more than a cache miss.
        if (string.IsNullOrWhiteSpace(site.Id))
            return ResolveSiteCategoryAsync(site, cancellationToken);

        var task = _siteCategoryCache.GetOrAdd(site.Id, _ => ResolveSiteCategoryAsync(site, cancellationToken));

        // The cached task captured the cancellation token of whichever caller asked first, so a
        // cancelled lookup would otherwise stay cached and keep failing for the lifetime of the
        // provider. Evict it so the next caller retries with its own token.
        if (task.IsCanceled || task.IsFaulted)
            _siteCategoryCache.TryRemove(new KeyValuePair<string, Task<SiteCategory>>(site.Id, task));

        return task;
    }

    /// <summary>
    /// Resolves the classification for a site. See <see cref="ClassifySiteAsync"/>.
    /// </summary>
    private async Task<SiteCategory> ResolveSiteCategoryAsync(GraphSite site, CancellationToken cancellationToken)
    {
        var category = ClassifySite(site);

        // Only personal sites have a single owning user account, so nothing else can be
        // attributed to an unlicensed user.
        if (category != SiteCategory.PersonalLicensedUser)
            return category;

        var index = await GetUnlicensedUserSiteIndexAsync(cancellationToken).ConfigureAwait(false);
        return index.Contains(site)
            ? SiteCategory.PersonalUnlicensedUser
            : SiteCategory.PersonalLicensedUser;
    }

    /// <summary>
    /// Gets the index of the sites hosting the OneDrive of an unlicensed user, building it on
    /// first use.
    /// </summary>
    private Task<UnlicensedUserSiteIndex> GetUnlicensedUserSiteIndexAsync(CancellationToken cancellationToken)
    {
        lock (_unlicensedUserSiteIndexLock)
        {
            // A cancelled build captured the token of whichever caller asked first, so it must
            // not stay cached; rebuild it with the current caller's token instead.
            var existing = _unlicensedUserSiteIndex;
            if (existing != null && !existing.IsCanceled && !existing.IsFaulted)
                return existing;

            return _unlicensedUserSiteIndex = BuildUnlicensedUserSiteIndexAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Builds the index of the sites hosting the OneDrive of an unlicensed user.
    /// </summary>
    /// <remarks>
    /// The whole index costs a single listing of the tenant's accounts, selecting nothing but
    /// the user principal name and the assigned licenses. No request is made per account and
    /// none per site: a personal site is matched to its owner through the site URL, because
    /// SharePoint derives the URL of a personal site from the owner's user principal name (see
    /// <see cref="SanitizeUserPrincipalName"/>). Asking Graph instead — either which site hosts
    /// each account's OneDrive, or who owns each personal site — would cost one request per
    /// account or per site, which is not acceptable while enumerating for display.
    /// <para>
    /// The unlicensed accounts cannot be selected server-side the way the disabled ones could:
    /// filtering on <c>assignedLicenses</c> requires an advanced directory query, which needs a
    /// <c>ConsistencyLevel</c> request header that the paged reader does not send. The listing is
    /// therefore unfiltered and the licensing state is evaluated here, which costs one page per
    /// <see cref="APIHelper.BIG_PAGE_SIZE"/> accounts in the tenant. The index is built at most
    /// once per provider instance, and only if a personal site is encountered at all.
    /// </para>
    /// <para>
    /// The URL is derived from the user principal name at the time the OneDrive is provisioned
    /// and does not follow a later rename, so a renamed account is not recognized. Such a site
    /// keeps counting as a seat, which over-counts rather than under-counts, matching how every
    /// other unresolved case is treated here.
    /// </para>
    /// <para>
    /// A failure never propagates: the index is then built from whatever could be read, which
    /// attributes the remaining personal sites to a licensed user and so over-counts seats
    /// rather than under-counting them. That incomplete index is still cached, because
    /// rebuilding it for every personal site would multiply the cost of a failure that is
    /// unlikely to be transient; it is retried on the next operation, since the index does not
    /// outlive the provider instance. Whether the result is complete is reported once as a
    /// warning so the over-counting is not silent.
    /// </para>
    /// </remarks>
    private async Task<UnlicensedUserSiteIndex> BuildUnlicensedUserSiteIndexAsync(CancellationToken cancellationToken)
    {
        var sanitizedUserPrincipalNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            await foreach (var user in RootApi.ListUserLicenseStatesAsync(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only a positively unlicensed account is exempt. An account whose licensing
                // state could not be read is left out of the index and so keeps its seat.
                if (HasAssignedLicense(user) != false)
                    continue;

                var sanitized = SanitizeUserPrincipalName(user.UserPrincipalName);
                if (sanitized != null)
                    sanitizedUserPrincipalNames.Add(sanitized);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Paging may fail part-way through, in which case the users listed so far are still
            // usable; only the remaining ones keep counting as a seat.
            if (Interlocked.Exchange(ref _unlicensedUserLookupWarningIssued, 1) == 0)
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "UnlicensedUserLookupFailed", ex,
                    sanitizedUserPrincipalNames.Count == 0
                        ? Strings.UnlicensedUserLookupFailed
                        : Strings.UnlicensedUserLookupIncomplete(sanitizedUserPrincipalNames.Count));
        }

        return new UnlicensedUserSiteIndex(sanitizedUserPrincipalNames);
    }

    /// <summary>
    /// Converts a user principal name into the form SharePoint uses for the path segment of the
    /// owner's personal site, by replacing <c>@</c> and <c>.</c> with an underscore, so that
    /// <c>first.last@contoso.com</c> becomes <c>first_last_contoso_com</c>.
    /// </summary>
    /// <param name="userPrincipalName">The user principal name to convert.</param>
    /// <returns>The sanitized name, or <c>null</c> if there is nothing to convert.</returns>
    private static string? SanitizeUserPrincipalName(string? userPrincipalName)
        => string.IsNullOrWhiteSpace(userPrincipalName)
            ? null
            : userPrincipalName.Trim().Replace('@', '_').Replace('.', '_');

    /// <summary>
    /// Extracts the owner part of the URL of a personal site, i.e. the segment that follows
    /// <c>/personal/</c>, which is the sanitized user principal name of the owner.
    /// </summary>
    /// <param name="webUrl">The site URL to inspect.</param>
    /// <returns>The sanitized owner name, or <c>null</c> if the URL is not a personal site URL.</returns>
    /// <remarks>
    /// Only the segment right after <c>/personal/</c> is taken, so that a subsite of a personal
    /// site is attributed to the same owner as its site collection.
    /// </remarks>
    private static string? GetPersonalSiteOwnerSegment(string? webUrl)
    {
        const string marker = "/personal/";

        if (string.IsNullOrWhiteSpace(webUrl))
            return null;

        var start = webUrl.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return null;

        start += marker.Length;
        var end = webUrl.IndexOf('/', start);
        var segment = end < 0
            ? webUrl[start..]
            : webUrl[start..end];

        if (string.IsNullOrWhiteSpace(segment))
            return null;

        // A user principal name may contain characters that are percent-encoded in the URL,
        // notably the "#EXT#" marker of a guest account.
        return Uri.UnescapeDataString(segment);
    }

    /// <summary>
    /// A lookup of the sites that host the OneDrive of an unlicensed user account, keyed by the
    /// sanitized user principal name that SharePoint puts in the URL of a personal site.
    /// </summary>
    /// <param name="sanitizedUserPrincipalNames">
    /// The sanitized user principal names of the unlicensed accounts.
    /// </param>
    private sealed class UnlicensedUserSiteIndex(HashSet<string> sanitizedUserPrincipalNames)
    {
        /// <summary>
        /// Determines whether the given site hosts the OneDrive of an unlicensed user account.
        /// </summary>
        /// <param name="site">The site to test.</param>
        /// <returns><c>true</c> if the site belongs to an unlicensed user; otherwise <c>false</c>.</returns>
        public bool Contains(GraphSite site)
        {
            var owner = GetPersonalSiteOwnerSegment(site.WebUrl);
            return owner != null && sanitizedUserPrincipalNames.Contains(owner);
        }
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
        // An unknown licensing state is reported as licensed, for the same reason it counts as
        // a seat. See <see cref="HasAssignedLicense"/>.
        => (HasAssignedLicense(user) ?? true) ? "Licensed" : "Unlicensed";

    /// <summary>
    /// Classifies a group using only the data already present on the directory object,
    /// without any additional Graph API call.
    /// </summary>
    /// <param name="group">The group to classify.</param>
    /// <returns>The classification string suitable for item metadata.</returns>
    internal static string ClassifyGroupFromDirectory(GraphGroup group)
        => GroupCountsAsSeat(group) ? "Unified" : "NotUnified";

    /// <summary>
    /// Determines whether the seat limit for the given entry type has already been reached.
    /// </summary>
    /// <param name="type">The entry type to check.</param>
    /// <returns><c>true</c> if the seat limit has been reached; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// While seats remain, <see cref="LicenseApprovedForEntry"/> approves the entry whether or
    /// not it consumes one. Callers that only need the approval answer, and that do not
    /// increment the counters, can therefore skip resolving <c>countsAsSeat</c> until this
    /// returns <c>true</c>. That matters for sites, where resolving it requires the
    /// unlicensed-user index.
    /// </remarks>
    internal bool SeatLimitReached(Office365MetaType type)
    {
        // Restores are unlimited, so the limit is never reached.
        if (UsedForRestoreOperation)
            return false;

        if (type.HasFlag(Office365MetaType.Users))
            return _userCount >= LicenseChecker.LicenseHelper.AvailableOffice365UserSeats;
        if (type.HasFlag(Office365MetaType.Groups))
            return _groupCount >= LicenseChecker.LicenseHelper.AvailableOffice365GroupSeats;
        if (type.HasFlag(Office365MetaType.Sites))
            return _siteCount >= LicenseChecker.LicenseHelper.AvailableOffice365SiteSeats;

        // Unknown types are not limited.
        return false;
    }

    /// <summary>
    /// Determines whether the license is approved for the given entry.
    /// </summary>
    /// <param name="path">The path to verify.</param>
    /// <param name="type">The type of entry.</param>
    /// <param name="id">The ID of the entry.</param>
    /// <param name="increment">Whether to increment the counter if the license is approved.</param>
    /// <param name="countsAsSeat">
    /// Whether this entry consumes a licensed seat. When <c>false</c>, the entry is always
    /// approved and never counted against the seat limit (used for unlicensed users, for
    /// security groups and distribution lists, and for the personal sites of unlicensed users).
    /// </param>
    /// <returns><c>true</c> if the license is approved; otherwise, <c>false</c>.</returns>
    internal bool LicenseApprovedForEntry(string path, Office365MetaType type, string id, bool increment, bool countsAsSeat = true)
    {
        // We do not limit restores
        if (UsedForRestoreOperation)
            return true;

        // Entries that do not consume a seat (e.g. users without an assigned license) are
        // always approved and never counted against the seat limit.
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
